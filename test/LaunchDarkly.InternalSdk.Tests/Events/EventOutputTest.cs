using System;
using System.Collections.Immutable;
using Xunit;

using static LaunchDarkly.Sdk.Internal.Events.EventProcessorInternal;
using static LaunchDarkly.Sdk.Internal.Events.EventTypes;

namespace LaunchDarkly.Sdk.Internal.Events
{
    public class EventOutputTest
    {
        private static readonly UnixMillisecondTime _fixedTimestamp = UnixMillisecondTime.OfMillis(100000);

        [Fact]
        public void AllUserAttributesAreSerialized()
        {
            var f = new EventOutputFormatter(new EventsConfiguration());
            var user = User.Builder("userkey")
                .Anonymous(true)
                .Avatar("http://avatar")
                .Country("US")
                .Custom("custom1", "value1")
                .Custom("custom2", "value2")
                .Email("test@example.com")
                .FirstName("first")
                .IPAddress("1.2.3.4")
                .LastName("last")
                .Name("me")
                .Secondary("s")
                .Build();
            var userJson = LdValue.Parse(@"{
                ""key"":""userkey"",
                ""anonymous"":true,
                ""avatar"":""http://avatar"",
                ""country"":""US"",
                ""custom"":{""custom1"":""value1"",""custom2"":""value2""},
                ""email"":""test@example.com"",
                ""firstName"":""first"",
                ""ip"":""1.2.3.4"",
                ""lastName"":""last"",
                ""name"":""me"",
                ""secondary"":""s""
                }");
            TestInlineUserSerialization(user, userJson, new EventsConfiguration());
        }

        [Fact]
        public void UnsetUserAttributesAreNotSerialized()
        {
            var f = new EventOutputFormatter(new EventsConfiguration());
            var user = User.Builder("userkey")
                .Build();
            var userJson = LdValue.Parse(@"{
                ""key"":""userkey""
                }");
            TestInlineUserSerialization(user, userJson, new EventsConfiguration());
        }

        [Fact]
        public void AllAttributesPrivateMakesAttributesPrivate()
        {
            var f = new EventOutputFormatter(new EventsConfiguration());
            var user = User.Builder("userkey")
                .Anonymous(true)
                .Avatar("http://avatar")
                .Country("US")
                .Custom("custom1", "value1")
                .Custom("custom2", "value2")
                .Email("test@example.com")
                .FirstName("first")
                .IPAddress("1.2.3.4")
                .LastName("last")
                .Name("me")
                .Secondary("s")
                .Build();
            var userJson = LdValue.Parse(@"{
                ""key"":""userkey"",
                ""anonymous"":true,
                ""privateAttrs"":[
                    ""avatar"", ""country"", ""custom1"", ""custom2"", ""email"",
                    ""firstName"", ""ip"", ""lastName"", ""name"", ""secondary""
                ]
                }");
            var config = new EventsConfiguration() { AllAttributesPrivate = true };
            TestInlineUserSerialization(user, userJson, config);
        }
        
        [Fact]
        public void GlobalPrivateAttributeNamesMakeAttributesPrivate()
        {
            TestPrivateAttribute("avatar", true);
            TestPrivateAttribute("country", true);
            TestPrivateAttribute("custom1", true);
            TestPrivateAttribute("custom2", true);
            TestPrivateAttribute("email", true);
            TestPrivateAttribute("firstName", true);
            TestPrivateAttribute("ip", true);
            TestPrivateAttribute("lastName", true);
            TestPrivateAttribute("name", true);
        }
        
        [Fact]
        public void PerUserPrivateAttributesMakeAttributesPrivate()
        {
            TestPrivateAttribute("avatar", false);
            TestPrivateAttribute("country", false);
            TestPrivateAttribute("custom1", false);
            TestPrivateAttribute("custom2", false);
            TestPrivateAttribute("email", false);
            TestPrivateAttribute("firstName", false);
            TestPrivateAttribute("ip", false);
            TestPrivateAttribute("lastName", false);
            TestPrivateAttribute("name", false);
        }

