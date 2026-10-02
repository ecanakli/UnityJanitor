namespace Ecanakli.Janitor
{
    /// <summary>
    /// A handle to one item registered in a <see cref="Lifetime"/>. <see cref="Cancel"/> terminates exactly that item.
    /// Stale or repeated calls are no-ops. It deliberately does not implement <see cref="System.IDisposable"/>:
    /// cancelling terminates the item, it does not merely unregister it.
    /// </summary>
    public readonly struct LifetimeRegistration
    {
        private readonly Lifetime _lifetime;
        private readonly int _generation;
        private readonly int _slot;
        private readonly int _version;

        internal LifetimeRegistration(Lifetime lifetime, int generation, int slot, int version)
        {
            _lifetime = lifetime;
            _generation = generation;
            _slot = slot;
            _version = version;
        }

        // The owner entry's coordinates; owned events and task entries store them next to their own state.
        internal int EntryId => _slot;

        internal int EntryVersion => _version;

        /// <summary>
        /// True while the item is registered and not yet terminated. It is false for a default handle, for an
        /// item that was refused because its lifetime was ending, and after the item ended. Off the main
        /// thread the value may be stale.
        /// </summary>
        public bool IsActive => _lifetime != null && _lifetime.IsRegistrationLive(_generation, _slot, _version);

        /// <summary>
        /// Terminates the item now. Does nothing when the item already ended, when this handle is stale or
        /// default, or when it is called again. Off the main thread the call is marshalled to the next
        /// main-thread tick. Never throws.
        /// </summary>
        public void Cancel()
        {
            if (_lifetime != null)
            {
                _lifetime.CancelRegistration(_generation, _slot, _version);
            }
        }
    }
}
