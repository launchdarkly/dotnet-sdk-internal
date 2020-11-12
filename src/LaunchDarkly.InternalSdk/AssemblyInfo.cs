using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("LaunchDarkly.InternalSdk.Tests")]

// Allow mock/proxy objects in unit tests to access internal classes
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
