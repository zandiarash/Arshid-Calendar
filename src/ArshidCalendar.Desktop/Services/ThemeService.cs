using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using ArshidCalendar.Core;

namespace ArshidCalendar.Desktop.Services;

/// <summary>
/// Swaps the light / dark palette (following Windows when set to System) and
/// paints the seasonal accent colours: spring green, summer sun, autumn ember, winter ice.
/// </summary>
public sealed class ThemeService
{
    private static readonly (Color From, Color To)[] SeasonColors =
    [.. Enum.GetValues<Season>().Select(static s =>
    {
        var (from, to) = SeasonPalette.Hex(s);
        return ((Color)ColorConverter.ConvertFromString(from), (Color)ColorConverter.ConvertFromString(to));
    })];

    private AppTheme _theme;

    public event Action? ThemeChanged;

    public bool IsDark { get; private set; }

    public ThemeService() => SystemEvents.UserPreferenceChanged += (_, e) =>
    {
        // Theme (System only) or the Windows accent colour changed.
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
            Application.Current.Dispatcher.BeginInvoke(() => { if (_theme == AppTheme.System) Apply(_theme); else ApplySystemAccent(); });
    };

    public void Apply(AppTheme theme)
    {
        _theme = theme;
        IsDark = theme switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => IsSystemDark(),
        };

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var palette = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/{(IsDark ? "Dark" : "Light")}.xaml", UriKind.Absolute),
        };
        dictionaries[0] = palette;
        ApplySystemAccent();
        ThemeChanged?.Invoke();
    }

    /// <summary>
    /// The Windows accent colour, as the shell uses it: the darker shade on light surfaces and the lighter one on dark
    /// (e.g. #0067C0 / #4CC2FF for the default blue). Used by the taskbar flyout so it matches the native calendar.
    /// </summary>
    public void ApplySystemAccent()
    {
        var color = IsDark ? Color.FromRgb(0x4C, 0xC2, 0xFF) : Color.FromRgb(0x00, 0x67, 0xC0);
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent");
            // AccentPalette: 8 RGBA entries — Light3, Light2, Light1, Base, Dark1, Dark2, Dark3, (unused).
            if (key?.GetValue("AccentPalette") is byte[] { Length: >= 32 } palette)
            {
                var i = (IsDark ? 1 : 4) * 4;
                color = Color.FromRgb(palette[i], palette[i + 1], palette[i + 2]);
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            // Keep the default Windows blue.
        }
        Application.Current.Resources["SystemAccentBrush"] = Freeze(new SolidColorBrush(color));
    }

    /// <summary>Hero gradient follows today's season.</summary>
    /// <param name="translucent">Glass mode: let the blurred desktop glow through the coloured header.</param>
    public static void ApplyHeroSeason(Season season, bool translucent)
    {
        var (from, to) = SeasonColors[(int)season];
        var alpha = translucent ? (byte)0xD0 : (byte)0xFF;
        Application.Current.Resources["HeroBrush"] = Gradient(WithAlpha(from, alpha), WithAlpha(to, alpha));
    }

    /// <summary>Accent colours follow the season of the month on screen.</summary>
    public static void ApplyAccentSeason(Season season)
    {
        var (from, to) = SeasonColors[(int)season];
        var res = Application.Current.Resources;
        res["AccentBrush"] = Freeze(new SolidColorBrush(from));
        res["AccentGradientBrush"] = Gradient(from, to);
        res["AccentSoftBrush"] = Freeze(new SolidColorBrush(Color.FromArgb(0x2E, from.R, from.G, from.B)));
        res["AccentGlowColor"] = from;
    }

    private static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    public static Color SeasonColor(Season season) => SeasonColors[(int)season].From;

    private static LinearGradientBrush Gradient(Color from, Color to) =>
        Freeze(new LinearGradientBrush(from, to, new Point(0, 0), new Point(1, 1)));

    private static T Freeze<T>(T brush) where T : Freezable
    {
        brush.Freeze();
        return brush;
    }

    private static bool IsSystemDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int and 0;
    }
}
