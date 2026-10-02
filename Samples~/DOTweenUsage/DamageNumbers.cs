using DG.Tweening;
using Ecanakli.Janitor;
using UnityEngine;

namespace Ecanakli.Janitor.Samples.DOTweenUsage
{
    /// <summary>Registers its tweens into the Combat category.</summary>
    public sealed class DamageNumbers
    {
        private readonly GameplayLifetimes _lifetimes;

        public DamageNumbers(GameplayLifetimes lifetimes) => _lifetimes = lifetimes;

        public void Pop(Transform label) => label.DOPunchScale(Vector3.one * 0.3f, 0.2f).AddTo(_lifetimes.Combat);
    }
}
