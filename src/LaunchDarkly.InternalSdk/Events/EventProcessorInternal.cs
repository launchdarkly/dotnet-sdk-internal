using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.Logging;

namespace LaunchDarkly.Sdk.Internal.Events
{
    internal class EventProcessorInternal
    {
        #region Inner types

        // These types are used only for communication between EventProcessor and
        // EventProcessorInternal on their shared queue.

        internal interface IEventMessage { }

        internal class EventMessage : IEventMessage
        {
            internal IEvent Event { get; private set; }

            internal EventMessage(IEvent e)
            {
                Event = e;
            }
        }

        internal class FlushMessage : IEventMessage { }

        internal class FlushUsersMessage : IEventMessage { }

        internal class DiagnosticMessage : IEventMessage { }

        internal class SynchronousMessage : IEventMessage
        {
            internal readonly Semaphore _reply;

            internal SynchronousMessage()
            {
                _reply = new Semaphore(0, 1);
            }

            internal void WaitForCompletion()
            {
                _reply.WaitOne();
            }

            internal void Completed()
            {
                _reply.Release();
            }
        }

        internal class TestSyncMessage : SynchronousMessage { }

        internal class ShutdownMessage : SynchronousMessage { }

        internal interface IEvent
        {
            UnixMillisecondTime Timestamp { get; }
            User User { get; }
        }

        internal class FeatureRequestEvent : IEvent
        {
            public UnixMillisecondTime Timestamp { get; set; }
            public User User { get; set; }
            public string FlagKey { get; set; }
            public int? FlagVersion { get; set; }
            public int? Variation { get; set; }
            public LdValue Value { get; set; }
            public LdValue Default { get; set; }
            public EvaluationReason? Reason { get; set; }
            public string PrereqOf { get; set; }
            public bool TrackEvents { get; set; }
            public UnixMillisecondTime? DebugEventsUntilDate { get; set; }
        }

        internal class IdentifyEvent : IEvent
        {
            public UnixMillisecondTime Timestamp { get; set; }
            public User User { get; set; }
        }

        internal class CustomEvent : IEvent
        {
            public UnixMillisecondTime Timestamp { get; set; }
            public User User { get; set; }
            public String EventKey { get; set; }
            public LdValue Data { get; set; }
            public double? MetricValue { get; set; }
        }

        internal class IndexEvent : IEvent
        {
            public UnixMillisecondTime Timestamp { get; set; }
            public User User { get; set; }
        }

        internal class DebugEvent : IEvent
        {
            public FeatureRequestEvent FromEvent { get; set; }
            public UnixMillisecondTime Timestamp => FromEvent.Timestamp;
            public User User => FromEvent.User;
        }


        internal sealed class FlushPayload
        {
            internal IEvent[] Events { get; set; }
            internal EventSummary Summary { get; set; }
        }

        internal sealed class EventBuffer
        {
            private readonly List<IEvent> _events;
            private readonly EventSummarizer _summarizer;
            private readonly IDiagnosticStore _diagnosticStore;
            private readonly int _capacity;
            private readonly Logger _logger;
            private bool _exceededCapacity;

            internal EventBuffer(int capacity, IDiagnosticStore diagnosticStore, Logger logger)
            {
                _capacity = capacity;
                _events = new List<IEvent>();
                _summarizer = new EventSummarizer();
                _diagnosticStore = diagnosticStore;
                _logger = logger;
            }

            internal void AddEvent(IEvent e)
            {
                if (_events.Count >= _capacity)
                {
                    _diagnosticStore?.IncrementDroppedEvents();
                    if (!_exceededCapacity)
                    {
                        _logger.Warn("Exceeded event queue capacity. Increase capacity to avoid dropping events.");
                        _exceededCapacity = true;
                    }
                }
                else
                {
                    _events.Add(e);
                    _exceededCapacity = false;
                }
            }

            internal void AddToSummary(IEvent e)
            {
                if (e is FeatureRequestEvent fe)
                {
                    _summarizer.SummarizeEvent(fe.Timestamp, fe.FlagKey, fe.FlagVersion, fe.Variation, fe.Value, fe.Default);
                }
            }

            internal FlushPayload GetPayload()
            {
                return new FlushPayload { Events = _events.ToArray(), Summary = _summarizer.Snapshot() };
            }

