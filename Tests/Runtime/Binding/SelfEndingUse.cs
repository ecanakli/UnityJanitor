using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Ecanakli.Janitor.Tests.Binding
{
    // A use of a pooled object that ends itself: a task on the active lifetime turns the object off a frame after it was turned on.
    [AddComponentMenu("")]
    internal sealed class SelfEndingUse : MonoBehaviour
    {
        internal int Uses;

        // How often the OnCancel action of the active lifetime ran.
        internal int Cancelled;

        // The OnCancel actions that ran inside the SetActive call, summed over the uses.
        internal int CancelledInsideTheCall;

        // How often the statement after the SetActive call ran.
        internal int AfterTheCall;

        internal bool TokenCancelledAfterTheCall;

        private void OnEnable()
        {
            Uses++;
            var active = this.GetActiveLifetime();
            active.OnCancel(this, static use => use.Cancelled++);
            active.Run(this, static (use, token) => use.EndAsync(token));
        }

        private async UniTask EndAsync(CancellationToken token)
        {
            await UniTask.Yield(token);
            var before = Cancelled;
            gameObject.SetActive(false);
            CancelledInsideTheCall += Cancelled - before;
            TokenCancelledAfterTheCall = token.IsCancellationRequested;
            AfterTheCall++;
        }
    }
}
