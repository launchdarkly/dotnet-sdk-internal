using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Common.Logging;
using LaunchDarkly.Sdk.Interfaces;
using LaunchDarkly.Sdk.Internal.Helpers;

namespace LaunchDarkly.Sdk.Internal.Events
{
    public interface IEventSender : IDisposable
    {
        Task<EventSenderResult> SendEventDataAsync(EventDataKind kind, string data, int eventCount);
    }

    public enum EventDataKind
    {
        AnalyticsEvents,
        DiagnosticEvent
    };

    public enum DeliveryStatus
    {
        Succeeded,
        Failed,
        FailedAndMustShutDown
    };

    public struct EventSenderResult
    {
        public DeliveryStatus Status { get; private set; }
        public DateTime? TimeFromServer { get; private set; }

        public EventSenderResult(DeliveryStatus status, DateTime? timeFromServer)
        {
            Status = status;
            TimeFromServer = timeFromServer;
        }
    }
}
