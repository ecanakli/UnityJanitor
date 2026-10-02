using System;

namespace Ecanakli.Janitor.EditorTools
{
    /// <summary>
    /// One row of the lifetime tree. One instance lives as long as its lifetime is in the tree and is updated in place on every
    /// refresh. The texts are cached strings, so rebinding a visible row allocates nothing.
    /// </summary>
    internal sealed class LifetimeRow
    {
        internal const string UnnamedText = "(unnamed)";

        // Set by the tree model on every pass.
        internal int Stamp;
        internal int Index;
        internal int Depth;
        internal bool Shown;

        // The lifetime's data, copied from the snapshot node.
        internal int Id;
        internal int ParentId;
        internal Lifetime Lifetime;
        internal UnityEngine.Object Owner;
        internal bool OwnerDestroyed;
        internal LifetimeKind Kind;
        internal LifetimeState State;
        internal int Generation;
        internal int CancelCount;
        internal int CreatedFrame;
        internal int AgeFrames;
        internal int Tasks;
        internal int Tweens;
        internal int Coroutines;
        internal int Subscriptions;
        internal int Others;
        internal int ChildCount;
        internal int TotalRegistered;
        internal int RefusedCount;
        internal string FirstRefusedMember;
        internal int FirstRefusedLine;

        // Cached cell texts.
        internal string NameText;
        internal string KindText;
        internal string StateText;
        internal string GenerationText;
        internal string TasksText;
        internal string TweensText;
        internal string CoroutinesText;
        internal string SubscriptionsText;
        internal string OthersText;
        internal string ChildrenText;
        internal string AgeText;

        internal bool IsDisposed => State == LifetimeState.Disposed;

        // True when the owner object can be pinged: it exists and Unity has not destroyed it.
        internal bool HasLiveOwner => !OwnerDestroyed && !ReferenceEquals(Owner, null) && Owner != null;

        internal void Update(in LifetimeNode node)
        {
            Id = node.Id;
            ParentId = node.ParentId;
            Lifetime = node.Lifetime;
            Owner = node.Owner;
            OwnerDestroyed = node.OwnerDestroyed;
            Kind = node.Kind;
            State = node.State;
            Generation = node.Generation;
            CancelCount = node.CancelCount;
            CreatedFrame = node.CreatedFrame;
            AgeFrames = node.AgeFrames;
            Tasks = node.Tasks;
            Tweens = node.Tweens;
            Coroutines = node.Coroutines;
            Subscriptions = node.Subscriptions;
            Others = node.Others;
            ChildCount = node.ChildCount;
            TotalRegistered = node.TotalRegistered;
            RefusedCount = node.RefusedCount;
            FirstRefusedMember = node.FirstRefusedMember;
            FirstRefusedLine = node.FirstRefusedLine;

            NameText = node.Label ?? UnnamedText;
            KindText = DisplayNames.Kind(node.Kind);
            StateText = DisplayNames.State(node.State, node.OwnerDestroyed);
            GenerationText = CountText.Get(node.Generation);
            TasksText = CountText.Get(node.Tasks);
            TweensText = CountText.Get(node.Tweens);
            CoroutinesText = CountText.Get(node.Coroutines);
            SubscriptionsText = CountText.Get(node.Subscriptions);
            OthersText = CountText.Get(node.Others);
            ChildrenText = CountText.Get(node.ChildCount);
            AgeText = CountText.Frames(node.AgeFrames);
        }

        // The text filter: a case-insensitive match on the name, the kind or the state.
        internal bool Matches(string filter)
        {
            if (string.IsNullOrEmpty(filter))
            {
                return true;
            }

            return Contains(NameText, filter) || Contains(KindText, filter) || Contains(StateText, filter);
        }

        // The number shown in a numeric column; 0 for a text column.
        internal int CountOf(TreeColumn column)
        {
            switch (column)
            {
                case TreeColumn.Generation:
                    return Generation;
                case TreeColumn.Tasks:
                    return Tasks;
                case TreeColumn.Tweens:
                    return Tweens;
                case TreeColumn.Coroutines:
                    return Coroutines;
                case TreeColumn.Subscriptions:
                    return Subscriptions;
                case TreeColumn.Other:
                    return Others;
                case TreeColumn.Children:
                    return ChildCount;
                case TreeColumn.Age:
                    return AgeFrames;
                default:
                    return 0;
            }
        }

        // The text of a column.
        internal string TextOf(TreeColumn column)
        {
            switch (column)
            {
                case TreeColumn.Name:
                    return NameText;
                case TreeColumn.Kind:
                    return KindText;
                case TreeColumn.State:
                    return StateText;
                case TreeColumn.Generation:
                    return GenerationText;
                case TreeColumn.Tasks:
                    return TasksText;
                case TreeColumn.Tweens:
                    return TweensText;
                case TreeColumn.Coroutines:
                    return CoroutinesText;
                case TreeColumn.Subscriptions:
                    return SubscriptionsText;
                case TreeColumn.Other:
                    return OthersText;
                case TreeColumn.Children:
                    return ChildrenText;
                case TreeColumn.Age:
                    return AgeText;
                default:
                    return string.Empty;
            }
        }

        private static bool Contains(string text, string filter)
        {
            return text != null && text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
