using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace Ecanakli.Janitor.Tests.Binding
{
    // Scene loading and waiting for the binding tests. Every wait is bounded by a real-time deadline.
    internal static class BindingScenes
    {
        internal const string EmptyScenePath = "Packages/com.ecanakli.janitor/Tests/Runtime/Probes/ProbeEmpty.unity";
        internal const string AwakeScenePath = "Packages/com.ecanakli.janitor/Tests/Runtime/Binding/BindingAwakeScene.unity";
        internal const string AwakeSceneName = "BindingAwakeScene";
        internal const float DeadlineSeconds = 30f;

        internal static IEnumerator Frames(int count)
        {
            for (var i = 0; i < count; i++)
            {
                yield return null;
            }
        }

        // Replaces every loaded scene with the empty probe scene; all objects of the old scenes are destroyed.
        internal static IEnumerator LoadEmptySingle()
        {
#if UNITY_EDITOR
            yield return Wait(EditorSceneManager.LoadSceneAsyncInPlayMode(EmptyScenePath, new LoadSceneParameters(LoadSceneMode.Single)));
#else
            Assert.Ignore("The probe scenes are loaded through the editor.");
            yield break;
#endif
        }

        // Adds the scene whose only object is a ProbeBehaviour; its Awake runs during this load.
        internal static IEnumerator LoadAwakeAdditive()
        {
#if UNITY_EDITOR
            yield return Wait(EditorSceneManager.LoadSceneAsyncInPlayMode(AwakeScenePath, new LoadSceneParameters(LoadSceneMode.Additive)));
#else
            Assert.Ignore("The probe scenes are loaded through the editor.");
            yield break;
#endif
        }

        // Starts the additive load and returns the operation, so a test can hold it before activation.
        internal static AsyncOperation BeginAwakeAdditive()
        {
#if UNITY_EDITOR
            return EditorSceneManager.LoadSceneAsyncInPlayMode(AwakeScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
#else
            Assert.Ignore("The probe scenes are loaded through the editor.");
            return null;
#endif
        }

        internal static IEnumerator Wait(AsyncOperation operation)
        {
            if (operation == null)
            {
                Assert.Fail("The scene operation was not started.");
                yield break;
            }

            var deadline = Time.realtimeSinceStartup + DeadlineSeconds;
            while (!operation.isDone)
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Assert.Fail("The scene operation did not finish within " + DeadlineSeconds + " s.");
                }

                yield return null;
            }
        }

        // The unload the test asks for. sceneUnloaded has fired when this finishes.
        internal static IEnumerator Unload(Scene scene)
        {
            Assert.That(scene.IsValid() && scene.isLoaded, Is.True, "The scene to unload must be loaded.");
            yield return Wait(SceneManager.UnloadSceneAsync(scene));
        }

        // Teardown unload: a scene that is gone, or the last one, is skipped without failing.
        internal static IEnumerator UnloadQuietly(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded || SceneManager.sceneCount < 2)
            {
                yield break;
            }

            var operation = SceneManager.UnloadSceneAsync(scene);
            if (operation == null)
            {
                yield break;
            }

            var deadline = Time.realtimeSinceStartup + DeadlineSeconds;
            while (!operation.isDone && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
        }
    }
}
