using System.Collections.Generic;
using UnityEngine;

namespace Ecanakli.Janitor
{
    // Unity-object state of a lifetime: category placement, scene membership and owner helpers.
    public sealed partial class Lifetime
    {
        // True while the structural parent is a category chosen through GetLifetime(parent) or GetActiveLifetime(parent).
        internal bool Placed;

        // Set once a category dispose moved this lifetime under its scene; it can never be placed again. RehomedWarned: JANITOR113 was raised.
        internal bool Rehomed;
        internal bool RehomedWarned;

        // The scene lifetime that holds this object's dispose-only membership entry, or null.
        internal Lifetime Membership;

        // Position in Membership.Members, kept so removal is a swap-remove.
        internal int MemberIndex;

        // Scene lifetimes only: placed object lifetimes whose category lives outside this scene's own subtree.
        internal List<Lifetime> Members;

        // The active lifetime whose GameObject decides if this lifetime accepts work: itself, the one above an area, or null.
        internal Lifetime ActivityGate;

        internal bool IsObjectKind => Kind == LifetimeKind.Component || Kind == LifetimeKind.GameObject || Kind == LifetimeKind.Active;

        // The GameObject that owns this lifetime, or null when there is none or it was destroyed.
        internal GameObject OwnerGameObject
        {
            get
            {
                var owner = OwnerObject;
                if (owner == null)
                {
                    return null;
                }

                var gameObject = owner as GameObject;
                if (gameObject != null)
                {
                    return gameObject;
                }

                var component = owner as Component;
                return component != null ? component.gameObject : null;
            }
        }

        // Active lifetimes: false while the GameObject is inactive. Lifetimes without a trigger are never refused.
        internal bool IsOwnerActive()
        {
            var trigger = OwnerObject as ActiveLifetimeTrigger;
            if (ReferenceEquals(trigger, null))
            {
                return true;
            }

            // A trigger the owner disabled is enabled again here, so the next deactivation reaches it.
            trigger.EnableAgain();
            return trigger.IsGameObjectActive;
        }

        // True, after the warning and the refused count, while the gating GameObject is inactive. No gate costs one field read.
        internal bool RefuseForInactiveObject(string member, int line)
        {
            var gate = ActivityGate;
            if (gate == null || gate.IsOwnerActive())
            {
                return false;
            }

            DevWarnings.RegistrationOnInactiveObject(gate);
            LifetimeDiagnostics.RegistrationRefused(this, member, line);
            return true;
        }

        internal bool IsDescendantOrSelfOf(Lifetime ancestor)
        {
            for (var node = this; node != null; node = node.Parent)
            {
                if (ReferenceEquals(node, ancestor))
                {
                    return true;
                }
            }

            return false;
        }

        internal void AddMember(Lifetime member)
        {
            if (Members == null)
            {
                Members = new List<Lifetime>(4);
            }

            member.Membership = this;
            member.MemberIndex = Members.Count;
            Members.Add(member);
        }

        internal void RemoveMember(Lifetime member)
        {
            member.Membership = null;
            var list = Members;
            var index = member.MemberIndex;
            if (list == null || index >= list.Count || !ReferenceEquals(list[index], member))
            {
                return;
            }

            var last = list.Count - 1;
            if (index != last)
            {
                var moved = list[last];
                list[index] = moved;
                moved.MemberIndex = index;
            }

            list.RemoveAt(last);
        }

        internal void DetachMembership()
        {
            var scene = Membership;
            if (scene != null)
            {
                scene.RemoveMember(this);
            }
        }
    }
}
