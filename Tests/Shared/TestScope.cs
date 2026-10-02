using NUnit.Framework;

namespace Ecanakli.Janitor.Tests
{
    // Per-test bundle: an isolated tree, a capturing error handler and a call log. Complete() checks the invariants.
    public sealed class TestScope
    {
        public TestScope()
        {
            Tree = new TestTree();
            Errors = new CapturingErrorHandler();
            Log = new CallLog();
        }

        public TestTree Tree { get; }

        public CapturingErrorHandler Errors { get; }

        public CallLog Log { get; }

        public Lifetime App => Tree.App;

        // Call from TearDown. Tests that expect routed errors call Errors.Clear() after asserting on them.
        public void Complete()
        {
            try
            {
                Assert.That(Errors.Count, Is.Zero, "unexpected routed errors:\n" + Errors);
                Assert.That(Tree.RunningOperations, Is.Zero, "a Cancel or Dispose is still on the teardown stack");
            }
            finally
            {
                Errors.Dispose();
                Tree.Dispose();
            }
        }
    }
}
