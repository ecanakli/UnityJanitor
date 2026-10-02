using System.Threading;
using Cysharp.Threading.Tasks;

namespace Ecanakli.Janitor.Samples.DOTweenUsage
{
    /// <summary>Stand-in for the game's outfit storage.</summary>
    public interface IOutfitRepository
    {
        UniTask<OutfitLook> LoadLookAsync(string outfitId, CancellationToken ct);

        UniTask SaveSelectionAsync(string outfitId, CancellationToken ct);
    }
}
