using System;
using System.Collections.Generic;
using System.Net.Http;
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
        public void HttpMessageHandler()
        {
            Assert.Null(HttpProperties.Default.HttpMessageHandler);

            var hmh = new HttpClientHandler();
            Assert.Same(
                hmh,
                HttpProperties.Default.WithHttpMessageHandler(hmh).HttpMessageHandler
                );
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
    }
}
