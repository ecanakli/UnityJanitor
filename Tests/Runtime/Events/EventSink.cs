namespace Ecanakli.Janitor.Tests.Events
{
    // A plain (non-Unity) handler target that logs what it received, one method per UnityEvent arity.
    internal sealed class EventSink
    {
        private readonly CallLog _log;
        private readonly string _name;

        internal int Calls;

        internal EventSink(CallLog log, string name)
        {
            _log = log;
            _name = name;
        }

        internal void Zero()
        {
            Calls++;
            _log.Add(_name);
        }

        // A second method on the same target, to tell "same target, other method" from a duplicate.
        internal void OtherZero()
        {
            Calls++;
            _log.Add(_name + "!");
        }

        internal void One(int a)
        {
            Calls++;
            _log.Add(_name + ":" + a);
        }

        internal void Two(int a, string b)
        {
            Calls++;
            _log.Add(_name + ":" + a + "," + b);
        }

        internal void Three(int a, string b, long c)
        {
            Calls++;
            _log.Add(_name + ":" + a + "," + b + "," + c);
        }

        internal void Four(int a, string b, long c, bool d)
        {
            Calls++;
            _log.Add(_name + ":" + a + "," + b + "," + c + "," + d);
        }
    }

    // Counts without formatting anything, for the allocation tests.
    internal sealed class CountingSink
    {
        internal int Value;

        internal void Zero()
        {
            Value++;
        }

        internal void One(int a)
        {
            Value++;
        }

        internal void Four(int a, string b, long c, bool d)
        {
            Value++;
        }
    }
}
