using System;

namespace LaunchDarkly.Sdk.Internal.Stream
{
    public sealed class StreamJsonParsingException : Exception
    {
        public StreamJsonParsingException(string message) : base(message) { }
    }
}
