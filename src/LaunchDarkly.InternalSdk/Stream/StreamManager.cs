using System;
using System.Linq;
using System.Threading.Tasks;
using LaunchDarkly.EventSource;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Internal.Events;
using LaunchDarkly.Sdk.Internal.Helpers;
using LaunchDarkly.Sdk.Internal.Http;

namespace LaunchDarkly.Sdk.Internal.Stream
{ 
    /// <summary>
    /// Internal base implementation of the LaunchDarkly streaming connection.
    /// </summary>
    /// <remarks>
    /// This class manages an EventSource instance, and is responsible for restarting the connection if
    /// necessary. It delegates all platform-specific logic to an implementation of IStreamProcessor
    /// that is provided by the caller, which does not interact with the stream directly.
    /// </remarks>
    public sealed class StreamManager : IDisposable
    {
        // The read timeout for the stream is not the same read timeout that can be set in the SDK configuration.
        // It is a fixed value that is set to be slightly longer than the expected interval between heartbeats
        // from the LaunchDarkly streaming server. If this amount of time elapses with no new data, the connection
        // will be cycled.
        private static readonly TimeSpan LaunchDarklyStreamReadTimeout = TimeSpan.FromMinutes(5);

        public delegate IEventSource EventSourceCreator(StreamProperties streamProperties, HttpProperties httpProperties);

        private readonly IStreamProcessor _streamProcessor;
        private readonly StreamProperties _streamProperties;
        private readonly HttpProperties _httpProperties;
        private readonly TimeSpan _initialReconnectDelay;
        private readonly TaskCompletionSource<bool> _initTask;
        private readonly EventSourceCreator _esCreator;
        private readonly EventSource.ExponentialBackoffWithDecorrelation _backOff;
        private readonly IDiagnosticStore _diagnosticStore;
        private readonly AtomicBoolean _initialized = new AtomicBoolean(false);
        private readonly Logger _logger;
        private IEventSource _es;
        internal DateTime _esStarted;

        /// <summary>
        /// Constructs a StreamManager instance.
        /// </summary>
        /// <param name="streamProcessor">A platform-specific implementation of IStreamProcessor.</param>
        /// <param name="streamProperties">HTTP request properties for the stream.</param>
        /// <param name="httpProperties">General HTTP configuration properties.</param>
        /// <param name="config">An implementation of IBaseConfiguration.</param>
        /// <param name="clientEnvironment">A subclass of ClientEnvironment.</param>
        /// <param name="eventSourceCreator">Null in normal usage; pass a non-null delegate if you
        /// are in a unit test and want to mock out the event source.</param>
        /// <param name="diagnosticStore">An implementation of IDiagnosticStore. The StreamManager
        /// will call AddStreamInit to record the stream init for diagnostics.</param>
        /// <param name="logger">A logger instance for messages from StreamManager and EventSource.</param>
        public StreamManager(
            IStreamProcessor streamProcessor,
            StreamProperties streamProperties,
            HttpProperties httpProperties,
            TimeSpan initialReconnectDelay,
            EventSourceCreator eventSourceCreator,
            IDiagnosticStore diagnosticStore,
            Logger logger)
        {
            _logger = logger;
            _streamProcessor = streamProcessor;
            _streamProperties = streamProperties;
            _httpProperties = httpProperties
                .WithHeader("Accept", "text/event-stream");
            _initialReconnectDelay = initialReconnectDelay;
            _diagnosticStore = diagnosticStore;
            _esCreator = eventSourceCreator ?? DefaultEventSourceCreator;
            _initTask = new TaskCompletionSource<bool>();
            _backOff = new EventSource.ExponentialBackoffWithDecorrelation(initialReconnectDelay,
                EventSource.Configuration.MaximumRetryDuration);
        }

        // Stream processors should set this property to true as soon as they have received their
        // first complete set of feature data. Setting it to true causes the Task created by Start
        // to be completed.
        public bool Initialized
        {
            get
            {
                return _initialized.Get();
            }
            set
            {
                if (!_initialized.GetAndSet(value) && value)
                {
                    _initTask.SetResult(true);
                    _logger.Info("Initialized LaunchDarkly Stream Processor.");
                }
            }
        }

