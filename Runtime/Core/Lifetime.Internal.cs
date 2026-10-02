using System;
using System.Threading;

namespace Ecanakli.Janitor
{
    public sealed partial class Lifetime
    {
        internal readonly LifetimeTree Tree;
        internal readonly LifetimeKind Kind;
        internal readonly bool PackageOwned;
        internal readonly string CreatorMember;
        internal readonly int CreatorLine;

        // Tree links: intrusive, children in creation order.
        internal Lifetime Parent;
        internal Lifetime FirstChild;
        internal Lifetime LastChild;
        internal Lifetime PrevSibling;
        internal Lifetime NextSibling;
        internal int ChildCount;

        // Unity binding: the owning object, the owner index key, the scene handle (scene lifetimes only) and the destroy-token registration.
        internal UnityEngine.Object OwnerObject;
        internal int OwnerKey;
        internal int SceneKey;
        internal CancellationTokenRegistration OwnerTokenRegistration;

        private readonly string _name;

        private LifetimeState _state;
        private int _opId;
        private int _generation;
        private CancellationTokenSource _cts;
        private EntryList _entries;
        private bool _sweeping;

        // Set by the mark pass for a placed object lifetime that a category dispose cancels and re-homes.
        private bool _rehome;

        internal Lifetime(LifetimeTree tree, string name, LifetimeKind kind, bool packageOwned, UnityEngine.Object owner, string member, int line)
        {
            Tree = tree;
            _name = name;
            Kind = kind;
            PackageOwned = packageOwned;
            OwnerObject = owner;
            CreatorMember = member;
            CreatorLine = line;
            if (kind == LifetimeKind.Active)
            {
                ActivityGate = this;
            }

            LifetimeDiagnostics.LifetimeCreated(this);
        }

        internal LifetimeState State => _state;

        internal int OpId => _opId;

        internal int Generation => _generation;

        internal int EntryCount => _entries.Count;

        internal int EntryCapacity => _entries.Capacity;

        // Display label for errors and warnings; allocates, so it is used on those paths only.
        internal string Label => _name ?? (CreatorMember != null ? CreatorMember + ":" + CreatorLine : null);

        // True while this node belongs to the running operation, whichever way that operation treats it.
        internal bool IsInOperation(int opId)
        {
            return _opId == opId && (_state == LifetimeState.Cancelling || _state == LifetimeState.Disposing);
        }

        internal bool IsRehome => _rehome;

        internal void Mark(LifetimeState state, int opId)
        {
            _state = state;
            _opId = opId;
            _rehome = false;
        }

        // A placed object lifetime under a category being disposed: cancelled now, re-homed at finalize.
        internal void MarkRehome(int opId)
        {
            _state = LifetimeState.Cancelling;
            _opId = opId;
            _rehome = true;
        }

        internal void CancelOnMainThread()
        {
            Tree.Teardown.Cancel(this);
        }

        internal void DisposeOnMainThread()
        {
            if (PackageOwned)
            {
                if (_state != LifetimeState.Disposed)
                {
                    DevWarnings.DisposeIgnored(this);
                }

                return;
            }

            Tree.Teardown.Dispose(this);
        }

        // Main thread only; the owner (destroy, scene disposal, app exit) may dispose package-owned lifetimes.
        internal void DisposeFromOwner()
        {
            Tree.Teardown.Dispose(this);
        }

        internal Lifetime CreateChildCore(string name, LifetimeKind kind, bool packageOwned, UnityEngine.Object owner, string member, int line)
        {
            var child = new Lifetime(Tree, name, kind, packageOwned, owner, member, line);

            // An area works only while the GameObject of the active lifetime above it is active.
            if (kind == LifetimeKind.Area || kind == LifetimeKind.Injected)
            {
                child.ActivityGate = ActivityGate;
            }

            if (_state == LifetimeState.Disposed)
            {
                if (Kind == LifetimeKind.Scene)
                {
                    SceneBinding.WarnDisposedScene(this);
                }

                child._state = LifetimeState.Disposed;
                return child;
            }

            AttachChild(child);

            if (_state != LifetimeState.Active && Tree.Teardown.TryAddBorn(child, _opId))
            {
                // Born inside a running operation: it joins that operation and is finalized with it.
                child.Mark(_state == LifetimeState.Disposing ? LifetimeState.Disposing : LifetimeState.Cancelling, _opId);
            }

            return child;
        }

