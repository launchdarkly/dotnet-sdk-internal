using System.Collections.Generic;
using System.Linq;
using LaunchDarkly.JsonStream;
using Xunit;

using static LaunchDarkly.TestHelpers.JsonAssertions;

namespace LaunchDarkly.Sdk.Internal.Events
{
    public class EventContextFormatterTest
    {
        private struct Params
        {
            public string name;
            public Context context;
            public EventsConfiguration config;
            public string json;
        }

        private static List<Params> TestCases = new List<Params>
        {
            new Params
            {
                name = "no attributes private, single kind",
                context = Context.Builder("my-key").Kind("org").
                    Name("my-name").
                    Set("attr1", "value1").
                    Build(),
                json = @"{""kind"": ""org"", ""key"": ""my-key"", ""name"": ""my-name"", ""attr1"": ""value1""}"
            },
            new Params
            {
                name = "no attributes private, multi-kind",
                context = Context.NewMulti(
                    Context.Builder("org-key").Kind("org").
                        Name("org-name").
                        Build(),
                    Context.Builder("user-key").
                        Name("user-name").
                        Set("attr1", "value1").
                        Build()
                    ),
                json = @"{
                    ""kind"": ""multi"",
                    ""org"": {""key"": ""org-key"", ""name"": ""org-name""},
                    ""user"": {""key"": ""user-key"", ""name"": ""user-name"", ""attr1"": ""value1""}
                }"
            },
            new Params
            {
                name = "transient",
                context = Context.Builder("my-key").Kind("org").Transient(true).Build(),
                json = @"{""kind"": ""org"", ""key"": ""my-key"", ""transient"": true}"
            },
            new Params
            {
                name = "secondary",
                context = Context.Builder("my-key").Kind("org").Secondary("x").Build(),
                json = @"{""kind"": ""org"", ""key"": ""my-key"", ""_meta"": {""secondary"": ""x""}}"
            },
        };

        public static IEnumerable<object[]> TestCaseNames => TestCases.Select(p => new object[] { p.name });

        [Theory]
        [MemberData(nameof(TestCaseNames))]
        public void TestOutput(string testCaseName)
        {
            // This somewhat indirect way of doing a parameterized test is necessary because Xunit has trouble
            // dealing with complex types as test parameters.
            var p = TestCases.Find(c => c.name == testCaseName);

            var w = JWriter.New();
            new EventContextFormatter(p.config ?? new EventsConfiguration()).Write(p.context, w);
            AssertJsonEqual(p.json, w.GetString());
        }
    }
}