        // Attempts to start the stream connection asynchronously. The resulting Task will be
        // marked as completed as soon as the subclass implementation sets Initialized to true. 
        public Task<bool> Start()
        {
            _es = _esCreator(_streamProperties, _httpProperties);

            _es.CommentReceived += OnComment;
            _es.MessageReceived += OnMessage;
            _es.Error += OnError;
            _es.Opened += OnOpen;
            _es.Closed += OnClose;

            Task.Run(() => {
                _esStarted = DateTime.Now;
                return _es.StartAsync();
            });

            return _initTask.Task;
        }

        // Closes and restarts the connection (using the same stream URI).
        public async void Restart()
        {
            TimeSpan sleepTime = _backOff.GetNextBackOff();
            if (sleepTime != TimeSpan.Zero)
            {
                _logger.Info("Stopping LaunchDarkly StreamProcessor. Waiting {0} milliseconds before reconnecting...",
                    sleepTime.TotalMilliseconds);
            }
            _es.Close();
            await Task.Delay(sleepTime);
            try
            {
                _esStarted = DateTime.Now;
                await _es.StartAsync();
                _backOff.ResetReconnectAttemptCount();
                _logger.Info("Reconnected to LaunchDarkly StreamProcessor");
            }
            catch (Exception exc)
            {
                LogHelpers.LogException(_logger, null, exc);
            }
        }

        private IEventSource DefaultEventSourceCreator(StreamProperties streamProperties, HttpProperties httpProperties)
        {
            var configBuilder = EventSource.Configuration.Builder(streamProperties.StreamUri)
                .Method(streamProperties.Method)
                .RequestBodyFactory(() => streamProperties.RequestBody)
                .MessageHandler(httpProperties.HttpMessageHandler)
                .ConnectionTimeout(httpProperties.ConnectTimeout)
                .DelayRetryDuration(_initialReconnectDelay)
                .ReadTimeout(LaunchDarklyStreamReadTimeout)
                .RequestHeaders(httpProperties.BaseHeaders.ToDictionary(kv => kv.Key, kv => kv.Value))
                .Logger(_logger);
            return new EventSource.EventSource(configBuilder.Build());
        }

        private async void OnMessage(object sender, EventSource.MessageReceivedEventArgs e)
        {
            try
            {
                await _streamProcessor.HandleMessage(this, e.EventName, e.Message.Data);
            }
            catch (StreamJsonParsingException ex)
            {
                _logger.Debug("Failed to deserialize JSON in {0} message:\n{1}",
                    e.EventName, e.Message.Data);
                LogHelpers.LogException(_logger, "Encountered an error reading stream data", ex);
                Restart();
            }
            catch (Exception ex)
            {
                LogHelpers.LogException(_logger, null, ex);
                Restart();
            }
        }

        private void RecordStreamInit(bool failed) {
            if (_diagnosticStore != null) {
                DateTime now = DateTime.Now;
                _diagnosticStore.AddStreamInit(_esStarted, now - _esStarted, failed);
                _esStarted = now;
            }
        }

        private void OnOpen(object sender, EventSource.StateChangedEventArgs e)
        {
            _logger.Debug("Eventsource Opened");
            RecordStreamInit(false);
        }

        private void OnClose(object sender, EventSource.StateChangedEventArgs e)
        {
            _logger.Debug("Eventsource Closed");
        }

        private void OnComment(object sender, EventSource.CommentReceivedEventArgs e)
        {
            _logger.Debug("Received a heartbeat.");
        }

        private void OnError(object sender, EventSource.ExceptionEventArgs e)
        {
            var ex = _httpProperties.HttpExceptionConverter(e.Exception);
            LogHelpers.LogException(_logger, "Encountered EventSource error", ex);
            if (ex is EventSource.EventSourceServiceUnsuccessfulResponseException respEx)
            {
                int status = respEx.StatusCode;
                _logger.Error(HttpErrors.ErrorMessage(status, "streaming connection", "will retry"));
                RecordStreamInit(true);
                if (!HttpErrors.IsRecoverable(status))
                {
                    _initTask.TrySetException(ex); // sends this exception to the client if we haven't already started up
                    ((IDisposable)this).Dispose();
                }
            }
        }

        void IDisposable.Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                _logger.Info("Stopping LaunchDarkly StreamProcessor");
                if (_es != null)
                {
                    _es.Close();
                }
            }
        }
    }
}
