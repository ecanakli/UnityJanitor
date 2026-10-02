using System;
using System.Collections;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Ecanakli.Janitor
{
    /// <summary>
    /// Binds Unity coroutines to a <see cref="Lifetime"/>. There is no global runner: the host is always an explicit
    /// <see cref="MonoBehaviour"/>, usually <c>this</c>. A coroutine that should only die with its host needs no
    /// lifetime, because Unity already stops it when the host is destroyed or deactivated.
    /// </summary>
    public static class LifetimeCoroutineExtensions
    {
        /// <summary>
        /// Starts <paramref name="routine"/> on <paramref name="host"/> and stops it when the current generation of
        /// <paramref name="lifetime"/> ends, or when the returned registration is cancelled. The routine is not
        /// started, and nothing is logged by Unity, when the lifetime is not active, or when the host is null,
        /// destroyed or inactive (the last two raise the development warning JANITOR110). A routine that finishes
        /// without ever yielding leaves nothing registered. An exception inside the routine is routed to
        /// <see cref="LifetimeErrors.Handler"/> with source <see cref="LifetimeErrorSource.Coroutine"/> and stops
        /// the routine. Setting <c>enabled = false</c> on the host does not stop a coroutine, exactly as in Unity.
        /// The first coroutine started on a GameObject adds a hidden <see cref="ActiveLifetimeTrigger"/> to it, without
        /// creating an active lifetime. The trigger counts deactivations: Unity stops a coroutine when its host is
        /// deactivated and never restarts it, so an entry whose host was deactivated since the start is reclaimed
        /// by the amortized sweep even when the host is active again.
        /// </summary>
        /// <param name="lifetime">The owner that decides how long the coroutine runs.</param>
        /// <param name="host">The MonoBehaviour that runs the coroutine. It must be active in the hierarchy.</param>
        /// <param name="routine">The coroutine body.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A handle that stops this one coroutine; default when it was not started or ended at once.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="lifetime"/> or <paramref name="routine"/> is null; nothing is started.</exception>
        /// <exception cref="InvalidOperationException">Called off the main thread.</exception>
        public static LifetimeRegistration StartCoroutine(this Lifetime lifetime, MonoBehaviour host, IEnumerator routine, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            if (lifetime == null)
            {
                throw new ArgumentNullException(nameof(lifetime));
            }

            if (routine == null)
            {
                throw new ArgumentNullException(nameof(routine));
            }

            lifetime.Tree.Guard.EnsureMainThread("Lifetime.StartCoroutine");
            if (lifetime.State != LifetimeState.Active)
            {
                LifetimeDiagnostics.RegistrationRefused(lifetime, member, line);
                return default;
            }

            return LifetimeCoroutine.Start(lifetime, host, routine, member, line);
        }
    }
}
