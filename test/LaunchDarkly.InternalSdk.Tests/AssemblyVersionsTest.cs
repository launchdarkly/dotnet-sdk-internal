using System;
using Xunit;

namespace LaunchDarkly.Sdk.Internal
{
    public class AssemblyVersionsTest
    {
        [Fact]
        public void GetVersionString()
        {
            Assert.Equal(
                "1.2.3", // this is hard-coded in LaunchDarkly.InternalSdk.Tests.csproj
                AssemblyVersions.GetAssemblyVersionStringForType(typeof(AssemblyVersionsTest))
                );
        }

        [Fact]
        public void GetVersion()
        {
            Assert.Equal(
                new Version("1.2.3.0"),
                AssemblyVersions.GetAssemblyVersionForType(typeof(AssemblyVersionsTest))
                );
        }
    }
}
