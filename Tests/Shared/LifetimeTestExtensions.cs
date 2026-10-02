namespace Ecanakli.Janitor.Tests
{
    public static class LifetimeTestExtensions
    {
        // Registers an item that appends label to the log when it terminates.
        public static LifetimeRegistration Record(this Lifetime lifetime, CallLog log, string label)
        {
            return lifetime.OnCancel(log, label, static (l, s) => l.Add(s));
        }

        // Registers a token callback that appends label to the log when the current generation's token fires.
        public static void RecordToken(this Lifetime lifetime, CallLog log, string label)
        {
            lifetime.Token.Register(() => log.Add(label));
        }
    }
}
