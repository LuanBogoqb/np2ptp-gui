namespace Np2ptpGui.Interop;

using System;
using System.Runtime.InteropServices;
using System.Threading;

/// <summary>
/// Sends CTRL_C to a specific child console process from a GUI process that
/// has no console of its own. tokio::signal::ctrl_c() on Windows only reacts
/// to CTRL_C_EVENT (not CTRL_BREAK_EVENT), and GenerateConsoleCtrlEvent can
/// only target CTRL_C at "every process on the calling process's current
/// console" — so this briefly attaches to the child's own console (the only
/// process on it), signals, then detaches.
/// </summary>
public static class ConsoleCtrl
{
    private const uint CTRL_C_EVENT = 0;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCtrlHandler(IntPtr handlerRoutine, bool add);

    public static bool TrySendCtrlC(int processId)
    {
        // AttachConsole fails with ERROR_ACCESS_DENIED if the calling process
        // is already attached to a console (e.g. a console-subsystem host such
        // as a test runner). This WPF app never has a console of its own, so
        // this is a no-op in production; detaching first makes the call work
        // regardless of the caller's own console state.
        FreeConsole();

        if (!AttachConsole((uint)processId)) return false;
        try
        {
            // While attached, this process is itself a sibling on the child's
            // console, so the CTRL_C broadcast below also targets us.
            // SetConsoleCtrlHandler(NULL, true) makes this process ignore it.
            SetConsoleCtrlHandler(IntPtr.Zero, true);
            var sent = GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0);

            // GenerateConsoleCtrlEvent only queues delivery; the ignore flag
            // above must still be in effect when the OS actually dispatches
            // the event to this process, or the default action (terminate)
            // applies to us too. Undoing it immediately races with that
            // async dispatch and can kill the caller. Give it a moment.
            Thread.Sleep(200);

            return sent;
        }
        finally
        {
            SetConsoleCtrlHandler(IntPtr.Zero, false);
            FreeConsole();
        }
    }
}
