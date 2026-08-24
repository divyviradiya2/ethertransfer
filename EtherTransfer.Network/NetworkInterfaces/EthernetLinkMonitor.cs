using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace EtherTransfer.Network.NetworkInterfaces;

public enum EthernetLinkState
{
    NoCable,
    Configuring,
    Ready,
    ConfigError
}

public class EthernetLinkMonitor : IDisposable
{
    private readonly INetworkInterfaceProvider _networkProvider;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _configTimeout;
    private readonly TimeSpan _debounceInterval = TimeSpan.FromMilliseconds(250);

    private readonly object _lock = new();
    private EthernetLinkState _currentState = EthernetLinkState.NoCable;

    private readonly HashSet<string> _modifiedInterfaces = new();

    private CancellationTokenSource? _monitorCts;
    private CancellationTokenSource? _configAttemptCts;
    private DateTime? _configStartTime;

    public event EventHandler<EthernetLinkState>? StateChanged;

    public EthernetLinkState CurrentState
    {
        get
        {
            lock (_lock) return _currentState;
        }
    }

    public string? LastErrorMessage { get; private set; }

    public EthernetLinkMonitor(
        INetworkInterfaceProvider networkProvider,
        TimeSpan? pollInterval = null,
        TimeSpan? configTimeout = null)
    {
        _networkProvider = networkProvider;
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(1000);
        _configTimeout = configTimeout ?? TimeSpan.FromSeconds(12);
    }

