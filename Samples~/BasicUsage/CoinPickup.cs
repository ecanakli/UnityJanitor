using System.Threading;
using Cysharp.Threading.Tasks;
using Ecanakli.Janitor;
using UnityEngine;

namespace Ecanakli.Janitor.Samples.BasicUsage
{
    /// <summary>A pooled coin: however its flight ends, the coin goes back to the pool.</summary>
    public sealed class CoinPickup : MonoBehaviour
    {
        [SerializeField] private float _flyDuration = 0.5f;
        private Transform _target;
        private CoinPool _pool;

        public void Launch(Transform target, CoinPool pool)
        {
            _target = target;
            _pool = pool;
            gameObject.SetActive(true);
        }

        private void OnEnable()
        {
            var spawn = this.GetActiveLifetime();                                       // ends on SetActive(false) and on any outside Cancel
            spawn.OnCancel(this, static coin => coin._pool.Release(coin));              // the one place that returns the coin
            spawn.Run(FlyAsync);
            spawn.After(_flyDuration, gameObject, static go => go.SetActive(false));    // the timer only ends the flight
        }

        private async UniTask FlyAsync(CancellationToken ct)
        {
            var start = transform.position;
            for (var elapsed = 0f; elapsed < _flyDuration; elapsed += Time.deltaTime)
            {
                var t = elapsed / _flyDuration;
                transform.position = Vector3.Lerp(start, _target.position, t * t);
                await UniTask.Yield(ct);
            }
        }
    }
}
