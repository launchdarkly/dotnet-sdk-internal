using System.Runtime.CompilerServices;

#if DEBUG
[assembly: InternalsVisibleTo("LaunchDarkly.InternalSdk.Tests")]
#endif

// Allow mock/proxy objects in unit tests to access internal classes
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
