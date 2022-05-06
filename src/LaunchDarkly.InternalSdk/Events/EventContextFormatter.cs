using System.Collections.Generic;
using System.Linq;
using LaunchDarkly.JsonStream;

using static LaunchDarkly.Sdk.Json.LdJsonConverters;

namespace LaunchDarkly.Sdk.Internal.Events
{
    internal sealed class EventContextFormatter
    {
        private readonly bool _allAttributesPrivate;
        private readonly IEnumerable<AttributeRef> _globalPrivateAttributes;

        public EventContextFormatter(EventsConfiguration config)
        {
            _allAttributesPrivate = config.AllAttributesPrivate;
            _globalPrivateAttributes = config.PrivateAttributes ?? Enumerable.Empty<AttributeRef>();
        }

        public void Write(in Context c, IValueWriter w)
        {
            if (c.Multiple)
            {
                var obj = w.Object();
                obj.Name("kind").String("multi");
                foreach (var mc in c.MultiKindContexts)
                {
                    WriteSingle(mc, obj.Name(mc.Kind), false);
                }
                obj.End();
            }
            else
            {
                WriteSingle(c, w, true);
            }
        }

        private void WriteSingle(in Context c, IValueWriter w, bool includeKind)
        {
            var obj = w.Object();

            if (includeKind)
            {
                obj.Name("kind").String(c.Kind);
            }
            obj.Name("key").String(c.Key);
            if (c.Transient)
            {
                obj.Name("transient").Bool(true);
            }

            List<string> redactedList = null;
            foreach (var attr in c.OptionalAttributeNames)
            {
                WriteOrRedact(attr, in c, obj, ref redactedList);
            }

            if (!(c.Secondary is null) || (!(redactedList is null)))
            {
                var meta = obj.Name("_meta").Object();
                if (!(c.Secondary is null))
                {
                    meta.Name("secondary").String(c.Secondary);
                }
                if (!(redactedList is null))
                {
                    var array = meta.Name("redactedAttributes").Array();
                    foreach (var attr in redactedList)
                    {
                        array.String(attr);
                    }
                    array.End();
                }
                meta.End();
            }

            obj.End();
        }

        // This is a placeholder where the context-aware attribute redaction logic will go.
        // Currently it does not redact anything.
        private void WriteOrRedact(string attrName, in Context c, ObjectWriter obj, ref List<string> redactedList)
        {
            LdValueConverter.WriteJsonValue(c.GetValue(attrName), obj.Name(attrName));
        }
    }
}
