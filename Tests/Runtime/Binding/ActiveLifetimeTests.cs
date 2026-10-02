using System;
using System.Collections;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ecanakli.Janitor.Tests.Binding
{
    // The active-object lifetime and its trigger.
    [TestFixture]
    public sealed class ActiveLifetimeTests
    {
        private BindingSession _s;
        private CallLog _log;

        [SetUp]
        public void SetUp()
        {
            _s = BindingSession.Begin();
            _log = new CallLog();
        }

        [TearDown]
        public void TearDown()
        {
            _s.Complete();
        }

        // Guard: ActiveLifetimeTrigger.OnDisable cancels the bound lifetime, which opens the next generation.
        [Test]
        public void Deactivate_CancelsTheActiveLifetime_AndReactivationOpensANewGeneration()
        {
            var probe = _s.NewProbe();
            var lifetime = probe.GetActiveLifetime();
            var firstToken = lifetime.Token;
            lifetime.Record(_log, "gen0");
            Assert.That(lifetime.Generation, Is.Zero);

            probe.gameObject.SetActive(false);

            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "gen0" }));
            Assert.That(firstToken.IsCancellationRequested, Is.True);
            Assert.That(lifetime.Generation, Is.EqualTo(1));
            Assert.That(lifetime.IsDisposed, Is.False, "deactivation cancels, it does not dispose");

            probe.gameObject.SetActive(true);

            var secondToken = lifetime.Token;
            Assert.That(secondToken.IsCancellationRequested, Is.False);
            Assert.That(secondToken, Is.Not.EqualTo(firstToken), "a generation has its own token");
            Assert.That(lifetime.Record(_log, "gen1").IsActive, Is.True);

            probe.gameObject.SetActive(false);

            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "gen0", "gen1" }));
            Assert.That(lifetime.Generation, Is.EqualTo(2));
            Assert.That(secondToken.IsCancellationRequested, Is.True);
        }

        [Test]
        public void Deactivate_DoesNotCancelTheComponentLifetime()
        {
            var probe = _s.NewProbe();
            var component = probe.GetLifetime();
            var active = probe.GetActiveLifetime();
            component.Record(_log, "component");
            active.Record(_log, "active");

            probe.gameObject.SetActive(false);

            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "active" }));
            Assert.That(component.Generation, Is.Zero);
            Assert.That(component.IsDisposed, Is.False);
        }

        // Pooling safety: a token from spawn 1 is dead in spawn 2.
        [UnityTest]
        public IEnumerator Deactivate_EndsATaskOfTheActiveLifetime_AndTheNextActivationStartsAFreshOne()
        {
            var probe = _s.NewProbe();
            var lifetime = probe.GetActiveLifetime();
            var firstToken = default(System.Threading.CancellationToken);
            var firstStage = 0;
            lifetime.Run(async ct =>
            {
                firstToken = ct;
                firstStage = 1;
                await UniTask.Delay(TimeSpan.FromSeconds(60), cancellationToken: ct);
                firstStage = 2;
            });
            Assert.That(lifetime.EntryCount, Is.EqualTo(1));

            probe.gameObject.SetActive(false);

            Assert.That(firstToken.IsCancellationRequested, Is.True);
            Assert.That(lifetime.EntryCount, Is.Zero);

            probe.gameObject.SetActive(true);
            var secondToken = default(System.Threading.CancellationToken);
            lifetime.Run(async ct =>
            {
                secondToken = ct;
                await UniTask.Delay(TimeSpan.FromSeconds(60), cancellationToken: ct);
            });
            yield return BindingScenes.Frames(2);

            Assert.That(secondToken.IsCancellationRequested, Is.False);
            Assert.That(firstToken.IsCancellationRequested, Is.True, "the first spawn's token stays dead");
            Assert.That(firstStage, Is.EqualTo(1), "the first task never continued");
            Assert.That(lifetime.EntryCount, Is.EqualTo(1));
        }

        // Guard: SubscriberList.FindDuplicate plus the generation pinned by the active lifetime.
        [Test]
        public void OwnedEventSubscribedInOnEnable_IsRemovedOnDisable_AndAReEnableDoesNotDoubleIt()
        {
            var evt = new OwnedEvent("enable");
            var hits = 0;
            Action handler = () => hits++;
            BindingProbe.EnableHook = p => evt.Subscribe(handler, p.GetActiveLifetime());

            var probe = _s.NewProbe();

            Assert.That(evt.SubscriberCount, Is.EqualTo(1), "OnEnable subscribed");
            evt.Invoke();
            Assert.That(hits, Is.EqualTo(1));
            for (var cycle = 0; cycle < 3; cycle++)
            {
                probe.gameObject.SetActive(false);
                Assert.That(evt.SubscriberCount, Is.Zero, "OnDisable removed it");
                evt.Invoke();
                Assert.That(hits, Is.EqualTo(1 + cycle), "a disabled object receives nothing");

                probe.gameObject.SetActive(true);
                Assert.That(evt.SubscriberCount, Is.EqualTo(1), "a re-enable must not double the subscription");
                evt.Invoke();
                Assert.That(hits, Is.EqualTo(2 + cycle), "exactly one delivery per enabled period");
            }

            Assert.That(_s.Logs.Count("JANITOR105"), Is.Zero, "no duplicate was ever attempted: the old generation was removed first");
        }

        // The contrast: a component owner lives across disable and enable, so the OnEnable subscription is a duplicate.
        [Test]
        public void OwnedEventSubscribedInOnEnableWithAComponentOwner_ReEnableIsADuplicateAndWarns()
        {
            var evt = new OwnedEvent("enable");
            var hits = 0;
            Action handler = () => hits++;
            BindingProbe.EnableHook = p => evt.Subscribe(handler, p);
            var probe = _s.NewProbe();
            Assert.That(evt.SubscriberCount, Is.EqualTo(1));
            probe.gameObject.SetActive(false);
            Assert.That(evt.SubscriberCount, Is.EqualTo(1), "the component lifetime survives a deactivation");
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR105"));

            probe.gameObject.SetActive(true);

            Assert.That(evt.SubscriberCount, Is.EqualTo(1), "the duplicate is ignored");
            evt.Invoke();
            Assert.That(hits, Is.EqualTo(1));
        }

        // Guard: Lifetime.Register refuses work on an Active-kind lifetime whose GameObject is inactive (JANITOR108).
        [Test]
        public void Register_WhileTheObjectIsInactive_IsTerminatedWithJanitor108()
        {
            var probe = _s.NewProbe();
            var lifetime = probe.GetActiveLifetime();
            probe.gameObject.SetActive(false);
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR108"));
            var ran = 0;

            var registration = lifetime.OnCancel(() => ran++);

            Assert.That(ran, Is.EqualTo(1), "the item is terminated at once");
            Assert.That(registration.IsActive, Is.False);
            Assert.That(lifetime.EntryCount, Is.Zero);
            Assert.That(_s.Logs.Count("JANITOR108"), Is.EqualTo(1));
        }

        [Test]
        public void Subscribe_WhileTheObjectIsInactive_AddsNothingAndRaisesJanitor108()
        {
            var probe = _s.NewProbe();
            var lifetime = probe.GetActiveLifetime();
            probe.gameObject.SetActive(false);
            var evt = new OwnedEvent("inactive");
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR108"));

            var registration = evt.Subscribe(() => { }, lifetime);

            Assert.That(evt.SubscriberCount, Is.Zero, "the terminated entry removes its slot");
            Assert.That(registration.IsActive, Is.False);
            Assert.That(lifetime.EntryCount, Is.Zero);
        }

        [Test]
        public void Subscribe_PairedFormWhileTheObjectIsInactive_AddsThenRemovesAtOnceWithJanitor108()
        {
            var probe = _s.NewProbe();
            var lifetime = probe.GetActiveLifetime();
            probe.gameObject.SetActive(false);
            var source = new BindingEventSource();
            var hits = 0;
            Action handler = () => hits++;
            LogAssert.Expect(LogType.Warning, new Regex("JANITOR108"));

            var registration = lifetime.Subscribe(h => source.Zero += h, h => source.Zero -= h, handler);
            source.RaiseZero();

            Assert.That(registration.IsActive, Is.False);
            Assert.That(source.Adds, Is.EqualTo(1), "add already ran when the registration was refused");
            Assert.That(source.Removes, Is.EqualTo(1), "remove ran at once");
            Assert.That(source.ZeroListeners, Is.Zero);
            Assert.That(hits, Is.Zero);
        }

        [Test]
        public void Register_AfterTheObjectIsActivatedAgain_IsAcceptedWithoutAWarning()
        {
            var probe = _s.NewProbe();
            var lifetime = probe.GetActiveLifetime();
            probe.gameObject.SetActive(false);
            probe.gameObject.SetActive(true);

            var registration = lifetime.Record(_log, "item");

            Assert.That(registration.IsActive, Is.True);
            Assert.That(_s.Logs.Count("JANITOR108"), Is.Zero);
            Assert.That(lifetime.EntryCount, Is.EqualTo(1));
        }

        [Test]
        public void GetActiveLifetime_WhileInactive_Works_AndRegistrationIsAcceptedOnceTheObjectIsActive()
        {
            var go = _s.NewInactiveObject();
            var probe = go.AddComponent<BindingProbe>();

            var lifetime = probe.GetActiveLifetime();

            Assert.That(lifetime.Kind, Is.EqualTo(LifetimeKind.Active));
            Assert.That(lifetime.IsDisposed, Is.False);
            Assert.That(lifetime.Parent, Is.SameAs(SceneLifetimes.Get(go.scene)));
            Assert.That(_s.Logs.Count("JANITOR108"), Is.Zero, "asking for the lifetime is not registering work");

            go.SetActive(true);

            Assert.That(lifetime.Record(_log, "item").IsActive, Is.True);
            Assert.That(_s.Logs.Count("JANITOR108"), Is.Zero);
        }

        // Destroy

        [UnityTest]
        public IEnumerator Destroy_CancelsOnDisableThenDisposes_AndRunsEachItemOnce()
        {
            var go = _s.NewObject();
            var probe = go.AddComponent<BindingProbe>();
            var lifetime = probe.GetActiveLifetime();
            lifetime.Record(_log, "item");
            lifetime.RecordToken(_log, "token");

            Object.Destroy(go);
            yield return null;

            Assert.That(lifetime.IsDisposed, Is.True);
            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "token", "item" }), "the item ran once, in the Cancel that OnDisable started");
            Assert.That(_s.Tree.Owners.Count, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Destroy_OfAnObjectThatWasNeverActivated_DisposesTheActiveLifetimeWithinAFewFrames()
        {
            var go = _s.NewInactiveObject();
            var probe = go.AddComponent<BindingProbe>();
            var lifetime = probe.GetActiveLifetime();

            Object.Destroy(go);
            yield return BindingScenes.Frames(2);

            Assert.That(lifetime.IsDisposed, Is.True);
            Assert.That(_s.Tree.Owners.Count, Is.Zero);
        }

        // One trigger per GameObject

        [Test]
        public void GetActiveLifetime_FromSeveralComponents_ResolvesToOneLifetimeAndOneTrigger()
        {
            var go = _s.NewObject();
            var first = go.AddComponent<BindingProbe>();
            var second = go.AddComponent<BindingProbe>();

            var fromFirst = first.GetActiveLifetime();
            var fromSecond = second.GetActiveLifetime();
            var fromTransform = go.transform.GetActiveLifetime();

            Assert.That(fromSecond, Is.SameAs(fromFirst));
            Assert.That(fromTransform, Is.SameAs(fromFirst), "any component of the object reaches the same active lifetime");
            Assert.That(go.GetComponents<ActiveLifetimeTrigger>().Length, Is.EqualTo(1));
            Assert.That(fromFirst.OwnerObject, Is.SameAs(go.GetComponent<ActiveLifetimeTrigger>()));
            Assert.That(fromFirst.Name, Is.EqualTo(go.name));
        }

        // The hidden trigger

        [Test]
        public void Trigger_AddedByGetActiveLifetime_IsHiddenInTheInspector_AndNothingElseIsTouched()
        {
            var go = _s.NewObject();
            var probe = go.AddComponent<BindingProbe>();

            probe.GetActiveLifetime();

            var trigger = go.GetComponent<ActiveLifetimeTrigger>();
            Assert.That(trigger.hideFlags, Is.EqualTo(HideFlags.HideInInspector));
            Assert.That(probe.hideFlags, Is.EqualTo(HideFlags.None));
            Assert.That(go.transform.hideFlags, Is.EqualTo(HideFlags.None));
            Assert.That(go.hideFlags, Is.EqualTo(HideFlags.None));
        }

        // Disabling the trigger: Unity reports it like a destruction, so the work is cancelled once and no deactivation is counted

        [Test]
        public void SelfDisable_OfTheTrigger_CancelsTheActiveLifetimeOnce_WithoutCountingADeactivation()
        {
            var probe = _s.NewProbe();
            var lifetime = probe.GetActiveLifetime();
            lifetime.Record(_log, "item");
            var trigger = probe.GetComponent<ActiveLifetimeTrigger>();
            var deactivations = trigger.Deactivations;

            trigger.enabled = false;

            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "item" }), "the work registered on the active lifetime is cancelled once");
            Assert.That(lifetime.Generation, Is.EqualTo(1));
            Assert.That(lifetime.IsDisposed, Is.False);
            Assert.That(trigger.Deactivations, Is.EqualTo(deactivations), "the object was not deactivated, so Unity stopped no coroutine");
        }

        [Test]
        public void BulkDisable_OfEveryBehaviour_CancelsTheActiveLifetimeOnce_AndTheNextUseOfItEnablesTheTriggerAgain()
        {
            var probe = _s.NewProbe();
            var lifetime = probe.GetActiveLifetime();
            lifetime.Record(_log, "item");
            var trigger = probe.GetComponent<ActiveLifetimeTrigger>();
            var behaviours = probe.GetComponentsInChildren<Behaviour>();
            Assert.That(behaviours.Length, Is.EqualTo(2), "the probe and the trigger");

            for (var i = 0; i < behaviours.Length; i++)
            {
                behaviours[i].enabled = false;
            }

            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "item" }), "the work is cancelled once, however many behaviours are disabled");
            Assert.That(trigger.enabled, Is.False, "nothing enables the trigger behind the owner's back");

            Assert.That(probe.GetActiveLifetime(), Is.SameAs(lifetime));

            Assert.That(trigger.enabled, Is.True, "using the lifetime enables the trigger again");
            for (var i = 0; i < behaviours.Length; i++)
            {
                behaviours[i].enabled = true;
            }

            Assert.That(lifetime.Record(_log, "next").IsActive, Is.True);
            probe.gameObject.SetActive(false);

            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "item", "next" }), "a later deactivation is seen");
            Assert.That(lifetime.Generation, Is.EqualTo(2));
        }

        [Test]
        public void Register_OnACachedActiveLifetime_AfterASelfDisable_EnablesTheTriggerAgain()
        {
            var probe = _s.NewProbe();
            var lifetime = probe.GetActiveLifetime();
            lifetime.Record(_log, "item");
            var trigger = probe.GetComponent<ActiveLifetimeTrigger>();
            trigger.enabled = false;
            Assert.That(trigger.enabled, Is.False);

            var registration = lifetime.Record(_log, "later");

            Assert.That(registration.IsActive, Is.True);
            Assert.That(trigger.enabled, Is.True, "the registration check enabled it");

            probe.gameObject.SetActive(false);

            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "item", "later" }), "a later deactivation is seen");
        }

        [Test]
        public void GetActiveLifetime_AfterTheTriggerWasDisabledWhileTheObjectWasInactive_EnablesItAgain()
        {
            var probe = _s.NewProbe();
            var lifetime = probe.GetActiveLifetime();
            var trigger = probe.GetComponent<ActiveLifetimeTrigger>();
            probe.gameObject.SetActive(false);

            trigger.enabled = false;
            probe.gameObject.SetActive(true);

            Assert.That(trigger.enabled, Is.False, "premise: Unity sent no OnDisable for a disable on an inactive object");
            Assert.That(probe.GetActiveLifetime(), Is.SameAs(lifetime));
            Assert.That(trigger.enabled, Is.True);
            lifetime.Record(_log, "item");

            probe.gameObject.SetActive(false);

            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "item" }));
        }

        // The engine facts the trigger relies on, per cause: what OnDisable sees in enabled and activeInHierarchy.
        [UnityTest]
        public IEnumerator OnDisable_SeesTheEnabledAndActiveFlagsOfEachCause_ThatTheTriggerTellsApartByEnabled()
        {
            string last = null;
            BindingProbe.DisableHook = p => last = p.enabled + "/" + p.gameObject.activeInHierarchy;
            var seen = new string[7];

            var deactivated = _s.NewProbe();
            deactivated.gameObject.SetActive(false);
            seen[0] = last;
            last = null;

            var deactivatedParent = _s.NewObject("DeactivatedParent");
            var childOfDeactivated = _s.NewProbe("Child");
            childOfDeactivated.transform.SetParent(deactivatedParent.transform);
            deactivatedParent.SetActive(false);
            seen[1] = last;
            last = null;

            var disabled = _s.NewProbe();
            disabled.enabled = false;
            seen[2] = last;
            last = null;

            var destroyed = _s.NewProbe();
            Object.Destroy(destroyed.gameObject);
            yield return null;
            seen[3] = last;
            last = null;

            var destroyedParent = _s.NewObject("DestroyedParent");
            var childOfDestroyed = _s.NewProbe("Child");
            childOfDestroyed.transform.SetParent(destroyedParent.transform);
            Object.Destroy(destroyedParent);
            yield return null;
            seen[4] = last;
            last = null;

            var destroyedComponent = _s.NewProbe();
            Object.Destroy(destroyedComponent);
            yield return null;
            seen[5] = last;
            last = null;

            var immediate = _s.NewProbe();
            Object.DestroyImmediate(immediate.gameObject);
            seen[6] = last;

            Assert.That(seen[0], Is.EqualTo("True/False"), "SetActive(false)");
            Assert.That(seen[1], Is.EqualTo("True/False"), "SetActive(false) on the parent");
            Assert.That(seen[2], Is.EqualTo("False/True"), "component.enabled = false");
            Assert.That(seen[3], Is.EqualTo("False/True"), "Destroy(gameObject)");
            Assert.That(seen[4], Is.EqualTo("False/True"), "Destroy(parent)");
            Assert.That(seen[5], Is.EqualTo("False/True"), "Destroy(component)");
            Assert.That(seen[6], Is.EqualTo("True/False"), "DestroyImmediate(gameObject)");
        }

        // The destroy path: Unity reports a destruction like a disable, and the work must still end before any OnDestroy.
        [UnityTest]
        public IEnumerator Destroy_EndsTheActiveLifetime_OnceAndBeforeAnyComponentOnDestroy()
        {
            var go = _s.NewObject();
            var probe = go.AddComponent<BindingProbe>();
            var lifetime = probe.GetActiveLifetime();
            lifetime.Record(_log, "item");
            BindingProbe.Log = _log;

            Object.Destroy(go);
            yield return null;

            var all = _log.ToArray();
            Assert.That(lifetime.IsDisposed, Is.True);
            Assert.That(Array.IndexOf(all, "item"), Is.GreaterThanOrEqualTo(0));
            Assert.That(Array.IndexOf(all, "item"), Is.EqualTo(Array.LastIndexOf(all, "item")), "the item ran once");
            Assert.That(Array.IndexOf(all, "item"), Is.LessThan(Array.IndexOf(all, "OnDestroy")), "the work was already ended when the component's OnDestroy ran");
        }

        [UnityTest]
        public IEnumerator SelfDisable_ThenDestroy_DisposesOnce()
        {
            var go = _s.NewObject();
            var probe = go.AddComponent<BindingProbe>();
            var lifetime = probe.GetActiveLifetime();
            lifetime.Record(_log, "item");
            go.GetComponent<ActiveLifetimeTrigger>().enabled = false;
            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "item" }), "the self-disable cancelled the work");

            Object.Destroy(go);
            yield return BindingScenes.Frames(2);

            Assert.That(lifetime.IsDisposed, Is.True);
            Assert.That(_log.ToArray(), Is.EqualTo(new[] { "item" }), "the destruction ends nothing a second time");
            LogAssert.NoUnexpectedReceived();
        }
    }
}
