﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LaunchDarkly.Sdk.Interfaces;
using LaunchDarkly.Sdk.Internal.Helpers;
using Newtonsoft.Json;

namespace LaunchDarkly.Sdk.Internal.Events
{
    public sealed class EventOutputFormatter
    {
        private readonly IEventProcessorConfiguration _config;

        public EventOutputFormatter(IEventProcessorConfiguration config)
        {
            _config = config;
        }

        public string SerializeOutputEvents(Event[] events, EventSummary summary, out int eventCountOut)
        {
            var stringWriter = new StringWriter();
            var scope = new EventOutputFormatterScope(_config, stringWriter, _config.InlineUsersInEvents);
            eventCountOut = scope.WriteOutputEvents(events, summary);
            return stringWriter.ToString();
        }
    }

    public struct EventOutputFormatterScope
    {
        private readonly IEventProcessorConfiguration _config;
        private readonly JsonWriter _jsonWriter;
        private readonly JsonSerializer _jsonSerializer;

        private struct MutableKeyValuePair<A, B>
        {
            public A Key { get; set; }
            public B Value { get; set; }

            public static MutableKeyValuePair<A, B> FromKeyValue(KeyValuePair<A, B> kv) =>
                new MutableKeyValuePair<A, B> { Key = kv.Key, Value = kv.Value };
        }

        public EventOutputFormatterScope(IEventProcessorConfiguration config, TextWriter tw, bool inlineUsers)
        {
            _config = config;
            _jsonWriter = new JsonTextWriter(tw);
            _jsonSerializer = new JsonSerializer();
        }

        public int WriteOutputEvents(Event[] events, EventSummary summary)
        {
            var eventCount = 0;
            _jsonWriter.WriteStartArray();
            foreach (Event e in events)
            {
                if (WriteOutputEvent(e))
                {
                    eventCount++;
                }
            }
            if (summary.Counters.Count > 0)
            {
                WriteSummaryEvent(summary);
                eventCount++;
            }
            _jsonWriter.WriteEndArray();
            _jsonWriter.Flush();
            return eventCount;
        }

        public bool WriteOutputEvent(Event e)
        {
            switch (e)
            {
                case FeatureRequestEvent fe:
                    WithBaseObject(fe.Debug ? "debug" : "feature", fe.CreationDate, fe.Key, me =>
                    {
                        me.WriteUserOrKey(fe.User, fe.Debug);
                        if (fe.Version.HasValue)
                        {
                            me._jsonWriter.WritePropertyName("version");
                            me._jsonWriter.WriteValue(fe.Version.Value);
                        }
                        if (fe.Variation.HasValue)
                        {
                            me._jsonWriter.WritePropertyName("variation");
                            me._jsonWriter.WriteValue(fe.Variation.Value);
                        }
                        me._jsonWriter.WritePropertyName("value");
                        LdValue.JsonConverter.WriteJson(me._jsonWriter, fe.Value, me._jsonSerializer);
                        if (!fe.Default.IsNull)
                        {
                            me._jsonWriter.WritePropertyName("default");
                            LdValue.JsonConverter.WriteJson(me._jsonWriter, fe.Default, me._jsonSerializer);
                        }
                        me.MaybeWriteString("prereqOf", fe.PrereqOf);
                        me.WriteReason(fe.Reason);
                    });
                    break;
                case IdentifyEvent ie:
                    WithBaseObject("identify", ie.CreationDate, e.User?.Key, me =>
                    {
                        me.WriteUser(ie.User);
                    });
                    break;
                case CustomEvent ce:
                    WithBaseObject("custom", ce.CreationDate, ce.Key, me =>
                    {
                        me.WriteUserOrKey(ce.User, false);
                        if (!ce.Data.IsNull)
                        {
                            me._jsonWriter.WritePropertyName("data");
                            LdValue.JsonConverter.WriteJson(me._jsonWriter, ce.Data, me._jsonSerializer);
                        }
                        if (ce.MetricValue.HasValue)
                        {
                            me._jsonWriter.WritePropertyName("metricValue");
                            me._jsonWriter.WriteValue(ce.MetricValue.Value);
                        }
                    });
                    break;
                case IndexEvent ie:
                    WithBaseObject("index", ie.CreationDate, null, me =>
                    {
                        me.WriteUserOrKey(ie.User, true);
                    });
                    break;
                default:
                    return false;
                }
            return true;
        }

