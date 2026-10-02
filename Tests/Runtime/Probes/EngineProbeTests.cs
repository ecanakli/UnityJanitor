using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace Ecanakli.Janitor.Tests.Probes
{
    // Pins engine behaviour the package relies on.
    public sealed class EngineProbeTests
    {
        private const string ProbeScenePath = "Packages/com.ecanakli.janitor/Tests/Runtime/Probes/ProbeEmpty.unity";

        private readonly List<string> _log = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _log.Clear();
            ProbeBehaviour.Log = _log;
            ProbeBehaviour.TokenReadInOnDestroy = default;
        }

        [TearDown]
        public void TearDown() => ProbeBehaviour.Log = null;

        [UnityTest]
        public IEnumerator EngineProbe_01a_DestroyToken_IsCancelled_OnUnloadSceneAsync()
        {
            Scene scene = SceneManager.CreateScene("JanitorProbe01a");
            var go = new GameObject("Probe");
            SceneManager.MoveGameObjectToScene(go, scene);
            CancellationToken token = go.AddComponent<ProbeBehaviour>().destroyCancellationToken;

            yield return WaitFor(SceneManager.UnloadSceneAsync(scene));

            Assert.That(token.IsCancellationRequested, Is.True);
        }

        [UnityTest]
        public IEnumerator EngineProbe_01b_DestroyToken_IsCancelled_OnSingleModeLoad()
        {
            CancellationToken token = new GameObject("Probe").AddComponent<ProbeBehaviour>().destroyCancellationToken;

            yield return LoadProbeSceneSingle();

            Assert.That(token.IsCancellationRequested, Is.True);
        }

        [UnityTest]
        public IEnumerator EngineProbe_02a_NeverActivated_TokenReadWhileInactive_IsNotCancelledOnDestroy()
        {
            var go = new GameObject("Probe");
            go.SetActive(false);
            CancellationToken token = go.AddComponent<ProbeBehaviour>().destroyCancellationToken;

            Object.Destroy(go);
            yield return null;
            yield return null;

            Assert.That(_log, Does.Not.Contain("Awake"));
            Assert.That(_log, Does.Not.Contain("OnDestroy"));
            Assert.That(token.IsCancellationRequested, Is.False);
        }

        [UnityTest]
        public IEnumerator EngineProbe_02b_TokenReadBeforeAwake_ThenActivated_CancelledOnDestroy()
        {
            var go = new GameObject("Probe");
            go.SetActive(false);
            CancellationToken token = go.AddComponent<ProbeBehaviour>().destroyCancellationToken;
            go.SetActive(true);

            Object.Destroy(go);
            yield return null;

            Assert.That(_log, Does.Contain("Awake"));
            Assert.That(token.IsCancellationRequested, Is.True, "a token read before Awake is cancelled once the object was activated and destroyed");
        }

        [UnityTest]
        public IEnumerator EngineProbe_04_TokenFirstReadInsideOnDestroy()
        {
            var probe = new GameObject("Probe").AddComponent<ProbeBehaviour>();
            probe.ReadTokenInOnDestroy = true;

            Object.Destroy(probe.gameObject);
            yield return null;
            yield return null;

            Assert.That(_log, Does.Contain("OnDestroy"), "OnDestroy ran, so the token was read");
            Assert.That(ProbeBehaviour.TokenReadInOnDestroy.IsCancellationRequested, Is.False, "a token first read inside OnDestroy is never cancelled");
        }

        [UnityTest]
        public IEnumerator EngineProbe_05_Order_OnDisable_TokenCallbacksReversed_OnDestroy()
        {
            var probe = new GameObject("Probe").AddComponent<ProbeBehaviour>();
            CancellationToken token = probe.destroyCancellationToken;
            token.Register(() => _log.Add("Callback1"));
            token.Register(() => _log.Add("Callback2"));
            _log.Clear();

            Object.Destroy(probe.gameObject);
            yield return null;

            Assert.That(_log, Is.EqualTo(new[] { "OnDisable", "Callback2", "Callback1", "OnDestroy" }));
        }

        [UnityTest]
        public IEnumerator EngineProbe_05b_Order_DestroyImmediate()
        {
            var probe = new GameObject("Probe").AddComponent<ProbeBehaviour>();
            CancellationToken token = probe.destroyCancellationToken;
            token.Register(() => _log.Add("Callback1"));
            _log.Clear();

            Object.DestroyImmediate(probe.gameObject);
            yield return null;

            Assert.That(_log, Is.EqualTo(new[] { "OnDisable", "Callback1", "OnDestroy" }));
        }

        [UnityTest]
        public IEnumerator EngineProbe_06a_SceneUnloaded_FiresAfterOnDestroy_OnUnloadSceneAsync()
        {
            Scene scene = SceneManager.CreateScene("JanitorProbe06a");
            var go = new GameObject("Probe");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<ProbeBehaviour>();
            int handle = scene.handle;
            UnityAction<Scene> onUnloaded = s =>
            {
                if (s.handle == handle) _log.Add("sceneUnloaded");
            };
            SceneManager.sceneUnloaded += onUnloaded;
            _log.Clear();

            yield return WaitFor(SceneManager.UnloadSceneAsync(scene));
            SceneManager.sceneUnloaded -= onUnloaded;

            Assert.That(_log, Is.EqualTo(new[] { "OnDisable", "OnDestroy", "sceneUnloaded" }));
        }

        [UnityTest]
        public IEnumerator EngineProbe_06b_SceneEvents_Order_OnSingleModeLoad()
        {
            new GameObject("Probe").AddComponent<ProbeBehaviour>();
            UnityAction<Scene> onUnloaded = s => _log.Add("sceneUnloaded");
            UnityAction<Scene, Scene> onActiveChanged = (from, to) => _log.Add("activeSceneChanged");
            UnityAction<Scene, LoadSceneMode> onLoaded = (s, mode) => _log.Add("sceneLoaded");
            SceneManager.sceneUnloaded += onUnloaded;
            SceneManager.activeSceneChanged += onActiveChanged;
            SceneManager.sceneLoaded += onLoaded;
            _log.Clear();

            yield return LoadProbeSceneSingle();
            SceneManager.sceneUnloaded -= onUnloaded;
            SceneManager.activeSceneChanged -= onActiveChanged;
            SceneManager.sceneLoaded -= onLoaded;

            Assert.That(_log, Is.EqualTo(new[] { "OnDisable", "OnDestroy", "sceneUnloaded", "activeSceneChanged", "sceneLoaded" }));
        }

        [UnityTest]
        public IEnumerator EngineProbe_07a_StopCoroutine_OnFinishedHandle_IsSilent()
        {
            var host = new GameObject("Host").AddComponent<ProbeBehaviour>();
            Coroutine handle = host.StartCoroutine(YieldOnce());
            Assert.That(handle, Is.Not.Null);
            yield return null;
            yield return null;

            Assert.DoesNotThrow(() => host.StopCoroutine(handle));
            LogAssert.NoUnexpectedReceived();
            Object.Destroy(host.gameObject);
        }

        [UnityTest]
        public IEnumerator EngineProbe_07b_StartCoroutine_ThatNeverYields_ReturnsNull()
        {
            var host = new GameObject("Host").AddComponent<ProbeBehaviour>();

            Coroutine handle = host.StartCoroutine(NeverYields());

            Assert.That(handle, Is.Null);
            Object.Destroy(host.gameObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator EngineProbe_08_StartCoroutine_OnInactiveObject_ReturnsNullAndLogsError()
        {
            var host = new GameObject("InactiveHost").AddComponent<ProbeBehaviour>();
            host.gameObject.SetActive(false);
            LogAssert.Expect(LogType.Error, new Regex("inactive"));

            Coroutine handle = host.StartCoroutine(YieldOnce());

            Assert.That(handle, Is.Null);
            Object.Destroy(host.gameObject);
            yield return null;
        }

        [Test]
        public void EngineProbe_14a_UnityEvent_RemovingCurrentListenerDuringInvoke_IsSafe()
        {
            var evt = new UnityEvent();
            int first = 0;
            int second = 0;
            UnityAction firstListener = null;
            firstListener = () =>
            {
                first++;
                evt.RemoveListener(firstListener);
            };
            evt.AddListener(firstListener);
            evt.AddListener(() => second++);

            Assert.DoesNotThrow(() => evt.Invoke());
            evt.Invoke();

            Assert.That(first, Is.EqualTo(1));
            Assert.That(second, Is.EqualTo(2));
        }

        [Test]
        public void EngineProbe_14b_UnityEvent_RemovingLaterListenerDuringInvoke()
        {
            var evt = new UnityEvent();
            int later = 0;
            UnityAction laterListener = () => later++;
            evt.AddListener(() => evt.RemoveListener(laterListener));
            evt.AddListener(laterListener);

            evt.Invoke();

            Assert.That(later, Is.EqualTo(1), "a later listener removed during Invoke still runs in that Invoke (the listeners are snapshotted)");

            evt.Invoke();

            Assert.That(later, Is.EqualTo(1), "and it is gone for the next Invoke");
        }

        private static IEnumerator LoadProbeSceneSingle()
        {
#if UNITY_EDITOR
            yield return WaitFor(EditorSceneManager.LoadSceneAsyncInPlayMode(ProbeScenePath, new LoadSceneParameters(LoadSceneMode.Single)));
#else
            Assert.Ignore("The probe scene is loaded through the editor.");
            yield break;
#endif
        }

        private static IEnumerator WaitFor(AsyncOperation operation)
        {
            while (!operation.isDone)
            {
                yield return null;
            }
        }

        private static IEnumerator YieldOnce()
        {
            yield return null;
        }

        private static IEnumerator NeverYields()
        {
            yield break;
        }
    }
}
