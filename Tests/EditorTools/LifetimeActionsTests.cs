using System;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.EditorTools.Tests
{
    // The Cancel and Ping owner buttons: what they do to a lifetime, when they are available, when they ask first.
    [TestFixture]
    public sealed class LifetimeActionsTests : DiagnosticsTestBase
    {
        private GameObject _owner;

        [TearDown]
        public void TearDown()
        {
            if (_owner != null)
            {
                Object.DestroyImmediate(_owner);
            }
        }

        // Cancel

        [Test]
        public void Cancel_ActiveLifetime_RunsItsEntriesAndKeepsItUsable()
        {
            var area = Scope.App.CreateChild("Area");
            var cancelled = 0;
            area.OnCancel(() => cancelled++);

            var outcome = LifetimeActions.Cancel(RowFor(area), null);

            Assert.That(outcome, Is.EqualTo(CancelOutcome.Cancelled));
            Assert.That(cancelled, Is.EqualTo(1));
            Assert.That(area.IsDisposed, Is.False, "Cancel ends a generation, not the lifetime");
            Assert.That(area.EntryCount, Is.Zero);
        }

        [Test]
        public void Cancel_ActiveLifetime_DoesNotAskForConfirmation()
        {
            var area = Scope.App.CreateChild("Area");
            var asked = 0;

            LifetimeActions.Cancel(RowFor(area), () =>
            {
                asked++;
                return true;
            });

            Assert.That(asked, Is.Zero);
        }

        [Test]
        public void Cancel_LifetimeWithDescendants_CancelsThemToo()
        {
            var area = Scope.App.CreateChild("Area");
            var child = area.CreateChild("Child");
            var cancelled = 0;
            child.OnCancel(() => cancelled++);

            LifetimeActions.Cancel(RowFor(area), null);

            Assert.That(cancelled, Is.EqualTo(1));
        }

        [Test]
        public void Cancel_AppLifetimeConfirmed_Cancels()
        {
            var cancelled = 0;
            Scope.App.OnCancel(() => cancelled++);
            var asked = 0;

            var outcome = LifetimeActions.Cancel(RowFor(Scope.App, LifetimeKind.App), () =>
            {
                asked++;
                return true;
            });

            Assert.That(outcome, Is.EqualTo(CancelOutcome.Cancelled));
            Assert.That(asked, Is.EqualTo(1), "App is the one lifetime that asks first");
            Assert.That(cancelled, Is.EqualTo(1));
        }

        [Test]
        public void Cancel_AppLifetimeDeclined_DoesNothing()
        {
            var cancelled = 0;
            Scope.App.OnCancel(() => cancelled++);

            var outcome = LifetimeActions.Cancel(RowFor(Scope.App, LifetimeKind.App), () => false);

            Assert.That(outcome, Is.EqualTo(CancelOutcome.Declined));
            Assert.That(cancelled, Is.Zero);
            Assert.That(Scope.App.EntryCount, Is.EqualTo(1), "the entry must still be registered");
        }

        [Test]
        public void Cancel_AppLifetimeWithoutAConfirmDelegate_IsDeclined()
        {
            var cancelled = 0;
            Scope.App.OnCancel(() => cancelled++);

            var outcome = LifetimeActions.Cancel(RowFor(Scope.App, LifetimeKind.App), null);

            Assert.That(outcome, Is.EqualTo(CancelOutcome.Declined));
            Assert.That(cancelled, Is.Zero);
        }

        [Test]
        public void Cancel_DisposedRow_IsANoOpAndDoesNotAsk()
        {
            var area = Scope.App.CreateChild("Area");
            var cancelled = 0;
            area.OnCancel(() => cancelled++);
            var asked = 0;

            var outcome = LifetimeActions.Cancel(RowFor(area, LifetimeKind.Area, LifetimeState.Disposed), () =>
            {
                asked++;
                return true;
            });

            Assert.That(outcome, Is.EqualTo(CancelOutcome.Unavailable));
            Assert.That(cancelled, Is.Zero);
            Assert.That(asked, Is.Zero);
        }

        [Test]
        public void Cancel_LifetimeDisposedAfterTheRowWasCaptured_IsANoOp()
        {
            var area = Scope.App.CreateChild("Area");
            var row = RowFor(area);
            area.Dispose();

            var outcome = LifetimeActions.Cancel(row, null);

            Assert.That(outcome, Is.EqualTo(CancelOutcome.Unavailable));
            Assert.That(LifetimeActions.CanCancel(row), Is.False);
        }

        [Test]
        public void Cancel_NullRow_IsUnavailable()
        {
            Assert.That(LifetimeActions.Cancel(null, null), Is.EqualTo(CancelOutcome.Unavailable));
        }

        [Test]
        public void Cancel_RowWithoutALifetimeObject_IsUnavailable()
        {
            var row = new LifetimeRow();
            var node = SnapshotBuilder.Node(1, 0, "restored");
            row.Update(in node);

            Assert.That(LifetimeActions.Cancel(row, null), Is.EqualTo(CancelOutcome.Unavailable));
        }

        [Test]
        public void Cancel_EntryThatThrows_StillReportsCancelledAndRoutesTheError()
        {
            var area = Scope.App.CreateChild("Area");
            area.OnCancel(() => throw new InvalidOperationException("boom"));

            var outcome = LifetimeActions.Cancel(RowFor(area), null);

            Assert.That(outcome, Is.EqualTo(CancelOutcome.Cancelled));
            Assert.That(Scope.Errors.Count, Is.EqualTo(1), "the failure goes to the error handler, not to the button");
            Scope.Errors.Clear();
        }

        [Test]
        public void Cancel_SelectedLifetimeOfTheViewModel_CancelsItsEntriesAndAdvancesTheGeneration()
        {
            Scope.Tree.MakeDefault();
            var area = Scope.App.CreateChild("Area");
            var cancelled = 0;
            area.OnCancel(() => cancelled++);
            var vm = new JanitorViewModel();
            vm.Refresh(true, true);
            vm.SelectLifetime(area.DiagId);
            var before = vm.Details.Row.GenerationText;

            var outcome = LifetimeActions.Cancel(vm.Details.Row, null);
            vm.Refresh(true, true);

            Assert.That(outcome, Is.EqualTo(CancelOutcome.Cancelled));
            Assert.That(cancelled, Is.EqualTo(1));
            Assert.That(vm.Details.Row.GenerationText, Is.Not.EqualTo(before));
            Assert.That(vm.Details.Entries.Rows, Is.Empty);
        }

        // Availability

        [Test]
        public void NeedsConfirmation_OnlyForTheAppLifetime()
        {
            var area = Scope.App.CreateChild("Area");

            Assert.That(LifetimeActions.NeedsConfirmation(RowFor(Scope.App, LifetimeKind.App)), Is.True);
            Assert.That(LifetimeActions.NeedsConfirmation(RowFor(area)), Is.False);
            Assert.That(LifetimeActions.NeedsConfirmation(RowFor(area, LifetimeKind.Scene)), Is.False);
            Assert.That(LifetimeActions.NeedsConfirmation(null), Is.False);
        }

        [Test]
        public void CanCancel_ActiveLifetimeWithAnObject_IsTrue()
        {
            var area = Scope.App.CreateChild("Area");

            Assert.That(LifetimeActions.CanCancel(RowFor(area)), Is.True);
            Assert.That(LifetimeActions.CanCancel(null), Is.False);
        }

        // Ping owner

        [Test]
        public void CanPing_LiveOwner_IsTrue()
        {
            _owner = new GameObject("owner");
            var area = Scope.App.CreateChild("Area");

            Assert.That(LifetimeActions.CanPing(RowFor(area, LifetimeKind.GameObject, LifetimeState.Active, _owner)), Is.True);
        }

        [Test]
        public void CanPing_NoOwner_IsFalse()
        {
            var area = Scope.App.CreateChild("Area");

            Assert.That(LifetimeActions.CanPing(RowFor(area)), Is.False);
            Assert.That(LifetimeActions.CanPing(null), Is.False);
        }

        [Test]
        public void CanPing_OwnerDestroyed_IsFalse()
        {
            _owner = new GameObject("owner");
            var area = Scope.App.CreateChild("Area");
            var row = RowFor(area, LifetimeKind.GameObject, LifetimeState.Active, _owner);
            Object.DestroyImmediate(_owner);
            _owner = null;

            Assert.That(LifetimeActions.CanPing(row), Is.False, "Unity's null check sees the destroyed object");
            Assert.That(LifetimeActions.Ping(row), Is.False);
        }

        [Test]
        public void CanPing_RowMarkedAsOrphaned_IsFalse()
        {
            _owner = new GameObject("owner");
            var area = Scope.App.CreateChild("Area");
            var node = SnapshotBuilder.Node(area.DiagId, 0, "Area");
            node.Lifetime = area;
            node.Owner = _owner;
            node.OwnerDestroyed = true;
            var row = new LifetimeRow();
            row.Update(in node);

            Assert.That(LifetimeActions.CanPing(row), Is.False);
        }

        [Test]
        public void Ping_LiveOwner_ReturnsTrue()
        {
            _owner = new GameObject("owner");
            var area = Scope.App.CreateChild("Area");

            Assert.That(LifetimeActions.Ping(RowFor(area, LifetimeKind.GameObject, LifetimeState.Active, _owner)), Is.True);
        }

        [Test]
        public void Ping_NoOwner_ReturnsFalse()
        {
            var area = Scope.App.CreateChild("Area");

            Assert.That(LifetimeActions.Ping(RowFor(area)), Is.False);
        }

        // Ping a warning's context

        [Test]
        public void PingContext_LiveObject_ReturnsTrue()
        {
            _owner = new GameObject("context");

            Assert.That(LifetimeActions.PingContext(_owner), Is.True);
        }

        [Test]
        public void PingContext_NoObject_ReturnsFalse()
        {
            Assert.That(LifetimeActions.PingContext(null), Is.False);
        }

        [Test]
        public void PingContext_DestroyedObject_ReturnsFalse()
        {
            _owner = new GameObject("context");
            var context = _owner;
            Object.DestroyImmediate(_owner);
            _owner = null;

            Assert.That(LifetimeActions.PingContext(context), Is.False, "Unity's null check sees the destroyed object");
        }

        private static LifetimeRow RowFor(Lifetime lifetime, LifetimeKind kind = LifetimeKind.Area, LifetimeState state = LifetimeState.Active, Object owner = null)
        {
            var node = SnapshotBuilder.Node(lifetime.DiagId, 0, "row", kind);
            node.State = state;
            node.Lifetime = lifetime;
            node.Owner = owner;
            var row = new LifetimeRow();
            row.Update(in node);
            return row;
        }
    }
}
