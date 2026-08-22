using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EtherTransfer.Core.Models;
using EtherTransfer.Transfer;
using NUnit.Framework;

namespace EtherTransfer.Tests;

[TestFixture]
public class ProtocolFramingTests
{
    private static (TcpListener listener, int port) StartLoopbackListener()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return (listener, port);
    }

    [Test]
    public async Task SendMessage_ThenReceiveMessage_TransfersTypedJsonAccurately()
    {
        var (listener, port) = StartLoopbackListener();

        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            var stream = serverClient.GetStream();
            return await ProtocolHelper.ReceiveMessageAsync<TransferRequestMessage>(stream, CancellationToken.None);
        });

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        var clientStream = client.GetStream();

        var requestToSend = new TransferRequestMessage
        {
            SenderName = "TestMachine",
            TotalFiles = 42,
            TotalSize = 1024 * 1024 * 50,
            ContainsFolders = true,
            PayloadFolderCount = 2,
            PayloadFileCount = 40,
            RootElementNames = new System.Collections.Generic.List<string> { "FolderA", "FolderB" }
        };

        await ProtocolHelper.SendMessageAsync(clientStream, requestToSend, CancellationToken.None);

        var received = await serverTask;
        listener.Stop();

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.SenderName, Is.EqualTo("TestMachine"));
        Assert.That(received.TotalFiles, Is.EqualTo(42));
        Assert.That(received.TotalSize, Is.EqualTo(1024 * 1024 * 50));
        Assert.That(received.RootElementNames.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task ReceiveMessage_WhenPayloadExceeds10MB_ThrowsInvalidDataException()
    {
        var (listener, port) = StartLoopbackListener();

        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            var stream = serverClient.GetStream();
            return await ProtocolHelper.ReceiveMessageAsync<TransferRequestMessage>(stream, CancellationToken.None);
        });

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        var clientStream = client.GetStream();

        // Write a 4-byte length prefix specifying 15 MB (exceeds 10 MB limit)
        var invalidLengthBytes = BitConverter.GetBytes(15 * 1024 * 1024);
        await clientStream.WriteAsync(invalidLengthBytes, 0, 4);

        Assert.ThrowsAsync<InvalidDataException>(async () => await serverTask);
        listener.Stop();
    }

    [Test]
    public async Task ReadExactAsync_ReadsFragmentedDataCompletely()
    {
        var (listener, port) = StartLoopbackListener();

        var testBytes = new byte[8192];
        new Random(42).NextBytes(testBytes);

        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            var stream = serverClient.GetStream();
            var buffer = new byte[8192];
            var success = await ProtocolHelper.ReadExactAsync(stream, buffer, 8192, CancellationToken.None);
            return (success, buffer);
        });

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        var clientStream = client.GetStream();

        // Send in small fragments with tiny delay
        await clientStream.WriteAsync(testBytes.AsMemory(0, 2048));
        await Task.Delay(10);
        await clientStream.WriteAsync(testBytes.AsMemory(2048, 2048));
        await Task.Delay(10);
        await clientStream.WriteAsync(testBytes.AsMemory(4096, 4096));

        var (success, receivedBytes) = await serverTask;
        listener.Stop();

        Assert.That(success, Is.True);
        Assert.That(receivedBytes, Is.EqualTo(testBytes));
    }

    [Test]
    public void DiscoveryMessage_Serialization_RoundTripsCorrectly()
    {
        var msg = new DiscoveryMessage
        {
            Type = "HELLO",
            Id = "EtherTransferApp-V1",
            SessionId = Guid.NewGuid().ToString(),
            ComputerName = "Alice-PC",
            TcpPort = 55000,
            OS = "Windows",
            SequenceNumber = 123
        };

        var json = JsonSerializer.Serialize(msg);
        var deserialized = JsonSerializer.Deserialize<DiscoveryMessage>(json);

        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.ComputerName, Is.EqualTo("Alice-PC"));
        Assert.That(deserialized.SessionId, Is.EqualTo(msg.SessionId));
        Assert.That(deserialized.TcpPort, Is.EqualTo(55000));
        Assert.That(deserialized.SequenceNumber, Is.EqualTo(123));
    }
}
