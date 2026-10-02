using UnityEngine;

namespace Ecanakli.Janitor.Tests.Binding
{
    // A pooled item that returns itself to the pool from the OnCancel action of its active lifetime.
    [AddComponentMenu("")]
    internal sealed class ReleasingItem : MonoBehaviour
    {
        // A release path that turns the item off only while it is active in the hierarchy.
        internal bool SkipWhenNotActiveInHierarchy;

        // Set at the end of a test so the destruction in the cleanup only counts.
        internal bool Disarmed;

        internal int Enabled;
        internal int Released;

        private void OnEnable()
        {
            Enabled++;
            this.GetActiveLifetime().OnCancel(this, static item => item.Release());
        }

        private void Release()
        {
            Released++;
            if (Disarmed || (SkipWhenNotActiveInHierarchy && !gameObject.activeInHierarchy))
            {
                return;
            }

            gameObject.SetActive(false);
        }
    }
}
