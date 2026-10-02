using System;
using Zenject;

namespace Ecanakli.Janitor.DependencyInjection
{
    // Cached static subscribe and terminate delegates per signal type, so a warm Subscribe allocates nothing from this
    // package. The terminate receives the bus and the handler the owner entry stores; it never throws.
    internal static class SignalOps<TSignal>
    {
        internal static readonly Action<SignalBus, Action<TSignal>> SubscribeTyped = AddTyped;
        internal static readonly Action<SignalBus, Action<TSignal>> TerminateTyped = RemoveTyped;
        internal static readonly Action<SignalBus, Action> SubscribePlain = AddPlain;
        internal static readonly Action<SignalBus, Action> TerminatePlain = RemovePlain;

        private static void AddTyped(SignalBus bus, Action<TSignal> handler)
        {
            bus.Subscribe<TSignal>(handler);
        }

        private static void AddPlain(SignalBus bus, Action handler)
        {
            bus.Subscribe<TSignal>(handler);
        }

        private static void RemoveTyped(SignalBus bus, Action<TSignal> handler)
        {
            if (SignalSubscriptions.IsArming(handler))
            {
                return;
            }

            SignalSubscriptions.Forget(bus, typeof(TSignal), handler);
            try
            {
                bus.TryUnsubscribe<TSignal>(handler);
            }
            catch (Exception exception)
            {
                ZenjectDiagnostics.Route(exception, LifetimeErrorSource.CancelAction, null, "SignalBus.TryUnsubscribe", 0);
            }
        }

        private static void RemovePlain(SignalBus bus, Action handler)
        {
            if (SignalSubscriptions.IsArming(handler))
            {
                return;
            }

            SignalSubscriptions.Forget(bus, typeof(TSignal), handler);
            try
            {
                bus.TryUnsubscribe<TSignal>(handler);
            }
            catch (Exception exception)
            {
                ZenjectDiagnostics.Route(exception, LifetimeErrorSource.CancelAction, null, "SignalBus.TryUnsubscribe", 0);
            }
        }
    }
}
