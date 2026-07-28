using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;
using System.Globalization;

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
        return os switch
        {
            "Windows" => _windowsIcon,
            "macOS" => _appleIcon,
            "Linux" => _linuxIcon,
            _ => null
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
