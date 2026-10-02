using System.Threading;
using Cysharp.Threading.Tasks;
using Ecanakli.Janitor;
using UnityEngine.SceneManagement;

namespace Ecanakli.Janitor.Samples.BasicUsage
{
    /// <summary>Loads and unloads additive scenes; the scene's work ends before Unity destroys its objects.</summary>
    public sealed class AdditiveSceneFlow
    {
        private readonly Lifetime _lifetime;   // a child of Lifetime.App: it must outlive the scenes it unloads

        public AdditiveSceneFlow(Lifetime lifetime) => _lifetime = lifetime;

        public void Load(string sceneName) => _lifetime.Run(ct => LoadAsync(sceneName, ct));

        public void Unload(Scene scene) => _lifetime.Run(ct => UnloadAsync(scene, ct));

        // Stops the scene's work and keeps the scene loaded; its lifetime stays usable.
        public void StopWork(Scene scene) => SceneLifetimes.Get(scene).Cancel();

        private static async UniTask LoadAsync(string sceneName, CancellationToken ct)
        {
            await SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive).ToUniTask(cancellationToken: ct);
        }

        private static async UniTask UnloadAsync(Scene scene, CancellationToken ct)
        {
            // Unity refuses an unloaded scene and the last loaded one, and disposal is final, so check before it.
            if (!scene.isLoaded || SceneManager.sceneCount < 2)
            {
                return;
            }

            SceneLifetimes.Dispose(scene);     // this scene's work stops now, before the unload destroys anything
            await SceneManager.UnloadSceneAsync(scene).ToUniTask(cancellationToken: ct);
        }
    }
}
