using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

namespace Ecanakli.Janitor
{
    /// <summary>
    /// Binds DOTween tweens to a <see cref="Lifetime"/>: <c>AddTo</c> kills (or completes) a tween when its owner ends, and
    /// <c>AwaitCompletionAsync</c> awaits a tween so that a cancel or a kill always throws instead of resuming. Main thread only.
    /// A registered tween is made non-recyclable, so a held reference and <c>IsActive()</c> stay reliable after a kill.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Register the root <c>Sequence</c> only. <c>Kill</c> on a tween nested in a Sequence is silently ignored by DOTween, so a
    /// nested tween registered on its own stays active after its owner ended; the parent Sequence keeps playing it.
    /// </para>
    /// <para>
    /// <c>AddTo</c> never touches <c>onKill</c>, <c>onComplete</c> or any <c>OnX</c> setter. Only <c>AwaitCompletionAsync</c> chains
    /// those two callbacks, and it restores them when the await is consumed.
    /// </para>
    /// </remarks>
    public static class LifetimeTweenExtensions
    {
        /// <summary>
        /// Kills, or completes, this tween when the current generation of <paramref name="owner"/> ends. The tween is made
        /// non-recyclable. A tween that is already inactive is left alone. When <paramref name="owner"/> is not active
        /// (cancelling or disposed) the tween is killed at once, whatever the mode: it never ran for this owner, so completing it would only
        /// fire callbacks in the middle of a teardown. Items of one lifetime end newest first. A warm call allocates nothing.
        /// Each call adds an entry that is dropped only when its tween is inactive, so registering the same live tween again and
        /// again on one lifetime adds one entry per call until the generation ends.
        /// </summary>
        /// <typeparam name="T">The tween type.</typeparam>
        /// <param name="tween">The tween, or the root Sequence.</param>
        /// <param name="owner">The lifetime that decides how long the tween may run.</param>
        /// <param name="onCancel">Whether the tween is killed or completed when the generation ends.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>The same tween, for chaining. It is its own handle: <c>tween.Kill()</c> is safe.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="owner"/> is null (the tween is killed first) or <paramref name="tween"/> is null.
        /// </exception>
        /// <exception cref="InvalidOperationException">Called off the main thread.</exception>
        public static T AddTo<T>(this T tween, Lifetime owner, TweenCancelMode onCancel = TweenCancelMode.Kill, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
            where T : Tween
        {
            TweenTermination.EnsureMainThread("Tween.AddTo");
            if (owner == null)
            {
                TweenTermination.KillQuietly(tween, member, line);
                throw new ArgumentNullException(nameof(owner));
            }

            if (tween == null)
            {
                throw new ArgumentNullException(nameof(tween));
            }

            tween.SetRecyclable(false);
            if (tween.IsActive())
            {
                TweenTermination.Register(tween, owner, onCancel, member, line);
            }

            return tween;
        }

        /// <summary>
        /// Kills, or completes, this tween when the current generation of the owner's lifetime ends. A
        /// <see cref="MonoBehaviour"/> owner uses its component lifetime; any other component uses the lifetime of its GameObject.
        /// A destroyed owner kills the tween at once. See <see cref="AddTo{T}(T, Lifetime, TweenCancelMode, string, int)"/> for the rules.
        /// </summary>
        /// <typeparam name="T">The tween type.</typeparam>
        /// <param name="tween">The tween, or the root Sequence.</param>
        /// <param name="owner">The owning component.</param>
        /// <param name="onCancel">Whether the tween is killed or completed when the generation ends.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>The same tween, for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="owner"/> is null (the tween is killed first) or <paramref name="tween"/> is null.
        /// </exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread.</exception>
        public static T AddTo<T>(this T tween, Component owner, TweenCancelMode onCancel = TweenCancelMode.Kill, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
            where T : Tween
        {
            TweenTermination.EnsureMainThread("Tween.AddTo");
            if (ReferenceEquals(owner, null))
            {
                TweenTermination.KillQuietly(tween, member, line);
                throw new ArgumentNullException(nameof(owner));
            }

            if (tween == null)
            {
                throw new ArgumentNullException(nameof(tween));
            }

            tween.SetRecyclable(false);
            if (!tween.IsActive())
            {
                return tween;
            }

            var lifetime = ResolveLifetime(owner);
            if (lifetime == null)
            {
                // The owner is already destroyed.
                TweenTermination.KillQuietly(tween, member, line);
                return tween;
            }

            TweenTermination.Register(tween, lifetime, onCancel, member, line);
            return tween;
        }

        /// <summary>
        /// Kills, or completes, this tween when the current generation of the GameObject's lifetime ends. A destroyed
        /// GameObject kills the tween at once. See <see cref="AddTo{T}(T, Lifetime, TweenCancelMode, string, int)"/> for the rules.
        /// </summary>
        /// <typeparam name="T">The tween type.</typeparam>
        /// <param name="tween">The tween, or the root Sequence.</param>
        /// <param name="owner">The owning GameObject.</param>
        /// <param name="onCancel">Whether the tween is killed or completed when the generation ends.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>The same tween, for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="owner"/> is null (the tween is killed first) or <paramref name="tween"/> is null.
        /// </exception>
        /// <exception cref="InvalidOperationException">Called outside Play Mode or off the main thread.</exception>
        public static T AddTo<T>(this T tween, GameObject owner, TweenCancelMode onCancel = TweenCancelMode.Kill, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
            where T : Tween
        {
            TweenTermination.EnsureMainThread("Tween.AddTo");
            if (ReferenceEquals(owner, null))
            {
                TweenTermination.KillQuietly(tween, member, line);
                throw new ArgumentNullException(nameof(owner));
            }

            if (tween == null)
            {
                throw new ArgumentNullException(nameof(tween));
            }

            tween.SetRecyclable(false);
            if (tween.IsActive())
            {
                TweenTermination.Register(tween, owner.GetLifetime(), onCancel, member, line);
            }

            return tween;
        }

        /// <summary>
        /// Registers the tween on <paramref name="lifetime"/> (killed when the generation ends, like
        /// <c>AddTo</c>) and awaits its completion. The await ends in an <see cref="OperationCanceledException"/> when the
        /// lifetime is cancelled or disposed, when anything else kills the tween (an external <c>Kill</c>, a <c>SetLink</c>
        /// target that was destroyed), and when the tween completes after its lifetime generation ended. A lifetime that
        /// has already ended kills the tween and returns a cancelled task. A tween that is already inactive completes,
        /// unless the lifetime has ended (reading the lifetime token may then create its token source). An infinite loop ends
        /// only by a kill, which cancels. The promise is pooled: a warm call allocates nothing, except the exception of a
        /// cancelled await.
        /// <para>
        /// The await chains <c>onComplete</c> and <c>onKill</c>, so set your own callbacks before this call; a callback set
        /// afterwards replaces the await's. An <c>onComplete</c> set afterwards means a normal completion is seen only
        /// through the auto kill, and the await then ends as cancelled. An <c>onKill</c> set afterwards does not stop the
        /// lifetime from cancelling the await when it ends; an external kill of such a tween leaves the await pending, and
        /// the lifetime keeps the tween's entry for as long as that await is pending, so ending the lifetime cancels it. A
        /// tween nested in a Sequence cannot be killed by DOTween: its await is cancelled when the lifetime ends, and the
        /// tween keeps running.
        /// </para>
        /// <para>
        /// Await a given tween once. Each call adds an entry to the lifetime, and an entry is dropped only when its tween is
        /// inactive: a tween that is kept alive and replayed (auto kill off) and awaited again on the same lifetime adds one
        /// entry per call until the generation ends. The same holds for repeated <c>AddTo</c> calls.
        /// </para>
        /// </summary>
        /// <param name="tween">The tween, or the root Sequence.</param>
        /// <param name="lifetime">The lifetime that decides how long the await may last.</param>
        /// <param name="member">Filled in by the compiler.</param>
        /// <param name="line">Filled in by the compiler.</param>
        /// <returns>A task that completes with the tween and is cancelled by anything that ends it early.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="lifetime"/> is null (the tween is killed first) or <paramref name="tween"/> is null.
        /// </exception>
        /// <exception cref="InvalidOperationException">Called off the main thread.</exception>
        public static UniTask AwaitCompletionAsync(this Tween tween, Lifetime lifetime, [CallerMemberName] string member = null, [CallerLineNumber] int line = 0)
        {
            TweenTermination.EnsureMainThread("Tween.AwaitCompletionAsync");
            if (lifetime == null)
            {
                TweenTermination.KillQuietly(tween, member, line);
                throw new ArgumentNullException(nameof(lifetime));
            }

            if (tween == null)
            {
                throw new ArgumentNullException(nameof(tween));
            }

            tween.SetRecyclable(false);
            if (!tween.IsActive())
            {
                return lifetime.Token.IsCancellationRequested ? UniTask.FromCanceled() : UniTask.CompletedTask;
            }

            var registration = TweenTermination.RegisterAwait(tween, lifetime, member, line);
            if (!registration.IsActive)
            {
                // The lifetime was not active, so registering already killed the tween.
                return UniTask.FromCanceled();
            }

            return TweenCompletionPromise.ForLifetime(tween, registration, lifetime.Name, member, line);
        }

        /// <summary>
        /// Awaits the completion of the tween and kills it when <paramref name="token"/> is cancelled. The await ends in an
        /// <see cref="OperationCanceledException"/> when the token is cancelled or when anything kills the tween. A token that is
        /// already cancelled kills the tween and returns a cancelled task; a tween that is already inactive completes. A
        /// cancellation raised off the main thread is moved to the next main-thread tick. The tween is made non-recyclable.
        /// An infinite loop ends only by a kill. Await a given tween once. It costs one token registration.
        /// Set your own <c>onComplete</c> and <c>onKill</c> before this call: the await chains them, and a callback set
        /// afterwards replaces the await's. An <c>onComplete</c> set afterwards means a normal completion is seen only
        /// through the auto kill, and the await then ends as cancelled.
        /// </summary>
        /// <param name="tween">The tween, or the root Sequence.</param>
        /// <param name="token">The token that owns the await; it is never disposed by this method.</param>
        /// <returns>A task that completes with the tween and is cancelled by anything that ends it early.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="tween"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Called off the main thread.</exception>
        public static UniTask AwaitCompletionAsync(this Tween tween, CancellationToken token)
        {
            TweenTermination.EnsureMainThread("Tween.AwaitCompletionAsync");
            if (tween == null)
            {
                throw new ArgumentNullException(nameof(tween));
            }

            tween.SetRecyclable(false);
            if (token.IsCancellationRequested)
            {
                TweenTermination.KillQuietly(tween, null, 0);
                return UniTask.FromCanceled(token);
            }

            if (!tween.IsActive())
            {
                return UniTask.CompletedTask;
            }

            return TweenCompletionPromise.ForToken(tween, token);
        }

        // Public-API owner resolution; null for a destroyed non-MonoBehaviour component.
        private static Lifetime ResolveLifetime(Component owner)
        {
            var behaviour = owner as MonoBehaviour;
            if (!ReferenceEquals(behaviour, null))
            {
                return behaviour.GetLifetime();
            }

            return owner == null ? null : owner.gameObject.GetLifetime();
        }
    }
}
