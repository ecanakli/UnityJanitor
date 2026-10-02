using System;
using UnityEngine;

namespace Ecanakli.Janitor
{
    /// <summary>
    /// Added once per GameObject by <c>GetActiveLifetime()</c>, and by a lifetime-bound coroutine that starts on the
    /// GameObject. With an active lifetime bound, it cancels that lifetime when the GameObject is deactivated, so the
    /// next activation starts a fresh generation. Without one, it only counts deactivations, which is how a coroutine
    /// that Unity stopped is noticed. It tracks GameObject activation, not the <c>enabled</c> flag of any other
    /// component. Disabling the trigger itself is reported by Unity exactly like a destruction, so the active lifetime
    /// is cancelled once, and the trigger enables itself again the next time the package uses it. It is hidden in the
    /// Inspector. There is no reason to add or use this component by hand.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class ActiveLifetimeTrigger : MonoBehaviour
    {
        private Lifetime _lifetime;
        private GameObject _owner;
        private int _deactivations;

        // Set when OnDisable saw the enabled flag cleared; the package enables the component again at its next touch.
        private bool _disabled;

        internal Lifetime BoundLifetime => _lifetime;

        // Incremented in every real deactivation, bound or not; a change means Unity stopped the coroutines of this GameObject.
        internal int Deactivations => _deactivations;

        // Cached so the per-registration activity check is one native call.
        internal bool IsGameObjectActive => _owner != null && _owner.activeInHierarchy;

        // The trigger of a GameObject, added unbound when missing. Never creates or binds an active lifetime.
        internal static ActiveLifetimeTrigger GetOrAdd(GameObject target)
        {
            if (target.TryGetComponent(out ActiveLifetimeTrigger trigger))
            {
                trigger.EnableAgain();
                return trigger;
            }

            return Add(target);
        }

        // The only place the package adds the component; it is hidden in the Inspector and nothing else is touched.
        internal static ActiveLifetimeTrigger Add(GameObject target)
        {
            var trigger = target.AddComponent<ActiveLifetimeTrigger>();
            if (trigger != null)
            {
                trigger.hideFlags |= HideFlags.HideInInspector;
            }

            return trigger;
        }

        internal void Bind(Lifetime lifetime)
        {
            _lifetime = lifetime;
            _owner = gameObject;
        }

        // A disabled component gets no OnDisable, so it would miss the next deactivation. Costs a managed bool test unless it was disabled.
        internal void EnableAgain()
        {
            if (_disabled)
            {
                _disabled = false;
                Enable();
            }
        }

        // For callers that already make native calls: also heals a trigger disabled while its object was inactive, which sends no OnDisable.
        internal void EnableAgainChecked()
        {
            _disabled = false;
            Enable();
        }

        private void OnDisable()
        {
            // Unity reports a destruction like a self-disable (enabled is false); only a deactivation leaves it true.
            if (enabled)
            {
                _deactivations++;
            }
            else
            {
                _disabled = true;
            }

            // The destroy order depends on this: the work ends here, before any component's OnDestroy.
            var lifetime = _lifetime;
            if (lifetime != null)
            {
                lifetime.Cancel();
            }
        }

        private void Enable()
        {
            try
            {
                if (this != null && !enabled)
                {
                    enabled = true;
                }
            }
            catch (Exception exception)
            {
                var tree = _lifetime != null ? _lifetime.Tree : LifetimeTree.Default;
                if (tree != null)
                {
                    tree.ReportError(exception, LifetimeErrorSource.CancelAction, _lifetime, null, 0);
                }
            }
        }
    }
}
