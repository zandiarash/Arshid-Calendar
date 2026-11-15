using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ArshidCalendar.Core;
using ArshidCalendar.Desktop.Services;
using ArshidCalendar.Desktop.ViewModels;
using static ArshidCalendar.Desktop.Services.NativeMethods;

namespace ArshidCalendar.Desktop;

/// <summary>
/// The taskbar calendar: a Windows 11-style flyout that opens above the notification area when the tray icon is
/// clicked, like the native clock/calendar flyout — date header, collapsible month grid, ▲/▼ month carets, a year
/// view, light dismiss. It uses its own <see cref="MainViewModel"/> so browsing here doesn't move the desktop widget.
/// </summary>
public partial class CalendarFlyout : Window
{
    /// <summary>A click on the tray icon right after the flyout was dismissed (by that same click) shouldn't reopen it.</summary>
    private const int ReopenGuardMs = 400;

    private readonly MainViewModel _vm;
    private readonly WidgetSettings _settings;
    private readonly ThemeService _theme;
    private nint _hwnd;
    private NativePoint _anchor;
    private long _hiddenAt;
    private bool _hiding;
    private bool _yearView;
    private int _yearViewYear;

    public CalendarFlyout(MainViewModel vm, WidgetSettings settings, ThemeService theme)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        _settings = settings;
        _theme = theme;

