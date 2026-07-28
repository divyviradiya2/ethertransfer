using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace EtherTransfer.UI;

public class OsToIconConverter : IValueConverter
{
    public static readonly OsToIconConverter Instance = new();

    // SVG Paths for OS icons
    private const string WindowsIcon = "M11,10 L0,11.5 L0,22 L11,22 L11,10 Z M12,9.8 L24,8 L24,22 L12,22 L12,9.8 Z M11,9.3 L11,0 L0,2 L0,8.8 L11,9.3 Z M12,9 L12,0 L24,0 L24,7.8 L12,9 Z";
    
    private const string AppleIcon = "M12.152,6.896 C13.535,6.896 14.851,6.299 15.659,5.201 C16.536,4.01 16.89,2.449 16.638,0.902 C15.176,1.077 13.578,1.968 12.636,3.153 C11.834,4.161 11.411,5.653 11.758,7.098 C13.344,7.218 14.939,6.241 15.864,5.031 L12.152,6.896 Z M16.924,23.111 C17.371,23.111 17.818,22.95 18.156,22.632 C18.73,22.091 21.053,18.895 21.849,17.202 C22.18,16.495 22.842,14.62 23.152,13.23 C23.237,12.845 23.364,12.28 23.364,11.77 C23.364,10.669 22.848,9.757 21.996,9.15 C21.144,8.543 19.988,8.232 18.875,8.232 C17.291,8.232 15.857,8.847 14.996,9.22 C14.37,9.49 13.626,9.49 12.999,9.22 C12.138,8.847 10.704,8.232 9.121,8.232 C8.008,8.232 6.852,8.543 6,9.15 C5.148,9.757 4.632,10.669 4.632,11.77 C4.632,12.28 4.759,12.845 4.844,13.23 C5.154,14.62 5.816,16.495 6.147,17.202 C6.943,18.895 9.266,22.091 9.84,22.632 C10.178,22.95 10.625,23.111 11.072,23.111 C11.53,23.111 11.97,22.923 12.44,22.454 C13.314,21.583 14.682,21.583 15.556,22.454 C16.026,22.923 16.466,23.111 16.924,23.111 Z";
    
    private const string LinuxIcon = "M12.775,1.758 C15.422,2.023 17.585,4.325 18.06,6.963 C18.258,8.063 18.158,9.18 17.778,10.228 C18.73,11.037 19.268,12.223 19.268,13.486 C19.268,15.705 17.469,17.504 15.25,17.504 C14.939,17.504 14.634,17.468 14.339,17.399 C13.784,18.887 12.355,19.92 10.722,19.92 C9.089,19.92 7.66,18.887 7.105,17.399 C6.81,17.468 6.505,17.504 6.194,17.504 C3.975,17.504 2.176,15.705 2.176,13.486 C2.176,12.223 2.714,11.037 3.666,10.228 C3.286,9.18 3.186,8.063 3.384,6.963 C3.859,4.325 6.022,2.023 8.669,1.758 C9.998,1.626 11.446,1.626 12.775,1.758 Z"; // Basic penguin blob

    private const string UnknownIcon = "M4,4 L20,4 L20,16 L4,16 Z M2,4 C2,2.895 2.895,2 4,2 L20,2 C21.105,2 22,2.895 22,4 L22,16 C22,17.105 21.105,18 20,18 L15,18 L15,20 L17,20 L17,22 L7,22 L7,20 L9,20 L9,18 L4,18 C2.895,18 2,17.105 2,16 L2,4 Z";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var os = value as string;
        string pathData = os switch
        {
            "Windows" => WindowsIcon,
            "macOS" => AppleIcon,
            "Linux" => LinuxIcon,
            _ => UnknownIcon
        };

        return Geometry.Parse(pathData);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
