using System;
using System.Collections.Generic;

namespace Ecanakli.Janitor.DependencyInjection.Tests.PlayMode
{
    // Plain services that ask for their lifetime in the constructor.
    internal sealed class AlphaService
    {
        public readonly Lifetime Injected;

        public AlphaService(Lifetime lifetime)
        {
            Injected = lifetime;
        }
    }

    internal sealed class BetaService
    {
        public readonly Lifetime Injected;

        public BetaService(Lifetime lifetime)
        {
            Injected = lifetime;
        }
    }

    // The categories-service pattern: one injected lifetime, one area per category.
    internal sealed class CategoryLifetimes
    {
        public readonly Lifetime Root;
        public readonly Lifetime Popups;
        public readonly Lifetime Combat;

        public CategoryLifetimes(Lifetime life)
        {
            Root = life;
            Popups = life.CreateChild("Popups");
            Combat = life.CreateChild("Combat");
        }
    }

    internal sealed class TestSignal
    {
        public int Value;
    }

    // Records, in Dispose order, whether the injected lifetime of each disposable had already ended when its Dispose ran.
    internal sealed class DisposeRecorder
    {
        public readonly List<string> Names = new List<string>();
        public readonly List<bool> LifetimeEnded = new List<bool>();

        public void Record(string name, bool lifetimeEnded)
        {
            Names.Add(name);
            LifetimeEnded.Add(lifetimeEnded);
        }
    }

    // A service disposable. It injects its own lifetime (a child of the scene lifetime), which ends only when the scene lifetime does.
    internal abstract class RecordingDisposable : IDisposable
    {
        private readonly DisposeRecorder _recorder;
        private readonly Lifetime _lifetime;
        private readonly string _name;

        protected RecordingDisposable(DisposeRecorder recorder, Lifetime lifetime, string name)
        {
            _recorder = recorder;
            _lifetime = lifetime;
            _name = name;
        }

        public void Dispose()
        {
            _recorder.Record(_name, _lifetime.IsDisposed);
            AfterRecord();
        }

        protected virtual void AfterRecord()
        {
        }
    }

    internal sealed class HighPriorityDisposable : RecordingDisposable
    {
        public HighPriorityDisposable(DisposeRecorder recorder, Lifetime lifetime)
            : base(recorder, lifetime, "high")
        {
        }
    }

    internal sealed class DefaultPriorityDisposable : RecordingDisposable
    {
        public DefaultPriorityDisposable(DisposeRecorder recorder, Lifetime lifetime)
            : base(recorder, lifetime, "default")
        {
        }
    }

    internal sealed class LowPriorityDisposable : RecordingDisposable
    {
        public LowPriorityDisposable(DisposeRecorder recorder, Lifetime lifetime)
            : base(recorder, lifetime, "low")
        {
        }
    }

    // Throws from Dispose, like a service whose cleanup fails.
    internal sealed class ThrowingDisposable : RecordingDisposable
    {
        public ThrowingDisposable(DisposeRecorder recorder, Lifetime lifetime)
            : base(recorder, lifetime, "throwing")
        {
        }

        protected override void AfterRecord()
        {
            throw new InvalidOperationException("the service failed to dispose");
        }
    }

    // Disposed after the thrower: Zenject's DisposableManager skips it, which is why the scene disposer must run before both.
    internal sealed class AfterThrowDisposable : RecordingDisposable
    {
        public AfterThrowDisposable(DisposeRecorder recorder, Lifetime lifetime)
            : base(recorder, lifetime, "after")
        {
        }
    }
}
