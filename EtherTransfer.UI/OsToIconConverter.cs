using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace EtherTransfer.UI;

/// <summary>
/// Converts an OS string ("Windows", "macOS", "Linux", etc.) into a crisp vector StreamGeometry
/// that automatically adapts to both Light and Dark theme modes with optimal contrast.
/// </summary>
public class OsToIconConverter : IValueConverter
{
    public static readonly OsToIconConverter Instance = new();

    private static readonly StreamGeometry _windowsGeometry = StreamGeometry.Parse("M3,3H11V11H3V3M13,3H21V11H13V3M3,13H11V21H3V13M13,13H21V21H13V13Z");
    private static readonly StreamGeometry _appleGeometry = StreamGeometry.Parse("M18.71,19.5C17.88,20.74 17,21.95 15.66,21.97C14.32,22 13.89,21.18 12.37,21.18C10.84,21.18 10.37,21.95 9.09,22C7.79,22.05 6.8,20.68 5.96,19.47C4.25,17 2.94,12.45 4.7,9.39C5.57,7.87 7.13,6.91 8.82,6.88C10.1,6.86 11.32,7.75 12.11,7.75C12.89,7.75 14.37,6.68 15.92,6.84C16.57,6.87 18.39,7.1 19.56,8.82C19.47,8.88 17.39,10.1 17.41,12.63C17.44,15.65 20.06,16.66 20.09,16.67C20.06,16.74 19.67,18.11 18.71,19.5M15.97,4.72C16.6,3.95 17.03,2.88 16.91,1.81C15.99,1.85 14.86,2.42 14.2,3.19C13.62,3.87 13.11,4.96 13.25,6C14.28,6.08 15.34,5.49 15.97,4.72Z");
    private static readonly StreamGeometry _linuxGeometry = StreamGeometry.Parse("M12,2A4,4 0 0,0 8,6C8,7.3 8.6,8.5 9.6,9.2C7.5,10.1 6,12.3 6,15C6,16.5 6.5,17.8 7.3,18.8C6.5,19.5 6,20.5 6,21.5A0.5,0.5 0 0,0 6.5,22H17.5A0.5,0.5 0 0,0 18,21.5C18,20.5 17.5,19.5 16.7,18.8C17.5,17.8 18,16.5 18,15C18,12.3 16.5,10.1 14.4,9.2C15.4,8.5 16,7.3 16,6A4,4 0 0,0 12,2M12,4A2,2 0 0,1 14,6A2,2 0 0,1 12,8A2,2 0 0,1 10,6A2,2 0 0,1 12,4M10.5,5.5A0.5,0.5 0 0,0 10,6A0.5,0.5 0 0,0 10.5,6.5A0.5,0.5 0 0,0 11,6A0.5,0.5 0 0,0 10.5,5.5M13.5,5.5A0.5,0.5 0 0,0 13,6A0.5,0.5 0 0,0 13.5,6.5A0.5,0.5 0 0,0 14,6A0.5,0.5 0 0,0 13.5,5.5M12,7C12.5,7 13,7.2 13,7.5C13,7.8 12.5,8 12,8C11.5,8 11,7.8 11,7.5C11,7.2 11.5,7 12,7Z");
    private static readonly StreamGeometry _genericGeometry = StreamGeometry.Parse("M21,16H3V4H21V16M21,2H3C1.9,2 1,2.9 1,4V16C1,17.1 1.9,18 3,18H10V20H8V22H16V20H14V18H21C22.1,18 23,17.1 23,16V4C23,2.9 22.1,2 21,2Z");

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var os = value as string;
        if (string.Equals(os, "Windows", StringComparison.OrdinalIgnoreCase))
            return _windowsGeometry;
        if (string.Equals(os, "macOS", StringComparison.OrdinalIgnoreCase) || string.Equals(os, "OSX", StringComparison.OrdinalIgnoreCase) || string.Equals(os, "Apple", StringComparison.OrdinalIgnoreCase))
            return _appleGeometry;
        if (string.Equals(os, "Linux", StringComparison.OrdinalIgnoreCase))
            return _linuxGeometry;

        return _genericGeometry;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