        // Appends a detached child as the newest one. Used for new children and for re-homing.
        internal void AttachChild(Lifetime child)
        {
            child.Parent = this;
            child.PrevSibling = LastChild;
            child.NextSibling = null;
            if (LastChild != null)
            {
                LastChild.NextSibling = child;
            }
            else
            {
                FirstChild = child;
            }

            LastChild = child;
            ChildCount++;
            LifetimeDiagnostics.ChildAttached(this);
        }

        internal void Unlink()
        {
            var parent = Parent;
            if (parent == null)
            {
                return;
            }

            if (PrevSibling != null)
            {
                PrevSibling.NextSibling = NextSibling;
            }
            else
            {
                parent.FirstChild = NextSibling;
            }

            if (NextSibling != null)
            {
                NextSibling.PrevSibling = PrevSibling;
            }
            else
            {
                parent.LastChild = PrevSibling;
            }

            parent.ChildCount--;
            Parent = null;
            PrevSibling = null;
            NextSibling = null;
        }

        // The generation token without the activity check: the caller registers right after, and registration refuses an inactive object.
        internal CancellationToken TokenForRegistration()
        {
            Tree.Guard.EnsureMainThread("Lifetime.Token");
            return _state != LifetimeState.Active ? new CancellationToken(true) : CurrentToken();
        }

        private CancellationToken CurrentToken()
        {
            if (_cts == null)
            {
                _cts = new CancellationTokenSource();
            }

            return _cts.Token;
        }

        // The single registration path. A lifetime that is not Active terminates the item on the spot.
        internal LifetimeRegistration Register(object a, object b, Delegate fn, EntryTerminate terminate, Delegate probe, EntryProbe probeInvoker, string member, int line, long aux = 0)
        {
            Tree.Guard.EnsureMainThread("registration");
            var entry = new EntrySlot
            {
                A = a,
                B = b,
                Fn = fn,
                Terminate = terminate,
                Probe = probe,
                ProbeInvoker = probeInvoker,
                Member = member,
                Aux = aux,
                Line = line,
            };

            if (_state != LifetimeState.Active)
            {
                if (_state == LifetimeState.Disposed && Kind == LifetimeKind.Scene)
                {
                    SceneBinding.WarnDisposedScene(this);
                }

                LifetimeDiagnostics.RegistrationRefused(this, member, line);
                RunTerminate(in entry);
                return default;
            }

            // An active lifetime, and any area below one, refuses work while its GameObject is inactive (one native call per registration).
            if (ActivityGate != null && RefuseForInactiveObject(member, line))
            {
                RunTerminate(in entry);
                return default;
            }

            _entries.Add(in entry, out var slot, out var version);
            var registration = new LifetimeRegistration(this, _generation, slot, version);
            if (_entries.ShouldSweep)
            {
                SweepEntries();
            }

            LifetimeDiagnostics.RegistrationAdded(this, slot, version);
            return registration;
        }

        internal bool IsRegistrationLive(int generation, int slot, int version)
        {
            return _generation == generation && _state != LifetimeState.Disposed && _entries.IsLive(slot, version);
        }

        // Read-only walk over the live entries, newest first: start at the head, then follow each entry's Next.
        internal int NewestEntryId => _entries.Head;

        internal ref EntrySlot EntryAt(int id)
        {
            return ref _entries.At(id);
        }

        internal void CancelRegistration(int generation, int slot, int version)
        {
            if (!Tree.Guard.IsMainThread)
            {
                Tree.Marshal.Enqueue(MarshalOp.CancelRegistration, this, generation, slot, version);
                DevWarnings.Marshalled("LifetimeRegistration.Cancel()", this);
                return;
            }

            CancelRegistrationOnMainThread(generation, slot, version);
        }

        internal void CancelRegistrationOnMainThread(int generation, int slot, int version)
        {
            if (_generation != generation || !_entries.TryTake(slot, version, out var entry))
            {
                return;
            }

            RunTerminate(in entry);
        }

        // Removes an entry without terminating it, for work that finished by itself. Stale ids are no-ops.
        internal void ReleaseEntry(int generation, int slot, int version)
        {
            if (_generation == generation)
            {
                _entries.TryTake(slot, version, out _);
            }
        }

