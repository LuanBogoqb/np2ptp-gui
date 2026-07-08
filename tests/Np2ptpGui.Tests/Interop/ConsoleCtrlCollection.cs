namespace Np2ptpGui.Tests.Interop;

using Xunit;

// ConsoleCtrl.TrySendCtrlC drives process-wide Win32 console-attach state
// (AttachConsole/SetConsoleCtrlHandler/FreeConsole). ConsoleCtrlTests and
// ProcessRunnerTests' CTRL_C test both exercise this state directly, so they
// must never run concurrently with each other during a test run — grouping
// them into this collection (with parallelization disabled) guarantees xUnit
// serializes them.
[CollectionDefinition("ConsoleCtrl", DisableParallelization = true)]
public class ConsoleCtrlCollectionDefinition
{
}
