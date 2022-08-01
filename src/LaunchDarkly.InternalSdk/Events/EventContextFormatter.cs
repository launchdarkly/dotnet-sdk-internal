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
                    WriteSingle(mc, obj.Name(mc.Kind.Value), false);
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
                obj.Name("kind").String(c.Kind.Value);
            }
            obj.Name("key").String(c.Key);
            if (c.Anonymous)
            {
                obj.Name("anonymous").Bool(true);
            }

            List<string> redactedList = null;
            var privateRefs = _globalPrivateAttributes.Concat(c.PrivateAttributes);
            foreach (var attr in c.OptionalAttributeNames)
            {
                if (_allAttributesPrivate)
                {
                    AddRedacted(ref redactedList, attr); // the entire attribute is redacted
                    continue;
                }
                WriteOrRedact(attr, c, ref obj, privateRefs, ref redactedList);
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

        // This method implements the context-aware attribute redaction logic, in which an attribute
        // can be either written as-is, fully redacted, or (for a JSON object) partially redacted.
        // In the latter two cases, this method returns the redacted attribute reference string;
        // otherwise it returns null.
        private void WriteOrRedact(
            string attrName,
            in Context c,
            ref ObjectWriter obj,
            IEnumerable<AttributeRef> privateRefs,
            ref List<string> redactedList
            )
        {
            // First check if the whole attribute is redacted by name.
            foreach (var a in privateRefs)
            {
                if (a.Depth == 1 && a.TryGetComponent(0, out var firstPathComponent) &&
                    firstPathComponent.Name == attrName)
                {
                    AddRedacted(ref redactedList, attrName); // the entire attribute is redacted
                    return;
                }
            }

            var value = c.GetValue(attrName);
            if (value.Type != LdValueType.Object)
            {
                LdValueConverter.WriteJsonValue(value, obj.Name(attrName));
                return;
            }

            // The value is a JSON object, and the attribute may need to be partially redacted.
            WriteRedactedValue(value, ref obj, privateRefs, 0, attrName, ref redactedList);
        }

        private void WriteRedactedValue(
            in LdValue value,
            ref ObjectWriter obj,
            IEnumerable<AttributeRef> allPrivate,
            int depth,
            string pathComponent,
            ref List<string> redactedList)
        {
            IEnumerable<AttributeRef> filteredPrivate = allPrivate.Where(a =>
                a.TryGetComponent(depth, out var p) && p.Name == pathComponent);

            var haveSubpaths = false;
            foreach (var a in filteredPrivate)
            {
                if (a.Depth <= depth + 1)
                {
                    // exact match for this subpath or a parent - the whole value is redacted
                    AddRedacted(ref redactedList, a.ToString());
                    return;
                }
                haveSubpaths = true;
            }

            if (!haveSubpaths || value.Type != LdValueType.Object)
            {
                LdValueConverter.WriteJsonValue(value, obj.Name(pathComponent));
                return;
            }

            var subObj = obj.Name(pathComponent).Object();
            foreach (var kv in value.Dictionary)
            {
                WriteRedactedValue(kv.Value, ref subObj, filteredPrivate, depth + 1, kv.Key, ref redactedList);
            }
            subObj.End();
        }

        private void AddRedacted(ref List<string> redactedList, string attrName)
        {
            if (redactedList is null)
            {
                redactedList = new List<string>();
            }
            redactedList.Add(attrName);
        }
    }
}
