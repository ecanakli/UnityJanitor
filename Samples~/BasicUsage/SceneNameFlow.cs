using System.Threading;
using Cysharp.Threading.Tasks;
using Ecanakli.Janitor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ecanakli.Janitor.Samples.BasicUsage
{
    /// <summary>The scene call for a scene name, async and synchronous; the name is checked before anything is disposed.</summary>
    public sealed class SceneNameFlow
    {
        private readonly Lifetime _lifetime;   // a child of Lifetime.App: it must outlive the scenes it unloads

        public SceneNameFlow(Lifetime lifetime) => _lifetime = lifetime;

        public void Load(string sceneName) => _lifetime.Run(ct => LoadAsync(sceneName, ct));

        // Synchronous: Unity replaces the scene in the next frame, so the caller's own work may be what is disposed here.
        public void LoadNow(string sceneName)
        {
            if (!CanLoad(sceneName))
            {
                return;
            }

            SceneLifetimes.DisposeAll();
            SceneManager.LoadScene(sceneName);
        }

        private static async UniTask LoadAsync(string sceneName, CancellationToken ct)
        {
            if (!CanLoad(sceneName))
            {
                return;
            }

            SceneLifetimes.DisposeAll();       // everything owned by the loaded scenes stops now, before any destroy
            await SceneManager.LoadSceneAsync(sceneName).ToUniTask(cancellationToken: ct);
        }

        // Disposal is final, so a name that is not in the Build Settings is refused before it.
        private static bool CanLoad(string sceneName)
        {
            if (Application.CanStreamedLevelBeLoaded(sceneName))
            {
                return true;
            }

            Debug.LogError($"Scene '{sceneName}' is not in the Build Settings; nothing was disposed.");
            return false;
        }
    }
}
