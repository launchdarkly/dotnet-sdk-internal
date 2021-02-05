using System.Collections.Generic;
using System.Linq;
using LaunchDarkly.JsonStream;

using static LaunchDarkly.Sdk.Internal.Events.EventTypes;
using static LaunchDarkly.Sdk.Json.LdJsonConverters;

namespace LaunchDarkly.Sdk.Internal.Events
{
    internal sealed class EventOutputFormatter
    {
        private readonly EventsConfiguration _config;

        public EventOutputFormatter(EventsConfiguration config)
        {
            _config = config;
        }

        public string SerializeOutputEvents(object[] events, EventSummary summary, out int eventCountOut)
        {
            var jsonWriter = JWriter.New();
            var scope = new EventOutputFormatterScope(_config, jsonWriter, _config.InlineUsersInEvents);
            eventCountOut = scope.WriteOutputEvents(events, summary);
            return jsonWriter.GetString();
        }
    }

    internal struct EventOutputFormatterScope
    {
        private readonly EventsConfiguration _config;
        private readonly JWriter _jsonWriter;
        private ObjectWriter _obj;

        private struct MutableKeyValuePair<A, B>
        {
            public A Key { get; set; }
            public B Value { get; set; }

            public static MutableKeyValuePair<A, B> FromKeyValue(KeyValuePair<A, B> kv) =>
                new MutableKeyValuePair<A, B> { Key = kv.Key, Value = kv.Value };
        }

        public EventOutputFormatterScope(EventsConfiguration config, JWriter jw, bool inlineUsers)
        {
            _config = config;
            _jsonWriter = jw;
            _obj = new ObjectWriter();
        }

        public int WriteOutputEvents(object[] events, EventSummary summary)
        {
            var eventCount = events.Length;
            var arr = _jsonWriter.Array();
            foreach (var e in events)
            {
                WriteOutputEvent(e);
            }
            if (summary.Counters.Count > 0)
            {
                WriteSummaryEvent(summary);
                eventCount++;
            }
            arr.End();
            return eventCount;
        }

        public void WriteOutputEvent(object e)
        {
            _obj = _jsonWriter.Object();
            switch (e)
            {
                case EvaluationEvent ee:
                    WriteEvaluationEvent(ee, false);
                    break;
                case IdentifyEvent ie:
                    WriteBase("identify", ie.Timestamp, ie.User?.Key);
                    WriteUser(ie.User);
                    break;
                case CustomEvent ce:
                    WriteBase("custom", ce.Timestamp, ce.EventKey);
                    WriteUserOrKey(ce.User, false);
                    if (!ce.Data.IsNull)
                    {
                        LdValueConverter.WriteJsonValue(ce.Data, _obj.Name("data"));
                    }
                    if (ce.MetricValue.HasValue)
                    {
                        _obj.Name("metricValue").Double(ce.MetricValue.Value);
                    }
                    break;
                case EventProcessorInternal.IndexEvent ie:
                    WriteBase("index", ie.Timestamp, null);
                    WriteUserOrKey(ie.User, true);
                    break;
                case EventProcessorInternal.DebugEvent de:
                    WriteEvaluationEvent(de.FromEvent, true);
                    break;
                default:
                    break;
            }
            _obj.End();
        }

        private void WriteEvaluationEvent(EvaluationEvent ee, bool debug)
        {
            WriteBase(debug ? "debug" : "feature", ee.Timestamp, ee.FlagKey);

            WriteUserOrKey(ee.User, debug);
            if (ee.FlagVersion.HasValue)
            {
                _obj.Name("version").Int(ee.FlagVersion.Value);
            }
            if (ee.Variation.HasValue)
            {
                _obj.Name("variation").Int(ee.Variation.Value);
            }
            LdValueConverter.WriteJsonValue(ee.Value, _obj.Name("value"));
            if (!ee.Default.IsNull)
            {
                LdValueConverter.WriteJsonValue(ee.Default, _obj.Name("default"));
            }
            _obj.MaybeName("prereqOf", ee.PrereqOf != null).String(ee.PrereqOf);
            WriteReason(ee.Reason);
        }

