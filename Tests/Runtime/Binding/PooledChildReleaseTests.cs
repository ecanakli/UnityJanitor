using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ecanakli.Janitor.Tests.Binding
{
    // A pooled child that returns itself to the pool from the OnCancel action of its active lifetime, while its parent is deactivated.
    [TestFixture]
    public sealed class PooledChildReleaseTests
    {
        private BindingSession _s;
        private GameObject _parent;
        private GameObject _child;
        private ReleasingItem _item;

        [SetUp]
        public void SetUp()
        {
            _s = BindingSession.Begin();
            _parent = _s.NewObject("Parent");
            _child = _s.NewInactiveObject("Child");
            _child.transform.SetParent(_parent.transform, false);
            _item = _child.AddComponent<ReleasingItem>();
            _child.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            _item.Disarmed = true;
            _s.Complete();
        }

        [Test]
        public void ParentDeactivated_TheChildReleasesItselfFromTheCancelAction_AndEndsUpOffWithoutAnyError()
        {
            Assert.That(_item.Enabled, Is.EqualTo(1));
            Assert.That(_item.Released, Is.Zero);

            _parent.SetActive(false);

            Assert.That(_item.Released, Is.EqualTo(1), "the cancel action ran once");
            Assert.That(_child.activeSelf, Is.False, "the release turned the child off");
            Assert.That(_child.activeInHierarchy, Is.False);
            Assert.That(_item.GetActiveLifetime().EntryCount, Is.Zero);
            Assert.That(_s.Errors.Count, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ParentShownAgain_AReleasedChildStaysOffAndIsNotEnabledUntilItIsActivatedItself()
        {
            _parent.SetActive(false);

            _parent.SetActive(true);

            Assert.That(_child.activeSelf, Is.False, "the parent does not bring the released child back");
            Assert.That(_child.activeInHierarchy, Is.False);
            Assert.That(_item.Enabled, Is.EqualTo(1), "no enable without a new activation of the child");

            _child.SetActive(true);

            Assert.That(_item.Enabled, Is.EqualTo(2));
            Assert.That(_child.activeInHierarchy, Is.True);
            Assert.That(_item.GetActiveLifetime().EntryCount, Is.EqualTo(1), "the new use registered its cancel action");

            _parent.SetActive(false);

            Assert.That(_item.Released, Is.EqualTo(2));
            Assert.That(_child.activeSelf, Is.False);
            Assert.That(_s.Errors.Count, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ParentDeactivated_AReleaseThatSkipsAnObjectNotActiveInTheHierarchy_LeavesTheChildSelfActive_SoItEnablesWithTheParent()
        {
            _item.SkipWhenNotActiveInHierarchy = true;

            _parent.SetActive(false);

            Assert.That(_item.Released, Is.EqualTo(1), "the cancel action still ran");
            Assert.That(_child.activeSelf, Is.True, "but it turned nothing off");
            Assert.That(_child.activeInHierarchy, Is.False);

            _parent.SetActive(true);

            Assert.That(_item.Enabled, Is.EqualTo(2), "the child enables again with the parent");
            Assert.That(_child.activeInHierarchy, Is.True);
            Assert.That(_item.GetActiveLifetime().EntryCount, Is.EqualTo(1));
            Assert.That(_s.Errors.Count, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
