using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace EtherTransfer.Transfer;

/// <summary>
/// Production-grade path sanitization for cross-platform file transfers.
/// Prevents path traversal attacks, absolute path injection, reserved filename abuse,
/// and ensures all received files remain strictly within the destination sandbox.
/// </summary>
public static class PathSanitizer
{
    // Windows reserved filenames (case-insensitive)
    private static readonly string[] WindowsReservedNames =
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    // Characters illegal in filenames on Windows (and generally problematic everywhere)
    private static readonly char[] IllegalChars = { '<', '>', ':', '"', '|', '?', '*' };

    /// <summary>
    /// Takes an untrusted relative path from the network and produces a safe absolute path
    /// guaranteed to be inside the sandbox directory. Returns null if the path is entirely invalid.
    /// </summary>
    public static string? SanitizeRelativePath(string sandboxDir, string untrustedRelativePath)
    {
        if (string.IsNullOrWhiteSpace(untrustedRelativePath))
            return null;

        // 1. Normalize separators to forward slash, then split into segments
        var normalized = untrustedRelativePath.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
            return null;

        // 2. Process each path segment individually
        var safeSegments = new string[segments.Length];
        for (int i = 0; i < segments.Length; i++)
        {
            var safe = SanitizeSegment(segments[i]);
            if (string.IsNullOrWhiteSpace(safe))
                return null; // Entire segment was invalid
            safeSegments[i] = safe;
        }

        // 3. Reconstruct using platform-native separator
        var relativePath = Path.Combine(safeSegments);
        var fullPath = Path.GetFullPath(Path.Combine(sandboxDir, relativePath));

        // 4. Final containment check — MUST start with the sandbox directory
        var normalizedSandbox = Path.GetFullPath(sandboxDir);
        if (!normalizedSandbox.EndsWith(Path.DirectorySeparatorChar.ToString()))
            normalizedSandbox += Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(normalizedSandbox, GetPathComparison()))
            return null; // Path escaped the sandbox

        return fullPath;
    }

    /// <summary>
    /// Sanitizes a single path segment (filename or directory name).
    /// Strips illegal characters, blocks traversal, handles reserved names.
    /// </summary>
    private static string SanitizeSegment(string segment)
    {
        // Block traversal segments
        if (segment == "." || segment == "..")
            return string.Empty;

        // Strip null bytes (used in some injection attacks)
        segment = segment.Replace("\0", "");

        // Remove illegal characters
        foreach (var c in IllegalChars)
            segment = segment.Replace(c.ToString(), "");

        // Remove control characters (0x00-0x1F)
        segment = new string(segment.Where(c => !char.IsControl(c)).ToArray());

        // Trim leading/trailing dots and spaces (Windows rejects these)
        segment = segment.Trim('.', ' ');

        if (string.IsNullOrWhiteSpace(segment))
            return string.Empty;

        // Handle Windows reserved names (CON, PRN, NUL, COM1, etc.)
        // Even on Linux, sanitize these for cross-platform safety
        var nameWithoutExt = Path.GetFileNameWithoutExtension(segment).ToUpperInvariant();
        if (WindowsReservedNames.Contains(nameWithoutExt))
        {
            segment = "_" + segment; // Prefix to make it safe
        }

        // Enforce maximum segment length (255 bytes is the universal filesystem limit)
        if (segment.Length > 255)
            segment = segment.Substring(0, 255);

        return segment;
    }

    /// <summary>
    /// Generates a unique filename when a collision exists at the destination.
    /// Example: "photo.jpg" -> "photo (1).jpg" -> "photo (2).jpg"
    /// </summary>
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

    /// <summary>
    /// Returns the appropriate StringComparison for the current platform.
    /// Windows/macOS use case-insensitive paths; Linux uses case-sensitive.
    /// </summary>
    private static StringComparison GetPathComparison()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return StringComparison.Ordinal;
        return StringComparison.OrdinalIgnoreCase;
    }
}