    public void Start()
    {
        _monitorCts = new CancellationTokenSource();
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;

        _ = Task.Run(() => PollLoopAsync(_monitorCts.Token));

        EvaluateState();
    }

    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        EvaluateState();
    }

    public void ManualRetry()
    {
        lock (_lock)
        {
            if (_currentState == EthernetLinkState.ConfigError)
            {

                TransitionTo(EthernetLinkState.Configuring);
            }
        }
        EvaluateState();
    }

    private void EvaluateState()
    {
        lock (_lock)
        {
            var interfaces = _networkProvider.GetEthernetInterfaces().ToList();
            var upInterfaces = interfaces.Where(i => i.OperationalStatus == OperationalStatus.Up).ToList();

            if (upInterfaces.Count == 0)
            {

                TransitionTo(EthernetLinkState.NoCable);
                return;
            }

            var hasIpv4 = upInterfaces.Any(i => i.Ipv4Addresses.Count > 0);

            if (hasIpv4)
            {
                TransitionTo(EthernetLinkState.Ready);
            }
            else
            {

                if (_currentState == EthernetLinkState.NoCable || _currentState == EthernetLinkState.Ready)
                {
                    TransitionTo(EthernetLinkState.Configuring);
                }
                else if (_currentState == EthernetLinkState.Configuring)
                {

                    if (_configStartTime.HasValue && (DateTime.UtcNow - _configStartTime.Value) > _configTimeout)
                    {
                        LastErrorMessage = "Configuration timed out. Check NetworkManager logs or try manually.";
                        TransitionTo(EthernetLinkState.ConfigError);
                    }
                }

            }
        }
    }

    private void TransitionTo(EthernetLinkState newState)
    {
        var changed = false;
        var needsConfig = false;

        lock (_lock)
        {
            if (_currentState == newState) return;

            _currentState = newState;
            changed = true;

            if (newState == EthernetLinkState.NoCable)
            {
                _configAttemptCts?.Cancel();
                _configAttemptCts = null;
                _configStartTime = null;
            }
            else if (newState == EthernetLinkState.Configuring)
            {
                _configStartTime = DateTime.UtcNow;
                needsConfig = true;
            }
            else if (newState == EthernetLinkState.Ready || newState == EthernetLinkState.ConfigError)
            {
                _configAttemptCts?.Cancel();
                _configAttemptCts = null;
                _configStartTime = null;
            }
        }

        if (changed)
        {
            StateChanged?.Invoke(this, newState);
        }

        if (needsConfig)
        {
            _configAttemptCts = new CancellationTokenSource();
            _ = Task.Run(() => AttemptConfigurationAsync(_configAttemptCts.Token));
        }
    }

    private async Task AttemptConfigurationAsync(CancellationToken ct)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {

            return;
        }

        List<string> interfacesToConfig = new();
        lock (_lock)
        {
            var interfaces = _networkProvider.GetEthernetInterfaces()
                .Where(i => i.OperationalStatus == OperationalStatus.Up && i.Ipv4Addresses.Count == 0)
                .ToList();
            interfacesToConfig.AddRange(interfaces.Select(i => i.Name));
        }

        try
        {
            await Task.Delay(_debounceInterval, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        foreach (var ifaceName in interfacesToConfig)
        {
            if (ct.IsCancellationRequested) return;

            var success = TryConfigureWithNmcli(ifaceName, out var error);
            if (success)
            {
                lock (_lock)
                {
                    _modifiedInterfaces.Add(ifaceName);
                }
            }
            else
            {
                lock (_lock)
                {
                    LastErrorMessage = $"nmcli failed on {ifaceName}: {error}";
                    if (_currentState == EthernetLinkState.Configuring)
                    {
                        TransitionTo(EthernetLinkState.ConfigError);
                    }
                }
            }
        }
    }

    private void TeardownConfiguration()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return;

        List<string> toRestore = new();
        lock (_lock)
        {
            toRestore.AddRange(_modifiedInterfaces);
            _modifiedInterfaces.Clear();
        }

        foreach (var ifaceName in toRestore)
        {
            try
            {
                Console.WriteLine($"[EtherTransfer] Tearing down link-local config for {ifaceName}...");

                string conName = $"EtherTransfer-{ifaceName}";
                RunCommand("nmcli", $"connection delete \"{conName}\"");

                RunCommand("nmcli", $"device reapply {ifaceName}");

                Console.WriteLine($"[EtherTransfer] Teardown complete for {ifaceName}.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EtherTransfer] Teardown failed for {ifaceName}: {ex.Message}");
            }
        }
    }

    private bool TryConfigureWithNmcli(string ifaceName, out string errorMessage)
    {
        errorMessage = "";
        try
        {
            var whichResult = RunCommand("which", "nmcli");
            if (whichResult.exitCode != 0)
            {
                errorMessage = "NetworkManager (nmcli) is not installed.";
                return false;
            }

            var devModResult = RunCommand("nmcli", $"device modify {ifaceName} ipv4.method link-local");
            if (devModResult.exitCode == 0)
            {
                return true;
            }

            var connectResult = RunCommand("nmcli", $"device connect {ifaceName}");
            if (connectResult.exitCode == 0)
            {
                var retryModResult = RunCommand("nmcli", $"device modify {ifaceName} ipv4.method link-local");
                if (retryModResult.exitCode == 0)
                {
                    return true;
                }
            }

            string conName = $"EtherTransfer-{ifaceName}";

            var conUpResult = RunCommand("nmcli", $"connection up \"{conName}\"");
            if (conUpResult.exitCode == 0)
            {
                return true;
            }

            var addResult = RunCommand("nmcli", $"connection add type ethernet ifname {ifaceName} con-name \"{conName}\" ipv4.method link-local autoconnect no");
            if (addResult.exitCode == 0)
            {
                var upResult = RunCommand("nmcli", $"connection up \"{conName}\"");
                if (upResult.exitCode == 0)
                {
                    return true;
                }
                else
                {
                    errorMessage = upResult.output;
                    return false;
                }
            }

            errorMessage = string.IsNullOrWhiteSpace(devModResult.output) ? addResult.output : devModResult.output;
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private (int exitCode, string output) RunCommand(string command, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return (-1, "Failed to start process");

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();

            bool exited = process.WaitForExit(5000);
            if (!exited)
            {
                try { process.Kill(); } catch { }
                return (-1, "Process timed out");
            }

            return (process.ExitCode, string.IsNullOrEmpty(output) ? error : output);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                EvaluateState();
                await Task.Delay(_pollInterval, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    public void Dispose()
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
        _monitorCts?.Cancel();
        _monitorCts?.Dispose();
        _configAttemptCts?.Cancel();
        _configAttemptCts?.Dispose();

        TeardownConfiguration();
    }
}

