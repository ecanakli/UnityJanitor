using Ecanakli.Janitor.Tests;
using NUnit.Framework;

namespace Ecanakli.Janitor.EditorTools.Tests
{
    // Isolates the core diagnostics statics and gives every test its own lifetime tree with deterministic frames.
    public abstract class DiagnosticsTestBase
    {
        protected const int StartFrame = 100;

        private int _frame;

        protected TestScope Scope { get; private set; }

        // The frame the diagnostics see. Tests move it to age lifetimes and entries.
        protected int Frame
        {
            get => _frame;
            set => _frame = value;
        }

        [SetUp]
        public void BaseSetUp()
        {
            LifetimeDiagnostics.ResetAll();
            _frame = StartFrame;
            LifetimeDiagnostics.FrameProvider = () => _frame;
            LifetimeDiagnostics.Scheduler = static action => { };
            Scope = new TestScope();
        }

        [TearDown]
        public void BaseTearDown()
        {
            try
            {
                Scope.Complete();
            }
            finally
            {
                // Back to the defaults, then the user's stored settings, so the editor session is left as it was.
                LifetimeDiagnostics.ResetAll();
                DiagnosticsSettings.Apply();
            }
        }
    }
}
