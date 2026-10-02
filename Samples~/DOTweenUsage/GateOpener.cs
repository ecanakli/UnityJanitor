using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using Ecanakli.Janitor;
using UnityEngine;

namespace Ecanakli.Janitor.Samples.DOTweenUsage
{
    /// <summary>Opens a gate with an awaited tween: opening again restarts it, and deactivating the object stops it.</summary>
    public sealed class GateOpener : MonoBehaviour
    {
        private Transform _gate;
        private Collider _passage;
        private Lifetime _open;

        // The parts arrive here because the demo builds its objects in code.
        public void Initialize(Transform gate, Collider passage)
        {
            _gate = gate;
            _passage = passage;
        }

        public void Open()
        {
            // An active lifetime accepts no work while its object is inactive.
            if (!gameObject.activeInHierarchy)
            {
                return;
            }

            // An area under the active lifetime: SetActive(false) cancels it, so it is kept and reused.
            _open ??= this.GetActiveLifetime().CreateChild("Open");

            _open.Cancel();                    // a second call kills the first tween before the new one starts
            _open.Run(OpenAsync);
        }

        private async UniTask OpenAsync(CancellationToken ct)
        {
            _passage.enabled = false;
            await _gate.DOLocalMoveY(2f, 0.6f).AwaitCompletionAsync(ct);   // a cancel kills the tween and throws
            _passage.enabled = true;
        }
    }
}
