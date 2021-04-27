using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.TestHelpers.HttpTest;
using Xunit;

namespace LaunchDarkly.Sdk.Internal.Http
{
    public class HttpPropertiesTest
    {
        [Fact]
        public void AuthorizationKey()
        {
            Assert.Empty(HttpProperties.Default.WithAuthorizationKey(null).BaseHeaders);

            ExpectSingleHeader(HttpProperties.Default.WithAuthorizationKey("sdk-key"),
                "Authorization", "sdk-key");
        }

        [Fact]
        public void ConnectTimeout()
        {
            Assert.Equal(HttpProperties.DefaultConnectTimeout,
                HttpProperties.Default.ConnectTimeout);

            Assert.Equal(TimeSpan.FromSeconds(7),
                HttpProperties.Default.WithConnectTimeout(TimeSpan.FromSeconds(7))
                    .ConnectTimeout);
        }


        [Fact]
        public void Header()
        {
            Assert.Empty(HttpProperties.Default.BaseHeaders);

            var hp = HttpProperties.Default
                .WithHeader("name1", "value1")
                .WithHeader("Name2", "value2-will-be-replaced")
                .WithHeader("name2", "value2");
            Assert.Equal(
                new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("name1", "value1"),
                    new KeyValuePair<string, string>("name2", "value2")
                },
                hp.BaseHeaders
                );
        }

        [Fact]
        public void HttpExceptionConverter()
        {
            var e = new Exception();
            Assert.NotNull(HttpProperties.Default.HttpExceptionConverter);
            Assert.Same(e, HttpProperties.Default.HttpExceptionConverter(e));

            Func<Exception, Exception> hec = ex => ex;
            Assert.Same(
                hec,
                HttpProperties.Default.WithHttpExceptionConverter(hec).HttpExceptionConverter
                );
        }

        [Fact]
        public void HttpMessageHandlerFactory()
        {
            Assert.Null(HttpProperties.Default.HttpMessageHandlerFactory);

            HttpMessageHandler hmh = new HttpClientHandler();
            Func<HttpProperties, HttpMessageHandler> hmhf = _ => hmh;

            var hp = HttpProperties.Default.WithHttpMessageHandlerFactory(hmhf);
            Assert.Same(hmhf, hp.HttpMessageHandlerFactory);
        }

        [Fact]
        public void ReadTimeout()
        {
            Assert.Equal(HttpProperties.DefaultReadTimeout,
                HttpProperties.Default.ReadTimeout);

            Assert.Equal(TimeSpan.FromSeconds(7),
                HttpProperties.Default.WithReadTimeout(TimeSpan.FromSeconds(7))
                    .ReadTimeout);
        }

        [Fact]
        public void UserAgent()
        {
            Assert.Empty(HttpProperties.Default.WithUserAgent(null).BaseHeaders);

            ExpectSingleHeader(HttpProperties.Default.WithUserAgent("x"),
                "User-Agent", "x");
            ExpectSingleHeader(HttpProperties.Default.WithUserAgent("x", "1.0.0"),
                "User-Agent", "x/1.0.0");
        }

        [Fact]
        public void Wrapper()
        {
            Assert.Empty(HttpProperties.Default.WithWrapper(null, null).BaseHeaders);
            Assert.Empty(HttpProperties.Default.WithWrapper("", "").BaseHeaders);

            ExpectSingleHeader(HttpProperties.Default.WithWrapper("x", null),
                "X-LaunchDarkly-Wrapper", "x");
            ExpectSingleHeader(HttpProperties.Default.WithWrapper("x", "1.0.0"),
                "X-LaunchDarkly-Wrapper", "x/1.0.0");
        }

        [Fact]
        public async Task HttpClientUsesCustomHandler()
        {
            HttpResponseMessage testResp = new HttpResponseMessage();
            HttpMessageHandler hmh = new TestHandler(testResp);
            Func<HttpProperties, HttpMessageHandler> hmhf = _ => hmh;

            var hp = HttpProperties.Default.WithHttpMessageHandlerFactory(hmhf);
            Assert.Same(hmhf, hp.HttpMessageHandlerFactory);

            using (var client = hp.NewHttpClient())
            {
                var resp = await client.GetAsync("http://fake");
                Assert.Same(testResp, resp);
            }
        }

        [Fact]
        public async Task HttpClientUsesConfiguredProxy()
        {
            using (var fakeProxyServer = HttpServer.Start(Handlers.Status(200)))
            {
                var proxy = new WebProxy(fakeProxyServer.Uri);
                var hp = HttpProperties.Default.WithProxy(proxy);

                using (var client = hp.NewHttpClient())
                {
                    var resp = await client.GetAsync("http://fake/");
                    Assert.Equal(200, (int)resp.StatusCode);

                    var request = fakeProxyServer.Recorder.RequireRequest();
                    Assert.Equal(new Uri("http://fake/"), request.Uri);
                }
            }
        }

        private void ExpectSingleHeader(HttpProperties hp, string name, string value)
        {
            Assert.Equal(
                new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>(name, value)
                },
                hp.BaseHeaders
                );
        }

        private class TestHandler : HttpMessageHandler
        {
            private readonly HttpResponseMessage _resp;

            public TestHandler(HttpResponseMessage resp)
            {
                _resp = resp;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(_resp);
            }
        }
    }
}
