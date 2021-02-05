using System;

namespace LaunchDarkly.Sdk.Internal.Events
{
    public struct TestFlagProperties
    {
        public string Key;
        public int Version;
        public bool TrackEvents;
        public UnixMillisecondTime? DebugEventsUntilDate;
    }

    public struct TestEvalProperties
    {
        public UnixMillisecondTime Timestamp;
        public User User;
        public int? Variation;
        public LdValue Value;
        public LdValue DefaultValue;
        public EvaluationReason? Reason;
        public string PrereqOf;
    }

    public struct TestCustomEventProperties
    {
        public UnixMillisecondTime Timestamp;
        public string Key;
        public User User;
        public LdValue Data;
        public double? MetricValue;
    }
}
