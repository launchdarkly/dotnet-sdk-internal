using System.Collections.Generic;
using System.Linq;
using LaunchDarkly.Logging;
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
    }
}
