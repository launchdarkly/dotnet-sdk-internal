using System.Threading;

namespace LaunchDarkly.Sdk.Internal
{
    /// <summary>
    /// A simple atomic boolean using Interlocked.Exchange.
    /// </summary>
    public class AtomicBoolean
    {
        internal volatile int _value;

        internal AtomicBoolean(bool value)
        {
            _value = value ? 1 : 0;
        }

        internal bool Get() => _value != 0;

        internal bool GetAndSet(bool newValue)
        {
            int old = Interlocked.Exchange(ref _value, newValue ? 1 : 0);
            return old != 0;
        }
    }
}