        // Runs one terminate action in its own try/catch; never throws.
        internal void RunTerminate(in EntrySlot entry)
        {
            if (entry.Terminate == null)
            {
                return;
            }

#if UNITY_EDITOR
            // Lets an integration report from inside its cancel action and have the report attributed to this item.
            var previousContext = LifetimeDiagnostics.EnterTerminate(this, entry.Member, entry.Line);
#endif
            try
            {
                entry.Terminate(entry.A, entry.B, entry.Fn, entry.Aux);
            }
            catch (Exception exception)
            {
                Tree.ReportError(exception, LifetimeErrorSource.CancelAction, this, entry.Member, entry.Line);
            }

#if UNITY_EDITOR
            LifetimeDiagnostics.ExitTerminate(previousContext);
#endif
        }

        // Signal pass: cancels this generation's token; callback failures are routed and never propagate.
        internal void SignalToken()
        {
            var cts = _cts;
            if (cts == null)
            {
                return;
            }

            try
            {
                cts.Cancel();
            }
            catch (AggregateException aggregate)
            {
                foreach (var inner in aggregate.Flatten().InnerExceptions)
                {
                    Tree.ReportError(inner, LifetimeErrorSource.TokenCallback, this, null, 0);
                }
            }
            catch (Exception exception)
            {
                Tree.ReportError(exception, LifetimeErrorSource.TokenCallback, this, null, 0);
            }
        }

        // Drain pass: pops entries newest first until the list is empty or another operation took this node over.
        internal void DrainEntries(int opId)
        {
            var state = _state;
            while (_opId == opId && _state == state && _entries.TryPopNewest(out var entry))
            {
                RunTerminate(in entry);
            }
        }

        // Finalize pass for Cancel: the token is dropped (never disposed) and a fresh generation opens.
        internal void FinishCancel()
        {
            LifetimeDiagnostics.GenerationCancelled(this);
            _cts = null;
            _generation++;
            _opId = 0;
            _rehome = false;
            _state = LifetimeState.Active;
        }

        // Finalize pass for Dispose: terminal; detaches from the parent, the indexes and any scene membership.
        // Scene lifetimes stay in the scene index until sceneUnloaded, so a later Get returns this disposed record.
        internal void FinishDispose()
        {
            LifetimeDiagnostics.LifetimeDisposed(this);
            _cts = null;
            _generation++;
            _opId = 0;
            _rehome = false;
            _state = LifetimeState.Disposed;
            Unlink();
            if (OwnerKey != 0)
            {
                Tree.Owners.Remove(OwnerKey, this);
            }

            DetachMembership();
            Members = null;
            OwnerTokenRegistration.Dispose();
            OwnerTokenRegistration = default;
        }

        // Amortized probe sweep: drops entries whose isFinished says true, without terminating them.
        private void SweepEntries()
        {
            if (_sweeping)
            {
                return;
            }

            _sweeping = true;
            try
            {
                if (_entries.ProbedCount > 0)
                {
                    SweepProbedEntries();
                }
            }
            finally
            {
                _sweeping = false;
                _entries.CompleteSweep();
            }
        }

        private void SweepProbedEntries()
        {
            var id = _entries.Head;
            while (id != 0)
            {
                ref var slot = ref _entries.At(id);
                var next = slot.Next;
                var probe = slot.Probe;
                var invoker = slot.ProbeInvoker;
                var state = slot.A;
                var member = slot.Member;
                var line = slot.Line;
                var version = slot.Version;
                var nextVersion = next != 0 ? _entries.At(next).Version : 0;

                if (probe != null && IsProbeFinished(invoker, state, probe, member, line))
                {
                    _entries.TryTake(id, version, out _);
                }

                // The probe is user code; if it changed the list under us, stop and let the next sweep continue.
                if (next != 0 && !_entries.IsLive(next, nextVersion))
                {
                    return;
                }

                id = next;
            }
        }

        private bool IsProbeFinished(EntryProbe invoker, object state, Delegate probe, string member, int line)
        {
            try
            {
                return invoker(state, probe);
            }
            catch (Exception exception)
            {
                Tree.ReportError(exception, LifetimeErrorSource.CancelAction, this, member, line);
                return false;
            }
        }
    }
}
