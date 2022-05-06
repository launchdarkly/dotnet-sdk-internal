using System;
using Xunit;

using static LaunchDarkly.Sdk.Internal.Events.EventProcessorInternal;
using static LaunchDarkly.Sdk.Internal.Events.EventTypes;

namespace LaunchDarkly.Sdk.Internal.Events
{
    public class EventOutputTest
    {
        private static readonly UnixMillisecondTime _fixedTimestamp = UnixMillisecondTime.OfMillis(100000);
        private static readonly Context SimpleContext = Context.Builder("userkey").Name("me").Build();
        private const string SimpleContextJson = @"{""kind"": ""user"", ""key"":""userkey"", ""name"": ""me""}";
        private const string SimpleContextKeysJson = @"{""user"": ""userkey""}";

        [Fact]
        public void ContextKeysAreSetForSingleOrMultiKindContexts()
        {
            Action<Context, string> doTest = (Context c, string keysJson) =>
            {
                var f = new EventOutputFormatter(new EventsConfiguration());

                var evalEvent = new EvaluationEvent { FlagKey = "flag", Context = c };
                var outputEvent = SerializeOneEvent(f, evalEvent);
                Assert.Equal(LdValue.Null, outputEvent.Get("context"));
                Assert.Equal(LdValue.Parse(keysJson), outputEvent.Get("contextKeys"));

                var customEvent = new CustomEvent { EventKey = "customkey", Context = c };
                outputEvent = SerializeOneEvent(f, customEvent);
                Assert.Equal(LdValue.Null, outputEvent.Get("context"));
                Assert.Equal(LdValue.Parse(keysJson), outputEvent.Get("contextKeys"));
            };

            var single = Context.NewWithKind("kind1", "key1");
            var singleJson = @"{""kind1"": ""key1""}";
            doTest(single, singleJson);

            var multi = Context.NewMulti(single, Context.NewWithKind("kind2", "key2"));
            var multiJson = @"{""kind1"": ""key1"", ""kind2"": ""key2""}";
            doTest(multi, multiJson);
        }

