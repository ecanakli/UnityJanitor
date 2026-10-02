using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Ecanakli.Janitor.Samples.DOTweenUsage
{
    /// <summary>An in-memory repository with fake latency; a save that completes writes one console line.</summary>
    public sealed class FakeOutfitRepository : IOutfitRepository
    {
        public async UniTask<OutfitLook> LoadLookAsync(string outfitId, CancellationToken ct)
        {
            await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: ct);
            return outfitId switch
            {
                "red" => new OutfitLook("Red outfit", new Color(0.85f, 0.25f, 0.25f, 1f)),
                "blue" => new OutfitLook("Blue outfit", new Color(0.25f, 0.45f, 0.9f, 1f)),
                _ => new OutfitLook("Plain outfit", new Color(0.6f, 0.6f, 0.6f, 1f)),
            };
        }

        public async UniTask SaveSelectionAsync(string outfitId, CancellationToken ct)
        {
            await UniTask.Delay(TimeSpan.FromSeconds(0.7), cancellationToken: ct);
            Debug.Log($"[FakeOutfitRepository] Saved outfit '{outfitId}'.");
        }
    }
}