        [Fact]
        public void UserKeyIsSetInsteadOfUserWhenNotInlined()
        {
            var user = User.Builder("userkey")
                .Name("me")
                .Build();
            var userJson = LdValue.Parse(@"{""key"":""userkey"",""name"":""me""}");
            var f = new EventOutputFormatter(new EventsConfiguration());
            var emptySummary = new EventSummary();

            var featureEvent = new EvaluationEvent { FlagKey = "flag", User = user };
            var outputEvent = LdValue.Parse(f.SerializeOutputEvents(new object[] { featureEvent }, emptySummary, out var count)).Get(0);
            Assert.Equal(1, count);
            Assert.Equal(LdValue.Null, outputEvent.Get("user"));
            Assert.Equal(user.Key, outputEvent.Get("userKey").AsString);

            // user is always inlined in identify event
            var identifyEvent = new IdentifyEvent { User = user };
            outputEvent = LdValue.Parse(f.SerializeOutputEvents(new object[] { identifyEvent }, emptySummary, out count)).Get(0);
            Assert.Equal(1, count);
            Assert.Equal(LdValue.Null, outputEvent.Get("userKey"));
            Assert.Equal(userJson, outputEvent.Get("user"));

            var customEvent = new CustomEvent { EventKey = "customkey", User = user };
            outputEvent = LdValue.Parse(f.SerializeOutputEvents(new object[] { customEvent }, emptySummary, out count)).Get(0);
            Assert.Equal(1, count);
            Assert.Equal(LdValue.Null, outputEvent.Get("user"));
            Assert.Equal(user.Key, outputEvent.Get("userKey").AsString);

            // user is always inlined in index event
            var indexEvent = new IndexEvent { Timestamp = _fixedTimestamp, User = user };
            outputEvent = LdValue.Parse(f.SerializeOutputEvents(new object[] { indexEvent }, emptySummary, out count)).Get(0);
            Assert.Equal(1, count);
            Assert.Equal(LdValue.Null, outputEvent.Get("userKey"));
            Assert.Equal(userJson, outputEvent.Get("user"));
        }

