using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
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
        /// A function to create an HTTP handler, or null for the standard one.
        /// </summary>
        /// <remarks>
        /// If specified, this factory will be called with the other properties as a parameter. This
        /// may be necessary on platforms like Xamarin where the SDK may want to use a platform-specific
        /// class that needs to be configured at the handler level.
        /// </remarks>
        public Func<HttpProperties, HttpMessageHandler> HttpMessageHandlerFactory { get; }

        /// <summary>
        /// The proxy configuration, if any.
        /// </summary>
        /// <remarks>
        /// This is only present if a proxy was specified programmatically, not if it was
        /// specified with an environment variable.
        /// </remarks>
        IWebProxy Proxy { get; }

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
            Func<HttpProperties, HttpMessageHandler> httpMessageHandlerFactory,
            IWebProxy proxy,
            TimeSpan readTimeout
            )
        {
            BaseHeaders = baseHeaders;
            ConnectTimeout = connectTimeout;
            HttpExceptionConverter = httpExceptionConverter;
            HttpMessageHandlerFactory = httpMessageHandlerFactory;
            Proxy = proxy;
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
                null,
                DefaultReadTimeout
                );

        public HttpProperties WithConnectTimeout(TimeSpan newConnectTimeout) =>
            new HttpProperties(
                BaseHeaders,
                newConnectTimeout,
                HttpExceptionConverter,
                HttpMessageHandlerFactory,
                Proxy,
                ReadTimeout
                );

        public HttpProperties WithHttpMessageHandlerFactory(Func<HttpProperties, HttpMessageHandler> factory) =>
            new HttpProperties(
                BaseHeaders,
                ConnectTimeout,
                HttpExceptionConverter,
                factory,
                Proxy,
                ReadTimeout
                );

        public HttpProperties WithHttpExceptionConverter(Func<Exception, Exception> newHttpExceptionConverter) =>
            new HttpProperties(
                BaseHeaders,
                ConnectTimeout,
                newHttpExceptionConverter,
                HttpMessageHandlerFactory,
                Proxy,
                ReadTimeout
                );

        public HttpProperties WithProxy(IWebProxy newProxy) =>
            new HttpProperties(
                BaseHeaders,
                ConnectTimeout,
                HttpExceptionConverter,
                HttpMessageHandlerFactory,
                newProxy,
                ReadTimeout
                );

        public HttpProperties WithReadTimeout(TimeSpan newReadTimeout) =>
            new HttpProperties(
                BaseHeaders,
                ConnectTimeout,
                HttpExceptionConverter,
                HttpMessageHandlerFactory,
                Proxy,
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
                BaseHeaders.Where(kv => !string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
                    .Concat(ImmutableList.Create(new KeyValuePair<string, string>(name, value))).ToImmutableList(),
                ConnectTimeout,
                HttpExceptionConverter,
                HttpMessageHandlerFactory,
                Proxy,
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
        /// Creates an <c>HttpClient</c> instance based on this configuration.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The client's <c>HttpMessageHandler</c> will be set as follows:
        /// </para>
        /// <list type="bullet">
        /// <item> If <c>HttpMessageHandlerFactory</c> was set, it will be called, passing the
        /// other properties as a parameter. </item>
        /// <item> Otherwise, if <c>Proxy</c> was set, we will create a handler instance and set
        /// its <c>Proxy</c> property. The handler class used in this case is <c>SocketsHttpHandler</c>
        /// in .NET Core 2.1+ and .NET 5.0+, or <c>HttpClientHandler</c> otherwise. </item>
        /// <item> If neither of the above was set (or if the factory function returned null),
        /// the platform default handler will be used: that is, the <c>HttpClient</c> constructor
        /// will be called without a handler. </item>
        /// </list>
        /// <para>
        /// The client will <i>not</i> be configured to send <c>BaseHeaders</c> automatically;
        /// headers must still be added to each request. This is because we may want to support
        /// having an application specify its own HTTP client instance.
        /// </para>
        /// <para>
        /// Currently there is not a standard way to specify connection timeout and socket read
        /// timeout separately in .NET. The <c>Timeout</c> property in <c>HttpClient</c> applies to
        /// the entire request-response cycle, and we wouldn't want to set it at the client level
        /// anyway because we might be using that client for a streaming connection that never ends.
        /// <c>LaunchDarkly.EventSource</c> does implement read timeouts.
        /// </para>
        /// </remarks>
        /// <returns>an HTTP client instance</returns>
        public HttpClient NewHttpClient()
        {
            var handler = (HttpMessageHandlerFactory ?? DefaultHttpMessageHandlerFactory)(this);
            return handler is null ?
                new HttpClient() :
                new HttpClient(handler, false);
        }

        private static HttpMessageHandler DefaultHttpMessageHandlerFactory(HttpProperties props)
        {
            if (props.Proxy != null)
            {
#if NETCOREAPP2_1 || NET5_0
                return new SocketsHttpHandler { Proxy = props.Proxy };
#else
                return new HttpClientHandler { Proxy = props.Proxy };
#endif
            }
            return null;
        }
    }
}
