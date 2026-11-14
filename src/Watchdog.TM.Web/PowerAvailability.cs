using System.ComponentModel;
using System.Runtime.InteropServices;

/// <summary>Requests that Windows not enter ordinary idle sleep while live monitoring is active.</summary>
public sealed class PowerAvailability : IDisposable
{
    IntPtr request;
    public string Status { get; private set; } = "Off";

    public void Apply(bool enabled)
    {
        if (!OperatingSystem.IsWindows()) { Status = enabled ? "Unavailable on this operating system" : "Off"; return; }
        if (!enabled) { Clear(); Status = "Off"; return; }
        if (request != IntPtr.Zero) { Status = "Keeping this computer awake for live monitoring"; return; }
        var context = new ReasonContext { Version = 0, Flags = 1, Reason = "Watchdog TM is performing live temperature monitoring." };
        request = PowerCreateRequest(ref context);
        if (request == IntPtr.Zero) { Status = "Could not request awake monitoring: " + new Win32Exception(Marshal.GetLastWin32Error()).Message; return; }
        if (!PowerSetRequest(request, 0))
        {
            Status = "Could not request awake monitoring: " + new Win32Exception(Marshal.GetLastWin32Error()).Message;
            CloseHandle(request); request = IntPtr.Zero; return;
        }
        Status = "Keeping this computer awake for live monitoring";
    }

    void Clear()
    {
        if (request == IntPtr.Zero) return;
        PowerClearRequest(request, 0);
        CloseHandle(request);
        request = IntPtr.Zero;
    }

    public void Dispose() => Clear();

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ReasonContext { public uint Version; public uint Flags; [MarshalAs(UnmanagedType.LPWStr)] public string Reason; }
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr PowerCreateRequest(ref ReasonContext context);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool PowerSetRequest(IntPtr request, int requestType);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool PowerClearRequest(IntPtr request, int requestType);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr handle);
}
