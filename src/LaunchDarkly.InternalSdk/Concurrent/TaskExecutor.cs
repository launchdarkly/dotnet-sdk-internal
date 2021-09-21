using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.Logging;

namespace LaunchDarkly.Sdk.Internal
{
    /// <summary>
    /// Abstraction of scheduling infrequent worker tasks.
    /// </summary>
    /// <remarks>
    /// We use this instead of just calling <c>Task.Run()</c> for two reasons. First, the default
    /// scheduling behavior of <c>Task.Run()</c> may not always be what we want. Second, this provides
    /// better error logging.
    /// </remarks>
    internal sealed class TaskExecutor
    {
        private readonly object _eventSender;
        private readonly Logger _log;

        /// <summary>
        /// Creates an instance.
        /// </summary>
        /// <param name="eventSender">object to use as the <c>sender</c> parameter when firing events</param>
        /// <param name="log">logger for logging errors from worker tasks</param>
        internal TaskExecutor(object eventSender, Logger log)
        {
            _eventSender = eventSender;
            _log = log;
        }

        /// <summary>
        /// Schedules delivery of an event to some number of event handlers.
        /// </summary>
        /// <remarks>
        /// In the current implementation, each handler call is a separate background task.
        /// </remarks>
        /// <typeparam name="T">the event type</typeparam>
        /// <param name="eventArgs">the event object</param>
        /// <param name="handlers">a handler list</param>
        public void ScheduleEvent<T>(T eventArgs, EventHandler<T> handlers)
        {
            if (handlers is null)
            {
                return;
            }
            var delegates = handlers.GetInvocationList();
            if (delegates is null || delegates.Length == 0)
            {
                return;
            }
            _log.Debug("scheduling task to send {0} to {1}", eventArgs, handlers);
            foreach (var handler in delegates)
            {
                _ = Task.Run(() =>
                {
                    _log.Debug("sending {0}", eventArgs);
                    try
                    {
                        handler.DynamicInvoke(_eventSender, eventArgs);
                    }
                    catch (Exception e)
                    {
                        if (e is TargetInvocationException wrappedException)
                        {
                            e = wrappedException.InnerException;
                        }
                        LogHelpers.LogException(_log,
                            string.Format("Unexpected exception from event handler for {0}", eventArgs.GetType().Name),
                            e);
                    }
                });
            }
        }

        /// <summary>
        /// Starts a repeating async task.
        /// </summary>
        /// <param name="initialDelay">time to wait before first execution</param>
        /// <param name="interval">interval at which to repeat</param>
        /// <param name="taskFn">the task to run</param>
        /// <returns>a <see cref="CancellationTokenSource"/> for stopping the task</returns>
        public CancellationTokenSource StartRepeatingTask(
            TimeSpan initialDelay,
            TimeSpan interval,
            Func<Task> taskFn
            )
        {
            var canceller = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                if (initialDelay.CompareTo(TimeSpan.Zero) > 0)
                {
                    try
                    {
                        await Task.Delay(initialDelay, canceller.Token);
                    }
                    catch (TaskCanceledException) { }
                }
                while (true)
                {
                    if (canceller.IsCancellationRequested)
                    {
                        return;
                    }
                    var nextTime = DateTime.Now.Add(interval);
                    try
                    {
                        await taskFn();
                    }
                    catch (Exception e)
                    {
                        LogHelpers.LogException(_log, "Unexpected exception from repeating task", e);
                    }
                    var timeToWait = nextTime.Subtract(DateTime.Now);
                    if (timeToWait.CompareTo(TimeSpan.Zero) > 0)
                    {
                        try
                        {
                            await Task.Delay(timeToWait, canceller.Token);
                        }
                        catch (TaskCanceledException) { }
                    }
                }
            });
            return canceller;
        }
    }
}
