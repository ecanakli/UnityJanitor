namespace Ecanakli.Janitor
{
    // Higher value wins; a transition never lowers it except the Cancelling to Active flip at the end of an operation.
    internal enum LifetimeState : byte
    {
        Active = 0,
        Cancelling = 1,
        Disposing = 2,
        Disposed = 3,
    }

    internal enum LifetimeKind : byte
    {
        App,
        Scene,
        Component,
        GameObject,
        Active,
        Area,
        Injected,
    }
}
