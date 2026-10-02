using System.Threading;
using Cysharp.Threading.Tasks;
using Ecanakli.Janitor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ecanakli.Janitor.Samples.BasicUsage
{
    /// <summary>Ends the work of the loaded scenes right before it loads the next one.</summary>
    public sealed class SceneFlow
    {
        private readonly Lifetime _lifetime;   // a child of Lifetime.App: it must outlive the scenes it unloads

        public SceneFlow(Lifetime lifetime) => _lifetime = lifetime;

        public void Load(string scenePath) => _lifetime.Run(ct => LoadAsync(scenePath, ct));

        // Disposal is final, so a load that can fail at runtime should use LoadingScreenFlow, which disposes right before activation.
        private static async UniTask LoadAsync(string scenePath, CancellationToken ct)
        {
            if (SceneUtility.GetBuildIndexByScenePath(scenePath) < 0)
            {
                Debug.LogError($"Scene '{scenePath}' is not in the Build Settings; nothing was disposed.");
                return;
            }

            SceneLifetimes.DisposeAll();       // everything owned by the loaded scenes stops now, before any destroy
            await SceneManager.LoadSceneAsync(scenePath).ToUniTask(cancellationToken: ct);
        }
    }
}
