using System.Collections.Generic;
using Xunit;

namespace LaunchDarkly.Sdk.Internal.Events
{
    public class EventSummarizerTest
    {
        private static readonly User _user = User.WithKey("key");

        [Fact]
        public void SummarizeEventSetsStartAndEndDates()
        {
            var time1 = UnixMillisecondTime.OfMillis(1000);
            var time2 = UnixMillisecondTime.OfMillis(2000);
            var time3 = UnixMillisecondTime.OfMillis(3000);
            EventSummarizer es = new EventSummarizer();
            es.SummarizeEvent(time2, "flag", null, null, LdValue.Null, LdValue.Null);
            es.SummarizeEvent(time1, "flag", null, null, LdValue.Null, LdValue.Null);
            es.SummarizeEvent(time3, "flag", null, null, LdValue.Null, LdValue.Null);
            EventSummary data = es.Snapshot();

            Assert.Equal(time1, data.StartDate);
            Assert.Equal(time3, data.EndDate);
        }

        [Fact]
        public void SummarizeEventIncrementsCounters()
        {
            var time = UnixMillisecondTime.OfMillis(1000);
            string flag1Key = "flag1", flag2Key = "flag2", unknownFlagKey = "badkey";
            int flag1Version = 100, flag2Version = 200;
            int variation1 = 1, variation2 = 2;
            LdValue value1 = LdValue.Of("value1"), value2 = LdValue.Of("value2"),
                value99 = LdValue.Of("value99"),
                default1 = LdValue.Of("default1"), default2 = LdValue.Of("default2"),
                default3 = LdValue.Of("default3");
            EventSummarizer es = new EventSummarizer();
            es.SummarizeEvent(time, flag1Key, flag1Version, variation1, value1, default1);
            es.SummarizeEvent(time, flag1Key, flag1Version, variation2, value2, default1);
            es.SummarizeEvent(time, flag2Key, flag2Version, variation1, value99, default2);
            es.SummarizeEvent(time, flag1Key, flag1Version, variation1, value1, default1);
            es.SummarizeEvent(time, unknownFlagKey, null, null, default3, default3);
            EventSummary data = es.Snapshot();

            Dictionary<EventsCounterKey, EventsCounterValue> expected = new Dictionary<EventsCounterKey, EventsCounterValue>();
            Assert.Equal(new EventsCounterValue(2, value1, default1),
                data.Counters[new EventsCounterKey(flag1Key, flag1Version, variation1)]);
            Assert.Equal(new EventsCounterValue(1, value2, default1),
                data.Counters[new EventsCounterKey(flag1Key, flag1Version, variation2)]);
            Assert.Equal(new EventsCounterValue(1, value99, default2),
                data.Counters[new EventsCounterKey(flag2Key, flag2Version, variation1)]);
            Assert.Equal(new EventsCounterValue(1, default3, default3),
                data.Counters[new EventsCounterKey(unknownFlagKey, null, null)]);
        }
    }
}