        public void WriteSummaryEvent(EventSummary summary)
        {
            var obj = _jsonWriter.Object();

            obj.Name("kind").String("summary");
            obj.Name("startDate").Long(summary.StartDate.Value);
            obj.Name("endDate").Long(summary.EndDate.Value);

            var flagsObj = obj.Name("features").Object();

            var unprocessedCounters = summary.Counters.Select(kv => MutableKeyValuePair<EventsCounterKey, EventsCounterValue>.FromKeyValue(kv)).ToArray();
            for (var i = 0; i < unprocessedCounters.Length; i++)
            {
                var firstEntry = unprocessedCounters[i];
                if (firstEntry.Value is null)
                { // already processed
                    continue;
                }
                var flagKey = firstEntry.Key.Key;
                var flagDefault = firstEntry.Value.Default;

                var flagObj = flagsObj.Name(flagKey).Object();
                LdValueConverter.WriteJsonValue(flagDefault, flagObj.Name("default"));
                var countersArr = flagObj.Name("counters").Array();

                for (var j = i; j < unprocessedCounters.Length; j++)
                {
                    var entry = unprocessedCounters[j];
                    var key = entry.Key;
                    if (key.Key == flagKey && entry.Value != null)
                    {
                        var counter = entry.Value;
                        unprocessedCounters[j].Value = null; // mark as already processed

                        var counterObj = countersArr.Object();
                        if (key.Variation.HasValue)
                        {
                            counterObj.Name("variation").Int(key.Variation.Value);
                        }
                        LdValueConverter.WriteJsonValue(counter.FlagValue, counterObj.Name("value"));
                        if (key.Version.HasValue)
                        {
                            counterObj.Name("version").Int(key.Version.Value);
                        }
                        else
                        {
                            counterObj.Name("unknown").Bool(true);
                        }
                        counterObj.Name("count").Int(counter.Count);
                        counterObj.End();
                    }
                }

                countersArr.End();
                flagObj.End();
            }

            flagsObj.End();
            obj.End();
        }

        private void WriteBase(string kind, UnixMillisecondTime creationDate, string key)
        {
            _obj.Name("kind").String(kind);
            _obj.Name("creationDate").Long(creationDate.Value);
            _obj.MaybeName("key", key != null).String(key);
        }

        private void WriteUserOrKey(User user, bool forceInline)
        {
            if (forceInline || _config.InlineUsersInEvents)
            {
                WriteUser(user);
            }
            else if (user != null)
            {
                _obj.Name("userKey").String(user.Key);
            }
        }

        private void WriteUser(User user)
        {
            if (user is null)
            {
                return;
            }
            var eu = EventUser.FromUser(user, _config);

            var userObj = _obj.Name("user").Object();
            userObj.Name("key").String(eu.Key);
            userObj.MaybeName("secondary", eu.Secondary != null).String(eu.Secondary);
            userObj.MaybeName("ip", eu.IPAddress != null).String(eu.IPAddress);
            userObj.MaybeName("country", eu.Country != null).String(eu.Country);
            userObj.MaybeName("firstName", eu.FirstName != null).String(eu.FirstName);
            userObj.MaybeName("lastName", eu.LastName != null).String(eu.LastName);
            userObj.MaybeName("name", eu.Name != null).String(eu.Name);
            userObj.MaybeName("avatar", eu.Avatar != null).String(eu.Avatar);
            userObj.MaybeName("email", eu.Email != null).String(eu.Email);
            if (eu.Anonymous.HasValue)
            {
                userObj.Name("anonymous").Bool(eu.Anonymous.Value);
            }
            if (eu.Custom != null && eu.Custom.Count > 0)
            {
                var customObj = userObj.Name("custom").Object();
                foreach (var kv in eu.Custom)
                {
                    LdValueConverter.WriteJsonValue(kv.Value, customObj.Name(kv.Key));
                }
                customObj.End();
            }
            if (eu.PrivateAttrs != null)
            {
                var arr = userObj.Name("privateAttrs").Array();
                foreach (var a in eu.PrivateAttrs)
                {
                    arr.String(a);
                }
                arr.End();
            }
            userObj.End();
        }

        public void WriteReason(EvaluationReason? reason)
        {
            if (reason.HasValue)
            {
                EvaluationReasonConverter.WriteJsonValue(reason.Value, _obj.Name("reason"));
            }
        }
    }
}
