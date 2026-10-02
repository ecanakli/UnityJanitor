using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using Ecanakli.Janitor;
using UnityEngine;

namespace Ecanakli.Janitor.Samples.DOTweenUsage
{
    /// <summary>Applies an outfit: a new call restarts the work, and another service can cancel it.</summary>
    public sealed class CustomizationService
    {
        private readonly AvatarPreview _preview;
        private readonly IOutfitRepository _repository;
        private readonly Lifetime _apply;

        // The lifetime comes from the composition root; with Zenject it is injected.
        public CustomizationService(Lifetime lifetime, AvatarPreview preview, IOutfitRepository repository)
        {
            _preview = preview;
            _repository = repository;
            _apply = lifetime.CreateChild("Apply");
        }

        public void Apply(Outfit outfit)
        {
            _apply.Cancel();                                  // the previous apply stops: load, punch and save
            _apply.Run(ct => ApplyAsync(outfit, ct));
        }

        // Intent method: callers cancel the apply without seeing the raw area.
        public void CancelApply() => _apply.Cancel();

        private async UniTask ApplyAsync(Outfit outfit, CancellationToken ct)
        {
            var look = await _repository.LoadLookAsync(outfit.Id, ct);
            _preview.Show(look);
            await _preview.Root.DOPunchScale(Vector3.one * 0.1f, 0.3f).AwaitCompletionAsync(ct);  // a cancel kills it and throws
            await _repository.SaveSelectionAsync(outfit.Id, ct);
        }
    }
}
