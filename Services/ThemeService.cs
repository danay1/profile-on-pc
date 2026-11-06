using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;

namespace ProfileOnPC.Services;

public static class ThemeService
{
    private const int DwmUseImmersiveDarkMode = 20;

    public static void Apply(string theme, string accentHex)
    {
        var application = Application.Current;
        if (application is null)
            return;

        var dictionaries = application.Resources.MergedDictionaries;
        var oldTheme = dictionaries.FirstOrDefault(d =>
            d.Source?.OriginalString.Contains("Theme.xaml", StringComparison.OrdinalIgnoreCase) == true);

        var newTheme = new ResourceDictionary
        {
            Source = new Uri(
                theme.Equals("Light", StringComparison.OrdinalIgnoreCase)
                    ? "Themes/LightTheme.xaml"
                    : "Themes/DarkTheme.xaml",
                UriKind.Relative)
        };

        if (oldTheme is not null)
        {
            var index = dictionaries.IndexOf(oldTheme);
            dictionaries[index] = newTheme;
        }
        else
        {
            dictionaries.Insert(0, newTheme);
        }

        var accent = ParseColor(accentHex, MediaColor.FromRgb(37, 99, 166));
        application.Resources["AccentColor"] = accent;
        application.Resources["AccentBrush"] = new SolidColorBrush(accent);
        application.Resources["AccentHoverBrush"] = new SolidColorBrush(ChangeBrightness(accent, 0.12));
        application.Resources["AccentPressedBrush"] = new SolidColorBrush(ChangeBrightness(accent, -0.12));

        foreach (Window window in application.Windows)
            ApplyTitleBar(window, theme);
    }

    public static void ApplyTitleBar(Window window, string theme)
    {
        if (!window.IsLoaded)
        {
            window.SourceInitialized += (_, _) => ApplyTitleBar(window, theme);
            return;
        }

        var value = theme.Equals("Dark", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        var handle = new WindowInteropHelper(window).Handle;
        _ = DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref value, sizeof(int));
    }

    private static MediaColor ParseColor(string value, MediaColor fallback)
    {
        try
        {
            return (MediaColor)ColorConverter.ConvertFromString(value);
        }
        catch
        {
            return fallback;
        }
    }

    private static MediaColor ChangeBrightness(MediaColor color, double amount)
    {
        byte Change(byte channel)
        {
            var value = amount >= 0
                ? channel + (255 - channel) * amount
                : channel * (1 + amount);
            return (byte)Math.Clamp(value, 0, 255);
        }

        return MediaColor.FromRgb(Change(color.R), Change(color.G), Change(color.B));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}
