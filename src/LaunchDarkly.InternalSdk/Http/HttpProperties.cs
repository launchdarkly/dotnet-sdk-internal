using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Net.Http;

namespace LaunchDarkly.Sdk.Internal.Http
{
    /// <summary>
    /// Internal representation of HTTP options that are supported by both .NET and Xamarin SDKs,
    /// including the logic for constructing the standard set of headers for HTTP requests.
    /// </summary>
    /// <remarks>
    /// This is an immutable struct. The "With" methods for setting properties will return a new
    /// struct based on the current instance.
    /// </remarks>
    public struct HttpProperties
    {
        /// <summary>
        /// An arbitrary default for ConnectTimeout. SDKs should define their own defaults.
        /// </summary>
        public static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(1);

        /// <summary>
        /// An arbitrary default for ReadTimeout. SDKs should define their own defaults.
        /// </summary>
        public static readonly TimeSpan DefaultReadTimeout = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Headers that should be included in every request.
        /// </summary>
        public ImmutableList<KeyValuePair<string, string>> BaseHeaders { get; }

        /// <summary>
        /// The configured TCP connection timeout.
        /// </summary>
        /// <remarks>
        /// See comments on <see cref="NewHttpClient"/> regarding timeouts.
        /// </remarks>
        public TimeSpan ConnectTimeout { get; }

        /// <summary>
        /// A function that transforms platform-specific exceptions if necessary.
        /// </summary>
        /// <remarks>
        /// For mobile platforms where HTTP requests might throw platform-specific exceptions,
        /// you can provide a function to translate them to standard .NET exceptions. By default,
        /// exceptions are not changed.
        /// </remarks>
        public Func<Exception, Exception> HttpExceptionConverter { get; }

        /// <summary>
        /// A custom HTTP handler, or null for the standard one.
        /// </summary>
        public HttpMessageHandler HttpMessageHandler { get; }

        /// <summary>
        /// The configured TCP socket read timeout.
        /// </summary>
        /// <remarks>
        /// See comments on <see cref="NewHttpClient"/> regarding timeouts.
        /// </remarks>
        public TimeSpan ReadTimeout { get; }

        private HttpProperties(
            ImmutableList<KeyValuePair<string, string>> baseHeaders,
            TimeSpan connectTimeout,
            Func<Exception, Exception> httpExceptionConverter,
            HttpMessageHandler httpMessageHandler,
            TimeSpan readTimeout
            )
        {
            BaseHeaders = baseHeaders;
            ConnectTimeout = connectTimeout;
            HttpExceptionConverter = httpExceptionConverter;
            HttpMessageHandler = httpMessageHandler;
            ReadTimeout = readTimeout;
        }

        /// <summary>
        /// An instance with default properties.
        /// </summary>
        public static HttpProperties Default =>
            new HttpProperties(
                ImmutableList.Create<KeyValuePair<string, string>>(),
                DefaultConnectTimeout,
                e => e,
                null,
                DefaultReadTimeout
                );

        public HttpProperties WithConnectTimeout(TimeSpan newConnectTimeout) =>
            new HttpProperties(
                BaseHeaders,
                newConnectTimeout,
                HttpExceptionConverter,
                HttpMessageHandler,
                ReadTimeout
                );

        public HttpProperties WithHttpExceptionConverter(Func<Exception, Exception> newHttpExceptionConverter) =>
            new HttpProperties(
                BaseHeaders,
                ConnectTimeout,
                newHttpExceptionConverter,
                HttpMessageHandler,
                ReadTimeout
                );

        public HttpProperties WithHttpMessageHandler(HttpMessageHandler newHttpMessageHandler) =>
            new HttpProperties(
                BaseHeaders,
                ConnectTimeout,
                HttpExceptionConverter,
                newHttpMessageHandler,
                ReadTimeout
                );

        public HttpProperties WithReadTimeout(TimeSpan newReadTimeout) =>
            new HttpProperties(
                BaseHeaders,
                ConnectTimeout,
                HttpExceptionConverter,
                HttpMessageHandler,
                newReadTimeout
                );

        public HttpProperties WithAuthorizationKey(string key) =>
            string.IsNullOrEmpty(key) ? this :
                WithHeader("Authorization", key);

        public HttpProperties WithUserAgent(string userAgent) =>
            string.IsNullOrEmpty(userAgent) ? this :
                WithHeader("User-Agent", userAgent);

        public HttpProperties WithUserAgent(string userAgentName, string userAgentVersion) =>
            string.IsNullOrEmpty(userAgentName) ? this :
                WithHeader("User-Agent", userAgentName + "/" + userAgentVersion);

        public HttpProperties WithWrapper(string wrapperName, string wrapperVersion) =>
            string.IsNullOrEmpty(wrapperName) ? this :
                WithHeader("X-LaunchDarkly-Wrapper",
                    string.IsNullOrEmpty(wrapperVersion) ? wrapperName :
                        wrapperName + "/" + wrapperVersion);

        public HttpProperties WithHeader(string name, string value) =>
            new HttpProperties(
                BaseHeaders.Add(new KeyValuePair<string, string>(name, value)),
                ConnectTimeout,
                HttpExceptionConverter,
                HttpMessageHandler,
                ReadTimeout
                );

        /// <summary>
        /// Adds BaseHeaders to a request.
        /// </summary>
        /// <param name="req">the HTTP request</param>
        public void AddHeaders(HttpRequestMessage req)
        {
            var rh = req.Headers;
            foreach (var h in BaseHeaders)
            {
                rh.Add(h.Key, h.Value);
            }
        }

        /// <summary>
        /// Creates an HttpClient instance based on this configuration.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The client will be configured to use the <c>HttpMessageHandler</c> that was specified,
        /// if any. It will <i>not</i> be configured to send <c>BaseHeaders</c> automatically;
        /// headers must still be added to each request. This is because we may want to support
        /// having an application specify its own client instance.
        /// </para>
        /// <para>
        /// Currently there is not a standard way to specify connection timeout and socket read
        /// timeout separately in .NET. The <c>Timeout</c> property in <c>HttpClient</c> applies to
        /// the entire request-response cycle, and we wouldn't want to set it at the client level
        /// anyway because we might be using that client for a streaming connection that never ends.
        /// <c>LaunchDarkly.EventSource</c> does implement read timeouts.
        /// </para>
        /// </remarks>
        /// <returns></returns>
        public HttpClient NewHttpClient()
        {
            var httpClient = HttpMessageHandler is null ?
                new HttpClient() :
                new HttpClient(HttpMessageHandler, false);
            foreach (var h in BaseHeaders)
            {
                httpClient.DefaultRequestHeaders.Add(h.Key, h.Value);
            }
            return httpClient;
        }
    }
}
