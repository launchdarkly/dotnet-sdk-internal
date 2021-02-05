using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LaunchDarkly.Logging;
using WireMock.Logging;
using WireMock.Server;
using WireMock.Settings;
using Xunit;

namespace LaunchDarkly.Sdk
{
    public class TestUtil
    {
        public static Logger NullLogger = Logs.None.Logger("");

        public static void AssertJsonEquals(string expected, string actual)
        {
            Assert.Equal(LdValue.Parse(expected), LdValue.Parse(actual));
        }
        
        public static void AssertContainsInAnyOrder<T>(IEnumerable<T> items, params T[] expectedItems)
        {
            Assert.Equal(expectedItems.Length, items.Count());
            foreach (var e in expectedItems)
            {
                Assert.Contains(e, items);
            }
        }

        public static WireMockServer NewServer()
        {
            return WireMockServer.Start(new WireMockServerSettings
            {
                Logger = new WireMockNullLogger(),
                AllowAnyHttpStatusCodeInResponse = true // without this setting, WireMock will silently change errors like 429 to 200
            });
        }

        public static void WithServer(Action<WireMockServer> a)
        {
            var server = NewServer();
            try
            {
                a(server);
            }
            finally
            {
                server.Stop();
            }
        }

        public static async Task WithServer(Func<WireMockServer, Task> a)
        {
            var server = NewServer();
            try
            {
                await a(server);
            }
            finally
            {
                server.Stop();
            }
        }
    }
}
