using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;

namespace EtherTransfer.UI;

public class OsToIconConverter : IValueConverter
{
    public static readonly OsToIconConverter Instance = new();

    private static Bitmap? _windowsLight;
    private static Bitmap? _windowsDark;
    private static Bitmap? _appleLight;
    private static Bitmap? _appleDark;
    private static Bitmap? _linuxIcon;

    static OsToIconConverter()
    {
        try
        {
            var asmName = typeof(OsToIconConverter).Assembly.GetName().Name;
            _windowsLight = new Bitmap(AssetLoader.Open(new Uri($"avares://{asmName}/Assets/windows.png")));
            _windowsDark = new Bitmap(AssetLoader.Open(new Uri($"avares://{asmName}/Assets/windows_white.png")));
            _appleLight = new Bitmap(AssetLoader.Open(new Uri($"avares://{asmName}/Assets/apple.png")));
            _appleDark = new Bitmap(AssetLoader.Open(new Uri($"avares://{asmName}/Assets/apple_white.png")));
            _linuxIcon = new Bitmap(AssetLoader.Open(new Uri($"avares://{asmName}/Assets/linux.png")));
        }
        catch { }
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var os = value as string;
        bool isDark = Application.Current?.ActualThemeVariant != ThemeVariant.Light;

        if (string.Equals(os, "Windows", StringComparison.OrdinalIgnoreCase))
            return isDark ? _windowsDark : _windowsLight;

        if (string.Equals(os, "macOS", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(os, "OSX", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(os, "Apple", StringComparison.OrdinalIgnoreCase))
            return isDark ? _appleDark : _appleLight;

        if (string.Equals(os, "Linux", StringComparison.OrdinalIgnoreCase))
            return _linuxIcon;

        return isDark ? _windowsDark : _windowsLight;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

