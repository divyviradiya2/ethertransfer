using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

Console.WriteLine("=== EtherTransfer Network Diagnostic Tool ===");
Console.WriteLine($"Machine Name: {Environment.MachineName}");
Console.WriteLine($"OS: {Environment.OSVersion}");
Console.WriteLine($"Time: {DateTime.Now}");
Console.WriteLine();

// ──────────────────────────────────────────────
// STEP 1: Dump ALL network interfaces
// ──────────────────────────────────────────────
Console.WriteLine("════════════════════════════════════════");
Console.WriteLine("STEP 1: ALL NETWORK INTERFACES");
Console.WriteLine("════════════════════════════════════════");

var allInterfaces = NetworkInterface.GetAllNetworkInterfaces();
int idx = 0;
foreach (var ni in allInterfaces)
{
    idx++;
    Console.WriteLine($"  [{idx}] Name: \"{ni.Name}\"");
    Console.WriteLine($"      Description: \"{ni.Description}\"");
    Console.WriteLine($"      Type: {ni.NetworkInterfaceType}");
    Console.WriteLine($"      Status: {ni.OperationalStatus}");
    
    var ipProps = ni.GetIPProperties();
    foreach (var addr in ipProps.UnicastAddresses)
    {
        if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
        {
            Console.WriteLine($"      IPv4: {addr.Address}  Mask: {addr.IPv4Mask}");
        }
    }
    Console.WriteLine();
}

// ──────────────────────────────────────────────
// STEP 2: What GetEthernetInterfaces() returns
// ──────────────────────────────────────────────
Console.WriteLine("════════════════════════════════════════");
Console.WriteLine("STEP 2: ETHERNET INTERFACES (what our app sees)");
Console.WriteLine("════════════════════════════════════════");

var ethernetInterfaces = new List<(IPAddress Local, IPAddress Broadcast)>();

var filteredInterfaces = NetworkInterface.GetAllNetworkInterfaces()
    .Where(ni => ni.OperationalStatus == OperationalStatus.Up && 
                 ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                 ni.NetworkInterfaceType != NetworkInterfaceType.Wireless80211);

foreach (var ni in filteredInterfaces)
{
    var name = ni.Name.ToLowerInvariant();
    var desc = ni.Description.ToLowerInvariant();
    
    bool isWireless = name.Contains("wi-fi") || name.Contains("wlan") || name.StartsWith("wl") || desc.Contains("wireless");
    
    Console.WriteLine($"  Interface: \"{ni.Name}\" (Type={ni.NetworkInterfaceType})");
    Console.WriteLine($"    Wireless filter: {(isWireless ? "SKIPPED (wireless)" : "PASSED (not wireless)")}");
    
    if (isWireless) 
    {
        Console.WriteLine();
        continue;
    }

    var ipProps = ni.GetIPProperties();
    foreach (var ip in ipProps.UnicastAddresses)
    {
        if (ip.Address.AddressFamily == AddressFamily.InterNetwork && ip.IPv4Mask != null)
        {
            var ipBytes = ip.Address.GetAddressBytes();
            var maskBytes = ip.IPv4Mask.GetAddressBytes();
            
            if (maskBytes.Length == 4 && ipBytes.Length == 4)
            {
                var broadcastBytes = new byte[4];
                for (int i = 0; i < 4; i++)
                {
                    broadcastBytes[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
                }
                
                bool maskAllZero = maskBytes.All(b => b == 0);
                var broadcastAddr = new IPAddress(broadcastBytes);
                
                Console.WriteLine($"    IPv4: {ip.Address}  Mask: {ip.IPv4Mask}  Broadcast: {broadcastAddr}  MaskAllZero: {maskAllZero}");
                
                if (!maskAllZero)
                {
                    ethernetInterfaces.Add((ip.Address, broadcastAddr));
                    Console.WriteLine($"    >>> INCLUDED as Ethernet interface");
                }
                else
                {
                    Console.WriteLine($"    >>> EXCLUDED (mask is all zeros)");
                }
            }
        }
    }
    Console.WriteLine();
}

if (ethernetInterfaces.Count == 0)
{
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("  *** NO ETHERNET INTERFACES DETECTED! ***");
    Console.WriteLine("  This is why your device list is blank.");
    Console.WriteLine("  The app has zero interfaces to broadcast on.");
    Console.ResetColor();
    Console.WriteLine();
    Console.WriteLine("  Possible causes:");
    Console.WriteLine("  - Ethernet cable not plugged in / no link");
    Console.WriteLine("  - OS hasn't assigned an IP yet (APIPA can take 30-60 seconds)");
    Console.WriteLine("  - Interface type is being filtered out incorrectly");
    Console.WriteLine();
}
else
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"  Found {ethernetInterfaces.Count} Ethernet interface(s) to use.");
    Console.ResetColor();
}

