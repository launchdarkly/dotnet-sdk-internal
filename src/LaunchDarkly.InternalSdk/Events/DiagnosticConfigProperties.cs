using System;
using System.Net;
using LaunchDarkly.Sdk.Internal.Http;

namespace LaunchDarkly.Sdk.Internal.Events
{
    /// <summary>
    /// Helpers for standard properties we include in diagnostic event configuration data,
    /// ensuring that their names and types are consistent across SDKs. It is up to each SDK
    /// to call these with values from its own Configuration type. Properties that only exist
    /// in server-side or in client-side are not included.
    /// </summary>
    public static class DiagnosticConfigProperties
    {
        /// <summary>
        /// Adds the standard <c>autoAliasingOptOut</c> property.
        /// </summary>
        /// <param name="builder">the object builder</param>
        /// <param name="value">the value</param>
        /// <returns>the builder</returns>
        public static LdValue.ObjectBuilder WithAutoAliasingOptOut(this LdValue.ObjectBuilder builder, bool value) =>
            builder.Add("autoAliasingOptOut", value);

        /// <summary>
        /// Adds the standard properties for events configuration.
        /// </summary>
        /// <param name="builder">the object builder</param>
        /// <param name="config">the standard event properties</param>
        /// <param name="customEventsBaseUri">true if the SDK is using a custom base URI for events</param>
        /// <returns>the builder</returns>
        public static LdValue.ObjectBuilder WithEventProperties(
            this LdValue.ObjectBuilder builder,
            EventsConfiguration config,
            bool customEventsBaseUri
            ) =>
            builder.Add("allAttributesPrivate", config.AllAttributesPrivate)
                .Add("customEventsURI", customEventsBaseUri)
                .Add("diagnosticRecordingIntervalMillis", config.DiagnosticRecordingInterval.TotalMilliseconds)
                .Add("eventsCapacity", config.EventCapacity)
                .Add("eventsFlushIntervalMillis", config.EventFlushInterval.TotalMilliseconds)
                .Add("inlineUsersInEvents", config.InlineUsersInEvents);

        /// <summary>
        /// Adds the standard properties for HTTP configuration.
        /// </summary>
        /// <param name="builder">the object builder</param>
        /// <param name="props">the standard HTTP properties</param>
        /// <returns>the builder</returns>
        public static LdValue.ObjectBuilder WithHttpProperties(this LdValue.ObjectBuilder builder, HttpProperties props) =>
            builder.Add("connectTimeoutMillis", props.ConnectTimeout.TotalMilliseconds)
                .Add("socketTimeoutMillis", props.ReadTimeout.TotalMilliseconds)
                .Add("usingProxy", DetectProxy(props))
                .Add("usingProxyAuthenticator", DetectProxyAuth(props));

        /// <summary>
        /// Adds the standard <c>startWaitMillis</c> property.
        /// </summary>
        /// <param name="builder">the object builder</param>
        /// <param name="value">the value</param>
        /// <returns>the builder</returns>
        public static LdValue.ObjectBuilder WithStartWaitTime(this LdValue.ObjectBuilder builder, TimeSpan value) =>
            builder.Add("startWaitMillis", value.TotalMilliseconds);

        /// <summary>
        /// Adds the standard properties for streaming.
        /// </summary>
        /// <param name="builder">the object builder</param>
        /// <param name="customStreamingBaseUri">true if the SDK is using a custom base URI for streaming</param>
        /// <param name="customPollingBaseUri">true if the SDK is using a custom base URI for polling</param>
        /// <param name="initialReconnectDelay">the initial reconnect delay</param>
        /// <returns>the builder</returns>
        public static LdValue.ObjectBuilder WithStreamingProperties(
            this LdValue.ObjectBuilder builder,
            bool customStreamingBaseUri,
            bool customPollingBaseUri,
            TimeSpan initialReconnectDelay
            ) =>
            builder.Add("streamingDisabled", false)
                .Add("customBaseURI", customPollingBaseUri)
                .Add("customStreamURI", customStreamingBaseUri)
                .Add("reconnectTimeMillis", initialReconnectDelay.TotalMilliseconds);

        /// <summary>
        /// Adds the standard properties for polling.
        /// </summary>
        /// <param name="builder">the object builder</param>
        /// <param name="customPollingBaseUri">true if the SDK is using a custom base URI for polling</param>
        /// <param name="pollingInterval">the polling interval</param>
        /// <returns>the builder</returns>
        public static LdValue.ObjectBuilder WithPollingProperties(
            this LdValue.ObjectBuilder builder,
            bool customPollingBaseUri,
            TimeSpan pollingInterval
            ) =>
            builder.Add("streamingDisabled", true)
                .Add("customBaseURI", customPollingBaseUri)
                .Add("customStreamURI", false)
                .Add("pollingIntervalMillis", pollingInterval.TotalMilliseconds)
                .Add("reconnectTimeMillis", LdValue.Null); // reconnectTimeMillis is for streaming only

        // DetectProxy and DetectProxyAuth do not cover every mechanism that could be used to configure
        // a proxy; for instance, there is HttpClient.DefaultProxy, which only exists in .NET Core 3.x and
        // .NET 5.x. But since we're only trying to gather diagnostic stats, this doesn't have to be perfect.
        private static bool DetectProxy(HttpProperties props) =>
            props.Proxy != null ||
            !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("HTTP_PROXY")) ||
            !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("HTTPS_PROXY")) ||
            !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("ALL_PROXY"));

        private static bool DetectProxyAuth(HttpProperties props) =>
            props.Proxy is WebProxy wp &&
            (wp.Credentials != null || wp.UseDefaultCredentials);
    }
}
