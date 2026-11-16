using System.Windows;
using ArshidCalendar.Core;
using ArshidCalendar.Desktop.Services;
using ArshidCalendar.Desktop.ViewModels;

namespace ArshidCalendar.Desktop;

/// <summary>Composition root and app-wide commands (shared by the window menu and the tray icon).</summary>
public partial class App : Application
{
    private const string InstanceName = @"Local\ArshidCalendar.SingleInstance";
    private const string ShowSignalName = @"Local\ArshidCalendar.Show";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showSignal;
    private readonly SettingsService _settings = new();
    private readonly ThemeService _theme = new();
    private TrayIconService? _tray;
    private MainViewModel? _vm;
    private MainWindow? _window;
    private MainViewModel? _flyoutVm;
    private CalendarFlyout? _flyout;

    public static new App Current => (App)Application.Current;

    /// <summary>Four-part software version (AppVersion in Directory.Build.props), in Persian digits.</summary>
    public static string Version
    {
        get;
    } =
        (System.Reflection.CustomAttributeExtensions
            .GetCustomAttribute<System.Reflection.AssemblyFileVersionAttribute>(typeof(App).Assembly)?.Version ?? "0.0.0.0")
        .ToPersianDigits();

    public bool IsExiting
    {
        get; private set;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // ---- Single instance: a second launch just brings the existing widget forward ----
        _instanceMutex = new Mutex(initiallyOwned: true, InstanceName, out var isFirst);
        if (!isFirst)
        {
            if (EventWaitHandle.TryOpenExisting(ShowSignalName, out var signal)) signal.Set();
            Shutdown();
            return;
        }
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
        ThreadPool.RegisterWaitForSingleObject(_showSignal,
            (_, _) => Dispatcher.BeginInvoke(() => _window?.ShowWidget()), null, Timeout.Infinite, executeOnlyOnce: false);

        base.OnStartup(e);

        var settings = _settings.Load().Current;
        _theme.Apply(settings.Theme);

        _vm = new MainViewModel(TimeProvider.System, settings.HijriAdjustment)
        {
            ShowSeconds = settings.ShowSeconds,
            ShowGregorianNumbers = settings.ShowGregorianNumbers,
        };

        _window = new MainWindow(_vm, settings, _theme);
        MainWindow = _window;

        // Taskbar calendar flyout: its own view model, so browsing months there doesn't move the widget.
        _flyoutVm = new MainViewModel(TimeProvider.System, settings.HijriAdjustment);
        _flyout = new CalendarFlyout(_flyoutVm, settings, _theme);
        _flyout.OpenWidgetRequested += () => _window.ShowWidget();

        _tray = new TrayIconService();
        _tray.LeftClick += ToggleFlyout;
        _tray.RightClick += () => { _window.ShowWidget(); _window.ShowMenuAtCursor(); };
        UpdateTray();
        _tray.Show();
        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.TodayDay)) UpdateTray();
        };

        _window.Show();

        // `ArshidCalendar.exe --snapshot out.png [--dark|--light] [--glass|--normal] [--flyout]` renders a PNG and exits
        // (the flags only affect the snapshot; saved settings are left alone).
        if (e.Args is ["--snapshot", var path, .. var rest])
        {
            if (rest.Contains("--light")) _theme.Apply(AppTheme.Light);
            else if (rest.Contains("--dark")) _theme.Apply(AppTheme.Dark);
            if (rest.Contains("--normal") || rest.Contains("--glass"))
            {
                settings.Glass = rest.Contains("--glass");
                _window.ApplyBackdrop();
            }
            var flyout = rest.Contains("--flyout");
            if (flyout)
            {
                _flyout.StayOpen = true;
                _flyout.ShowFlyout();
            }
            _window.Dispatcher.BeginInvoke(() =>
            {
                if (flyout) _flyout.SaveSnapshot(path);
                else _window.SaveSnapshot(path);
                IsExiting = true;
                Shutdown();
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
    }

    private void UpdateTray() =>
        _tray?.Update(_vm!.Today, ThemeService.SeasonColor(_vm.Today.Season));

    /// <summary>Tray left-click: the taskbar calendar flyout, like clicking the clock on the taskbar.</summary>
    public void ToggleFlyout() => _flyout?.Toggle();

    public void ToggleWindow()
    {
        if (_window is null) return;
        if (_window.IsVisible) _window.Hide();
        else _window.ShowWidget();
    }

    public void SetLayer(WindowLayer layer)
    {
        _settings.Current.Layer = layer;
        _window?.ApplyLayer();
        SaveSettings();
    }

    public void SetTheme(AppTheme theme)
    {
        _settings.Current.Theme = theme;
        _theme.Apply(theme);
        SaveSettings();
    }

    public void SetGlass(bool glass)
    {
        _settings.Current.Glass = glass;
        _window?.ApplyBackdrop();
        SaveSettings();
    }

    public void ToggleSeconds()
    {
        _settings.Current.ShowSeconds = !_settings.Current.ShowSeconds;
        _vm?.ShowSeconds = _settings.Current.ShowSeconds;
        SaveSettings();
    }

    public void ToggleGregorianNumbers()
    {
        _settings.Current.ShowGregorianNumbers = !_settings.Current.ShowGregorianNumbers;
        _vm?.ShowGregorianNumbers = _settings.Current.ShowGregorianNumbers;
        SaveSettings();
    }

    public void ToggleStartup() => StartupService.IsEnabled = !StartupService.IsEnabled;

    public void SaveSettings() => _settings.Save();

    public void ExitApp()
    {
        IsExiting = true;
        SaveSettings();
        _window?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _vm?.Dispose();
        _flyoutVm?.Dispose();
        _showSignal?.Dispose();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Opens a web link in the user's default browser. Only absolute http/https URLs are accepted.</summary>
    internal void OpenUrl(string url)
    {
        // Never hand arbitrary strings to the shell: it would happily launch executables or file paths.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            ShowLinkError("آدرس نامعتبر است.");
            return;
        }

        try
        {
            // UseShellExecute routes the URL to the default browser; the returned handle (often null) is disposed right away.
            using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ShowLinkError(ex.Message);
        }
    }

    /// <summary>
    /// Starts a new email to <paramref name="email"/> (a bare address or a <c>mailto:</c> link) in the default
    /// mail app (Outlook, Windows Mail, …). If no mail app is set up, composes it on Outlook.com in the browser.
    /// </summary>
    internal void MailTo(string email)
    {
        var address = email.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ? email["mailto:".Length..] : email;
        // Accept a single plain address only, so nothing else (extra recipients, headers, paths) reaches the shell.
        if (!System.Net.Mail.MailAddress.TryCreate(address, out var parsed) || parsed.Address != address.Trim())
        {
            ShowLinkError("آدرس ایمیل نامعتبر است.");
            return;
        }

        var to = Uri.EscapeDataString(parsed.Address);
        var subject = Uri.EscapeDataString($"تقویم آرشید {Version}");
        try
        {
            using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = $"mailto:{to}?subject={subject}",
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No app is registered for mailto: → Outlook.com's compose page.
            OpenUrl($"https://outlook.live.com/mail/0/deeplink/compose?to={to}&subject={subject}");
        }
    }

    private void ShowLinkError(string detail)
    {
        var owner = _window is { IsVisible: true } ? _window : null;
        const string message = "خطا در باز کردن لینک:\n";
        const MessageBoxOptions rtl = MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign;
        if (owner is null)
        {
            MessageBox.Show(message + detail, "خطا", MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.OK, rtl);
        }
        else
        {
            MessageBox.Show(owner, message + detail, "خطا", MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.OK, rtl);
        }
    }

}
