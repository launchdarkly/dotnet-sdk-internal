using System.Collections.Generic;
using System.Linq;
using LaunchDarkly.JsonStream;
using LaunchDarkly.Sdk.Json;

namespace LaunchDarkly.Sdk.Internal.Events
{
    internal sealed class EventOutputFormatter
    {
        private readonly EventsConfiguration _config;

        public EventOutputFormatter(EventsConfiguration config)
        {
            _config = config;
        }

        public string SerializeOutputEvents(EventProcessorInternal.IEvent[] events, EventSummary summary, out int eventCountOut)
        {
            var jsonWriter = JWriter.New();
            var scope = new EventOutputFormatterScope(_config, jsonWriter, _config.InlineUsersInEvents);
            eventCountOut = scope.WriteOutputEvents(events, summary);
            return jsonWriter.GetString();
        }
    }

    internal struct EventOutputFormatterScope
    {
        private static readonly IJsonStreamConverter<LdValue> ValueConverter = new LdJsonConverters.LdValueConverter();
        private static readonly IJsonStreamConverter<EvaluationReason> ReasonConverter =
            new LdJsonConverters.EvaluationReasonConverter();

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

        public int WriteOutputEvents(EventProcessorInternal.IEvent[] events, EventSummary summary)
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

        public void WriteOutputEvent(EventProcessorInternal.IEvent e)
        {
            _obj = _jsonWriter.Object();
            switch (e)
            {
                case EventProcessorInternal.FeatureRequestEvent fe:
                    WriteFeatureEvent(fe, false);
                    break;
                case EventProcessorInternal.IdentifyEvent ie:
                    WriteBase("identify", ie.Timestamp, e.User?.Key);
                    WriteUser(ie.User);
                    break;
                case EventProcessorInternal.CustomEvent ce:
                    WriteBase("custom", ce.Timestamp, ce.EventKey);
                    WriteUserOrKey(ce.User, false);
                    if (!ce.Data.IsNull)
                    {
                        ValueConverter.WriteJson(ce.Data, _obj.Property("data"));
                    }
                    if (ce.MetricValue.HasValue)
                    {
                        _obj.Property("metricValue").Double(ce.MetricValue.Value);
                    }
                    break;
                case EventProcessorInternal.IndexEvent ie:
                    WriteBase("index", ie.Timestamp, null);
                    WriteUserOrKey(ie.User, true);
                    break;
                case EventProcessorInternal.DebugEvent de:
                    WriteFeatureEvent(de.FromEvent, true);
                    break;
                default:
                    break;
            }
            _obj.End();
        }

        private void WriteFeatureEvent(EventProcessorInternal.FeatureRequestEvent fe, bool debug)
        {
            WriteBase(debug ? "debug" : "feature", fe.Timestamp, fe.FlagKey);

            WriteUserOrKey(fe.User, debug);
            if (fe.FlagVersion.HasValue)
            {
                _obj.Property("version").Int(fe.FlagVersion.Value);
            }
            if (fe.Variation.HasValue)
            {
                _obj.Property("variation").Int(fe.Variation.Value);
            }
            ValueConverter.WriteJson(fe.Value, _obj.Property("value"));
            if (!fe.Default.IsNull)
            {
                ValueConverter.WriteJson(fe.Default, _obj.Property("default"));
            }
            _obj.MaybeProperty("prereqOf", fe.PrereqOf != null).String(fe.PrereqOf);
            WriteReason(fe.Reason);
        }

        public void WriteSummaryEvent(EventSummary summary)
        {
            var obj = _jsonWriter.Object();

            obj.Property("kind").String("summary");
            obj.Property("startDate").Long(summary.StartDate.Value);
            obj.Property("endDate").Long(summary.EndDate.Value);

            var flagsObj = obj.Property("features").Object();

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

                var flagObj = flagsObj.Property(flagKey).Object();
                ValueConverter.WriteJson(flagDefault, flagObj.Property("default"));
                var countersArr = flagObj.Property("counters").Array();

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
                            counterObj.Property("variation").Int(key.Variation.Value);
                        }
                        ValueConverter.WriteJson(counter.FlagValue, counterObj.Property("value"));
                        if (key.Version.HasValue)
                        {
                            counterObj.Property("version").Int(key.Version.Value);
                        }
                        else
                        {
                            counterObj.Property("unknown").Bool(true);
                        }
                        counterObj.Property("count").Int(counter.Count);
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
            _obj.Property("kind").String(kind);
            _obj.Property("creationDate").Long(creationDate.Value);
            _obj.MaybeProperty("key", key != null).String(key);
        }

        private void WriteUserOrKey(User user, bool forceInline)
        {
            if (forceInline || _config.InlineUsersInEvents)
            {
                WriteUser(user);
            }
            else if (user != null)
            {
                _obj.Property("userKey").String(user.Key);
            }
        }

        private void WriteUser(User user)
        {
            if (user is null)
            {
                return;
            }
            var eu = EventUser.FromUser(user, _config);

            var userObj = _obj.Property("user").Object();
            userObj.Property("key").String(eu.Key);
            userObj.MaybeProperty("secondary", eu.Secondary != null).String(eu.Secondary);
            userObj.MaybeProperty("ip", eu.IPAddress != null).String(eu.IPAddress);
            userObj.MaybeProperty("country", eu.Country != null).String(eu.Country);
            userObj.MaybeProperty("firstName", eu.FirstName != null).String(eu.FirstName);
            userObj.MaybeProperty("lastName", eu.LastName != null).String(eu.LastName);
            userObj.MaybeProperty("name", eu.Name != null).String(eu.Name);
            userObj.MaybeProperty("avatar", eu.Avatar != null).String(eu.Avatar);
            userObj.MaybeProperty("email", eu.Email != null).String(eu.Email);
            if (eu.Anonymous.HasValue)
            {
                userObj.Property("anonymous").Bool(eu.Anonymous.Value);
            }
            if (eu.Custom != null && eu.Custom.Count > 0)
            {
                var customObj = userObj.Property("custom").Object();
                foreach (var kv in eu.Custom)
                {
                    ValueConverter.WriteJson(kv.Value, customObj.Property(kv.Key));
                }
                customObj.End();
            }
            if (eu.PrivateAttrs != null)
            {
                var arr = userObj.Property("privateAttrs").Array();
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
                ReasonConverter.WriteJson(reason.Value, _obj.Property("reason"));
            }
        }
    }
}