            internal void Clear()
            {
                _events.Clear();
                _summarizer.Clear();
            }
        }

        #endregion

        #region Private fields

        private static readonly int MaxFlushWorkers = 5;

        private readonly EventsConfiguration _config;
        private readonly IDiagnosticStore _diagnosticStore;
        private readonly IUserDeduplicator _userDeduplicator;
        private readonly CountdownEvent _flushWorkersCounter;
        private readonly Action _testActionOnDiagnosticSend;
        private readonly IEventSender _eventSender;
        private readonly Logger _logger;
        private readonly Random _random;
        private long _lastKnownPastTime;
        private volatile bool _disabled;

        #endregion

        #region Constructor

        internal EventProcessorInternal(
            EventsConfiguration config,
            BlockingCollection<IEventMessage> messageQueue,
            IEventSender eventSender,
            IUserDeduplicator userDeduplicator,
            IDiagnosticStore diagnosticStore,
            Logger logger,
            Action testActionOnDiagnosticSend
            )
        {
            _config = config;
            _diagnosticStore = diagnosticStore;
            _userDeduplicator = userDeduplicator;
            _testActionOnDiagnosticSend = testActionOnDiagnosticSend;
            _flushWorkersCounter = new CountdownEvent(1);
            _eventSender = eventSender;
            _logger = logger;
            _random = new Random();

            EventBuffer buffer = new EventBuffer(config.EventCapacity > 0 ? config.EventCapacity : 1, _diagnosticStore, _logger);

            Task.Run(() => RunMainLoop(messageQueue, buffer));
        }

        #endregion

