using System;
using System.Collections;
using UnityEngine;

namespace Ecanakli.Janitor
{
    // Wraps a user routine and steps it by hand, so the package always knows when it ended and never calls
    // StopCoroutine with a null handle. Not pooled. Every member runs on the main thread.
    internal sealed class LifetimeCoroutine : IEnumerator, IDisposable
    {
        // The owner's entry stores the wrapper; teardown stops it, and the probe sweep reclaims dead ones.
        internal static readonly EntryTerminate Terminate = TerminateCore;
        internal static readonly Func<LifetimeCoroutine, bool> IsFinishedProbe = IsFinishedCore;

        private readonly Lifetime _lifetime;
        private readonly MonoBehaviour _host;
        private readonly string _member;
        private readonly int _line;

        // The host's deactivation counter at start; a change means Unity stopped this coroutine.
        private readonly ActiveLifetimeTrigger _trigger;
        private readonly int _deactivations;

        private IEnumerator _routine;
        private object _current;
        private Coroutine _handle;
        private int _generation;
        private int _slot;
        private int _version;

        // Terminated or ended: a stray resume yields nothing.
        private bool _stopped;

        // The routine ended by itself, failed, or was never started; the entry is gone.
        private bool _finished;

        private bool _stepping;
        private bool _disposeAfterStep;

        private LifetimeCoroutine(Lifetime lifetime, MonoBehaviour host, IEnumerator routine, string member, int line, ActiveLifetimeTrigger trigger)
        {
            _lifetime = lifetime;
            _host = host;
            _routine = routine;
            _member = member;
            _line = line;
            _trigger = trigger;
            _deactivations = ReferenceEquals(trigger, null) ? 0 : trigger.Deactivations;
        }

        public object Current => _current;

        // Runs the pre-checks, registers the wrapper and starts it. Arguments are validated by the caller.
        internal static LifetimeRegistration Start(Lifetime lifetime, MonoBehaviour host, IEnumerator routine, string member, int line)
        {
            lifetime.Tree.Guard.EnsureMainThread("Lifetime.StartCoroutine");
            if (lifetime.State != LifetimeState.Active)
            {
                return default;
            }

            // Unity's == covers both a C# null and a destroyed host. Checked before Unity can log its own error.
            if (host == null || !host.gameObject.activeInHierarchy)
            {
                DevWarnings.CoroutineNotStarted(lifetime, host, member, line);
                return default;
            }

            // Unity never disposes a stopped coroutine; the host's deactivation counter is the only sign it died. Added unbound.
            ActiveLifetimeTrigger trigger;
            try
            {
                trigger = ActiveLifetimeTrigger.GetOrAdd(host.gameObject);
            }
            catch (Exception exception)
            {
                lifetime.Tree.ReportError(exception, LifetimeErrorSource.Coroutine, lifetime, member, line);
                return default;
            }

            var coroutine = new LifetimeCoroutine(lifetime, host, routine, member, line, trigger);
            var registration = lifetime.Register(coroutine, null, null, Terminate, IsFinishedProbe, EntryInvoker<LifetimeCoroutine>.IsFinished, member, line);

            // Refused, or ended while registering (a sweep probe ran user code): Terminate has already run.
            if (!registration.IsActive)
            {
                return default;
            }

            coroutine.Bind(in registration);

            // The first step of the routine runs inside StartCoroutine, before the handle exists.
            Coroutine handle;
            try
            {
                handle = host.StartCoroutine(coroutine);
            }
            catch (Exception exception)
            {
                coroutine.Report(exception);
                coroutine.Finish();
                return default;
            }

            return coroutine.Started(handle, in registration);
        }

        public bool MoveNext()
        {
            if (_stopped)
            {
                _current = null;
                return false;
            }

            var more = Step();
            if (more && !_stopped)
            {
                return true;
            }

            // Ended, failed, or stopped during the step (the routine cancelled its own lifetime).
            _current = null;
            if (!more)
            {
                Finish();
            }

            if (_disposeAfterStep)
            {
                _disposeAfterStep = false;
                var routine = _routine;
                Finish();
                DisposeRoutine(routine);
            }

            return false;
        }

        public void Reset()
        {
            throw new NotSupportedException();
        }

        // Unity 6000.3 never disposes a stopped coroutine, so only a manual call reaches this; it keeps finally blocks right if an engine version disposes.
        public void Dispose()
        {
            if (_stepping)
            {
                // The routine is running right now; it is disposed as soon as its step returns.
                _stopped = true;
                _disposeAfterStep = true;
                return;
            }

            var routine = _routine;
            Finish();
            DisposeRoutine(routine);
        }

        // Remembers where the owner's entry lives, so the wrapper can remove it when it ends by itself.
        private void Bind(in LifetimeRegistration registration)
        {
            _generation = _lifetime.Generation;
            _slot = registration.EntryId;
            _version = registration.EntryVersion;
        }

        private static void TerminateCore(object first, object second, Delegate fn, long aux)
        {
            ((LifetimeCoroutine)first).Stop();
        }

        private static bool IsFinishedCore(LifetimeCoroutine coroutine)
        {
            // Unity stops a coroutine when its host is destroyed or deactivated; the host check must come first.
            var host = coroutine._host;
            if (coroutine._finished || host == null || !host.gameObject.activeInHierarchy)
            {
                return true;
            }

            // Unity never restarts it, so a deactivation since start ends it even though the host is active again.
            var trigger = coroutine._trigger;
            return !ReferenceEquals(trigger, null) && trigger.Deactivations != coroutine._deactivations;
        }

        private bool Step()
        {
            _stepping = true;
            try
            {
                if (!_routine.MoveNext())
                {
                    return false;
                }

                _current = _routine.Current;
                return true;
            }
            catch (Exception exception)
            {
                Report(exception);
                return false;
            }
            finally
            {
                _stepping = false;
            }
        }

        private LifetimeRegistration Started(Coroutine handle, in LifetimeRegistration registration)
        {
            if (handle == null)
            {
                // Finished on its first step (a routine that never yields), or refused by Unity: nothing runs, so nothing stays registered.
                Finish();
                return default;
            }

            if (_stopped)
            {
                StopHandle(handle);
                return default;
            }

            _handle = handle;
            return registration;
        }

        // The terminate action: never throws, and a repeated or early call only sets the flag.
        private void Stop()
        {
            if (_stopped)
            {
                return;
            }

            _stopped = true;
            var handle = _handle;
            _handle = null;
            if (handle != null)
            {
                StopHandle(handle);
            }
        }

        private void StopHandle(Coroutine handle)
        {
            var host = _host;
            if (host == null)
            {
                // A destroyed host has already lost all its coroutines.
                return;
            }

            try
            {
                host.StopCoroutine(handle);
            }
            catch (Exception exception)
            {
                Report(exception);
            }
        }

        // Ends the wrapper without terminating anything: removes its own entry and drops the routine.
        private void Finish()
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            _stopped = true;
            _routine = null;
            _handle = null;
            _lifetime.ReleaseEntry(_generation, _slot, _version);
        }

        private void DisposeRoutine(IEnumerator routine)
        {
            var disposable = routine as IDisposable;
            if (disposable == null)
            {
                return;
            }

            try
            {
                disposable.Dispose();
            }
            catch (Exception exception)
            {
                Report(exception);
            }
        }

        private void Report(Exception exception)
        {
            _lifetime.Tree.ReportError(exception, LifetimeErrorSource.Coroutine, _lifetime, _member, _line);
        }
    }
}
