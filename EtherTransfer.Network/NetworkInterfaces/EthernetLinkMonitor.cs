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
    
    // Track configured interfaces to teardown later
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
        
        // Initial evaluation
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
                // Force it back to configuring
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
                // Instant transition to NoCable on unplug
                TransitionTo(EthernetLinkState.NoCable);
                return;
            }

            var hasIpv4 = upInterfaces.Any(i => i.HasIpv4Address);

            if (hasIpv4)
            {
                TransitionTo(EthernetLinkState.Ready);
            }
            else
            {
                // Has UP interfaces, but no IPv4. 
                if (_currentState == EthernetLinkState.NoCable || _currentState == EthernetLinkState.Ready)
                {
                    TransitionTo(EthernetLinkState.Configuring);
                }
                else if (_currentState == EthernetLinkState.Configuring)
                {
                    // Check for timeout
                    if (_configStartTime.HasValue && (DateTime.UtcNow - _configStartTime.Value) > _configTimeout)
                    {
                        LastErrorMessage = "Configuration timed out. Check NetworkManager logs or try manually.";
                        TransitionTo(EthernetLinkState.ConfigError);
                    }
                }
                // If ConfigError, we don't auto-retry just because it's still Up with no IP. User must hit Retry, or it must go Down -> Up.
            }
        }
    }

    private void TransitionTo(EthernetLinkState newState)
    {
        var changed = false;
        var needsTeardown = false;
        var needsConfig = false;

        lock (_lock)
        {
            if (_currentState == newState) return;

            // Debouncing: We only debounce going out of Configuring to NoCable? 
            // "A short debounce (150–300ms) applies only to smoothing a brief renegotiation blip during Configuring."
            // "It must NOT delay the NoCable transition."
            // Wait, if we get an event saying Down, and we are Configuring, do we transition to NoCable instantly? Yes!
            
            _currentState = newState;
            changed = true;

            if (newState == EthernetLinkState.NoCable)
            {
                _configAttemptCts?.Cancel();
                _configAttemptCts = null;
                _configStartTime = null;
                needsTeardown = true;
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

        if (needsTeardown)
        {
            TeardownConfiguration();
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
            // Windows/Mac handle link-local natively quite well, just wait.
            return;
        }

        List<string> interfacesToConfig = new();
        lock (_lock)
        {
            var interfaces = _networkProvider.GetEthernetInterfaces()
                .Where(i => i.OperationalStatus == OperationalStatus.Up && !i.HasIpv4Address)
                .ToList();
            interfacesToConfig.AddRange(interfaces.Select(i => i.Name));
        }

        // Apply a short debounce here before actually triggering nmcli, in case NM is just resetting the interface
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
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "nmcli",
                    Arguments = $"device reapply {ifaceName}",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                
                // CRITICAL: We must block and wait for this to exit. 
                // If this is running during an app shutdown, firing and forgetting 
                // will cause the OS to kill the nmcli child process before it finishes!
                p?.WaitForExit(3000); 
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
            else
            {
                errorMessage = devModResult.output;
                return false;
            }
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