// ──────────────────────────────────────────────
// STEP 3: Test UDP Send
// ──────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("════════════════════════════════════════");
Console.WriteLine("STEP 3: UDP BROADCAST TEST (sending)");
Console.WriteLine("════════════════════════════════════════");

var testPayload = Encoding.UTF8.GetBytes("{\"Type\":\"HELLO\",\"Id\":\"EtherTransferApp-V1\",\"ComputerName\":\"" + Environment.MachineName + "\",\"TcpPort\":55000}");

foreach (var (local, broadcast) in ethernetInterfaces)
{
    Console.Write($"  Sending broadcast from {local} to {broadcast}:50000 ... ");
    try
    {
        using var sender = new UdpClient();
        sender.Client.Bind(new IPEndPoint(local, 0));
        sender.EnableBroadcast = true;
        sender.Send(testPayload, testPayload.Length, new IPEndPoint(broadcast, 50000));
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("OK");
        Console.ResetColor();
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"FAILED: {ex.Message}");
        Console.ResetColor();
    }
}

// ──────────────────────────────────────────────
// STEP 4: Test UDP Receive (5 seconds)
// ──────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("════════════════════════════════════════");
Console.WriteLine("STEP 4: UDP LISTEN TEST (5 seconds on port 50000)");
Console.WriteLine("  Run this tool on the OTHER PC at the same time!");
Console.WriteLine("════════════════════════════════════════");

try
{
    using var listener = new UdpClient();
    listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
    listener.Client.Bind(new IPEndPoint(IPAddress.Any, 50000));
    
    Console.WriteLine("  Listening on 0.0.0.0:50000 ...");
    
    // Also send a broadcast every second while listening
    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    
    // Background sender
    _ = Task.Run(async () =>
    {
        while (!cts.Token.IsCancellationRequested)
        {
            foreach (var (local, broadcast) in ethernetInterfaces)
            {
                try
                {
                    using var s = new UdpClient();
                    s.Client.Bind(new IPEndPoint(local, 0));
                    s.EnableBroadcast = true;
                    s.Send(testPayload, testPayload.Length, new IPEndPoint(broadcast, 50000));
                }
                catch { }
            }
            try { await Task.Delay(1000, cts.Token); } catch { }
        }
    });
    
    int received = 0;
    while (!cts.Token.IsCancellationRequested)
    {
        try
        {
            var task = listener.ReceiveAsync(cts.Token);
            var result = await task;
            received++;
            var text = Encoding.UTF8.GetString(result.Buffer);
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"  [{received}] From {result.RemoteEndPoint}: {text}");
            Console.ResetColor();
        }
        catch (OperationCanceledException) { break; }
        catch (Exception ex)
        {
            Console.WriteLine($"  Receive error: {ex.Message}");
            break;
        }
    }
    
    if (received == 0)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("  *** NO PACKETS RECEIVED in 5 seconds ***");
        Console.WriteLine("  This means either:");
        Console.WriteLine("  1. The other PC isn't running this tool / the app");
        Console.WriteLine("  2. A FIREWALL is blocking UDP port 50000");
        Console.WriteLine("  3. The Ethernet cable has no link");
        Console.ResetColor();
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  Received {received} packet(s). UDP is working!");
        Console.ResetColor();
    }
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"  FAILED to bind listener: {ex.Message}");
    Console.WriteLine("  This usually means another app is exclusively using port 50000.");
    Console.ResetColor();
}

Console.WriteLine();
Console.WriteLine("=== Diagnostic Complete ===");
Console.WriteLine("Please share this output so we can fix the issue.");
