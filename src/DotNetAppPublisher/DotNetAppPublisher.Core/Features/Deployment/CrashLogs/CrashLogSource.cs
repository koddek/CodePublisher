namespace DotNetAppPublisher.Features.Deployment.CrashLogs;

/// <summary>Where a crash report came from.</summary>
public enum CrashLogSource
{
    /// <summary>The app wrote the file itself into its own data folder.</summary>
    AppData,

    /// <summary>Android's own crash store (the DropBox service).</summary>
    AndroidCrashStore,

    /// <summary>The device log buffer (logcat on Android).</summary>
    DeviceLog,

    /// <summary>The operating system's crash report (the .ips files iOS and macOS write).</summary>
    SystemCrashLog,

    /// <summary>A local dump file (the Windows local dumps folder).</summary>
    LocalDump
}
