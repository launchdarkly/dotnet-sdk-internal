using System;

namespace LaunchDarkly.Sdk.Internal
{
    public sealed class UnsuccessfulResponseException : Exception
    {
        public int StatusCode
        {
            get;
            private set;
        }

        public UnsuccessfulResponseException(int statusCode) :
            base(string.Format("HTTP status {0}", statusCode))
        {
            StatusCode = statusCode;
        }
    }
}
