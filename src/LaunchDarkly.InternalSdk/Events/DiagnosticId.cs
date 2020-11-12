using System;
using Newtonsoft.Json;

namespace LaunchDarkly.Sdk.Internal.Events
{
    public class DiagnosticId
    {
        [JsonProperty(PropertyName = "diagnosticId", NullValueHandling = NullValueHandling.Ignore)]
        public readonly Guid Id;
        [JsonProperty(PropertyName = "sdkKeySuffix", NullValueHandling = NullValueHandling.Ignore)]
        public readonly string SdkKeySuffix;

        public DiagnosticId(string sdkKey, Guid diagnosticId)
        {
            if (sdkKey != null)
            {
                SdkKeySuffix = sdkKey.Substring(Math.Max(0, sdkKey.Length - 6));
            }
            Id = diagnosticId;
        }
    }
}
