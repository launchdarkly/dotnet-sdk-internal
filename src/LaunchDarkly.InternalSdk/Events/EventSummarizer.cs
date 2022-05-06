﻿using System.Collections.Generic;

namespace LaunchDarkly.Sdk.Internal.Events
{
    internal sealed class EventSummarizer
    {
        private EventSummary _eventsState;

        public EventSummarizer()
        {
            _eventsState = new EventSummary();
        }

        // Adds this event to our counters, if it is a type of event we need to count.
        public void SummarizeEvent(
            UnixMillisecondTime timestamp,
            string flagKey,
            int? flagVersion,
            int? variation,
            in LdValue value,
            in LdValue defaultValue,
            in Context context
            )
        {
            _eventsState.IncrementCounter(flagKey, variation, flagVersion, value, defaultValue, context);
            _eventsState.NoteTimestamp(timestamp);
        }

        // Returns the current summarized event data.
        public EventSummary Snapshot()
        {
            EventSummary ret = _eventsState;
            _eventsState = new EventSummary();
            return ret;
        }

        public void Clear()
        {
            _eventsState = new EventSummary();
        }
    }

    internal sealed class EventSummary
    {
        public readonly Dictionary<string, FlagSummary> Flags =
            new Dictionary<string, FlagSummary>();

        public UnixMillisecondTime StartDate { get; private set; }
        public UnixMillisecondTime EndDate { get; private set; }

        public bool Empty => Flags.Count == 0;

        public void IncrementCounter(string key, int? variation, int? version, LdValue flagValue, LdValue defaultVal, in Context context)
        {
            if (!Flags.TryGetValue(key, out var flagSummary))
            {
                flagSummary = new FlagSummary(key, defaultVal);
                Flags[key] = flagSummary;
            }

            var contextKinds = flagSummary.ContextKinds;
            if (context.Multiple)
            {
                foreach (var mc in context.MultiKindContexts)
                {
                    contextKinds.Add(mc.Kind);
                }
            }
            else
            {
                contextKinds.Add(context.Kind);
            }

            EventsCounterKey counterKey = new EventsCounterKey(version, variation);
            if (flagSummary.Counters.TryGetValue(counterKey, out EventsCounterValue value))
            {
                value.Increment();
            }
            else
            {
                flagSummary.Counters[counterKey] = new EventsCounterValue(1, flagValue);
            }
        }

        public void NoteTimestamp(UnixMillisecondTime timestamp)
        {
            if (StartDate.Value == 0 || timestamp.Value < StartDate.Value)
            {
                StartDate = timestamp;
            }
            if (timestamp.Value > EndDate.Value)
            {
                EndDate = timestamp;
            }
        }
    }

    internal sealed class FlagSummary
    {
        public readonly string Key;
        public readonly LdValue Default;
        public readonly HashSet<string> ContextKinds = new HashSet<string>();
        public readonly Dictionary<EventsCounterKey, EventsCounterValue> Counters =
            new Dictionary<EventsCounterKey, EventsCounterValue>();

        public FlagSummary(string key, LdValue defaultVal)
        {
            Key = key;
            Default = defaultVal;
        }
    }

    internal sealed class EventsCounterKey
    {
        public readonly int? Version;
        public readonly int? Variation;

        public EventsCounterKey(int? version, int? variation)
        {
            Version = version;
            Variation = variation;
        }

        // Required because we use this class as a dictionary key
        public override bool Equals(object obj)
        {
            if (obj is EventsCounterKey o)
            {
                return Variation == o.Variation && Version == o.Version;
            }
            return false;
        }

        // Required because we use this class as a dictionary key
        public override int GetHashCode() =>
            (Variation ?? -1) * 17 + (Version ?? -1);
    }

    internal sealed class EventsCounterValue
    {
        public int Count;
        public readonly LdValue FlagValue;

        public EventsCounterValue(int count, in LdValue flagValue)
        {
            Count = count;
            FlagValue = flagValue;
        }

        public void Increment()
        {
            Count++;
        }

        // Used only in tests
        public override bool Equals(object obj)
        {
            if (obj is EventsCounterValue o)
            {
                return Count == o.Count && object.Equals(FlagValue, o.FlagValue);
            }
            return false;
        }

        // Used only in tests
        public override int GetHashCode()
        {
            return HashCodeBuilder.New().With(Count).With(FlagValue).Value;
        }

        // Used only in tests
        public override string ToString()
        {
            return "{" + Count + ", " + FlagValue + "}";
        }
    }
}