        #region Public methods

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private methods

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                _eventSender.Dispose();
            }
        }

        private void RunMainLoop(BlockingCollection<IEventMessage> messageQueue, EventBuffer buffer)
        {
            bool running = true;
            while (running)
            {
                try
                {
                    IEventMessage message = messageQueue.Take();
                    switch (message)
                    {
                        case EventMessage em:
                            ProcessEvent(em.Event, buffer);
                            break;
                        case FlushMessage fm:
                            StartFlush(buffer);
                            break;
                        case FlushUsersMessage fm:
                            if (_userDeduplicator != null)
                            {
                                _userDeduplicator.Flush();
                            }
                            break;
                        case DiagnosticMessage dm:
                            SendAndResetDiagnostics(buffer);
                            break;
                        case TestSyncMessage tm:
                            WaitForFlushes();
                            tm.Completed();
                            break;
                        case ShutdownMessage sm:
                            WaitForFlushes();
                            running = false;
                            sm.Completed();
                            break;
                    }
                }
                catch (Exception e)
                {
                    LogHelpers.LogException(_logger, "Unexpected error in event dispatcher thread", e);
                }
            }
        }

        private void SendAndResetDiagnostics(EventBuffer buffer)
        {
            if (_diagnosticStore != null)
            {
                Task.Run(() => SendDiagnosticEventAsync(_diagnosticStore.CreateEventAndReset()));
            }
        }

        private void WaitForFlushes()
        {
            // Our CountdownEvent was initialized with a count of 1, so that's the lowest it can be at this point.
            _flushWorkersCounter.Signal(); // Drop the count to zero if there are no active flush tasks.
            _flushWorkersCounter.Wait();   // Wait until it is zero.
            _flushWorkersCounter.Reset(1);
        }

        private void ProcessEvent(IEvent e, EventBuffer buffer)
        {
            if (_disabled)
            {
                return;
            }

            // Always record the event in the summarizer.
            buffer.AddToSummary(e);

            // Decide whether to add the event to the payload. Feature events may be added twice, once for
            // the event (if tracked) and once for debugging.
            bool willAddFullEvent;
            IEvent debugEvent = null;
            if (e is FeatureRequestEvent fe)
            {
                willAddFullEvent = fe.TrackEvents;
                if (ShouldDebugEvent(fe))
                {
                    debugEvent = new DebugEvent { FromEvent = fe };
                }
            }
            else
            {
                willAddFullEvent = true;
            }

            // Tell the user deduplicator, if any, about this user; this may produce an index event.
            // We only need to do this if there is *not* already going to be a full-fidelity event
            // containing an inline user.
            if (!(willAddFullEvent && _config.InlineUsersInEvents))
            {
                if (_userDeduplicator != null && e.User != null)
                {
                    bool needUserEvent = _userDeduplicator.ProcessUser(e.User);
                    if (needUserEvent && !(e is IdentifyEvent))
                    {
                        IndexEvent ie = new IndexEvent { Timestamp = e.Timestamp, User = e.User };
                        buffer.AddEvent(ie);
                    }
                    else if (!(e is IdentifyEvent))
                    {
                        _diagnosticStore?.IncrementDeduplicatedUsers();
                    }
                }
            }

            if (willAddFullEvent)
            {
                buffer.AddEvent(e);
            }
            if (debugEvent != null)
            {
                buffer.AddEvent(debugEvent);
            }
        }

        private bool ShouldDebugEvent(FeatureRequestEvent fe)
        {
            if (fe.DebugEventsUntilDate != null)
            {
                long lastPast = Interlocked.Read(ref _lastKnownPastTime);
                if (fe.DebugEventsUntilDate.Value.Value > lastPast &&
                    fe.DebugEventsUntilDate.Value.Value > UnixMillisecondTime.Now.Value)
                {
                    return true;
                }
            }
            return false;
        }

        private bool ShouldTrackFullEvent(IEvent e)
        {
            if (e is FeatureRequestEvent fe)
            {
                if (fe.TrackEvents)
                {
                    return true;
                }
                if (fe.DebugEventsUntilDate != null)
                {
                    long lastPast = Interlocked.Read(ref _lastKnownPastTime);
                    if (fe.DebugEventsUntilDate.Value.Value > lastPast &&
                        fe.DebugEventsUntilDate.Value.Value > UnixMillisecondTime.Now.Value)
                    {
                        return true;
                    }
                }
                return false;
            }
            else
            {
                return true;
            }
        }

        // Grabs a snapshot of the current internal state, and starts a new task to send it to the server.
        private void StartFlush(EventBuffer buffer)
        {
            if (_disabled)
            {
                return;
            }
            FlushPayload payload = buffer.GetPayload();
            if (_diagnosticStore != null)
            {
                _diagnosticStore.RecordEventsInBatch(payload.Events.Length);
            }
            if (payload.Events.Length > 0 || !payload.Summary.Empty)
            {
                lock (_flushWorkersCounter)
                {
                    // Note that this counter will be 1, not 0, when there are no active flush workers.
                    // This is because a .NET CountdownEvent can't be reused without explicitly resetting
                    // it once it has gone to zero.
                    if (_flushWorkersCounter.CurrentCount >= MaxFlushWorkers + 1)
                    {
                        // We already have too many workers, so just leave the events as is
                        return;
                    }
                    // We haven't hit the limit, we'll go ahead and start a flush task
                    _flushWorkersCounter.AddCount(1);
                }
                buffer.Clear();
                Task.Run(async () => {
                    try
                    {
                        await FlushEventsAsync(payload);
                    }
                    finally
                    {
                        _flushWorkersCounter.Signal();
                    }
                });
            }
        }

        private async Task FlushEventsAsync(FlushPayload payload)
        {
            EventOutputFormatter formatter = new EventOutputFormatter(_config);
            string jsonEvents;
            int eventCount;
            try
            {
                jsonEvents = formatter.SerializeOutputEvents(payload.Events, payload.Summary, out eventCount);
            }
            catch (Exception e)
            {
                LogHelpers.LogException(_logger, "Error preparing events, will not send", e);
                return;
            }

            var result = await _eventSender.SendEventDataAsync(EventDataKind.AnalyticsEvents,
                jsonEvents, eventCount);
            if (result.Status == DeliveryStatus.FailedAndMustShutDown)
            {
                _disabled = true;
            }
            if (result.TimeFromServer.HasValue)
            {
                Interlocked.Exchange(ref _lastKnownPastTime,
                    UnixMillisecondTime.FromDateTime(result.TimeFromServer.Value).Value);
            }
        }

        internal async Task SendDiagnosticEventAsync(DiagnosticEvent diagnostic)
        {
            var jsonDiagnostic = diagnostic.JsonValue.ToJsonString();
            await _eventSender.SendEventDataAsync(EventDataKind.DiagnosticEvent, jsonDiagnostic, 1);
            _testActionOnDiagnosticSend?.Invoke();
        }

        #endregion
    }
}
