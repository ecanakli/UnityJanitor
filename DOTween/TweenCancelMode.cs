namespace Ecanakli.Janitor
{
    /// <summary>What happens to a registered tween when the generation of its owner ends.</summary>
    public enum TweenCancelMode
    {
        /// <summary>
        /// The tween is killed. No completion callback runs and the target keeps the value it has reached. This is
        /// the default: nothing fires into a world that is being torn down.
        /// </summary>
        Kill,

        /// <summary>
        /// The tween is completed and then killed. The target lands on its end value and the completion callbacks
        /// run. Use it for visuals that must not freeze midway. It applies to a tween that was running when its owner
        /// ended; a tween registered on an owner that had already ended is killed instead.
        /// </summary>
        Complete,
    }
}
