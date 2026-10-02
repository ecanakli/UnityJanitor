using System;
using System.Threading;

namespace Ecanakli.Janitor.Tests
{
    // Runs an action on a real worker thread and reports what it threw. The join is bounded so a hang fails the test.
    public static class ThreadRunner
    {
        private const int JoinTimeoutMilliseconds = 10000;

        public static Exception Run(Action action)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });

            thread.IsBackground = true;
            thread.Start();
            if (!thread.Join(JoinTimeoutMilliseconds))
            {
                throw new TimeoutException("The worker thread did not finish.");
            }

            return failure;
        }
    }
}
