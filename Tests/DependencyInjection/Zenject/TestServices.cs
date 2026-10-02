namespace Ecanakli.Janitor.DependencyInjection.Tests
{
    // A plain service that asks for its lifetime in the constructor.
    public sealed class AlphaService
    {
        public readonly Lifetime Injected;

        public AlphaService(Lifetime lifetime)
        {
            Injected = lifetime;
        }
    }

    public sealed class BetaService
    {
        public readonly Lifetime Injected;

        public BetaService(Lifetime lifetime)
        {
            Injected = lifetime;
        }
    }

    // Signals used by the SignalBus tests. OtherSignal gives a second declared type; UndeclaredSignal is never declared.
    public sealed class TestSignal
    {
        public int Value;
    }

    public sealed class OtherSignal
    {
    }

    public sealed class UndeclaredSignal
    {
    }
}
