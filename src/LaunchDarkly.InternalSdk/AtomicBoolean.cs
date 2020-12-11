using System.Threading;

namespace LaunchDarkly.Sdk.Internal
{
    /// <summary>
    /// A simple atomic boolean using Interlocked.Exchange.
    /// </summary>
    public sealed class AtomicBoolean
    {
        private volatile int _value;

        public AtomicBoolean(bool value)
        {
            _value = value ? 1 : 0;
        }

        public bool Get() => _value != 0;

        public bool GetAndSet(bool newValue)
        {
            int old = Interlocked.Exchange(ref _value, newValue ? 1 : 0);
            return old != 0;
        }
    }
}
