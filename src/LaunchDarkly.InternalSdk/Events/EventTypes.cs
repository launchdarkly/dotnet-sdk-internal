﻿
namespace LaunchDarkly.Sdk.Internal.Events
{
    /// <summary>
    /// This class contains inner types that are used as parameter types for the EventProcessor
    /// methods for recording different kinds of events.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reason we define these as types, rather than just having those methods take a bunch
    /// of individual parameters like timestamp and key, is to make it as easy as possible to add
    /// new optional properties: adding a parameter to a method is a backward-incompatible change
    /// (unless we add overloads, in which case we'll keep accumulating overloads) whereas adding
    /// a property to a struct means it'll just have its default value if the caller doesn't set
    /// it. We could get a similar effect by using named method parameters with defaults, but
    /// that only ensures compile-time compatibility - at runtime the method still has a
    /// different signature - so this way is a bit cleaner.
    /// </para>
    /// <para>
    /// Note that these are declared as structs rather than classes so that, at least when
    /// they're originally created, they can live on the stack instead of the heap. They will
    /// still eventually end up getting treated as class instances and moved to the heap, because
    /// of .NET's boxing rules: whenever a struct is treated as a dynamically-typed object (i.e.
    /// when we wrap it in an EventMessage to put it on our queue), it is boxed. But until that
    /// point, application code can freely create these and pass them around without incurring
    /// any allocations, right up until the moment when EventProcessor decides to put the event
    /// onto a queue (if it does). This is also why there's a separate EventProcessor method
    /// for each type, rather than having a single RecordEvent that takes a base type: using
    /// polymorphism in that way would cause boxing to always happen.
    /// </para>
    /// </remarks>
    public static class EventTypes
    {
        /// <summary>
        /// Used in <see cref="AliasEvent"/>.
        /// </summary>
        public enum ContextKind
        {
            User,
            AnonymousUser
        };

        public static string ToIdentifier(this ContextKind value)
        {
            switch (value)
            {
                case ContextKind.AnonymousUser:
                    return "anonymousUser";
                default:
                    return "user";
            }
        }

        /// <summary>
        /// Parameters for <see cref="EventProcessor.RecordEvaluationEvent(EvaluationEvent)"/>.
        /// Note that the "kind" string identifying this type of event in JSON data is "feature",
        /// not "evaluation".
        /// </summary>
        public struct EvaluationEvent
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

        /// <summary>
        /// Parameters for <see cref="EventProcessor.RecordIdentifyEvent(IdentifyEvent)"/>.
        /// </summary>
        public struct IdentifyEvent
        {
            public UnixMillisecondTime Timestamp { get; set; }
            public User User { get; set; }
        }

        /// <summary>
        /// Parameters for <see cref="EventProcessor.RecordCustomEvent(UnixMillisecondTime, User, string, LdValue, double?)"/>.
        /// </summary>
        public struct CustomEvent
        {
            public UnixMillisecondTime Timestamp { get; set; }
            public User User { get; set; }
            public string EventKey { get; set; }
            public LdValue Data { get; set; }
            public double? MetricValue { get; set; }
        }

        /// <summary>
        /// Parameters for <see cref="EventProcessor.RecordAliasEvent(UnixMillisecondTime, string, string)"/>.
        /// </summary>
        public struct AliasEvent
        {
            public UnixMillisecondTime Timestamp { get; set; }
            public string Key { get; set; }
            public string PreviousKey { get; set; }
            public ContextKind ContextKind { get; set; }
            public ContextKind PreviousContextKind { get; set; }
        }
    }
}
