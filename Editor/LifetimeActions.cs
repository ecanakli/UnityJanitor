using System;
using UnityEditor;
using UnityEngine;

namespace Ecanakli.Janitor.EditorTools
{
    /// <summary>The result of the Cancel button.</summary>
    internal enum CancelOutcome
    {
        Cancelled,
        Declined,
        Unavailable,
        Failed,
    }

    /// <summary>What the Cancel and Ping owner buttons do, kept apart from the buttons so it can be tested without a window.</summary>
    internal static class LifetimeActions
    {
        internal const string AppConfirmTitle = "Cancel the App lifetime?";
        internal const string AppConfirmMessage = "This stops every task, tween, coroutine and subscription registered on the App lifetime and on all of its children. Use it only as a debugging aid.";

        internal static bool CanCancel(LifetimeRow row)
        {
            return row != null && row.Lifetime != null && !row.IsDisposed && !row.Lifetime.IsDisposed;
        }

        internal static bool CanPing(LifetimeRow row)
        {
            return row != null && row.HasLiveOwner;
        }

        // Cancelling App stops the whole session's work, so that one asks first.
        internal static bool NeedsConfirmation(LifetimeRow row)
        {
            return row != null && row.Kind == LifetimeKind.App;
        }

        /// <summary>
        /// Calls Cancel on the row's lifetime. A debugging aid: it ends the current generation of the lifetime and its descendants.
        /// <paramref name="confirm"/> is asked for the App lifetime only. Never throws.
        /// </summary>
        internal static CancelOutcome Cancel(LifetimeRow row, Func<bool> confirm)
        {
            if (!CanCancel(row))
            {
                return CancelOutcome.Unavailable;
            }

            if (NeedsConfirmation(row) && (confirm == null || !confirm()))
            {
                return CancelOutcome.Declined;
            }

            try
            {
                row.Lifetime.Cancel();
                return CancelOutcome.Cancelled;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return CancelOutcome.Failed;
            }
        }

        internal static bool ConfirmCancelApp()
        {
            return EditorUtility.DisplayDialog(AppConfirmTitle, AppConfirmMessage, "Cancel App lifetime", "Keep running");
        }

        /// <summary>Pings the owner object in the editor. Returns false when there is no live owner.</summary>
        internal static bool Ping(LifetimeRow row)
        {
            if (!CanPing(row))
            {
                return false;
            }

            EditorGUIUtility.PingObject(row.Owner);
            return true;
        }

        /// <summary>Pings the object a warning named as its context. Returns false when there is none or it was destroyed. Never throws.</summary>
        internal static bool PingContext(UnityEngine.Object context)
        {
            if (ReferenceEquals(context, null) || context == null)
            {
                return false;
            }

            try
            {
                EditorGUIUtility.PingObject(context);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return false;
            }
        }
    }
}
