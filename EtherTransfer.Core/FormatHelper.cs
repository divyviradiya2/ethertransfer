namespace EtherTransfer.Core;

public static class FormatHelper
{
    public static string FormatSize(long bytes)
    {
        double mb = bytes / 1024.0 / 1024.0;
        if (mb >= 1024.0)
        {
            double gb = mb / 1024.0;
            return $"{gb:F2} GB";
        }
        else if (mb >= 1.0 || bytes == 0)
        {
            return $"{mb:F1} MB";
        }
        else
        {
            double kb = bytes / 1024.0;
            return $"{kb:F1} KB";
        }
    }
}
