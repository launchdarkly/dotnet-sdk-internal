﻿using System;
using LaunchDarkly.Sdk.Interfaces;
using LaunchDarkly.Sdk.Internal.Helpers;

namespace LaunchDarkly.Sdk.Internal.Events
{
    /// <summary>
    /// Shared logic for generating analytics events. Note that these are the "input" events that
    /// get fed into the EventProcessor, not the "output" events that are actually sent to
    /// LaunchDarkly.
    /// </summary>
    public struct EventFactory
    {
        // These two instances are the only ones we'll need in production use. The only difference
        // between them is that the "WithReasons" one always includes the evaluation reason in the
        // event, and the other one doesn't (except in the "experiment" case described in
        // IFlagEventProperties.IsExperiment).
        public static EventFactory Default { get; } = new EventFactory(CurrentTime, false);
        public static EventFactory DefaultWithReasons { get; } = new EventFactory(CurrentTime, true);

        public readonly Func<long> GetTimestamp;
        public bool IncludeReasons { get; }

        public EventFactory(Func<long> getTimestamp, bool includeReasons)
        {
            GetTimestamp = getTimestamp;
            IncludeReasons = includeReasons;
        }

        private static long CurrentTime() => Util.GetUnixTimestampMillis(DateTime.UtcNow);

        /// <summary>
        /// Creates a feature request event for a successful evaluation.
        /// </summary>
        /// <param name="flag">abstraction of the basic flag properties we need</param>
        /// <param name="user">the user passed to the Variation method</param>
        /// <param name="result">the evaluation result</param>
        /// <param name="defaultVal">the default value passed to the Variation method</param>
        /// <returns>an event</returns>
        public FeatureRequestEvent NewFeatureRequestEvent(IFlagEventProperties flag, User user,
            EvaluationDetail<LdValue> result, LdValue defaultVal)
        {
            bool experiment = flag.IsExperiment(result.Reason);
            return new FeatureRequestEvent(GetTimestamp(), flag.Key, user, result.VariationIndex, result.Value, defaultVal,
                flag.EventVersion, null, experiment || flag.TrackEvents, flag.DebugEventsUntilDate, false,
                (experiment || IncludeReasons) ? (EvaluationReason?)result.Reason : null);
        }

        /// <summary>
        /// Creates a feature request event for an evaluation that returned the default value
        /// (i.e. an error), even though the flag existed.
        /// </summary>
        /// <param name="flag">abstraction of the basic flag properties we need</param>
        /// <param name="user">the user passed to the Variation method</param>
        /// <param name="defaultVal">the default value passed to the Variation method</param>
        /// <param name="errorKind">the type of error</param>
        /// <returns>an event</returns>
        public FeatureRequestEvent NewDefaultFeatureRequestEvent(IFlagEventProperties flag, User user,
            LdValue defaultVal, EvaluationErrorKind errorKind)
        {
            return new FeatureRequestEvent(GetTimestamp(), flag.Key, user, null, defaultVal, defaultVal,
                flag.EventVersion, null, flag.TrackEvents, flag.DebugEventsUntilDate, false,
                IncludeReasons ? (EvaluationReason?)EvaluationReason.ErrorReason(errorKind) : null);
        }

        /// <summary>
        /// Creates a feature request event for an evaluation that returned the default value
        /// when the flag did not exist or the feature store was unavailable.
        /// </summary>
        /// <param name="key">the flag key that was requested</param>
        /// <param name="user">the user passed to the Variation method</param>
        /// <param name="defaultVal">the default value passed to the Variation method</param>
        /// <param name="errorKind">the type of error</param>
        /// <returns>an event</returns>
        public FeatureRequestEvent NewUnknownFeatureRequestEvent(string key, User user,
            LdValue defaultVal, EvaluationErrorKind errorKind)
        {
            return new FeatureRequestEvent(GetTimestamp(), key, user, null, defaultVal, defaultVal,
                null, null, false, null, false,
                IncludeReasons ? (EvaluationReason?)EvaluationReason.ErrorReason(errorKind) : null);
        }

        /// <summary>
        /// Creates a feature request event for a successful evaluation of a prerequisite flag.
        /// </summary>
        /// <param name="prereqFlag">the prerequisite flag</param>
        /// <param name="user">the user passed to the Variation method</param>
        /// <param name="result">the evaluation result</param>
        /// <param name="prereqOf">the flag that used this flag as a prerequisite</param>
        /// <returns>an event</returns>
        public FeatureRequestEvent NewPrerequisiteFeatureRequestEvent(IFlagEventProperties prereqFlag, User user,
            EvaluationDetail<LdValue> result, IFlagEventProperties prereqOf)
        {
            bool experiment = prereqFlag.IsExperiment(result.Reason);
            return new FeatureRequestEvent(GetTimestamp(), prereqFlag.Key, user, result.VariationIndex, result.Value, LdValue.Null,
                prereqFlag.EventVersion, prereqOf.Key, experiment || prereqFlag.TrackEvents, prereqFlag.DebugEventsUntilDate, false,
                (experiment || IncludeReasons) ? (EvaluationReason?)result.Reason : null);
        }

        /// <summary>
        /// Creates a "debug" version of an existing feature request event.
        /// </summary>
        /// <param name="from">the existing event</param>
        /// <returns>an equivalent debug event</returns>
        public FeatureRequestEvent NewDebugEvent(FeatureRequestEvent from)
        {
            return new FeatureRequestEvent(from.CreationDate, from.Key, from.User, from.Variation, from.Value, from.Default,
                from.Version, from.PrereqOf, from.TrackEvents, from.DebugEventsUntilDate, true, from.Reason);
        }

        /// <summary>
        /// Creates a custom event (from the Track method).
        /// </summary>
        /// <param name="key">the event name</param>
        /// <param name="user">the user</param>
        /// <param name="data">optional event data, may be null</param>
        /// <param name="metricValue">optional numeric value for analytics</param>
        /// <returns>an event</returns>
        public CustomEvent NewCustomEvent(string key, User user, LdValue data, double? metricValue = null)
        {
            return new CustomEvent(GetTimestamp(), key, user, data, metricValue);
        }

        /// <summary>
        /// Creates an identify event.
        /// </summary>
        /// <param name="user">the user</param>
        /// <returns>an event</returns>
        public IdentifyEvent NewIdentifyEvent(User user)
        {
            return new IdentifyEvent(GetTimestamp(), user);
        }
    }
}
