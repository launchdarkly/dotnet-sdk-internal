using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.EventSource;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Internal.Events;
using LaunchDarkly.Sdk.Internal.Helpers;

namespace LaunchDarkly.Sdk.Internal.Stream
{
    // Internal base implementation of the LaunchDarkly streaming connection. This class
    // manages an EventSource instance, and is responsible for restarting the connection if
    // necessary. It delegates all platform-specific logic to an implementation of
    // IStreamProcessor. The IStreamProcessor does not interact with the EventSource API directly.
    public sealed class StreamManager : IDisposable
    {
        private const int UNINITIALIZED = 0;
        private const int INITIALIZED = 1;

        public delegate IEventSource EventSourceCreator(StreamProperties streamProperties, IDictionary<string, string> headers);

        private readonly IStreamProcessor _streamProcessor;
        private readonly StreamProperties _streamProperties;
        private readonly IStreamManagerConfiguration _config;
        private readonly ClientEnvironment _clientEnvironment;
        private readonly TaskCompletionSource<bool> _initTask;
        private readonly EventSourceCreator _esCreator;
        private readonly EventSource.ExponentialBackoffWithDecorrelation _backOff;
        private readonly IDiagnosticStore _diagnosticStore;
        private readonly Logger _logger;
        private IEventSource _es;
        private int _initialized = UNINITIALIZED;
        internal DateTime _esStarted;

        /// <summary>
        /// Constructs a StreamManager instance.
        /// </summary>
        /// <param name="streamProcessor">A platform-specific implementation of IStreamProcessor.</param>
        /// <param name="streamProperties">HTTP request properties for the stream.</param>
        /// <param name="config">An implementation of IBaseConfiguration.</param>
        /// <param name="clientEnvironment">A subclass of ClientEnvironment.</param>
        /// <param name="eventSourceCreator">Null in normal usage; pass a non-null delegate if you
        /// are in a unit test and want to mock out the event source.</param>
        /// <param name="diagnosticStore">An implementation of IDiagnosticStore. The StreamManager
        /// will call AddStreamInit to record the stream init for diagnostics.</param>
        /// <param name="logger">A logger instance for messages from StreamManager and EventSource.</param>
        public StreamManager(IStreamProcessor streamProcessor, StreamProperties streamProperties,
            IStreamManagerConfiguration config, ClientEnvironment clientEnvironment,
            EventSourceCreator eventSourceCreator, IDiagnosticStore diagnosticStore,
            Logger logger)
        {
            _logger = logger;
            _streamProcessor = streamProcessor;
            _streamProperties = streamProperties;
            _config = config;
            _clientEnvironment = clientEnvironment;
            _diagnosticStore = diagnosticStore;
            _esCreator = eventSourceCreator ?? DefaultEventSourceCreator;
            _initTask = new TaskCompletionSource<bool>();
            _backOff = new EventSource.ExponentialBackoffWithDecorrelation(_config.ReconnectTime, TimeSpan.FromMilliseconds(30000));
        }

        // Stream processors should set this property to true as soon as they have received their
        // first complete set of feature data. Setting it to true causes the Task created by Start
        // to be completed.
        public bool Initialized
        {
            get
            {
                return _initialized == INITIALIZED;
            }
            set
            {
                var newState = value ? INITIALIZED : UNINITIALIZED;
                if (Interlocked.Exchange(ref _initialized, newState) == UNINITIALIZED && value)
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
            Dictionary<string, string> headers = Util.GetRequestHeaders(_config, _clientEnvironment);
            headers.Add("Accept", "text/event-stream");

            _es = _esCreator(_streamProperties, headers);

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
                _logger.Error("Unexpected error: {0}", LogValues.ExceptionSummary(exc));
                _logger.Debug(LogValues.ExceptionTrace(exc));
            }
        }

        private IEventSource DefaultEventSourceCreator(StreamProperties streamProperties, IDictionary<string, string> headers)
        {
            EventSource.Configuration config = EventSource.Configuration.Builder(streamProperties.StreamUri)
                .Method(streamProperties.Method)
                .RequestBodyFactory(() => streamProperties.RequestBody)
                .MessageHandler(_config.HttpMessageHandler)
                .ConnectionTimeout(_config.HttpClientTimeout)
                .DelayRetryDuration(_config.ReconnectTime)
                .ReadTimeout(_config.ReadTimeout)
                .RequestHeaders(headers)
                .Logger(_logger)
                .Build();
            return new EventSource.EventSource(config);
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
                    ex, e.EventName, e.Message.Data);

                _logger.Error("Encountered an error reading feature data: {0}", LogValues.ExceptionSummary(ex));
                _logger.Debug(LogValues.ExceptionTrace(ex));

                Restart();
            }
            catch (Exception ex)
            {
                _logger.Error("Unexpected error: {0}", LogValues.ExceptionSummary(ex));
                _logger.Debug(LogValues.ExceptionTrace(ex));

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
            var ex = _config.TranslateHttpException(e.Exception);
            _logger.Error("Encountered EventSource error: {0}", LogValues.ExceptionSummary(ex));
            _logger.Debug(LogValues.ExceptionTrace(ex));
            if (ex is EventSource.EventSourceServiceUnsuccessfulResponseException respEx)
            {
                int status = respEx.StatusCode;
                _logger.Error(Util.HttpErrorMessage(status, "streaming connection", "will retry"));
                RecordStreamInit(true);
                if (!Util.IsHttpErrorRecoverable(status))
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
