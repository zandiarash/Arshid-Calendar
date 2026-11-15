using System.Runtime.InteropServices;
using Microsoft.Win32;
using Windows.ApplicationModel;

namespace ArshidCalendar.Desktop.Services;

/// <summary>
/// "Start with Windows" (per-user, no admin rights needed).
/// <list type="bullet">
/// <item>Packaged (Microsoft Store / MSIX): the manifest's <c>windows.startupTask</c>. Registry writes are
/// virtualized inside a package, and Store policy asks for the startup task anyway.</item>
/// <item>Unpackaged (zip): the HKCU Run key.</item>
/// </list>
/// </summary>
public static partial class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ArshidCalendar";
    /// <summary>Must match <c>uap5:StartupTask TaskId</c> in Package.appxmanifest.</summary>
    private const string TaskId = "ArshidCalendarStartup";

    /// <summary>True when running from an MSIX package (has package identity).</summary>
    public static bool IsPackaged { get; } = HasPackageIdentity();

    public static bool IsEnabled
    {
        get
        {
            if (IsPackaged)
            {
                var state = GetTask()?.State;
                return state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
            }
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
        set
        {
            if (IsPackaged)
            {
                if (GetTask() is not { } task) return;
                if (value)
                {
                    // Shows nothing unless the user turned it off in Settings → Apps → Startup earlier
                    // (DisabledByUser); then Windows only lets the user turn it back on there.
                    var state = task.RequestEnableAsync().AsTask().GetAwaiter().GetResult();
                    if (state == StartupTaskState.DisabledByUser) OpenStartupSettings();
                }
                else
                {
                    task.Disable();
                }
                return;
            }

            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value && Environment.ProcessPath is { } exe)
                key.SetValue(ValueName, $"\"{exe}\"");
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    private static StartupTask? GetTask()
    {
        try { return StartupTask.GetAsync(TaskId).AsTask().GetAwaiter().GetResult(); }
        catch (Exception ex) when (ex is ArgumentException or COMException) { return null; }
    }

    private static void OpenStartupSettings()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private static unsafe bool HasPackageIdentity()
    {
        uint length = 0;
        // ERROR_INSUFFICIENT_BUFFER (122) → has identity; APPMODEL_ERROR_NO_PACKAGE (15700) → unpackaged.
        return GetCurrentPackageFullName(ref length, null) == 122;
    }

    [LibraryImport("kernel32.dll")]
    private static unsafe partial int GetCurrentPackageFullName(ref uint packageFullNameLength, char* packageFullName);
}
