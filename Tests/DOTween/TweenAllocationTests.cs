using Cysharp.Threading.Tasks;
using DG.Tweening;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace Ecanakli.Janitor.Tests.TweenBinding
{
    // tween.AddTo(owner) and a warm AwaitCompletionAsync(lifetime) allocate nothing. Tweens are made
    // outside the measured block. Every measured delegate runs twice before it is measured (Mono allocates on the first run
    // of an un-run lambda, and every generic helper creates its cached delegates on first use), and each test asserts
    // afterwards that the measured block did the work.
    [TestFixture]
    public sealed class TweenAllocationTests
    {
        private const int Count = 16;

        private TweenSession _s;
        private Lifetime _area;
        private TweenTestHost _host;
        private Transform _hostTransform;
        private GameObject _hostObject;
        private Tween[] _batch;
        private UniTask[] _tasks;

        [SetUp]
        public void SetUp()
        {
            _s = TweenSession.Begin();
            _area = _s.Area();
            _host = _s.NewHost();
            _hostTransform = _host.transform;
            _hostObject = _host.gameObject;
            _tasks = new UniTask[Count];
        }

        [TearDown]
        public void TearDown()
        {
            _s.Complete();
        }

        [Test]
        public void AddTo_Lifetime_KillMode_WarmCall_AllocatesNothing()
        {
            AssertAddAllocatesNothing(AddBatchToLifetime, _area);
        }

        [Test]
        public void AddTo_Lifetime_CompleteMode_WarmCall_AllocatesNothing()
        {
            AssertAddAllocatesNothing(AddBatchToLifetimeComplete, _area);
        }

        [Test]
        public void AddTo_MonoBehaviourOwner_WarmCall_AllocatesNothing()
        {
            AssertAddAllocatesNothing(AddBatchToHost, _host.GetLifetime());
        }

        [Test]
        public void AddTo_TransformOwner_WarmCall_AllocatesNothing()
        {
            AssertAddAllocatesNothing(AddBatchToTransform, _hostObject.GetLifetime());
        }

        [Test]
        public void AddTo_GameObjectOwner_WarmCall_AllocatesNothing()
        {
            AssertAddAllocatesNothing(AddBatchToGameObject, _hostObject.GetLifetime());
        }

        [Test]
        public void AwaitCompletionAsync_Lifetime_WarmPool_RegistrationAllocatesNothing()
        {
            WarmAwaitRounds();
            _batch = _s.NewSteppedTweens(Count);
            Assert.That(TweenCompletionPromise.PooledCount, Is.GreaterThanOrEqualTo(Count), "premise: the pool holds a promise per await");
            TestDelegate awaitBatch = AwaitBatch;

            Assert.That(awaitBatch, Is.Not.AllocatingGCMemory());

            Assert.That(_area.EntryCount, Is.EqualTo(Count), "the measured block must really have registered");
            for (var i = 0; i < Count; i++)
            {
                TweenAwaits.AssertStatus(_tasks[i], UniTaskStatus.Pending);
            }

            _s.Step(2f);
            ConsumeAll();
        }

        [Test]
        public void AwaitCompletionAsync_Lifetime_WarmPool_AwaitCompleteAndConsumeAllocatesNothing()
        {
            for (var round = 0; round < 2; round++)
            {
                _batch = _s.NewSteppedTweens(Count);
                CycleBatch();
                _area.Cancel();
            }

            _batch = _s.NewSteppedTweens(Count);
            var pooledBefore = TweenCompletionPromise.PooledCount;
            TestDelegate cycle = CycleBatch;

            Assert.That(cycle, Is.Not.AllocatingGCMemory(), "if this fails, the baseline test below tells whether DOTween's own Complete allocates");

            for (var i = 0; i < Count; i++)
            {
                Assert.That(_batch[i].IsActive(), Is.False, "the measured block must really have completed the tweens");
            }

            Assert.That(TweenCompletionPromise.PooledCount, Is.EqualTo(pooledBefore), "and every rented promise came back");
        }

        [Test]
        public void Baseline_CompletingStartedTweensWithoutJanitor_AllocatesNothing()
        {
            for (var round = 0; round < 2; round++)
            {
                _batch = _s.NewSteppedTweens(Count);
                CompleteBatch();
            }

            _batch = _s.NewSteppedTweens(Count);
            TestDelegate complete = CompleteBatch;

            Assert.That(complete, Is.Not.AllocatingGCMemory(), "DOTween's own Complete must be free, or the cycle test above cannot be 0 B");

            for (var i = 0; i < Count; i++)
            {
                Assert.That(_batch[i].IsActive(), Is.False);
            }
        }

        private void AssertAddAllocatesNothing(TestDelegate add, Lifetime lifetime)
        {
            _batch = _s.NewSteppedTweens(Count);
            add();
            lifetime.Cancel();
            _batch = _s.NewSteppedTweens(Count);
            add();
            lifetime.Cancel();
            _batch = _s.NewSteppedTweens(Count);

            Assert.That(add, Is.Not.AllocatingGCMemory());

            Assert.That(lifetime.EntryCount, Is.EqualTo(Count), "the measured block must really have registered");
            lifetime.Cancel();
            for (var i = 0; i < Count; i++)
            {
                Assert.That(_batch[i].IsActive(), Is.False, "and Cancel must have killed what it registered");
            }
        }

        // Two rounds that rent, settle and return Count promises, so the measured round pops from a warm pool.
        private void WarmAwaitRounds()
        {
            for (var round = 0; round < 2; round++)
            {
                _batch = _s.NewSteppedTweens(Count);
                AwaitBatch();
                _s.Step(2f);
                ConsumeAll();
                _area.Cancel();
            }
        }

        private void ConsumeAll()
        {
            for (var i = 0; i < Count; i++)
            {
                TweenAwaits.AssertStatus(_tasks[i], UniTaskStatus.Succeeded);
                TweenAwaits.Consume(_tasks[i]);
            }
        }

        private void AddBatchToLifetime()
        {
            for (var i = 0; i < Count; i++)
            {
                _batch[i].AddTo(_area);
            }
        }

        private void AddBatchToLifetimeComplete()
        {
            for (var i = 0; i < Count; i++)
            {
                _batch[i].AddTo(_area, TweenCancelMode.Complete);
            }
        }

        private void AddBatchToHost()
        {
            for (var i = 0; i < Count; i++)
            {
                _batch[i].AddTo(_host);
            }
        }

        private void AddBatchToTransform()
        {
            for (var i = 0; i < Count; i++)
            {
                _batch[i].AddTo(_hostTransform);
            }
        }

        private void AddBatchToGameObject()
        {
            for (var i = 0; i < Count; i++)
            {
                _batch[i].AddTo(_hostObject);
            }
        }

        private void AwaitBatch()
        {
            for (var i = 0; i < Count; i++)
            {
                _tasks[i] = _batch[i].AwaitCompletionAsync(_area);
            }
        }

        private void CycleBatch()
        {
            for (var i = 0; i < Count; i++)
            {
                var task = _batch[i].AwaitCompletionAsync(_area);
                _batch[i].Complete();
                task.GetAwaiter().GetResult();
            }
        }

        private void CompleteBatch()
        {
            for (var i = 0; i < Count; i++)
            {
                _batch[i].Complete();
            }
        }
    }
}
