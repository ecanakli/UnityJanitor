using UnityEngine;

namespace Ecanakli.Janitor.Samples.BasicUsage
{
    /// <summary>A loading screen that writes console lines instead of showing a canvas.</summary>
    public sealed class FakeLoadingScreen : ILoadingScreen
    {
        public void Show() => Debug.Log("[LoadingScreen] Shown.");

        public void Hide() => Debug.Log("[LoadingScreen] Hidden.");
    }
}
