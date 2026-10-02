using System.Threading;
using Cysharp.Threading.Tasks;
using Ecanakli.Janitor;
using UnityEngine.SceneManagement;

namespace Ecanakli.Janitor.Samples.BasicUsage
{
    /// <summary>Loads a scene behind a loading screen and ends the old scene's work right before it is replaced.</summary>
    public sealed class LoadingScreenFlow
    {
        private readonly Lifetime _lifetime;   // a child of Lifetime.App: it must outlive the scenes it unloads
        private readonly ILoadingScreen _screen;
        private bool _loading;

        public LoadingScreenFlow(Lifetime lifetime, ILoadingScreen screen)
        {
            _lifetime = lifetime;
            _screen = screen;
        }

        public void Load(string sceneName) => _lifetime.Run(ct => LoadAsync(sceneName, ct));

        private async UniTask LoadAsync(string sceneName, CancellationToken ct)
        {
            if (_loading)
            {
                return;                        // a second load would replace the scene this one is loading
            }

            _loading = true;
            _screen.Show();
            try
            {
                var operation = SceneManager.LoadSceneAsync(sceneName);
                if (operation == null)
                {
                    return;                    // unknown scene: nothing started, nothing was disposed
                }

                operation.allowSceneActivation = false;
                try
                {
                    await UniTask.WaitUntil(() => operation.progress >= 0.9f, cancellationToken: ct);   // loaded, not yet activated
                }
                finally
                {
                    SceneLifetimes.DisposeAll();               // right before activation, also when the wait was cancelled
                    operation.allowSceneActivation = true;     // a started load cannot be aborted, and a held one stalls later loads
                }

                await operation.ToUniTask(cancellationToken: ct);
            }
            finally
            {
                _screen.Hide();
                _loading = false;
            }
        }
    }
}
