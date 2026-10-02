#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;

namespace Ecanakli.Janitor
{
    // Tells registered items apart without the core knowing DOTween or Zenject: first by the identity of the cached terminate
    // action, then by the type of the item. Classification allocates nothing; only FormatLabel builds strings, and only when asked.
    internal static class EntryClassifier
    {
        private const byte TypeOther = 0;
        private const byte TypeTween = 1;
        private const byte TypeSignalBus = 2;

        // Type to code, filled on first sight of each type. Main thread only.
        private static readonly Dictionary<Type, byte> TypeCodes = new Dictionary<Type, byte>();

        internal static EntryLabelKind Classify(in EntrySlot slot)
        {
            var terminate = slot.Terminate;
            if (ReferenceEquals(terminate, LifetimeTaskEntry.Terminate))
            {
                return EntryLabelKind.Task;
            }

            if (ReferenceEquals(terminate, LifetimeCoroutine.Terminate))
            {
                return EntryLabelKind.Coroutine;
            }

            if (ReferenceEquals(terminate, UnityEventGuardBase.Terminate))
            {
                return EntryLabelKind.UnityEvent;
            }

            if (ReferenceEquals(terminate, OwnedEventEntry.Terminate))
            {
                return EntryLabelKind.OwnedEvent;
            }

            if (ReferenceEquals(terminate, EntryInvoker.DisposeItem))
            {
                return EntryLabelKind.Dispose;
            }

            if (ReferenceEquals(terminate, EntryInvoker.InvokeAction))
            {
                return EntryLabelKind.Action;
            }

            // Paired Subscribe: the handler is the item and the remove action is the delegate; an OnCancel state may be a delegate too.
            var first = slot.A;
            if (first == null)
            {
                return EntryLabelKind.State;
            }

            if (slot.Aux == EntryAux.Paired)
            {
                return EntryLabelKind.Paired;
            }

            var code = CodeOf(first.GetType());
            if (code == TypeTween)
            {
                return EntryLabelKind.Tween;
            }

            // A SignalBus subscription is OnCancel(bus, handler, ...).
            if (code == TypeSignalBus && slot.B is Delegate)
            {
                return EntryLabelKind.Signal;
            }

            return EntryLabelKind.State;
        }

        internal static EntryKind KindOf(EntryLabelKind labelKind)
        {
            switch (labelKind)
            {
                case EntryLabelKind.Task:
                    return EntryKind.Task;
                case EntryLabelKind.Tween:
                    return EntryKind.Tween;
                case EntryLabelKind.Coroutine:
                    return EntryKind.Coroutine;
                case EntryLabelKind.UnityEvent:
                case EntryLabelKind.OwnedEvent:
                case EntryLabelKind.Signal:
                case EntryLabelKind.Paired:
                    return EntryKind.Subscription;
                default:
                    return EntryKind.Other;
            }
        }

        // Allocates. Called only when the window needs the text of a visible row.
        internal static string FormatLabel(EntryLabelKind labelKind, LifetimeTaskKind taskKind, object first, object second)
        {
            switch (labelKind)
            {
                case EntryLabelKind.Task:
                    return "Task." + taskKind;
                case EntryLabelKind.Coroutine:
                    return "Coroutine";
                case EntryLabelKind.UnityEvent:
                    return first is UnityEventGuardBase guard ? guard.Label : "UnityEvent";
                case EntryLabelKind.OwnedEvent:
                    return first is IOwnedEventNode node ? node.Label : "OwnedEvent";
                case EntryLabelKind.Signal:
                    return SignalLabel(second);
                case EntryLabelKind.Paired:
                    return "Paired<" + TypeLabel(first == null ? null : first.GetType()) + ">";
                case EntryLabelKind.Tween:
                    return "Tween";
                case EntryLabelKind.Dispose:
                    return "IDisposable " + TypeLabel(first == null ? null : first.GetType());
                case EntryLabelKind.Action:
                    return "OnCancel";
                default:
                    return StateLabel(first, second);
            }
        }

        // A readable name for a type: List<Int32> instead of List`1.
        internal static string TypeLabel(Type type)
        {
            if (type == null)
            {
                return "null";
            }

            if (!type.IsGenericType)
            {
                return type.Name;
            }

            var name = type.Name;
            var tick = name.IndexOf('`');
            if (tick >= 0)
            {
                name = name.Substring(0, tick);
            }

            var arguments = type.GetGenericArguments();
            var builder = new StringBuilder(name);
            builder.Append('<');
            for (var i = 0; i < arguments.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(TypeLabel(arguments[i]));
            }

            builder.Append('>');
            return builder.ToString();
        }

        private static string SignalLabel(object handler)
        {
            var type = handler == null ? null : handler.GetType();
            if (type != null && type.IsGenericType)
            {
                var arguments = type.GetGenericArguments();
                if (arguments.Length == 1)
                {
                    return "Signal<" + TypeLabel(arguments[0]) + ">";
                }
            }

            return "Signal";
        }

        private static string StateLabel(object first, object second)
        {
            if (first == null)
            {
                return "OnCancel";
            }

            if (second == null)
            {
                return "OnCancel<" + TypeLabel(first.GetType()) + ">";
            }

            return "OnCancel<" + TypeLabel(first.GetType()) + ", " + TypeLabel(second.GetType()) + ">";
        }

        private static byte CodeOf(Type type)
        {
            if (TypeCodes.TryGetValue(type, out var code))
            {
                return code;
            }

            code = ComputeCode(type);
            TypeCodes[type] = code;
            return code;
        }

        // DOTween's Tween is found by name, so the core never references DOTween.dll.
        private static byte ComputeCode(Type type)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                if (current.FullName == "DG.Tweening.Tween")
                {
                    return TypeTween;
                }
            }

            return type.Name == "SignalBus" ? TypeSignalBus : TypeOther;
        }
    }
}
#endif
