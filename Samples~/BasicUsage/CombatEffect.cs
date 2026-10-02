using System.Threading;
using Cysharp.Threading.Tasks;
using Ecanakli.Janitor;
using TMPro;
using UnityEngine;

namespace Ecanakli.Janitor.Samples.BasicUsage
{
    /// <summary>A one-shot effect in the Combat category: its task is never restarted, and one cancel removes it.</summary>
    public sealed class CombatEffect : MonoBehaviour
    {
        private GameplayLifetimes _lifetimes;
        private TMP_Text _label;
        private Lifetime _life;

        // Call it before the object is enabled, so Awake sees the category.
        public void Initialize(GameplayLifetimes lifetimes, TMP_Text label)
        {
            _lifetimes = lifetimes;
            _label = label;
        }

        private void Awake()
        {
            // The first access decides the parent, so join Combat before anything else uses this lifetime.
            _life = this.GetLifetime(_lifetimes.Combat);
            _life.OnCancel(gameObject, static go => Destroy(go));      // the one place that removes the effect
        }

        public void Play(int damage)
        {
            _life.Run(ct => PlayAsync(damage, ct));
            _life.After(1f, _life, static life => life.Cancel());      // the normal end is a cancel too
        }

        private async UniTask PlayAsync(int damage, CancellationToken ct)
        {
            _label.text = "-" + damage;
            var start = transform.position;
            for (var elapsed = 0f; elapsed < 1f; elapsed += Time.deltaTime)
            {
                transform.position = start + Vector3.up * (elapsed * 80f);
                await UniTask.Yield(ct);
            }
        }
    }
}
