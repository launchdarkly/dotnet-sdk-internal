using System;
using System.Collections.Generic;

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
            in LdValue defaultValue
            )
        {
            _eventsState.IncrementCounter(flagKey, variation, flagVersion, value, defaultValue);
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
        public Dictionary<EventsCounterKey, EventsCounterValue> Counters { get; } =
            new Dictionary<EventsCounterKey, EventsCounterValue>();
        public UnixMillisecondTime StartDate { get; private set; }
        public UnixMillisecondTime EndDate { get; private set; }
        public bool Empty
        {
            get  
            {
                return Counters.Count == 0;
            }
        }

        public void IncrementCounter(string key, int? variation, int? version, in LdValue flagValue, in LdValue defaultVal)
        {
            EventsCounterKey counterKey = new EventsCounterKey(key, version, variation);
            if (Counters.TryGetValue(counterKey, out EventsCounterValue value))
            {
                value.Increment();
            }
            else
            {
                Counters[counterKey] = new EventsCounterValue(1, flagValue, defaultVal);
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

    internal sealed class EventsCounterKey
    {
        public readonly string Key;
        public readonly int? Version;
        public readonly int? Variation;

        public EventsCounterKey(string key, int? version, int? variation)
        {
            Key = key;
            Version = version;
            Variation = variation;
        }

        // Required because we use this class as a dictionary key
        public override bool Equals(object obj)
        {
            if (obj is EventsCounterKey o)
            {
                return Key == o.Key && Variation == o.Variation && Version == o.Version;
            }
            return false;
        }

        // Required because we use this class as a dictionary key
        public override int GetHashCode()
        {
            return HashCodeBuilder.New().With(Key).With(Variation).With(Version).Value;
        }
    }

    internal sealed class EventsCounterValue
    {
        public int Count;
        public readonly LdValue FlagValue;
        public readonly LdValue Default;

        public EventsCounterValue(int count, in LdValue flagValue, in LdValue defaultVal)
        {
            Count = count;
            FlagValue = flagValue;
            Default = defaultVal;
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
                return Count == o.Count && Object.Equals(FlagValue, o.FlagValue) && Object.Equals(Default, o.Default);
            }
            return false;
        }

        // Used only in tests
        public override int GetHashCode()
        {
            return HashCodeBuilder.New().With(Count).With(FlagValue).With(Default).Value;
        }

        // Used only in tests
        public override string ToString()
        {
            return "{" + Count + ", " + FlagValue + ", " + Default + "}";
        }
    }
}
