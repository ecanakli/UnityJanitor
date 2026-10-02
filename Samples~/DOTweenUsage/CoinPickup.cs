using DG.Tweening;
using Ecanakli.Janitor;
using UnityEngine;

namespace Ecanakli.Janitor.Samples.DOTweenUsage
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
            transform.DOMove(_target.position, _flyDuration).SetEase(Ease.InQuad).AddTo(spawn);
            spawn.After(_flyDuration, gameObject, static go => go.SetActive(false));    // the timer only ends the flight
        }
    }
}
