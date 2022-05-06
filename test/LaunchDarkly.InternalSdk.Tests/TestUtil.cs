using System;
using System.Collections.Generic;
using System.Linq;
using LaunchDarkly.Logging;
using Xunit;

namespace LaunchDarkly.Sdk
{
    public class TestUtil
    {
        public static Logger NullLogger = Logs.None.Logger("");

        public static void AssertContainsInAnyOrder<T>(IEnumerable<T> items, params T[] expectedItems)
        {
            Assert.Equal(expectedItems.Length, items.Count());
            foreach (var e in expectedItems)
            {
                Assert.Contains(e, items);
            }
        }

        public static LdValue TryParseJson(string json)
        {
            try
            {
                return LdValue.Parse(json);
            }
            catch (Exception e)
            {
                throw new Exception(string.Format("received invalid JSON ({0}): {1}", e.Message, json));
            }
        }
    }
}
