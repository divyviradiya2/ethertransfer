using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace EtherTransfer.Network.NetworkInterfaces;

public interface IPlatformEnvironment
{
    bool IsLinux { get; }
    bool IsWindows { get; }

    bool DirectoryExists(string path);
    string? GetSymlinkTarget(string path);
    string? GetRegistryValue(string keyPath, string valueName);
}

public class DefaultPlatformEnvironment : IPlatformEnvironment
{
    public bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    public bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public string? GetSymlinkTarget(string path)
    {
        if (Directory.Exists(path))
        {
            return new DirectoryInfo(path).LinkTarget;
        }
        return null;
    }

    public string? GetRegistryValue(string keyPath, string valueName)
    {
#if NET6_0_OR_GREATER
        if (!IsWindows) return null;
#pragma warning disable CA1416
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath);
        return key?.GetValue(valueName) as string;
#pragma warning restore CA1416
#else
        return null;
#endif
    }
}
