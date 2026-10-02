#pragma warning disable CS0649 // Assigned by Zenject.

using Ecanakli.Janitor;
using UnityEngine;
using Zenject;

namespace Ecanakli.Janitor.Samples.ZenjectUsage
{
    /// <summary>Subscribes to events the game does not own; the add and the remove sit in one call.</summary>
    public sealed class PlatformHooks : MonoBehaviour
    {
        [Inject] private IAdsSdk _ads;

        private void Awake()
        {
            // Custom delegate type: explicit type argument; static lambdas allocate nothing.
            this.Subscribe<Application.LowMemoryCallback>(
                static h => Application.lowMemory += h,
                static h => Application.lowMemory -= h,
                OnLowMemory);

            // Action<T> event: C# 9 cannot infer T from lambdas or method groups, so write <string>.
            this.Subscribe<string>(h => _ads.RewardGranted += h, h => _ads.RewardGranted -= h, OnRewardGranted);

            // Plain Action event: no type argument needed.
            this.Subscribe(h => _ads.Closed += h, h => _ads.Closed -= h, OnAdClosed);
        }

        private void OnLowMemory() => Resources.UnloadUnusedAssets();
        private void OnRewardGranted(string placement) => Debug.Log($"[PlatformHooks] Reward granted for '{placement}'.");
        private void OnAdClosed() => Debug.Log("[PlatformHooks] Ad closed.");
    }
}
