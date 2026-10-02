using System.Threading;
using Cysharp.Threading.Tasks;
using Ecanakli.Janitor;
using UnityEngine;

namespace Ecanakli.Janitor.Samples.BasicUsage
{
    /// <summary>A pooled gem whose flight task ends its own use; one cleanup returns it to the pool.</summary>
    public sealed class GemPickup : MonoBehaviour
    {
        [SerializeField] private float _flyDuration = 0.6f;
        private Vector3 _end;
        private GemPool _pool;

        // The per-use data arrives here, after the object exists.
        public void Launch(Vector3 end, GemPool pool)
        {
            _end = end;
            _pool = pool;
            gameObject.SetActive(true);
        }

        private void OnEnable()
        {
            // An object instantiated from an active prefab runs OnEnable before the pool can hand it anything.
            if (_pool == null)
            {
                return;
            }

            var use = this.GetActiveLifetime();                                // ends on SetActive(false) and on any outside Cancel
            use.OnCancel(this, static gem => gem._pool.Release(gem));          // returns the gem once, however the flight ends
            use.Run(FlyAsync);
        }

        private async UniTask FlyAsync(CancellationToken ct)
        {
            var start = transform.position;
            for (var elapsed = 0f; elapsed < _flyDuration; elapsed += Time.deltaTime)
            {
                transform.position = Vector3.Lerp(start, _end, elapsed / _flyDuration);
                await UniTask.Yield(ct);
            }

            transform.position = _end;
            gameObject.SetActive(false);       // the flight ends its own use; the cleanup above runs inside this call
        }
    }
}
