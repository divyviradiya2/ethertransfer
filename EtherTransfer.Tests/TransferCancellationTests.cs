using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using EtherTransfer.Core.Models;
using EtherTransfer.Transfer;
using NUnit.Framework;

namespace EtherTransfer.Tests;

[TestFixture]
public class TransferCancellationTests
{
    private string _tempSourceDir = "";
    private string _tempDestDir = "";

    [SetUp]
    public void SetUp()
    {
        _tempSourceDir = Path.Combine(Path.GetTempPath(), "EtherTransfer_Src_" + Guid.NewGuid());
        _tempDestDir = Path.Combine(Path.GetTempPath(), "EtherTransfer_Dst_" + Guid.NewGuid());

        Directory.CreateDirectory(_tempSourceDir);
        Directory.CreateDirectory(_tempDestDir);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_tempSourceDir)) Directory.Delete(_tempSourceDir, true); } catch { }
        try { if (Directory.Exists(_tempDestDir)) Directory.Delete(_tempDestDir, true); } catch { }
    }

    private static (TcpListener listener, int port) StartTestListener()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return (listener, port);
    }

    [Test]
    public async Task SingleFile_CancelledMidStream_DeletesPartialFileOnReceiver()
    {
        // Arrange
        var testFilePath = Path.Combine(_tempSourceDir, "largefile.dat");
        byte[] testData = new byte[10 * 1024 * 1024]; // 10 MB
        new Random(42).NextBytes(testData);
        await File.WriteAllBytesAsync(testFilePath, testData);

        var (listener, port) = StartTestListener();

        var receiver = new TransferReceiver();
        using var cancelCts = new CancellationTokenSource();

        receiver.OnIncomingTransfer = (req, ct) =>
        {
            return Task.FromResult((true, _tempDestDir, cancelCts.Token));
        };

        var receiverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            return await receiver.HandleClientAsync(client, CancellationToken.None);
        });

        var sender = new TransferSender();
        var session = new TransferSession
        {
            PayloadFileCount = 1
        };
        session.AddFiles(new List<FileSelectionItem>
        {
            new() { AbsolutePath = testFilePath, RelativePath = "largefile.dat", RootName = "largefile.dat", Size = testData.Length }
        });

        // Act: Cancel as soon as first progress report arrives
        sender.ProgressUpdated += (_, e) =>
        {
            if (e.BytesSent > 1024 * 1024) // After 1MB
            {
                cancelCts.Cancel();
            }
        };

        try
        {
            await sender.TransmitSessionAsync("127.0.0.1", port, "TestSender", session, CancellationToken.None);
        }
        catch { }

        var result = await receiverTask;
        listener.Stop();

        // Assert
        Assert.That(result.Success, Is.False, "Receiver result should be failure upon cancellation.");
        var receivedFilePath = Path.Combine(_tempDestDir, "largefile.dat");
        Assert.That(File.Exists(receivedFilePath), Is.False, "Partial in-flight file MUST be deleted from receiver disk!");
    }

    [Test]
    public async Task SingleFolder_CancelledMidStream_RollsBackAllFilesForSingleFolder()
    {
        // Arrange
        var testSubDir = Path.Combine(_tempSourceDir, "MyFolder");
        Directory.CreateDirectory(testSubDir);

        var file1Path = Path.Combine(testSubDir, "file1.dat");
        var file2Path = Path.Combine(testSubDir, "file2.dat");

        byte[] smallData = new byte[1024];
        byte[] largeData = new byte[10 * 1024 * 1024]; // 10 MB
        await File.WriteAllBytesAsync(file1Path, smallData);
        await File.WriteAllBytesAsync(file2Path, largeData);

        var (listener, port) = StartTestListener();

        var receiver = new TransferReceiver();
        using var cancelCts = new CancellationTokenSource();

        receiver.OnIncomingTransfer = (req, ct) =>
        {
            return Task.FromResult((true, _tempDestDir, cancelCts.Token));
        };

        var receiverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            return await receiver.HandleClientAsync(client, CancellationToken.None);
        });

        var sender = new TransferSender();
        var session = new TransferSession
        {
            ContainsFolders = true,
            PayloadFolderCount = 1
        };
        session.AddFiles(new List<FileSelectionItem>
        {
            new() { AbsolutePath = file1Path, RelativePath = "MyFolder/file1.dat", RootName = "MyFolder", Size = smallData.Length },
            new() { AbsolutePath = file2Path, RelativePath = "MyFolder/file2.dat", RootName = "MyFolder", Size = largeData.Length }
        });

        // Cancel during file2
        sender.ProgressUpdated += (_, e) =>
        {
            if (e.BytesSent > smallData.Length + 1024 * 1024)
            {
                cancelCts.Cancel();
            }
        };

        try
        {
            await sender.TransmitSessionAsync("127.0.0.1", port, "TestSender", session, CancellationToken.None);
        }
        catch { }

        var result = await receiverTask;
        listener.Stop();

        // Assert
        Assert.That(result.Success, Is.False, "Transfer of single folder should be marked failed.");
        var receivedFile1 = Path.Combine(_tempDestDir, "MyFolder", "file1.dat");
        var receivedFile2 = Path.Combine(_tempDestDir, "MyFolder", "file2.dat");
        
        Assert.That(File.Exists(receivedFile2), Is.False, "Partial in-flight file2 must be deleted.");
        Assert.That(File.Exists(receivedFile1), Is.False, "Single folder cancellation should roll back session files.");
    }

    [Test]
    public async Task MultiItemTransfer_CancelledDuringSecondItem_PreservesFirstItem()
    {
        // Arrange
        var file1Path = Path.Combine(_tempSourceDir, "item1.dat");
        var file2Path = Path.Combine(_tempSourceDir, "item2.dat");

        byte[] smallData = new byte[1024];
        byte[] largeData = new byte[10 * 1024 * 1024]; // 10 MB
        await File.WriteAllBytesAsync(file1Path, smallData);
        await File.WriteAllBytesAsync(file2Path, largeData);

        var (listener, port) = StartTestListener();

        var receiver = new TransferReceiver();
        using var cancelCts = new CancellationTokenSource();

        receiver.OnIncomingTransfer = (req, ct) =>
        {
            return Task.FromResult((true, _tempDestDir, cancelCts.Token));
        };

        var receiverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            return await receiver.HandleClientAsync(client, CancellationToken.None);
        });

        var sender = new TransferSender();
        var session = new TransferSession
        {
            PayloadFileCount = 2
        };
        session.AddFiles(new List<FileSelectionItem>
        {
            new() { AbsolutePath = file1Path, RelativePath = "item1.dat", RootName = "item1.dat", Size = smallData.Length },
            new() { AbsolutePath = file2Path, RelativePath = "item2.dat", RootName = "item2.dat", Size = largeData.Length }
        });

        // Cancel during item2
        sender.ProgressUpdated += (_, e) =>
        {
            if (e.BytesSent > smallData.Length + 1024 * 1024)
            {
                cancelCts.Cancel();
            }
        };

        try
        {
            await sender.TransmitSessionAsync("127.0.0.1", port, "TestSender", session, CancellationToken.None);
        }
        catch { }

        var result = await receiverTask;
        listener.Stop();

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.CompletedElementsCount, Is.EqualTo(1));
        Assert.That(result.CompletedElementNames, Contains.Item("item1.dat"));

        var receivedItem1 = Path.Combine(_tempDestDir, "item1.dat");
        var receivedItem2 = Path.Combine(_tempDestDir, "item2.dat");

        Assert.That(File.Exists(receivedItem1), Is.True, "Completed item 1 should be preserved in multi-item transfer.");
        Assert.That(File.Exists(receivedItem2), Is.False, "Cancelled item 2 partial file must be deleted.");
    }

    [Test]
    public async Task MultiFolder_CancelledDuringSecondFolder_PreservesFirstFolderAndRollsBackSecondFolder()
    {
        // Arrange
        var folder1 = Path.Combine(_tempSourceDir, "Folder1");
        var folder2 = Path.Combine(_tempSourceDir, "Folder2");
        Directory.CreateDirectory(folder1);
        Directory.CreateDirectory(folder2);

        var f1_file1 = Path.Combine(folder1, "f1.dat");
        var f2_file1 = Path.Combine(folder2, "f2_1.dat");
        var f2_file2 = Path.Combine(folder2, "f2_2.dat");

        byte[] smallData = new byte[1024];
        byte[] largeData = new byte[10 * 1024 * 1024]; // 10 MB

        await File.WriteAllBytesAsync(f1_file1, smallData);
        await File.WriteAllBytesAsync(f2_file1, smallData);
        await File.WriteAllBytesAsync(f2_file2, largeData);

        var (listener, port) = StartTestListener();

        var receiver = new TransferReceiver();
        using var cancelCts = new CancellationTokenSource();

        receiver.OnIncomingTransfer = (req, ct) =>
        {
            return Task.FromResult((true, _tempDestDir, cancelCts.Token));
        };

        var receiverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            return await receiver.HandleClientAsync(client, CancellationToken.None);
        });

        var sender = new TransferSender();
        var session = new TransferSession
        {
            ContainsFolders = true,
            PayloadFolderCount = 2
        };
        session.AddFiles(new List<FileSelectionItem>
        {
            new() { AbsolutePath = f1_file1, RelativePath = "Folder1/f1.dat", RootName = "Folder1", Size = smallData.Length },
            new() { AbsolutePath = f2_file1, RelativePath = "Folder2/f2_1.dat", RootName = "Folder2", Size = smallData.Length },
            new() { AbsolutePath = f2_file2, RelativePath = "Folder2/f2_2.dat", RootName = "Folder2", Size = largeData.Length }
        });

        // Cancel during Folder2/f2_2.dat
        sender.ProgressUpdated += (_, e) =>
        {
            if (e.BytesSent > (smallData.Length * 2) + 1024 * 1024)
            {
                cancelCts.Cancel();
            }
        };

        try
        {
            await sender.TransmitSessionAsync("127.0.0.1", port, "TestSender", session, CancellationToken.None);
        }
        catch { }

        var result = await receiverTask;
        listener.Stop();

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.CompletedElementsCount, Is.EqualTo(1));
        Assert.That(result.CompletedElementNames, Contains.Item("Folder1"));

        var receivedF1 = Path.Combine(_tempDestDir, "Folder1", "f1.dat");
        var receivedF2_1 = Path.Combine(_tempDestDir, "Folder2", "f2_1.dat");
        var receivedF2_2 = Path.Combine(_tempDestDir, "Folder2", "f2_2.dat");

        Assert.That(File.Exists(receivedF1), Is.True, "Completed Folder1 must be preserved on receiver.");
        Assert.That(File.Exists(receivedF2_1), Is.False, "Incomplete Folder2's earlier files must be rolled back.");
        Assert.That(File.Exists(receivedF2_2), Is.False, "Incomplete Folder2's in-flight partial file must be deleted.");
    }

    [Test]
    public async Task SenderCancels_DuringSingleFileTransfer_ReceiverFailsAndDeletesPartialFile()
    {
        // Arrange
        var testFilePath = Path.Combine(_tempSourceDir, "largefile.dat");
        byte[] testData = new byte[10 * 1024 * 1024]; // 10 MB
        new Random(42).NextBytes(testData);
        await File.WriteAllBytesAsync(testFilePath, testData);

        var (listener, port) = StartTestListener();

        var receiver = new TransferReceiver();
        receiver.DebugLog += (_, msg) => Console.WriteLine($"[RECEIVER LOG] {msg.EventId}: {msg.Message}");
        // Receiver does NOT cancel. Receiver is waiting normally.
        receiver.OnIncomingTransfer = (req, ct) =>
        {
            return Task.FromResult((true, _tempDestDir, CancellationToken.None));
        };

        var receiverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            return await receiver.HandleClientAsync(client, CancellationToken.None);
        });

        var sender = new TransferSender();
        using var senderCts = new CancellationTokenSource();
        var session = new TransferSession
        {
            PayloadFileCount = 1
        };
        session.AddFiles(new List<FileSelectionItem>
        {
            new() { AbsolutePath = testFilePath, RelativePath = "largefile.dat", RootName = "largefile.dat", Size = testData.Length }
        });

        // Sender cancels after 1MB
        sender.ProgressUpdated += (_, e) =>
        {
            if (e.BytesSent > 1024 * 1024)
            {
                senderCts.Cancel();
            }
        };

        var senderResult = await sender.TransmitSessionAsync("127.0.0.1", port, "TestSender", session, senderCts.Token);
        var receiverResult = await receiverTask;
        listener.Stop();

        // Assert
        Assert.That(senderResult.Success, Is.False, "Sender result should be false upon sender cancellation.");
        Assert.That(receiverResult.Success, Is.False, "Receiver MUST NOT be marked successful when sender cancels!");
        
        var receivedFilePath = Path.Combine(_tempDestDir, "largefile.dat");
        Assert.That(File.Exists(receivedFilePath), Is.False, "Receiver must delete partial file when sender cancels!");
    }

    [Test]
    public async Task SenderCancels_DuringFolderTransfer_ReceiverFailsAndRollsBack()
    {
        // Arrange
        var folder = Path.Combine(_tempSourceDir, "MyFolder");
        Directory.CreateDirectory(folder);

        var f1 = Path.Combine(folder, "f1.dat");
        var f2 = Path.Combine(folder, "f2.dat");

        byte[] smallData = new byte[1024];
        byte[] largeData = new byte[10 * 1024 * 1024]; // 10 MB

        await File.WriteAllBytesAsync(f1, smallData);
        await File.WriteAllBytesAsync(f2, largeData);

        var (listener, port) = StartTestListener();

        var receiver = new TransferReceiver();
        receiver.OnIncomingTransfer = (req, ct) =>
        {
            return Task.FromResult((true, _tempDestDir, CancellationToken.None));
        };

        var receiverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            return await receiver.HandleClientAsync(client, CancellationToken.None);
        });

        var sender = new TransferSender();
        using var senderCts = new CancellationTokenSource();
        var session = new TransferSession
        {
            ContainsFolders = true,
            PayloadFolderCount = 1
        };
        session.AddFiles(new List<FileSelectionItem>
        {
            new() { AbsolutePath = f1, RelativePath = "MyFolder/f1.dat", RootName = "MyFolder", Size = smallData.Length },
            new() { AbsolutePath = f2, RelativePath = "MyFolder/f2.dat", RootName = "MyFolder", Size = largeData.Length }
        });

        // Sender cancels during f2
        sender.ProgressUpdated += (_, e) =>
        {
            if (e.BytesSent > smallData.Length + 1024 * 1024)
            {
                senderCts.Cancel();
            }
        };

        var senderResult = await sender.TransmitSessionAsync("127.0.0.1", port, "TestSender", session, senderCts.Token);
        var receiverResult = await receiverTask;
        listener.Stop();

        // Assert
        Assert.That(senderResult.Success, Is.False);
        Assert.That(receiverResult.Success, Is.False, "Receiver must not be marked successful when sender cancels!");

        var receivedF1 = Path.Combine(_tempDestDir, "MyFolder", "f1.dat");
        var receivedF2 = Path.Combine(_tempDestDir, "MyFolder", "f2.dat");

        Assert.That(File.Exists(receivedF2), Is.False, "Partial f2 must be deleted.");
        Assert.That(File.Exists(receivedF1), Is.False, "Single folder transfer must roll back f1 when sender cancels.");
    }

    [Test]
    public async Task SenderCancels_DuringMultiFileTransfer_ReceiverPopulatesCompletedElementsAndRollsBackIncomplete()
    {
        // Arrange: 2 separate root files
        var file1 = Path.Combine(_tempSourceDir, "file1.dat");
        var file2 = Path.Combine(_tempSourceDir, "file2.dat");

        byte[] file1Data = new byte[1024]; // 1 KB (finishes quickly)
        byte[] file2Data = new byte[10 * 1024 * 1024]; // 10 MB (cancelled mid-stream)

        await File.WriteAllBytesAsync(file1, file1Data);
        await File.WriteAllBytesAsync(file2, file2Data);

        var (listener, port) = StartTestListener();

        var receiver = new TransferReceiver();
        receiver.OnIncomingTransfer = (req, ct) =>
        {
            return Task.FromResult((true, _tempDestDir, CancellationToken.None));
        };

        var receiverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            return await receiver.HandleClientAsync(client, CancellationToken.None);
        });

        var sender = new TransferSender();
        using var senderCts = new CancellationTokenSource();
        var session = new TransferSession
        {
            PayloadFileCount = 2
        };
        session.AddFiles(new List<FileSelectionItem>
        {
            new() { AbsolutePath = file1, RelativePath = "file1.dat", RootName = "file1.dat", Size = file1Data.Length },
            new() { AbsolutePath = file2, RelativePath = "file2.dat", RootName = "file2.dat", Size = file2Data.Length }
        });

        // Sender cancels during file2.dat
        sender.ProgressUpdated += (_, e) =>
        {
            if (e.BytesSent > file1Data.Length + 1024 * 1024)
            {
                senderCts.Cancel();
            }
        };

        var senderResult = await sender.TransmitSessionAsync("127.0.0.1", port, "TestSender", session, senderCts.Token);
        var receiverResult = await receiverTask;
        listener.Stop();

        // Assert
        Assert.That(senderResult.Success, Is.False);
        Assert.That(senderResult.CompletedElementsCount, Is.EqualTo(1));
        Assert.That(senderResult.CompletedElementNames, Contains.Item("file1.dat"));

        Assert.That(receiverResult.Success, Is.False);
        Assert.That(receiverResult.TotalElements, Is.EqualTo(2));
        Assert.That(receiverResult.CompletedElementsCount, Is.EqualTo(1), "Receiver must have 1 completed element recorded!");
        Assert.That(receiverResult.CompletedElementNames, Contains.Item("file1.dat"), "Receiver completed elements must contain file1.dat!");

        var receivedFile1 = Path.Combine(_tempDestDir, "file1.dat");
        var receivedFile2 = Path.Combine(_tempDestDir, "file2.dat");

        Assert.That(File.Exists(receivedFile1), Is.True, "Completed file1.dat must be preserved on receiver disk!");
        Assert.That(File.Exists(receivedFile2), Is.False, "Cancelled file2.dat must be deleted from receiver disk!");
    }
}