        public void WriteSummaryEvent(EventSummary summary)
        {
            _jsonWriter.WriteStartObject();

            _jsonWriter.WritePropertyName("kind");
            _jsonWriter.WriteValue("summary");
            _jsonWriter.WritePropertyName("startDate");
            _jsonWriter.WriteValue(summary.StartDate);
            _jsonWriter.WritePropertyName("endDate");
            _jsonWriter.WriteValue(summary.EndDate);

            _jsonWriter.WritePropertyName("features");
            _jsonWriter.WriteStartObject();

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

                _jsonWriter.WritePropertyName(flagKey);
                _jsonWriter.WriteStartObject();
                _jsonWriter.WritePropertyName("default");
                LdValue.JsonConverter.WriteJson(_jsonWriter, flagDefault, _jsonSerializer);
                _jsonWriter.WritePropertyName("counters");
                _jsonWriter.WriteStartArray();

                for (var j = i; j < unprocessedCounters.Length; j++)
                {
                    var entry = unprocessedCounters[j];
                    var key = entry.Key;
                    if (key.Key == flagKey && entry.Value != null)
                    {
                        var counter = entry.Value;
                        unprocessedCounters[j].Value = null; // mark as already processed

                        _jsonWriter.WriteStartObject();
                        if (key.Variation.HasValue)
                        {
                            _jsonWriter.WritePropertyName("variation");
                            _jsonWriter.WriteValue(key.Variation.Value);
                        }
                        _jsonWriter.WritePropertyName("value");
                        LdValue.JsonConverter.WriteJson(_jsonWriter, counter.FlagValue, _jsonSerializer);
                        if (key.Version.HasValue)
                        {
                            _jsonWriter.WritePropertyName("version");
                            _jsonWriter.WriteValue(key.Version.Value);
                        }
                        else
                        {
                            _jsonWriter.WritePropertyName("unknown");
                            _jsonWriter.WriteValue(true);
                        }
                        _jsonWriter.WritePropertyName("count");
                        _jsonWriter.WriteValue(counter.Count);
                        _jsonWriter.WriteEndObject();
                    }
                }

                _jsonWriter.WriteEndArray();
                _jsonWriter.WriteEndObject();
            }

            _jsonWriter.WriteEndObject();

            _jsonWriter.WriteEndObject();
        }

        public void MaybeWriteString(string name, string value)
        {
            if (value != null)
            {
                _jsonWriter.WritePropertyName(name);
                _jsonWriter.WriteValue(value);
            }
        }

        public void WithBaseObject(string kind, long creationDate, string key, Action<EventOutputFormatterScope> moreActions)
        {
            _jsonWriter.WriteStartObject();
            _jsonWriter.WritePropertyName("kind");
            _jsonWriter.WriteValue(kind);
            _jsonWriter.WritePropertyName("creationDate");
            _jsonWriter.WriteValue(creationDate);
            MaybeWriteString("key", key);
            moreActions(this);
            _jsonWriter.WriteEndObject();
        }

        public void WriteUserOrKey(User user, bool forceInline)
        {
            if (forceInline || _config.InlineUsersInEvents)
            {
                WriteUser(user);
            }
            else if (user != null)
            {
                _jsonWriter.WritePropertyName("userKey");
                _jsonWriter.WriteValue(user.Key);
            }
        }

        public void WriteUser(User user)
        {
            if (user is null)
            {
                return;
            }
            var eu = EventUser.FromUser(user, _config);
            _jsonWriter.WritePropertyName("user");
            _jsonWriter.WriteStartObject();
            MaybeWriteString("key", eu.Key);
            MaybeWriteString("secondary", eu.Secondary);
            MaybeWriteString("ip", eu.IPAddress);
            MaybeWriteString("country", eu.Country);
            MaybeWriteString("firstName", eu.FirstName);
            MaybeWriteString("lastName", eu.LastName);
            MaybeWriteString("name", eu.Name);
            MaybeWriteString("avatar", eu.Avatar);
            MaybeWriteString("email", eu.Email);
            if (eu.Anonymous.HasValue)
            {
                _jsonWriter.WritePropertyName("anonymous");
                _jsonWriter.WriteValue(eu.Anonymous.Value);
            }
            if (eu.Custom != null && eu.Custom.Count > 0)
            {
                _jsonWriter.WritePropertyName("custom");
                _jsonWriter.WriteStartObject();
                foreach (var kv in eu.Custom)
                {
                    _jsonWriter.WritePropertyName(kv.Key);
                    _jsonSerializer.Serialize(_jsonWriter, kv.Value);
                }
                _jsonWriter.WriteEndObject();
            }
            if (eu.PrivateAttrs != null)
            {
                _jsonWriter.WritePropertyName("privateAttrs");
                _jsonWriter.WriteStartArray();
                foreach (var a in eu.PrivateAttrs)
                {
                    _jsonWriter.WriteValue(a);
                }
                _jsonWriter.WriteEndArray();
            }
            _jsonWriter.WriteEndObject();
        }

        public void WriteReason(EvaluationReason? reason)
        {
            if (!reason.HasValue)
            {
                return;
            }
            _jsonWriter.WritePropertyName("reason");
            EvaluationReason.JsonConverter.WriteJson(_jsonWriter, reason.Value, _jsonSerializer);
        }
    }
}
