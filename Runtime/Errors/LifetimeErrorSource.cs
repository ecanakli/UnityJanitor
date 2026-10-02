namespace Ecanakli.Janitor
{
    /// <summary>Identifies which kind of work raised an error routed to <see cref="LifetimeErrors.Handler"/>.</summary>
    public enum LifetimeErrorSource
    {
        /// <summary>A task started through a lifetime failed.</summary>
        Task,

        /// <summary>A delayed or repeating callback failed.</summary>
        Timer,

        /// <summary>
        /// An action that ends an item failed while a lifetime was cancelled or disposed: an <c>OnCancel</c> action, a
        /// disposable, the remove action of a paired <c>Subscribe</c>, or a failing <c>isFinished</c> probe. An internal
        /// fault of <c>Cancel</c> or <c>Dispose</c> is routed here too.
        /// </summary>
        CancelAction,

        /// <summary>A lifetime-bound coroutine failed.</summary>
        Coroutine,

        /// <summary>A callback registered on a lifetime token failed while the token was cancelled.</summary>
        TokenCallback,

        /// <summary>
        /// An event handler failed: a handler invoked through an owned event, the add action of a paired
        /// <c>Subscribe</c>, or an owned event invoked more than 64 levels deep.
        /// </summary>
        EventHandler,
    }
}
