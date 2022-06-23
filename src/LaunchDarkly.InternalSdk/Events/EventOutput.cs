using System.Collections.Generic;
using LaunchDarkly.JsonStream;

using static LaunchDarkly.Sdk.Internal.Events.EventTypes;
using static LaunchDarkly.Sdk.Json.LdJsonConverters;

namespace LaunchDarkly.Sdk.Internal.Events
{
    internal sealed class EventOutputFormatter
    {
        private readonly EventsConfiguration _config;
        private readonly EventContextFormatter _contextFormatter;

        public EventOutputFormatter(EventsConfiguration config)
        {
            _config = config;
            _contextFormatter = new EventContextFormatter(config);
        }

        public string SerializeOutputEvents(object[] events, EventSummary summary, out int eventCountOut)
        {
            var jsonWriter = JWriter.New();
            eventCountOut = WriteOutputEvents(events, summary, jsonWriter);
            return jsonWriter.GetString();
        }
    
        private struct MutableKeyValuePair<A, B>
        {
            public A Key { get; set; }
            public B Value { get; set; }

            public static MutableKeyValuePair<A, B> FromKeyValue(KeyValuePair<A, B> kv) =>
                new MutableKeyValuePair<A, B> { Key = kv.Key, Value = kv.Value };
        }

        public int WriteOutputEvents(object[] events, EventSummary summary, IValueWriter w)
        {
            var eventCount = events.Length;
            var arr = w.Array();
            foreach (var e in events)
            {
                WriteOutputEvent(e, w);
            }
            if (!summary.Empty)
            {
                WriteSummaryEvent(summary, arr);
                eventCount++;
            }
            arr.End();
            return eventCount;
        }

        public void WriteOutputEvent(object e, IValueWriter w)
        {
            var obj = w.Object();
            switch (e)
            {
                case EvaluationEvent ee:
                    WriteEvaluationEvent(ee, ref obj, false);
                    break;
                case IdentifyEvent ie:
                    WriteBase("identify", ref obj, ie.Timestamp, null);
                    WriteContext(ie.Context, ref obj);
                    break;
                case CustomEvent ce:
                    WriteBase("custom", ref obj, ce.Timestamp, ce.EventKey);
                    WriteContextKeys(ce.Context, ref obj);
                    if (!ce.Data.IsNull)
                    {
                        LdValueConverter.WriteJsonValue(ce.Data, obj.Name("data"));
                    }
                    if (ce.MetricValue.HasValue)
                    {
                        obj.Name("metricValue").Double(ce.MetricValue.Value);
                    }
                    break;
                case EventProcessorInternal.IndexEvent ie:
                    WriteBase("index", ref obj, ie.Timestamp, null);
                    WriteContext(ie.Context, ref obj);
                    break;
                case EventProcessorInternal.DebugEvent de:
                    WriteEvaluationEvent(de.FromEvent, ref obj, true);
                    break;
                default:
                    break;
            }
            obj.End();
        }

        private void WriteEvaluationEvent(in EvaluationEvent ee, ref ObjectWriter obj, bool debug)
        {
            WriteBase(debug ? "debug" : "feature", ref obj, ee.Timestamp, ee.FlagKey);

            if (debug)
            {
                WriteContext(ee.Context, ref obj);
            }
            else
            {
                WriteContextKeys(ee.Context, ref obj);
            }
            if (ee.FlagVersion.HasValue)
            {
                obj.Name("version").Int(ee.FlagVersion.Value);
            }
            if (ee.Variation.HasValue)
            {
                obj.Name("variation").Int(ee.Variation.Value);
            }
            LdValueConverter.WriteJsonValue(ee.Value, obj.Name("value"));
            if (!ee.Default.IsNull)
            {
                LdValueConverter.WriteJsonValue(ee.Default, obj.Name("default"));
            }
            obj.MaybeName("prereqOf", ee.PrereqOf != null).String(ee.PrereqOf);
            WriteReason(ee.Reason, ref obj);
        }

        public void WriteSummaryEvent(EventSummary summary, IValueWriter w)
        {
            var obj = w.Object();

            obj.Name("kind").String("summary");
            obj.Name("startDate").Long(summary.StartDate.Value);
            obj.Name("endDate").Long(summary.EndDate.Value);

            var flagsObj = obj.Name("features").Object();

            foreach (var kvFlag in summary.Flags)
            {
                var flagObj = flagsObj.Name(kvFlag.Key).Object();

                var flagSummary = kvFlag.Value;

                LdValueConverter.WriteJsonValue(flagSummary.Default, flagObj.Name("default"));

                var contextKindsArr = flagObj.Name("contextKinds").Array();
                foreach (var kind in flagSummary.ContextKinds)
                {
                    contextKindsArr.String(kind);
                }
                contextKindsArr.End();

                var countersArr = flagObj.Name("counters").Array();

                foreach (var counter in flagSummary.Counters)
                {
                    var counterObj = countersArr.Object();

                    if (counter.Key.Variation.HasValue)
                    {
                        counterObj.Name("variation").Int(counter.Key.Variation.Value);
                    }

                    if (counter.Key.Variation.HasValue)
                    {
                        counterObj.Name("variation").Int(counter.Key.Variation.Value);
                    }
                    LdValueConverter.WriteJsonValue(counter.Value.FlagValue, counterObj.Name("value"));
                    if (counter.Key.Version.HasValue)
                    {
                        counterObj.Name("version").Int(counter.Key.Version.Value);
                    }
                    else
                    {
                        counterObj.Name("unknown").Bool(true);
                    }
                    counterObj.Name("count").Int(counter.Value.Count);

                    counterObj.End();
                }

                countersArr.End();

                flagObj.End();
            }

            flagsObj.End();
            obj.End();
        }

        private void WriteBase(string kind, ref ObjectWriter obj, UnixMillisecondTime creationDate, string key)
        {
            obj.Name("kind").String(kind);
            obj.Name("creationDate").Long(creationDate.Value);
            obj.MaybeName("key", key != null).String(key);
        }

        private void WriteContextKeys(in Context context, ref ObjectWriter obj)
        {
            var subObj = obj.Name("contextKeys").Object();
            if (context.Multiple)
            {
                foreach (var mc in context.MultiKindContexts)
                {
                    subObj.Name(mc.Kind.Value).String(mc.Key);
                }
            }
            else
            {
                subObj.Name(context.Kind.Value).String(context.Key);
            }
            subObj.End();
        }

        private void WriteContext(in Context context, ref ObjectWriter obj) =>
            _contextFormatter.Write(context, obj.Name("context"));

        public void WriteReason(EvaluationReason? reason, ref ObjectWriter obj)
        {
            if (reason.HasValue)
            {
                EvaluationReasonConverter.WriteJsonValue(reason.Value, obj.Name("reason"));
            }
        }
    }
}
