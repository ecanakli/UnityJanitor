namespace Ecanakli.Janitor
{
    /// <summary>Describes where an error routed to <see cref="LifetimeErrors.Handler"/> came from.</summary>
    public readonly struct LifetimeErrorContext
    {
        /// <summary>Creates a context.</summary>
        /// <param name="source">The kind of work that failed.</param>
        /// <param name="owner">The Unity object that owns the lifetime, or null.</param>
        /// <param name="lifetimeName">The lifetime's name or call-site label, or null.</param>
        /// <param name="member">The name of the member that registered the work, or null.</param>
        /// <param name="line">The line of the registration call, or 0.</param>
        public LifetimeErrorContext(LifetimeErrorSource source, UnityEngine.Object owner, string lifetimeName, string member, int line)
        {
            Source = source;
            Owner = owner;
            LifetimeName = lifetimeName;
            Member = member;
            Line = line;
        }

        /// <summary>The kind of work that failed.</summary>
        public LifetimeErrorSource Source { get; }

        /// <summary>The Unity object that owns the lifetime, or null when the lifetime has no owner object.</summary>
        public UnityEngine.Object Owner { get; }

        /// <summary>The lifetime's display name or call-site label, or null.</summary>
        public string LifetimeName { get; }

        /// <summary>The name of the member that registered the work, or null.</summary>
        public string Member { get; }

        /// <summary>The line of the registration call, or 0.</summary>
        public int Line { get; }
    }
}
