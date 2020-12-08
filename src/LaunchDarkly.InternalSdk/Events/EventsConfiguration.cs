using System;
using System.Collections.Immutable;

namespace LaunchDarkly.Sdk.Internal.Events
{
    /// <summary>
    /// Internal configuration properties for the events system.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The corresponding properties may or may not be configurable in the public SDK APIs.
    /// </para>
    /// <para>
    /// For simplicity in construction, this is a mutable class, but components should not
    /// modify its properties after passing it to another component. This is not a major
    /// risk since we do not expose this object in the public API and the SDKs have no
    /// reason to retain it after creating the event components.
    /// </para>
    /// </remarks>
    public sealed class EventsConfiguration
    {
        public bool AllAttributesPrivate { get; set; }

        public TimeSpan DiagnosticRecordingInterval { get; set; }

        public Uri DiagnosticUri { get; set; }

        public int EventCapacity { get; set; }

        public TimeSpan EventFlushInterval { get; set; }

        public Uri EventsUri { get; set; }

        public bool InlineUsersInEvents { get; set; }

        public IImmutableSet<string> PrivateAttributeNames { get; set;  }

        public TimeSpan? RetryInterval { get; set; }

        public int UserKeysCapacity { get; set; }

        public TimeSpan UserKeysFlushInterval { get; set; }
    }
}
