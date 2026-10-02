using System;

namespace Ecanakli.Janitor
{
    internal enum LifetimeTaskKind : byte
    {
        Run,
        After,
        Every,
    }

    // Identity shared by the pooled Run and timer continuations. The entry it registers pins the generation the
    // task started in; teardown terminates it (a hook call only) and the task later removes its own stale id as a no-op.
    internal abstract class LifetimeTaskEntry
    {
        internal static readonly EntryTerminate Terminate = TerminateCore;

        internal Lifetime Owner;
        internal int Generation;
        internal int Slot;
        internal int Version;
        internal string Member;
        internal int Line;
        internal LifetimeTaskKind Kind;

        // True from a successful registration until the task ends; only then are hooks raised.
        internal bool Live;

        internal void Bind(Lifetime owner, LifetimeTaskKind kind, string member, int line)
        {
            Owner = owner;
            Generation = owner.Generation;
            Kind = kind;
            Member = member;
            Line = line;
            Live = false;
        }

        // False when the generation ended during registration (a sweep probe ran user code); nothing may start then.
        internal bool TryRegister()
        {
            var registration = Owner.Register(this, null, null, Terminate, null, null, Member, Line);
            if (!registration.IsActive)
            {
                return false;
            }

            Slot = registration.EntryId;
            Version = registration.EntryVersion;
            Live = true;
            return true;
        }

        // Cancellation is silent; everything else reaches LifetimeErrors. The early return also skips the label allocation.
        protected void Report(Exception exception, LifetimeErrorSource source)
        {
            if (exception is OperationCanceledException)
            {
                return;
            }

            Owner.Tree.ReportError(exception, source, Owner, Member, Line);
        }

        // Hook, then release the entry; a no-op when teardown already drained it.
        protected void EndEntry()
        {
            if (!Live)
            {
                return;
            }

            Live = false;
            TaskOverrunHooks.TaskFinished(this);
            Owner.ReleaseEntry(Generation, Slot, Version);
        }

        protected void ClearIdentity()
        {
            Owner = null;
            Member = null;
            Live = false;
        }

        private static void TerminateCore(object first, object second, Delegate fn, long aux)
        {
            var task = (LifetimeTaskEntry)first;
            if (task.Live)
            {
                TaskOverrunHooks.TaskGenerationEnded(task);
            }
        }
    }
}
