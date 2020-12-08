using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.EventSource;
using LaunchDarkly.Sdk.Internal.Events;
using LaunchDarkly.Sdk.Internal.Http;
using Moq;
using Xunit;

using static LaunchDarkly.Sdk.TestUtil;

namespace LaunchDarkly.Sdk.Internal.Stream
{
    public class StreamManagerTest
    {
        private static Uri StreamUri = new Uri("http://test");

        private static HttpProperties FakeHttpProperties =
            HttpProperties.Default.WithHeader("header", "value");

        readonly Mock<IEventSource> _mockEventSource;
        readonly IEventSource _eventSource;
        readonly StubEventSourceCreator _eventSourceCreator;
        CountdownEvent _esStartedReady = new CountdownEvent(1);
        readonly Mock<IStreamProcessor> _mockStreamProcessor;
        readonly IStreamProcessor _streamProcessor;
        readonly StreamProperties _streamProperties;
        Mock<IDiagnosticStore> _mockDiagnosticStore;
        IDiagnosticStore _diagnosticStore;

        public StreamManagerTest()
        {
            _mockEventSource = new Mock<IEventSource>();
            _mockEventSource.Setup(es => es.StartAsync()).Returns(Task.CompletedTask).Callback(() => _esStartedReady.Signal());
            _eventSource = _mockEventSource.Object;
            _eventSourceCreator = new StubEventSourceCreator(_eventSource);
            _mockStreamProcessor = new Mock<IStreamProcessor>();
            _streamProcessor = _mockStreamProcessor.Object;
            _streamProperties = new StreamProperties(StreamUri, HttpMethod.Get, null);
        }

        private StreamManager CreateManager()
        {
            return new StreamManager(
                _streamProcessor,
                _streamProperties,
                FakeHttpProperties,
                TimeSpan.FromSeconds(1),
                _eventSourceCreator.Create,
                _diagnosticStore,
                NullLogger
                );
        }

        [Fact]
        public void StreamPropertiesArePassedToEventSourceFactory()
        {
            using (StreamManager sm = CreateManager())
            {
                sm.Start();
                Assert.Equal(_streamProperties, _eventSourceCreator.ReceivedProperties);
            }
        }

        [Fact]
        public void HttpPropertiesArePassedToEventSourceFactory()
        {
            using (StreamManager sm = CreateManager())
            {
                sm.Start();
                var expectedHttpProperties =
                    FakeHttpProperties.WithHeader("Accept", "text/event-stream");
                Assert.Equal(expectedHttpProperties.BaseHeaders,
                    _eventSourceCreator.ReceivedHttpProperties.BaseHeaders);
            }
        }

        [Fact]
        public void StreamInitDiagnosticRecordedOnOpen()
        {
            _mockDiagnosticStore = new Mock<IDiagnosticStore>();
            _diagnosticStore = _mockDiagnosticStore.Object;
            using (StreamManager sm = CreateManager())
            {
                sm.Start();
                Assert.True(_esStartedReady.Wait(TimeSpan.FromSeconds(1)));
                DateTime esStarted = sm._esStarted;
                Thread.Sleep(TimeSpan.FromMilliseconds(100));
                _mockEventSource.Raise(es => es.Opened += null, new EventSource.StateChangedEventArgs(ReadyState.Open));
                DateTime startCompleted = sm._esStarted;

                Assert.True(esStarted != startCompleted);
                _mockDiagnosticStore.Verify(ds => ds.AddStreamInit(esStarted, It.Is<TimeSpan>(ts => TimeSpan.Equals(ts, startCompleted - esStarted)), false));
            }
        }

        [Fact]
        public void StreamInitDiagnosticRecordedOnError()
        {
            _mockDiagnosticStore = new Mock<IDiagnosticStore>();
            _diagnosticStore = _mockDiagnosticStore.Object;
            using (StreamManager sm = CreateManager())
            {
                sm.Start();
                Assert.True(_esStartedReady.Wait(TimeSpan.FromSeconds(1)));
                DateTime esStarted = sm._esStarted;
                Thread.Sleep(TimeSpan.FromMilliseconds(100));
                _mockEventSource.Raise(es => es.Error += null, new EventSource.ExceptionEventArgs(new EventSource.EventSourceServiceUnsuccessfulResponseException("test", 401)));
                DateTime startFailed = sm._esStarted;

                Assert.True(esStarted != startFailed);
                _mockDiagnosticStore.Verify(ds => ds.AddStreamInit(esStarted, It.Is<TimeSpan>(ts => TimeSpan.Equals(ts, startFailed - esStarted)), true));
            }
        }