        vm.MonthChanged += OnMonthChanged;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.MonthName) or nameof(MainViewModel.MonthYear)) UpdateTitle();
        };
        theme.ThemeChanged += ApplyBackdrop;

        SourceInitialized += OnSourceInitialized;
        Deactivated += (_, _) => { if (!StayOpen) HideFlyout(); };
        SizeChanged += (_, _) => { if (IsVisible) PlaceNearTray(); }; // keep the bottom edge anchored when folding
        DpiChanged += (_, _) => { if (IsVisible) PlaceNearTray(); };
        PreviewKeyDown += OnKeyDown;

        ApplyCollapsed();
        UpdateTitle();
    }

    /// <summary>Raised by the «ویجت» button: the app shows the full desktop widget.</summary>
    public event Action? OpenWidgetRequested;

    /// <summary>Don't light-dismiss (used while rendering a snapshot).</summary>
    public bool StayOpen { get; set; }

    // ======================= Native window setup =======================

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(_hwnd)!.CompositionTarget.BackgroundColor = Colors.Transparent;

        // Tool window: no Alt+Tab or taskbar entry, like the shell's own flyouts.
        var ex = GetWindowLongPtr(_hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, (ex | WS_EX_TOOLWINDOW) & ~(nint)WS_EX_APPWINDOW);
        ApplyBackdrop();
    }

    /// <summary>The Windows 11 transient-window acrylic (the material of the shell's flyouts) with rounded corners.</summary>
    public void ApplyBackdrop()
    {
        if (_hwnd == 0) return;
        SetDwm(_hwnd, DwmWindowAttribute.UseImmersiveDarkMode, _theme.IsDark ? 1 : 0);
        SetDwm(_hwnd, DwmWindowAttribute.WindowCornerPreference, (int)DwmCorner.Round);
        SetDwm(_hwnd, DwmWindowAttribute.SystemBackdropType, (int)DwmBackdrop.Acrylic);
        _ = DwmExtendFrameIntoClientArea(_hwnd, new Margins(-1));
    }

    // ======================= Show / hide =======================

    public void Toggle()
    {
        if (IsVisible) HideFlyout();
        else if (Environment.TickCount64 - _hiddenAt > ReopenGuardMs) ShowFlyout();
    }

    public void ShowFlyout()
    {
        // Like the native calendar, it always reopens on today.
        _vm.ResetToToday();
        ShowDaysView(animate: false);
        GetCursorPos(out _anchor);

        new WindowInteropHelper(this).EnsureHandle();
        PlaceNearTray(); // before Show, so it never flashes at its old spot
        Show();
        Activate();
        PlaceNearTray(); // again, now that the window has its real size / DPI
        PlayEntrance();
    }

    public void HideFlyout()
    {
        if (!IsVisible || _hiding) return;
        _hiding = true;
        _hiddenAt = Environment.TickCount64;
        Hide();
        _hiding = false;
    }

    /// <summary>
    /// Bottom-right of the work area of the monitor that was clicked, 12 px clear of the taskbar — or next to the
    /// taskbar when it is docked at the top or the left.
    /// </summary>
    private void PlaceNearTray()
    {
        if (_hwnd == 0) return;
        var monitor = MonitorFromPoint(_anchor, MONITOR_DEFAULTTONEAREST);
        var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return;
        var scale = GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 ? dpi / 96.0 : VisualTreeHelper.GetDpi(this).DpiScaleX;

        Root.Measure(new Size(Width, double.PositiveInfinity));
        var w = (int)Math.Ceiling(Width * scale);
        var h = (int)Math.Ceiling(Root.DesiredSize.Height * scale);
        var margin = (int)Math.Round(12 * scale);

        var (work, bounds) = (info.rcWork, info.rcMonitor);
        var x = work.Left > bounds.Left ? work.Left + margin : work.Right - w - margin;
        var y = work.Top > bounds.Top ? work.Top + margin : work.Bottom - h - margin;
        SetWindowPos(_hwnd, 0, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    /// <summary>Content slides up and fades in, like the shell flyouts.</summary>
    private void PlayEntrance()
    {
        var ease = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 };
        BodyTranslate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(40, 0, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease });
        Body.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
    }

    // ======================= Header =======================

    private void Header_Click(object sender, RoutedEventArgs e)
    {
        _vm.ResetToToday();
        ShowDaysView(animate: true);
    }

    private void CollapseButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.FlyoutCollapsed = !_settings.FlyoutCollapsed;
        ApplyCollapsed();
        App.Current.SaveSettings();
    }

    private void ApplyCollapsed()
    {
        var collapsed = _settings.FlyoutCollapsed;
        CalendarPanel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        CollapseGlyph.Text = collapsed ? "" : ""; // ChevronUp : ChevronDown
        CollapseButton.ToolTip = collapsed ? "نمایش تقویم" : "پنهان کردن تقویم";
    }

    // ======================= Day view / year view =======================

    private void UpdateTitle()
    {
        TitleText.Text = _yearView ? _yearViewYear.ToPersianDigits() : $"{_vm.MonthName} {_vm.MonthYear}";
        TitleButton.IsHitTestVisible = !_yearView;
        TitleButton.ToolTip = _yearView ? null : "انتخاب ماه";
        UpButton.ToolTip = _yearView ? "سال قبل" : "ماه قبل";
        DownButton.ToolTip = _yearView ? "سال بعد" : "ماه بعد";
    }

    private void Title_Click(object sender, RoutedEventArgs e) => ShowYearView();

    private void ShowYearView()
    {
        _yearView = true;
        _yearViewYear = _vm.DisplayedMonth.Year;
        BuildMonths();
        DaysView.Visibility = Visibility.Collapsed;
        MonthsList.Visibility = Visibility.Visible;
        UpdateTitle();
        Zoom(from: 1.08);
    }

    private void ShowDaysView(bool animate)
    {
        if (!_yearView) return;
        _yearView = false;
        MonthsList.Visibility = Visibility.Collapsed;
        DaysView.Visibility = Visibility.Visible;
        UpdateTitle();
        if (animate) Zoom(from: 0.92);
    }

    private void BuildMonths()
    {
        var (today, shown) = (_vm.Today, _vm.DisplayedMonth);
        MonthsList.ItemsSource = Enumerable.Range(1, 12)
            .Select(m => new MonthCell(m, PersianText.Months[m - 1],
                IsCurrent: today.Year == _yearViewYear && today.Month == m,
                IsDisplayed: shown.Year == _yearViewYear && shown.Month == m))
            .ToList();
    }

    private void Month_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not MonthCell cell) return;
        var target = new JalaliDate(_yearViewYear, cell.Month, 1);
        ShowDaysView(animate: true);
        _vm.ShowMonth(target, direction: 0);
    }

    private void Up_Click(object sender, RoutedEventArgs e) => Step(-1);

    private void Down_Click(object sender, RoutedEventArgs e) => Step(+1);

    /// <summary>▲ goes back, ▼ forward: a month in the day view, a year in the year view.</summary>
    private void Step(int direction)
    {
        if (_yearView)
        {
            _yearViewYear += direction;
            BuildMonths();
            UpdateTitle();
            Slide(direction);
        }
        else
        {
            (direction < 0 ? _vm.PreviousMonthCommand : _vm.NextMonthCommand).Execute(null);
        }
    }

    private void ViewsHost_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        Step(e.Delta > 0 ? -1 : 1);
        e.Handled = true;
    }

    private void OnMonthChanged(int direction)
    {
        if (_yearView) ShowDaysView(animate: false); // e.g. «امروز» pressed while in the year view
        if (direction != 0) Slide(direction);
    }

    /// <summary>The grid scrolls vertically, matching the ▲ / ▼ carets: the next month rises from below.</summary>
    private void Slide(int direction)
    {
        var duration = TimeSpan.FromMilliseconds(260);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        ViewsTranslate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(28 * Math.Sign(direction), 0, duration) { EasingFunction = ease });
        ViewsHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, duration) { EasingFunction = ease });
    }

    private void Zoom(double from)
    {
        var duration = TimeSpan.FromMilliseconds(220);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        ViewsScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(from, 1, duration) { EasingFunction = ease });
        ViewsScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(from, 1, duration) { EasingFunction = ease });
        ViewsHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
    }

    // ======================= Footer / keyboard =======================

    private void OpenWidget_Click(object sender, RoutedEventArgs e)
    {
        HideFlyout();
        OpenWidgetRequested?.Invoke();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                if (_yearView) ShowDaysView(animate: true);
                else HideFlyout();
                break;
            case Key.PageUp or Key.Up:
                Step(-1);
                break;
            case Key.PageDown or Key.Down:
                Step(+1);
                break;
            case Key.Home or Key.T:
                Header_Click(this, e);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    // ======================= Snapshot / lifetime =======================

    /// <summary>Renders the flyout over a flat acrylic-like fill to a PNG — used by <c>--snapshot … --flyout</c>.</summary>
    public void SaveSnapshot(string path, double scale = 2)
    {
        BodyTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        Body.BeginAnimation(OpacityProperty, null);
        Body.Opacity = 1;
        var backdrop = new SolidColorBrush((Color)FindResource("FlyoutSnapshotColor"));
        SnapshotRenderer.Save(Root, backdrop, cornerRadius: 8, path, scale);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Alt+F4 just dismisses the flyout; the app exits from the menu.
        if (!App.Current.IsExiting)
        {
            e.Cancel = true;
            HideFlyout();
        }
        base.OnClosing(e);
    }
}