        [Fact]
        public void EvaluationEventIsSerialized()
        {
            Func<EvaluationEvent> MakeBasicEvent = () => new EvaluationEvent
            {
                Timestamp = _fixedTimestamp,
                FlagKey = "flag",
                FlagVersion = 11,
                Context = SimpleContext,
                Value = LdValue.Of("flagvalue"),
                Default = LdValue.Of("defaultvalue")
            };
            var fe = MakeBasicEvent();
            TestEventSerialization(fe, LdValue.Parse(@"{
                ""kind"":""feature"",
                ""creationDate"":100000,
                ""key"":""flag"",
                ""version"":11,
                ""contextKeys"":"+SimpleContextKeysJson+@",
                ""value"":""flagvalue"",
                ""default"":""defaultvalue""
                }"));

            var feWithVariation = MakeBasicEvent();
            feWithVariation.Variation = 1;
            TestEventSerialization(feWithVariation, LdValue.Parse(@"{
                ""kind"":""feature"",
                ""creationDate"":100000,
                ""key"":""flag"",
                ""version"":11,
                ""contextKeys"":" + SimpleContextKeysJson + @",
                ""value"":""flagvalue"",
                ""variation"":1,
                ""default"":""defaultvalue""
                }"));

            var feWithReason = MakeBasicEvent();
            feWithReason.Variation = 1;
            feWithReason.Reason = EvaluationReason.RuleMatchReason(1, "id");
            TestEventSerialization(feWithReason, LdValue.Parse(@"{
                ""kind"":""feature"",
                ""creationDate"":100000,
                ""key"":""flag"",
                ""version"":11,
                ""contextKeys"":" + SimpleContextKeysJson + @",
                ""value"":""flagvalue"",
                ""variation"":1,
                ""default"":""defaultvalue"",
                ""reason"":{""kind"":""RULE_MATCH"",""ruleIndex"":1,""ruleId"":""id""}
                }"));

            var feUnknownFlag = new EvaluationEvent
            {
                Timestamp = fe.Timestamp,
                FlagKey = "flag",
                Context = SimpleContext,
                Value = LdValue.Of("defaultvalue"),
                Default = LdValue.Of("defaultvalue")
            };
            TestEventSerialization(feUnknownFlag, LdValue.Parse(@"{
                ""kind"":""feature"",
                ""creationDate"":100000,
                ""key"":""flag"",
                ""contextKeys"":" + SimpleContextKeysJson + @",
                ""value"":""defaultvalue"",
                ""default"":""defaultvalue""
                }"));

            var debugEvent = new DebugEvent { FromEvent = feWithVariation };
            TestEventSerialization(debugEvent, LdValue.Parse(@"{
                ""kind"":""debug"",
                ""creationDate"":100000,
                ""key"":""flag"",
                ""version"":11,
                ""context"":"+SimpleContextJson+@",
                ""value"":""flagvalue"",
                ""variation"":1,
                ""default"":""defaultvalue""
                }"));
        }

        [Fact]
        public void IdentifyEventIsSerialized()
        {
            var user = User.Builder("userkey").Name("me").Build();
            var ie = new IdentifyEvent { Timestamp = _fixedTimestamp, Context = SimpleContext };
            TestEventSerialization(ie, LdValue.Parse(@"{
                ""kind"":""identify"",
                ""creationDate"":100000,
                ""context"":" + SimpleContextJson + @"
                }"));
        }

        [Fact]
        public void CustomEventIsSerialized()
        {
            Func<CustomEvent> MakeBasicEvent = () => new CustomEvent
            {
                Timestamp = _fixedTimestamp,
                EventKey = "customkey",
                Context = SimpleContext
            };
            var ceWithoutData = MakeBasicEvent();
            TestEventSerialization(ceWithoutData, LdValue.Parse(@"{
                ""kind"":""custom"",
                ""creationDate"":100000,
                ""key"":""customkey"",
                ""contextKeys"":" + SimpleContextKeysJson + @"
                }"));

            var ceWithData = MakeBasicEvent();
            ceWithData.Data = LdValue.Of("thing");
            TestEventSerialization(ceWithData, LdValue.Parse(@"{
                ""kind"":""custom"",
                ""creationDate"":100000,
                ""key"":""customkey"",
                ""contextKeys"":" + SimpleContextKeysJson + @",
                ""data"":""thing""
                }"));

            var ceWithMetric = MakeBasicEvent();
            ceWithMetric.MetricValue = 2.5;
            TestEventSerialization(ceWithMetric, LdValue.Parse(@"{
                ""kind"":""custom"",
                ""creationDate"":100000,
                ""key"":""customkey"",
                ""contextKeys"":" + SimpleContextKeysJson + @",
                ""metricValue"":2.5
                }"));

            var ceWithDataAndMetric = MakeBasicEvent();
            ceWithDataAndMetric.Data = ceWithData.Data;
            ceWithDataAndMetric.MetricValue = ceWithMetric.MetricValue;
            TestEventSerialization(ceWithDataAndMetric, LdValue.Parse(@"{
                ""kind"":""custom"",
                ""creationDate"":100000,
                ""key"":""customkey"",
                ""contextKeys"":" + SimpleContextKeysJson + @",
                ""data"":""thing"",
                ""metricValue"":2.5
                }"));
        }

        [Fact]
        public void SummaryEventIsSerialized()
        {
            var context1 = Context.NewWithKind("kind1", "key1");
            var context2 = Context.NewWithKind("kind2", "key2");

            var summary = new EventSummary();
            summary.NoteTimestamp(UnixMillisecondTime.OfMillis(1001));

            summary.IncrementCounter("first", 1, 11, LdValue.Of("value1a"), LdValue.Of("default1"), context1);

            summary.IncrementCounter("second", 1, 21, LdValue.Of("value2a"), LdValue.Of("default2"), context1);

            summary.IncrementCounter("first", 1, 11, LdValue.Of("value1a"), LdValue.Of("default1"), context1);
            summary.IncrementCounter("first", 1, 12, LdValue.Of("value1a"), LdValue.Of("default1"), context1);

            summary.IncrementCounter("second", 2, 21, LdValue.Of("value2b"), LdValue.Of("default2"), context2);
            summary.IncrementCounter("second", null, 21, LdValue.Of("default2"), LdValue.Of("default2"), context2); // flag exists (has version), but eval failed (no variation)

            summary.IncrementCounter("third", null, null, LdValue.Of("default3"), LdValue.Of("default3"), context2); // flag doesn't exist (no version)

            summary.NoteTimestamp(UnixMillisecondTime.OfMillis(1000));
            summary.NoteTimestamp(UnixMillisecondTime.OfMillis(1002));

            var f = new EventOutputFormatter(new EventsConfiguration());
            var outputEvent = LdValue.Parse(f.SerializeOutputEvents(new object[0], summary, out var count)).Get(0);
            Assert.Equal(1, count);

            Assert.Equal("summary", outputEvent.Get("kind").AsString);
            Assert.Equal(1000, outputEvent.Get("startDate").AsInt);
            Assert.Equal(1002, outputEvent.Get("endDate").AsInt);

            var featuresJson = outputEvent.Get("features");
            Assert.Equal(3, featuresJson.Count);

            var firstJson = featuresJson.Get("first");
            Assert.Equal("default1", firstJson.Get("default").AsString);
            Assert.Equal(LdValue.ArrayOf(LdValue.Of("kind1")), firstJson.Get("contextKinds")); // we evaluated this flag with only context1
            TestUtil.AssertContainsInAnyOrder(firstJson.Get("counters").List,
                LdValue.Parse(@"{""value"":""value1a"",""variation"":1,""version"":11,""count"":2}"),
                LdValue.Parse(@"{""value"":""value1a"",""variation"":1,""version"":12,""count"":1}"));

            var secondJson = featuresJson.Get("second");
            Assert.Equal("default2", secondJson.Get("default").AsString);
            TestUtil.AssertContainsInAnyOrder(secondJson.Get("contextKinds").List,
                LdValue.Of("kind1"), LdValue.Of("kind2")); // we evaluated this flag with both context1 and context2
            TestUtil.AssertContainsInAnyOrder(secondJson.Get("counters").List,
                LdValue.Parse(@"{""value"":""value2a"",""variation"":1,""version"":21,""count"":1}"),
                LdValue.Parse(@"{""value"":""value2b"",""variation"":2,""version"":21,""count"":1}"),
                LdValue.Parse(@"{""value"":""default2"",""version"":21,""count"":1}"));

            var thirdJson = featuresJson.Get("third");
            Assert.Equal("default3", thirdJson.Get("default").AsString);
            Assert.Equal(LdValue.ArrayOf(LdValue.Of("kind2")), thirdJson.Get("contextKinds")); // we evaluated this flag with only context2
            TestUtil.AssertContainsInAnyOrder(thirdJson.Get("counters").AsList(LdValue.Convert.Json),
                LdValue.Parse(@"{""unknown"":true,""value"":""default3"",""count"":1}"));
        }

        private LdValue SerializeOneEvent(EventOutputFormatter f, object e)
        {
            var emptySummary = new EventSummary();
            var json = f.SerializeOutputEvents(new object[] { e }, emptySummary, out var count);
            try
            {
                var outputEvent = LdValue.Parse(json).Get(0);
                Assert.Equal(1, count);
                return outputEvent;
            }
            catch (Exception ex)
            {
                throw new Exception(string.Format("received invalid JSON ({0}): {1}", ex.Message, json));
            }
        }

        private void TestEventSerialization(object e, LdValue expectedJsonValue)
        {
            var f = new EventOutputFormatter(new EventsConfiguration());
            var outputEvent = SerializeOneEvent(f, e);
            Assert.Equal(expectedJsonValue, outputEvent);
        }
    }
}