        [Fact]
        public void FeatureEventIsSerialized()
        {
            Func<EvaluationEvent> MakeBasicEvent = () => new EvaluationEvent
            {
                Timestamp = _fixedTimestamp,
                FlagKey = "flag",
                FlagVersion = 11,
                User = User.Builder("userkey").Name("me").Build(),
                Value = LdValue.Of("flagvalue"),
                Default = LdValue.Of("defaultvalue")
            };
            var fe = MakeBasicEvent();
            TestEventSerialization(fe, LdValue.Parse(@"{
                ""kind"":""feature"",
                ""creationDate"":100000,
                ""key"":""flag"",
                ""version"":11,
                ""userKey"":""userkey"",
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
                ""userKey"":""userkey"",
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
                ""userKey"":""userkey"",
                ""value"":""flagvalue"",
                ""variation"":1,
                ""default"":""defaultvalue"",
                ""reason"":{""kind"":""RULE_MATCH"",""ruleIndex"":1,""ruleId"":""id""}
                }"));

            var feUnknownFlag = new EvaluationEvent
            {
                Timestamp = fe.Timestamp,
                FlagKey = "flag",
                User = fe.User,
                Value = LdValue.Of("defaultvalue"),
                Default = LdValue.Of("defaultvalue")
            };
            TestEventSerialization(feUnknownFlag, LdValue.Parse(@"{
                ""kind"":""feature"",
                ""creationDate"":100000,
                ""key"":""flag"",
                ""userKey"":""userkey"",
                ""value"":""defaultvalue"",
                ""default"":""defaultvalue""
                }"));

            var debugEvent = new DebugEvent { FromEvent = feWithVariation };
            TestEventSerialization(debugEvent, LdValue.Parse(@"{
                ""kind"":""debug"",
                ""creationDate"":100000,
                ""key"":""flag"",
                ""version"":11,
                ""user"":{""key"":""userkey"",""name"":""me""},
                ""value"":""flagvalue"",
                ""variation"":1,
                ""default"":""defaultvalue""
                }"));
        }

        [Fact]
        public void IdentifyEventIsSerialized()
        {
            var user = User.Builder("userkey").Name("me").Build();
            var ie = new IdentifyEvent { Timestamp = _fixedTimestamp, User = user };
            TestEventSerialization(ie, LdValue.Parse(@"{
                ""kind"":""identify"",
                ""creationDate"":100000,
                ""key"":""userkey"",
                ""user"":{""key"":""userkey"",""name"":""me""}
                }"));
        }

        [Fact]
        public void CustomEventIsSerialized()
        {
            var user = User.Builder("userkey").Name("me").Build();
            Func<CustomEvent> MakeBasicEvent = () => new CustomEvent
            {
                Timestamp = _fixedTimestamp,
                EventKey = "customkey",
                User = user
            };
            var ceWithoutData = MakeBasicEvent();
            TestEventSerialization(ceWithoutData, LdValue.Parse(@"{
                ""kind"":""custom"",
                ""creationDate"":100000,
                ""key"":""customkey"",
                ""userKey"":""userkey""
                }"));

            var ceWithData = MakeBasicEvent();
            ceWithData.Data = LdValue.Of("thing");
            TestEventSerialization(ceWithData, LdValue.Parse(@"{
                ""kind"":""custom"",
                ""creationDate"":100000,
                ""key"":""customkey"",
                ""userKey"":""userkey"",
                ""data"":""thing""
                }"));

            var ceWithMetric = MakeBasicEvent();
            ceWithMetric.MetricValue = 2.5;
            TestEventSerialization(ceWithMetric, LdValue.Parse(@"{
                ""kind"":""custom"",
                ""creationDate"":100000,
                ""key"":""customkey"",
                ""userKey"":""userkey"",
                ""metricValue"":2.5
                }"));

            var ceWithDataAndMetric = MakeBasicEvent();
            ceWithDataAndMetric.Data = ceWithData.Data;
            ceWithDataAndMetric.MetricValue = ceWithMetric.MetricValue;
            TestEventSerialization(ceWithDataAndMetric, LdValue.Parse(@"{
                ""kind"":""custom"",
                ""creationDate"":100000,
                ""key"":""customkey"",
                ""userKey"":""userkey"",
                ""data"":""thing"",
                ""metricValue"":2.5
                }"));
        }

        [Fact]
        public void SummaryEventIsSerialized()
        {
            var summary = new EventSummary();
            summary.NoteTimestamp(UnixMillisecondTime.OfMillis(1001));

            summary.IncrementCounter("first", 1, 11, LdValue.Of("value1a"), LdValue.Of("default1"));

            summary.IncrementCounter("second", 1, 21, LdValue.Of("value2a"), LdValue.Of("default2"));

            summary.IncrementCounter("first", 1, 11, LdValue.Of("value1a"), LdValue.Of("default1"));
            summary.IncrementCounter("first", 1, 12, LdValue.Of("value1a"), LdValue.Of("default1"));

            summary.IncrementCounter("second", 2, 21, LdValue.Of("value2b"), LdValue.Of("default2"));
            summary.IncrementCounter("second", null, 21, LdValue.Of("default2"), LdValue.Of("default2")); // flag exists (has version), but eval failed (no variation)

            summary.IncrementCounter("third", null, null, LdValue.Of("default3"), LdValue.Of("default3")); // flag doesn't exist (no version)

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
            TestUtil.AssertContainsInAnyOrder(firstJson.Get("counters").AsList(LdValue.Convert.Json),
                LdValue.Parse(@"{""value"":""value1a"",""variation"":1,""version"":11,""count"":2}"),
                LdValue.Parse(@"{""value"":""value1a"",""variation"":1,""version"":12,""count"":1}"));

            var secondJson = featuresJson.Get("second");
            Assert.Equal("default2", secondJson.Get("default").AsString);
            TestUtil.AssertContainsInAnyOrder(secondJson.Get("counters").AsList(LdValue.Convert.Json),
                LdValue.Parse(@"{""value"":""value2a"",""variation"":1,""version"":21,""count"":1}"),
                LdValue.Parse(@"{""value"":""value2b"",""variation"":2,""version"":21,""count"":1}"),
                LdValue.Parse(@"{""value"":""default2"",""version"":21,""count"":1}"));

            var thirdJson = featuresJson.Get("third");
            Assert.Equal("default3", thirdJson.Get("default").AsString);
            TestUtil.AssertContainsInAnyOrder(thirdJson.Get("counters").AsList(LdValue.Convert.Json),
                LdValue.Parse(@"{""unknown"":true,""value"":""default3"",""count"":1}"));
        }

        private void TestInlineUserSerialization(User user, LdValue expectedJsonValue, EventsConfiguration config)
        {
            config.InlineUsersInEvents = true;
            var f = new EventOutputFormatter(config);
            var emptySummary = new EventSummary();

            var featureEvent = new EvaluationEvent { FlagKey = "flag", User = user };
            var outputEvent = LdValue.Parse(f.SerializeOutputEvents(new object[] { featureEvent }, emptySummary, out var count)).Get(0);
            Assert.Equal(1, count);
            Assert.Equal(LdValue.Null, outputEvent.Get("userKey"));
            Assert.Equal(expectedJsonValue, outputEvent.Get("user"));

            var identifyEvent = new IdentifyEvent { User = user };
            outputEvent = LdValue.Parse(f.SerializeOutputEvents(new object[] { identifyEvent }, emptySummary, out count)).Get(0);
            Assert.Equal(1, count);
            Assert.Equal(LdValue.Null, outputEvent.Get("userKey"));
            Assert.Equal(expectedJsonValue, outputEvent.Get("user"));

            var customEvent = new CustomEvent { EventKey = "customkey", User = user };
            outputEvent = LdValue.Parse(f.SerializeOutputEvents(new object[] { customEvent }, emptySummary, out count)).Get(0);
            Assert.Equal(1, count);
            Assert.Equal(LdValue.Null, outputEvent.Get("userKey"));
            Assert.Equal(expectedJsonValue, outputEvent.Get("user"));

            var indexEvent = new IndexEvent { User = user };
            outputEvent = LdValue.Parse(f.SerializeOutputEvents(new object[] { indexEvent }, emptySummary, out count)).Get(0);
            Assert.Equal(1, count);
            Assert.Equal(LdValue.Null, outputEvent.Get("userKey"));
            Assert.Equal(expectedJsonValue, outputEvent.Get("user"));
        }

        private void TestPrivateAttribute(string privateAttrName, bool globallyPrivate)
        {
            var builder = User.Builder("userkey")
                .Anonymous(true)
                .Secondary("s");
            var topJsonBuilder = LdValue.BuildObject()
                .Add("key", "userkey")
                .Add("anonymous", true)
                .Add("secondary", "s");
            var customJsonBuilder = LdValue.BuildObject();
            Action<string, Func<string, IUserBuilderCanMakeAttributePrivate>, string, LdValue.ObjectBuilder> setAttr =
                (attrName, setter, value, jsonBuilder) =>
                {
                    if (attrName == privateAttrName)
                    {
                        if (globallyPrivate)
                        {
                            setter(value);
                        }
                        else
                        {
                            setter(value).AsPrivateAttribute();
                        }
                    }
                    else
                    {
                        setter(value);
                        jsonBuilder.Add(attrName, value);
                    }
                };
            setAttr("avatar", builder.Avatar, "http://avatar", topJsonBuilder);
            setAttr("country", builder.Country, "US", topJsonBuilder);
            setAttr("custom1", v => builder.Custom("custom1", v), "value1", customJsonBuilder);
            setAttr("custom2", v => builder.Custom("custom2", v), "value2", customJsonBuilder);
            setAttr("email", builder.Email, "test@example.com", topJsonBuilder);
            setAttr("firstName", builder.FirstName, "first", topJsonBuilder);
            setAttr("ip", builder.IPAddress, "1.2.3.4", topJsonBuilder);
            setAttr("lastName", builder.LastName, "last", topJsonBuilder);
            setAttr("name", builder.Name, "me", topJsonBuilder);

            topJsonBuilder.Add("custom", customJsonBuilder.Build());
            topJsonBuilder.Add("privateAttrs", LdValue.ArrayOf(LdValue.Of(privateAttrName)));
            var userJson = topJsonBuilder.Build();
            var config = new EventsConfiguration();
            if (globallyPrivate)
            {
                config.PrivateAttributeNames = ImmutableHashSet.Create<UserAttribute>(
                    UserAttribute.ForName(privateAttrName));
            };

            TestInlineUserSerialization(builder.Build(), userJson, config);
        }

        private void TestEventSerialization(object e, LdValue expectedJsonValue)
        {
            var f = new EventOutputFormatter(new EventsConfiguration());
            var emptySummary = new EventSummary();

            var outputEvent = LdValue.Parse(f.SerializeOutputEvents(new object[] { e }, emptySummary, out var count)).Get(0);
            Assert.Equal(1, count);
            Assert.Equal(expectedJsonValue, outputEvent);
        }
    }
}
