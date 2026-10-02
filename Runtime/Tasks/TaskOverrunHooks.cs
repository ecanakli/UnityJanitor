using System.Diagnostics;

namespace Ecanakli.Janitor
{
    // Feeds JANITOR101 (a task still running after its generation ended) into the editor diagnostics.
    // Lifecycle per task: optionally TaskGenerationEnded, then TaskFinished exactly once. Tasks are pooled, so the
    // tracker copies what it needs and drops its reference on TaskFinished.
    internal static class TaskOverrunHooks
    {
        // The owner's generation ended while the task was still running (its entry was terminated by teardown).
        [Conditional("UNITY_EDITOR")]
        internal static void TaskGenerationEnded(LifetimeTaskEntry task)
        {
#if UNITY_EDITOR
            TaskOverrunTracker.Ended(task);
#endif
        }

        // The task completed, faulted or stopped; called exactly once per started task.
        [Conditional("UNITY_EDITOR")]
        internal static void TaskFinished(LifetimeTaskEntry task)
        {
#if UNITY_EDITOR
            TaskOverrunTracker.Finished(task);
#endif
        }
    }
}
