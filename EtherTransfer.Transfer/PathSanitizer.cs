using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace EtherTransfer.Transfer;

public static class PathSanitizer
{

    private static readonly string[] WindowsReservedNames =
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private static readonly char[] IllegalChars = { '<', '>', ':', '"', '|', '?', '*' };

    public static string? SanitizeRelativePath(string sandboxDir, string untrustedRelativePath)
    {
        if (string.IsNullOrWhiteSpace(untrustedRelativePath))
            return null;

        var normalized = untrustedRelativePath.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
            return null;

        var safeSegments = new string[segments.Length];
        for (int i = 0; i < segments.Length; i++)
        {
            var safe = SanitizeSegment(segments[i]);
            if (string.IsNullOrWhiteSpace(safe))
                return null;
            safeSegments[i] = safe;
        }

        var relativePath = Path.Combine(safeSegments);
        var fullPath = Path.GetFullPath(Path.Combine(sandboxDir, relativePath));

        var normalizedSandbox = Path.GetFullPath(sandboxDir);
        if (!normalizedSandbox.EndsWith(Path.DirectorySeparatorChar.ToString()))
            normalizedSandbox += Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(normalizedSandbox, GetPathComparison()))
            return null;

        return fullPath;
    }

    private static string SanitizeSegment(string segment)
    {

        if (segment == "." || segment == "..")
            return string.Empty;

        segment = segment.Replace("\0", "");

        foreach (var c in IllegalChars)
            segment = segment.Replace(c.ToString(), "");

        segment = new string(segment.Where(c => !char.IsControl(c)).ToArray());

        segment = segment.TrimStart(' ').TrimEnd('.', ' ');

        if (string.IsNullOrWhiteSpace(segment))
            return string.Empty;

        var nameWithoutExt = Path.GetFileNameWithoutExtension(segment).ToUpperInvariant();
        if (WindowsReservedNames.Contains(nameWithoutExt))
        {
            segment = "_" + segment;
        }

        if (segment.Length > 255)
            segment = segment.Substring(0, 255);

        return segment;
    }

    public static string ResolveCollision(string fullPath)
    {
        if (!File.Exists(fullPath))
            return fullPath;

        var dir = Path.GetDirectoryName(fullPath) ?? ".";
        var nameWithoutExt = Path.GetFileNameWithoutExtension(fullPath);
        var ext = Path.GetExtension(fullPath);

        int counter = 1;
        string candidate;
        do
        {
            candidate = Path.Combine(dir, $"{nameWithoutExt} ({counter}){ext}");
            counter++;
        } while (File.Exists(candidate));

        return candidate;
    }

    private static StringComparison GetPathComparison()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return StringComparison.Ordinal;
        return StringComparison.OrdinalIgnoreCase;
    }
}
