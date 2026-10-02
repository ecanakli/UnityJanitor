using System;
using System.Collections;
using UnityEngine;

namespace Ecanakli.Janitor.Tests.Coroutines
{
    // The engine handle of a plain coroutine, written after StartCoroutine returns and read by the routine on a later step.
    internal sealed class HandleHolder
    {
        internal Coroutine Handle;
    }

    // The wrapper of a package coroutine, written after StartCoroutine returns and read by the routine on a later step.
    internal sealed class WrapperHolder
    {
        internal LifetimeCoroutine Wrapper;
    }

    // Small routines with an observable step count. Every routine runs its first step inside StartCoroutine, as in Unity.
    internal static class CoroutineRoutines
    {
        // Steps once per frame until something stops it.
        internal static IEnumerator Forever(Counter counter)
        {
            while (true)
            {
                counter.Value++;
                yield return null;
            }
        }

        // Parks on an instruction that never finishes; Unity polls it every frame, so the poll count shows whether the engine still runs the routine.
        internal static IEnumerator WaitsForever(Counter polls)
        {
            yield return new WaitUntil(() =>
            {
                polls.Value++;
                return false;
            });
        }

        // Takes one step and ends without ever yielding.
        internal static IEnumerator NeverYields(Counter counter)
        {
            counter.Value++;
            yield break;
        }

        // Yields the given number of times, then takes one more step and ends: yields + 1 steps in total.
        internal static IEnumerator FinishesAfter(Counter counter, int yields)
        {
            for (var i = 0; i < yields; i++)
            {
                counter.Value++;
                yield return null;
            }

            counter.Value++;
        }

        // Yields the given number of times, then takes one more step and throws.
        internal static IEnumerator ThrowsAfter(Counter counter, int yields, Exception failure)
        {
            for (var i = 0; i < yields; i++)
            {
                counter.Value++;
                yield return null;
            }

            counter.Value++;
            throw failure;
        }

        // Suspended inside a try block, so its finally block runs only when the enumerator is disposed.
        internal static IEnumerator TryFinally(CallLog log, string tag)
        {
            try
            {
                log.Add(tag + ":start");
                while (true)
                {
                    yield return null;
                }
            }
            finally
            {
                log.Add(tag + ":finally");
            }
        }

        // Its finally block throws, to exercise a failing disposal.
        internal static IEnumerator FinallyThrows(Exception failure)
        {
            try
            {
                yield return null;
            }
            finally
            {
                throw failure;
            }
        }

        // Yields, then ends the lifetime from inside its own step and keeps going as if nothing happened.
        internal static IEnumerator EndsItsLifetime(Action end, CallLog log, int yields)
        {
            for (var i = 0; i < yields; i++)
            {
                log.Add("step" + i);
                yield return null;
            }

            log.Add("end");
            end();
            log.Add("after-end");
            yield return null;
            log.Add("resumed");
        }

        // Ends its lifetime and then throws, in the same step.
        internal static IEnumerator EndsItsLifetimeThenThrows(Action end, Exception failure)
        {
            yield return null;
            end();
            throw failure;
        }

        // Plain Unity: stops its own handle from inside its own step.
        internal static IEnumerator StopsItself(MonoBehaviour host, HandleHolder holder, CallLog log)
        {
            log.Add("step1");
            yield return null;
            log.Add("stop");
            host.StopCoroutine(holder.Handle);
            log.Add("after-stop");
            yield return null;
            log.Add("resumed");
        }

        // Disposes its own wrapper in the middle of a step, then yields again.
        internal static IEnumerator DisposesItsWrapperMidStep(WrapperHolder holder, CallLog log)
        {
            try
            {
                log.Add("step1");
                yield return null;
                log.Add("step2");
                holder.Wrapper.Dispose();
                log.Add("step2-after-dispose");
                yield return null;
                log.Add("step3");
            }
            finally
            {
                log.Add("finally");
            }
        }

        // Every kind of yield Unity understands, in a row: null, a timed wait, a nested routine, a predicate wait.
        internal static IEnumerator MixedInstructions(CallLog log, Counter release)
        {
            log.Add("a");
            yield return null;
            log.Add("b");
            yield return new WaitForSeconds(0.05f);
            log.Add("c");
            yield return Nested(log);
            log.Add("d");
            yield return new WaitUntil(() => release.Value > 0);
            log.Add("e");
        }

        private static IEnumerator Nested(CallLog log)
        {
            log.Add("n1");
            yield return null;
            log.Add("n2");
        }
    }
}