        [Fact]
        public void EventIsPassedToStreamProcessor()
        {
            string eventType = "put";
            string eventData = "{}";

            using (StreamManager sm = CreateManager())
            {
                sm.Start();
                MessageReceivedEventArgs e = new MessageReceivedEventArgs(new MessageEvent(eventData, null), eventType);
                _mockEventSource.Raise(es => es.MessageReceived += null, e);

                _mockStreamProcessor.Verify(sp => sp.HandleMessage(sm, eventType, eventData));
            }
        }

        [Fact]
        public void TaskIsNotCompletedByDefault()
        {
            using (StreamManager sm = CreateManager())
            {
                Task<bool> task = sm.Start();
                Assert.False(task.IsCompleted);
            }
        }

        [Fact]
        public void InitializedIsFalseByDefault()
        {
            using (StreamManager sm = CreateManager())
            {
                sm.Start();
                Assert.False(sm.Initialized);
            }
        }

        [Fact]
        public void SettingInitializedCausesTaskToBeCompleted()
        {
            using (StreamManager sm = CreateManager())
            {
                Task<bool> task = sm.Start();
                sm.Initialized = true;
                Assert.True(task.IsCompleted);
                Assert.False(task.IsFaulted);
            }
        }

        [Fact]
        public void GeneralExceptionDoesNotStopStream()
        {
            using (StreamManager sm = CreateManager())
            {
                sm.Start();
                var e = new Exception("whatever");
                var eea = new ExceptionEventArgs(e);
                _mockEventSource.Raise(es => es.Error += null, eea);

                _mockEventSource.Verify(es => es.Close(), Times.Never());

                _mockStreamProcessor.Verify(sp => sp.HandleError(sm, e, true));
            }
        }
        
        [Fact]
        public void Http401ErrorShutsDownStream()
        {
            VerifyUnrecoverableHttpError(401);
        }

        [Fact]
        public void Http403ErrorShutsDownStream()
        {
            VerifyUnrecoverableHttpError(403);
        }

        [Fact]
        public void Http408ErrorDoesNotShutDownStream()
        {
            VerifyRecoverableHttpError(408);
        }

        [Fact]
        public void Http429ErrorDoesNotShutDownStream()
        {
            VerifyRecoverableHttpError(429);
        }

        [Fact]
        public void Http500ErrorDoesNotShutDownStream()
        {
            VerifyRecoverableHttpError(500);
        }

        private void VerifyUnrecoverableHttpError(int status)
        {
            using (StreamManager sm = CreateManager())
            {
                Task<bool> initTask = sm.Start();
                var e = new EventSourceServiceUnsuccessfulResponseException("", status);
                var eea = new ExceptionEventArgs(e);
                _mockEventSource.Raise(es => es.Error += null, eea);

                _mockEventSource.Verify(es => es.Close());
                Assert.True(initTask.IsCompleted);
                Assert.True(initTask.IsFaulted);
                Assert.Equal(e, initTask.Exception.InnerException);
                Assert.False(sm.Initialized);

                _mockStreamProcessor.Verify(sp => sp.HandleError(sm, e, false));
            }
        }

        private void VerifyRecoverableHttpError(int status)
        {
            using (StreamManager sm = CreateManager())
            {
                Task<bool> initTask = sm.Start();

                var e = new EventSourceServiceUnsuccessfulResponseException("", 500);
                var eea = new ExceptionEventArgs(e);
                _mockEventSource.Raise(es => es.Error += null, eea);

                _mockEventSource.Verify(es => es.Close(), Times.Never());
                Assert.False(initTask.IsCompleted);

                _mockStreamProcessor.Verify(sp => sp.HandleError(sm, e, true));
            }
        }
    }

    internal class StubEventSourceCreator
    {
        public StreamProperties ReceivedProperties { get; private set; }
        public HttpProperties ReceivedHttpProperties { get; private set; }
        private readonly IEventSource _eventSource;

        public StubEventSourceCreator(IEventSource es)
        {
            _eventSource = es;
        }

        public IEventSource Create(StreamProperties sp, HttpProperties hp)
        {
            ReceivedProperties = sp;
            ReceivedHttpProperties = hp;
            return _eventSource;
        }
    }
}
