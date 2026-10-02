using System.Threading;
using Cysharp.Threading.Tasks;
using Ecanakli.Janitor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ecanakli.Janitor.Samples.ZenjectUsage
{
    /// <summary>Ends the work of the loaded scenes right before loading the next one. Bind it in the ProjectContext.</summary>
    public sealed class SceneFlow
    {
        private readonly Lifetime _lifetime;   // injected in the ProjectContext: a child of Lifetime.App

        public SceneFlow(Lifetime lifetime) => _lifetime = lifetime;

        public void Load(string scenePath) => _lifetime.Run(ct => LoadAsync(scenePath, ct));

        // Disposal is final, so a load that can fail at runtime should hold the activation and dispose right before it.
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
