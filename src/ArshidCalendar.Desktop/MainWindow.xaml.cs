using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using ArshidCalendar.Desktop.Services;
using ArshidCalendar.Desktop.ViewModels;
using static ArshidCalendar.Desktop.Services.NativeMethods;

namespace ArshidCalendar.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly WidgetSettings _settings;
    private readonly ThemeService _theme;
    private nint _hwnd;

    public MainWindow(MainViewModel vm, WidgetSettings settings, ThemeService theme)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        _settings = settings;
        _theme = theme;

        ThemeService.ApplyHeroSeason(vm.TodaySeason, translucent: settings.Glass);
        ThemeService.ApplyAccentSeason(vm.DisplayedSeason);

        vm.MonthChanged += AnimateMonth;
        vm.PropertyChanged += OnViewModelPropertyChanged;
        theme.ThemeChanged += ApplyBackdrop;

        ContextMenu = new ContextMenu();
        ContextMenuOpening += (_, _) => BuildMenu(ContextMenu);

        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => { RestorePosition(); PlayEntrance(); };
        PreviewKeyDown += OnKeyDown;
    }

    // ======================= Native window setup =======================

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(_hwnd)!;
        source.CompositionTarget.BackgroundColor = Colors.Transparent;
        source.AddHook(WndProc);

        // Tool window: hidden from Alt+Tab and the taskbar, like a real desktop gadget.
        var ex = GetWindowLongPtr(_hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, (ex | WS_EX_TOOLWINDOW) & ~(nint)WS_EX_APPWINDOW);

        ApplyBackdrop();
        ApplyLayer();
    }

    /// <summary>
    /// Glass: live blur of the desktop behind the widget + a light tint, sheen and translucent header.
    /// Normal: a solid, opaque background and header.
    /// Both get DWM rounded corners and the native shadow.
    /// </summary>
    public void ApplyBackdrop()
    {
        var glass = _settings.Glass;
        Root.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, glass ? "GlassTintBrush" : "SolidBackgroundBrush");
        Sheen.Visibility = glass ? Visibility.Visible : Visibility.Collapsed;
        Hero.BorderThickness = new Thickness(glass ? 1 : 0); // glass edge only in glass mode

        // Normal mode: opaque cards instead of the see-through glass ones (local override of the theme keys).
        Root.Resources.Remove("SurfaceBrush");
        Root.Resources.Remove("SurfaceStrokeBrush");
        if (!glass)
        {
            Root.Resources["SurfaceBrush"] = FindResource("SolidSurfaceBrush");
            Root.Resources["SurfaceStrokeBrush"] = FindResource("SolidSurfaceStrokeBrush");
        }
        ThemeService.ApplyHeroSeason(_vm.TodaySeason, translucent: glass);
        if (_hwnd == 0) return;

        SetDwm(_hwnd, DwmWindowAttribute.UseImmersiveDarkMode, _theme.IsDark ? 1 : 0);
        SetDwm(_hwnd, DwmWindowAttribute.WindowCornerPreference, (int)DwmCorner.Round);
        SetDwm(_hwnd, DwmWindowAttribute.SystemBackdropType, (int)DwmBackdrop.None);
        SetBlurBehind(_hwnd, glass);
        _ = DwmExtendFrameIntoClientArea(_hwnd, new Margins(-1));
    }

    public void ApplyLayer()
    {
        Topmost = _settings.Layer == WindowLayer.Topmost;
        if (_hwnd != 0 && _settings.Layer == WindowLayer.Desktop)
            SetWindowPos(_hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        switch (msg)
        {
            // "Pinned to desktop": keep the widget underneath every other window.
            case WM_WINDOWPOSCHANGING when _settings.Layer == WindowLayer.Desktop:
                var pos = Marshal.PtrToStructure<WindowPos>(lParam);
                if ((pos.flags & SWP_NOZORDER) == 0)
                {
                    pos.hwndInsertAfter = HWND_BOTTOM;
                    Marshal.StructureToPtr(pos, lParam, false);
                }
                break;

            // Keep the acrylic backdrop alive when the widget is not the active window.
            case WM_NCACTIVATE when wParam == 0:
                handled = true;
                return DefWindowProc(hwnd, msg, 1, lParam);
        }
        return 0;
    }

    // ======================= Position =======================

    private void RestorePosition()
    {
        var area = SystemParameters.WorkArea;
        var (left, top) = (_settings.Left ?? area.Right - ActualWidth - 24, _settings.Top ?? area.Top + 24);

        // Keep it reachable if a monitor was disconnected.
        var (vl, vt) = (SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop);
        var (vw, vh) = (SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        Left = Math.Clamp(left, vl, vl + vw - 80);
        Top = Math.Clamp(top, vt, vt + vh - 80);
    }

    private void DragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        DragMove();
        (_settings.Left, _settings.Top) = (Left, Top);
        App.Current.SaveSettings();
    }

    // ======================= Interaction =======================

    private void Days_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var step = e.Delta > 0 ? -1 : 1;
        var command = Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
            ? (step < 0 ? _vm.PreviousYearCommand : _vm.NextYearCommand)
            : (step < 0 ? _vm.PreviousMonthCommand : _vm.NextMonthCommand);
        command.Execute(null);
        e.Handled = true;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        ICommand? command = e.Key switch
        {
            Key.Left or Key.PageUp => _vm.PreviousMonthCommand, // ‹ previous
            Key.Right or Key.PageDown => _vm.NextMonthCommand,  // › next
            Key.Up => _vm.PreviousYearCommand,
            Key.Down => _vm.NextYearCommand,
            Key.Home or Key.T => _vm.GoToTodayCommand,
            Key.Escape => new RelayCommand(Hide),
            _ => null,
        };
        if (command is null) return;
        command.Execute(null);
        e.Handled = true;
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        BuildMenu(ContextMenu);
        ContextMenu.PlacementTarget = MoreButton;
        ContextMenu.Placement = PlacementMode.Bottom;
        ContextMenu.IsOpen = true;
    }

    /// <summary>Shows the same context menu at the mouse (used by the tray icon).</summary>
    public void ShowMenuAtCursor()
    {
        BuildMenu(ContextMenu);
        ContextMenu.PlacementTarget = null;
        ContextMenu.Placement = PlacementMode.MousePoint;
        ContextMenu.IsOpen = true;
    }

    private void BuildMenu(ContextMenu menu)
    {
        var app = App.Current;
        menu.Items.Clear();

        menu.Items.Add(new MenuItem
        {
            Header = $"تقویم آرشید نسخهٔ {App.Version}",
            Icon = new TextBlock { Text = "" },
            IsHitTestVisible = false,
            Focusable = false,
            Opacity = 0.55,
            FlowDirection = FlowDirection.RightToLeft,
        });
        menu.Items.Add(Item("سورس پروژه در گیت هاب", GitHubIcon(), false, () => app.OpenUrl("https://github.com/zandiarash/Arshid-Calendar")));
        menu.Items.Add(Item("آرش زندی ZandiArash@Hotmail.com", "", false, () => app.MailTo("ZandiArash@Hotmail.com"))); // Fluent "Mail"
        menu.Items.Add(new Separator());

        menu.Items.Add(Item("چسبیده به دسکتاپ", "", _settings.Layer == WindowLayer.Desktop, () => app.SetLayer(WindowLayer.Desktop)));
        menu.Items.Add(Item("همیشه روی پنجره‌ها", "", _settings.Layer == WindowLayer.Topmost, () => app.SetLayer(WindowLayer.Topmost)));
        menu.Items.Add(Item("پنجرهٔ معمولی", "", _settings.Layer == WindowLayer.Normal, () => app.SetLayer(WindowLayer.Normal)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("پوستهٔ سیستم", "", _settings.Theme == AppTheme.System, () => app.SetTheme(AppTheme.System)));
        menu.Items.Add(Item("پوستهٔ روشن", "", _settings.Theme == AppTheme.Light, () => app.SetTheme(AppTheme.Light)));
        menu.Items.Add(Item("پوستهٔ تیره", "", _settings.Theme == AppTheme.Dark, () => app.SetTheme(AppTheme.Dark)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("پس‌زمینهٔ شیشه‌ای", "", _settings.Glass, () => app.SetGlass(true)));
        menu.Items.Add(Item("پس‌زمینهٔ معمولی", "", !_settings.Glass, () => app.SetGlass(false)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("نمایش ثانیه", "", _settings.ShowSeconds, app.ToggleSeconds));
        menu.Items.Add(Item("نمایش روز میلادی در جدول", "", _settings.ShowGregorianNumbers, app.ToggleGregorianNumbers));
        menu.Items.Add(Item("اجرا همراه با ویندوز", "", StartupService.IsEnabled, app.ToggleStartup));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("پنهان کردن", "", false, Hide));
        menu.Items.Add(Item("خروج", "", false, app.ExitApp));


        // icon: a Segoe Fluent Icons glyph (string) or any element (e.g. GitHubIcon()).
        static MenuItem Item(string header, object icon, bool isChecked, Action action)
        {
            var item = new MenuItem
            {
                Header = header,
                Icon = icon as UIElement ?? new TextBlock { Text = (string)icon },
                IsChecked = isChecked,
            };
            item.Click += (_, _) => action();
            return item;
        }
    }

    /// <summary>GitHub mark (Octicons "mark-github", MIT) sized like a 14px Fluent glyph, in the menu item's text colour.</summary>
    private static Viewbox GitHubIcon()
    {
        var path = new System.Windows.Shapes.Path { Data = GitHubMark };
        path.SetBinding(System.Windows.Shapes.Shape.FillProperty, new System.Windows.Data.Binding(nameof(Foreground))
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(MenuItem), 1),
        });
        // A logo must not be mirrored by the right-to-left menu.
        return new Viewbox { Width = 14, Height = 14, Child = path, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left };
    }

    private static readonly Geometry GitHubMark = Freeze(Geometry.Parse(
        "M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59 0.4 0.07 0.55-0.17 0.55-0.38 0-0.19-0.01-0.82-0.01-1.49" +
        "-2.01 0.37-2.53-0.49-2.69-0.94-0.09-0.23-0.48-0.94-0.82-1.13-0.28-0.15-0.68-0.52-0.01-0.53 0.63-0.01 1.08 0.58 1.23 0.82" +
        " 0.72 1.21 1.87 0.87 2.33 0.66 0.07-0.52 0.28-0.87 0.51-1.07-1.78-0.2-3.64-0.89-3.64-3.95 0-0.87 0.31-1.59 0.82-2.15" +
        "-0.08-0.2-0.36-1.02 0.08-2.12 0 0 0.67-0.21 2.2 0.82 0.64-0.18 1.32-0.27 2-0.27 0.68 0 1.36 0.09 2 0.27 1.53-1.04 2.2-0.82 2.2-0.82" +
        " 0.44 1.1 0.16 1.92 0.08 2.12 0.51 0.56 0.82 1.27 0.82 2.15 0 3.07-1.87 3.75-3.65 3.95 0.29 0.25 0.54 0.73 0.54 1.48" +
        " 0 1.07-0.01 1.93-0.01 2.2 0 0.21 0.15 0.46 0.55 0.38A8.013 8.013 0 0 0 16 8c0-4.42-3.58-8-8-8z"));

    private static Geometry Freeze(Geometry g) { g.Freeze(); return g; }

    // ======================= Visual feedback =======================

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.DisplayedSeason):
                ThemeService.ApplyAccentSeason(_vm.DisplayedSeason);
                break;
            case nameof(MainViewModel.TodaySeason):
                ThemeService.ApplyHeroSeason(_vm.TodaySeason, translucent: _settings.Glass);
                break;
        }
    }

    private void AnimateMonth(int direction)
    {
        var duration = TimeSpan.FromMilliseconds(320);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        DaysTranslate.BeginAnimation(TranslateTransform.XProperty,
            // Next month slides in from the right (› side), previous from the left; X is mirrored in RTL.
            new DoubleAnimation(-36 * direction, 0, duration) { EasingFunction = ease });
        DaysList.BeginAnimation(OpacityProperty, new DoubleAnimation(0.15, 1, duration) { EasingFunction = ease });
    }

    private void PlayEntrance()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350)) { EasingFunction = ease });
        ProgressScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0, _vm.YearProgress, TimeSpan.FromMilliseconds(1100)) { EasingFunction = ease, BeginTime = TimeSpan.FromMilliseconds(200), FillBehavior = FillBehavior.Stop });
    }

    /// <summary>Renders the widget (over a backdrop-like fill) to a PNG — used by <c>--snapshot</c> for docs/CI.</summary>
    public void SaveSnapshot(string path, double scale = 2)
    {
        Root.BeginAnimation(OpacityProperty, null);
        Root.Opacity = 1;
        ProgressScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        // Stand-in for the blurred desktop: a soft wallpaper-like gradient (glass) or a flat acrylic fill.
        Brush backdrop = _settings.Glass
            ? new LinearGradientBrush(new GradientStopCollection
              {
                  new(Color.FromRgb(0x1E, 0x3A, 0x8A), 0), new(Color.FromRgb(0x7C, 0x3A, 0xED), 0.45),
                  new(Color.FromRgb(0xF9, 0x73, 0x16), 1),
              }, new Point(0, 0), new Point(1, 1))
            : new SolidColorBrush(_theme.IsDark ? Color.FromRgb(0x24, 0x22, 0x36) : Color.FromRgb(0xE9, 0xEC, 0xF5));
        SnapshotRenderer.Save(Root, backdrop, cornerRadius: 8, path, scale);
    }

    public void ShowWidget()
    {
        Show();
        WindowState = WindowState.Normal;
        if (_settings.Layer == WindowLayer.Desktop)
            ApplyLayer();
        else
            Activate();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Closing (Alt+F4) just hides the widget; exit from the menu.
        if (!App.Current.IsExiting)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }
}
