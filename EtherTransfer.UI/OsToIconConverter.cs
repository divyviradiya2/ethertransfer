using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace EtherTransfer.UI;

public class OsToIconConverter : IValueConverter
{
    public static readonly OsToIconConverter Instance = new();

    private static Bitmap? _windowsIcon;
    private static Bitmap? _appleIcon;
    private static Bitmap? _linuxIcon;

    static OsToIconConverter()
    {
        try
        {
            var asmName = typeof(OsToIconConverter).Assembly.GetName().Name;
            _windowsIcon = new Bitmap(AssetLoader.Open(new Uri($"avares://{asmName}/Assets/windows.png")));
            _appleIcon = new Bitmap(AssetLoader.Open(new Uri($"avares://{asmName}/Assets/apple.png")));
            _linuxIcon = new Bitmap(AssetLoader.Open(new Uri($"avares://{asmName}/Assets/linux.png")));
        }
        catch { }
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var os = value as string;

        if (string.Equals(os, "macOS", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(os, "OSX", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(os, "Apple", StringComparison.OrdinalIgnoreCase))
            return _appleIcon;

        if (string.Equals(os, "Linux", StringComparison.OrdinalIgnoreCase))
            return _linuxIcon;

        return _windowsIcon;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

