using System;

namespace Ecanakli.Janitor.Tests.Binding
{
    // A plain C# event source with counting accessors, for the paired Subscribe forms.
    internal sealed class BindingEventSource
    {
        internal delegate void CustomHandler(string name, int value);

        private Action _zero;
        private Action<int> _one;
        private Action<int, int> _two;
        private Action<int, int, int> _three;
        private CustomHandler _custom;

        internal int Adds;
        internal int Removes;

        internal int ZeroListeners => _zero == null ? 0 : _zero.GetInvocationList().Length;

        internal int OneListeners => _one == null ? 0 : _one.GetInvocationList().Length;

        internal int TwoListeners => _two == null ? 0 : _two.GetInvocationList().Length;

        internal int ThreeListeners => _three == null ? 0 : _three.GetInvocationList().Length;

        internal int CustomListeners => _custom == null ? 0 : _custom.GetInvocationList().Length;

        internal event Action Zero
        {
            add
            {
                _zero += value;
                Adds++;
            }
            remove
            {
                _zero -= value;
                Removes++;
            }
        }

        internal event Action<int> One
        {
            add
            {
                _one += value;
                Adds++;
            }
            remove
            {
                _one -= value;
                Removes++;
            }
        }

        internal event Action<int, int> Two
        {
            add
            {
                _two += value;
                Adds++;
            }
            remove
            {
                _two -= value;
                Removes++;
            }
        }

        internal event Action<int, int, int> Three
        {
            add
            {
                _three += value;
                Adds++;
            }
            remove
            {
                _three -= value;
                Removes++;
            }
        }

        internal event CustomHandler Custom
        {
            add
            {
                _custom += value;
                Adds++;
            }
            remove
            {
                _custom -= value;
                Removes++;
            }
        }

        internal void RaiseZero()
        {
            _zero?.Invoke();
        }

        internal void RaiseOne(int a)
        {
            _one?.Invoke(a);
        }

        internal void RaiseTwo(int a, int b)
        {
            _two?.Invoke(a, b);
        }

        internal void RaiseThree(int a, int b, int c)
        {
            _three?.Invoke(a, b, c);
        }

        internal void RaiseCustom(string name, int value)
        {
            _custom?.Invoke(name, value);
        }
    }
}
