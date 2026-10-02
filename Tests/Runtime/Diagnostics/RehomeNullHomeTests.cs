#if UNITY_EDITOR
using Ecanakli.Janitor.Tests.Binding;
using NUnit.Framework;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.Tests.DiagnosticsTests
{
    // JANITOR114 records a placed object lifetime that was moved back under its scene. When the owner is destroyed inside the
    // category's cancel callback, there is no object left to move: the lifetime ends with the category and no re-home is recorded.
    // The tests also check what must hold whichever way the owner goes: nothing throws, the lifetime ends, the owner index is clean.
    [TestFixture]
    public sealed class RehomeNullHomeTests
    {
        private BindingSession _s;
        private DiagnosticsRecordingKit _d;

        [SetUp]
        public void SetUp()
        {
            _s = BindingSession.Begin();
            _d = new DiagnosticsRecordingKit();
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                _s.Complete();
            }
            finally
            {
                _d.Dispose();
            }
        }

        // An object that never ran Awake gets no OnDestroy, so nothing disposes its lifetime inside the callback: the finalize pass
        // finds no home for it.
        [Test]
        public void Dispose_OnACategoryWhoseCallbackDestroysAnOwnerThatNeverRan_EndsTheLifetimeAndRecordsNoRehome()
        {
            var go = _s.NewInactiveObject("NeverRan");
            var probe = go.AddComponent<BindingProbe>();
            var category = Lifetime.App.CreateChild("Popups");
            var lifetime = probe.GetLifetime(category);
            var key = probe.GetInstanceID();
            var ownersBefore = _s.Tree.Owners.Count;
            category.OnCancel(() => Object.DestroyImmediate(go));

            Assert.DoesNotThrow(() => category.Dispose());

            Assert.That(lifetime.IsDisposed, Is.True, "with no home to go to, the lifetime ends with its category");
            Assert.That(_s.Tree.Owners.TryGet(key, out _), Is.False, "nothing is left in the owner index");
            Assert.That(_s.Tree.Owners.Count, Is.EqualTo(ownersBefore - 1));
            Assert.That(_s.Tree.RehomeCount, Is.Zero, "no object was moved");
            Assert.That(_d.Count(DiagnosticIds.Rehomed), Is.Zero, "so no re-home is recorded");
        }

        // A running object is destroyed through its destroy token, which disposes the lifetime inside the callback.
        [Test]
        public void Dispose_OnACategoryWhoseCallbackDestroysARunningOwner_EndsTheLifetimeOnceAndRecordsNoRehome()
        {
            var probe = _s.NewProbe("Running");
            var go = probe.gameObject;
            var category = Lifetime.App.CreateChild("Popups");
            var lifetime = probe.GetLifetime(category);
            var key = probe.GetInstanceID();
            var ownersBefore = _s.Tree.Owners.Count;
            var ended = 0;
            lifetime.OnCancel(() => ended++);
            category.OnCancel(() => Object.DestroyImmediate(go));

            Assert.DoesNotThrow(() => category.Dispose());

            Assert.That(lifetime.IsDisposed, Is.True);
            Assert.That(ended, Is.EqualTo(1), "the work registered on the lifetime was stopped once");
            Assert.That(_s.Tree.Owners.TryGet(key, out _), Is.False);
            Assert.That(_s.Tree.Owners.Count, Is.EqualTo(ownersBefore - 1));
            Assert.That(_s.Tree.RehomeCount, Is.Zero);
            Assert.That(_d.Count(DiagnosticIds.Rehomed), Is.Zero);
        }

        // The control: the same category with an owner that survives is re-homed and recorded.
        [Test]
        public void Dispose_OnACategoryWhoseOwnerSurvives_RecordsTheRehome()
        {
            var probe = _s.NewProbe("Survivor");
            var category = Lifetime.App.CreateChild("Popups");
            var lifetime = probe.GetLifetime(category);

            category.Dispose();

            Assert.That(lifetime.IsDisposed, Is.False);
            Assert.That(_s.Tree.RehomeCount, Is.EqualTo(1));
            Assert.That(_d.Count(DiagnosticIds.Rehomed), Is.EqualTo(1));
        }
    }
}
#endif
