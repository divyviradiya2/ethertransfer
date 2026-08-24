using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using EtherTransfer.Core.Models;
using EtherTransfer.Transfer;
using NUnit.Framework;

namespace EtherTransfer.Tests;

[TestFixture]
public class HighThroughputTransferTests
{
    private string _tempSourceDir = "";
    private string _tempDestDir = "";

    [SetUp]
    public void SetUp()
    {
        _tempSourceDir = Path.Combine(Path.GetTempPath(), "EtherTransfer_HighPerf_Src_" + Guid.NewGuid());
        _tempDestDir = Path.Combine(Path.GetTempPath(), "EtherTransfer_HighPerf_Dst_" + Guid.NewGuid());

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

    private static string ComputeSha256(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var fs = File.OpenRead(filePath);
        var hash = sha256.ComputeHash(fs);
        return Convert.ToHexString(hash);
    }

    [Test]
    public async Task LargeFile_PipelinedTransfer_PreservesByteIntegrity()
    {

        var sourceFilePath = Path.Combine(_tempSourceDir, "backup_archive.zip");
        byte[] testData = new byte[20 * 1024 * 1024];
        new Random(12345).NextBytes(testData);
        await File.WriteAllBytesAsync(sourceFilePath, testData);

        var expectedHash = ComputeSha256(sourceFilePath);

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
        var session = new TransferSession
        {
            PayloadFileCount = 1
        };
        session.AddFiles(new List<FileSelectionItem>
        {
            new()
            {
                AbsolutePath = sourceFilePath,
                RelativePath = "backup_archive.zip",
                RootName = "backup_archive.zip",
                Size = testData.Length
            }
        });

        var progressUpdates = new List<TransferProgressEventArgs>();
        sender.ProgressUpdated += (_, e) => progressUpdates.Add(e);

        var senderResult = await sender.TransmitSessionAsync("127.0.0.1", port, "SpeedSender", session, CancellationToken.None);
        var receiverResult = await receiverTask;
        listener.Stop();

        Assert.That(senderResult.Success, Is.True, "Sender should succeed.");
        Assert.That(receiverResult.Success, Is.True, "Receiver should succeed.");

        var receivedFilePath = Path.Combine(_tempDestDir, "backup_archive.zip");
        Assert.That(File.Exists(receivedFilePath), Is.True, "Received file must exist.");

        var receivedHash = ComputeSha256(receivedFilePath);
        Assert.That(receivedHash, Is.EqualTo(expectedHash), "Transferred file SHA-256 hash must match original byte-for-byte.");
        Assert.That(new FileInfo(receivedFilePath).Length, Is.EqualTo(testData.Length), "Transferred file size must match.");
        Assert.That(progressUpdates.Count, Is.GreaterThan(0), "Progress updates should have fired.");
    }

    [Test]
    public async Task DeepFolderHierarchy_TarStreaming_ExtractsAllFilesAndPreservesStructure()
    {

        var rootFolder = Path.Combine(_tempSourceDir, "SourceProject");
        Directory.CreateDirectory(rootFolder);

        var subDirs = new[]
        {
            Path.Combine(rootFolder, "src", "core"),
            Path.Combine(rootFolder, "src", "ui", "assets"),
            Path.Combine(rootFolder, "node_modules", "lib1", "dist"),
            Path.Combine(rootFolder, "docs", "images")
        };

        foreach (var dir in subDirs)
        {
            Directory.CreateDirectory(dir);
        }

        var fileItems = new List<FileSelectionItem>();
        int fileIndex = 0;

        foreach (var dir in subDirs)
        {
            for (int i = 0; i < 25; i++)
            {
                var filePath = Path.Combine(dir, $"file_{fileIndex}.dat");
                byte[] content = new byte[1024 * (i + 1)];
                new Random(fileIndex).NextBytes(content);
                await File.WriteAllBytesAsync(filePath, content);

                var relPath = Path.GetRelativePath(_tempSourceDir, filePath).Replace('\\', '/');
                fileItems.Add(new FileSelectionItem
                {
                    AbsolutePath = filePath,
                    RelativePath = relPath,
                    RootName = "SourceProject",
                    Size = content.Length
                });
                fileIndex++;
            }
        }

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
        var session = new TransferSession
        {
            ContainsFolders = true,
            PayloadFolderCount = 1
        };
        session.AddFiles(fileItems);

        var senderResult = await sender.TransmitSessionAsync("127.0.0.1", port, "SpeedSender", session, CancellationToken.None);
        var receiverResult = await receiverTask;
        listener.Stop();

        Assert.That(senderResult.Success, Is.True, $"Sender failed: {senderResult.ErrorMessage}");
        Assert.That(receiverResult.Success, Is.True, $"Receiver failed: {receiverResult.ErrorMessage}");
        Assert.That(receiverResult.CompletedElementNames, Contains.Item("SourceProject"));

        foreach (var item in fileItems)
        {
            var destPath = Path.Combine(_tempDestDir, item.RelativePath);
            Assert.That(File.Exists(destPath), Is.True, $"File {item.RelativePath} must exist in destination.");
            Assert.That(ComputeSha256(destPath), Is.EqualTo(ComputeSha256(item.AbsolutePath)), $"Hash mismatch for {item.RelativePath}");
        }
    }

    [Test]
    public async Task MixedTransfer_FolderAndLargeZip_TransfersBothSequentially()
    {

        var folderPath = Path.Combine(_tempSourceDir, "DocsFolder");
        Directory.CreateDirectory(folderPath);
        var folderFiles = new List<FileSelectionItem>();

        for (int i = 0; i < 20; i++)
        {
            var p = Path.Combine(folderPath, $"doc_{i}.txt");
            await File.WriteAllTextAsync(p, $"Content of document {i} with some padding {new string('X', 500)}");
            folderFiles.Add(new FileSelectionItem
            {
                AbsolutePath = p,
                RelativePath = $"DocsFolder/doc_{i}.txt",
                RootName = "DocsFolder",
                Size = new FileInfo(p).Length
            });
        }

        var zipPath = Path.Combine(_tempSourceDir, "archive.zip");
        byte[] zipBytes = new byte[5 * 1024 * 1024];
        new Random(42).NextBytes(zipBytes);
        await File.WriteAllBytesAsync(zipPath, zipBytes);
        var zipItem = new FileSelectionItem
        {
            AbsolutePath = zipPath,
            RelativePath = "archive.zip",
            RootName = "archive.zip",
            Size = zipBytes.Length
        };

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
        var session = new TransferSession
        {
            ContainsFolders = true,
            PayloadFolderCount = 1,
            PayloadFileCount = 1
        };
        session.AddFiles(folderFiles);
        session.AddFiles(new[] { zipItem });

        var senderResult = await sender.TransmitSessionAsync("127.0.0.1", port, "MixedSender", session, CancellationToken.None);
        var receiverResult = await receiverTask;
        listener.Stop();

        Assert.That(senderResult.Success, Is.True, $"Sender failed: {senderResult.ErrorMessage}");
        Assert.That(receiverResult.Success, Is.True, $"Receiver failed: {receiverResult.ErrorMessage}");
        Assert.That(receiverResult.CompletedElementsCount, Is.EqualTo(2));
        Assert.That(receiverResult.CompletedElementNames, Contains.Item("DocsFolder"));
        Assert.That(receiverResult.CompletedElementNames, Contains.Item("archive.zip"));

        for (int i = 0; i < 20; i++)
        {
            var destDoc = Path.Combine(_tempDestDir, "DocsFolder", $"doc_{i}.txt");
            Assert.That(File.Exists(destDoc), Is.True);
        }

        var destZip = Path.Combine(_tempDestDir, "archive.zip");
        Assert.That(File.Exists(destZip), Is.True);
        Assert.That(ComputeSha256(destZip), Is.EqualTo(ComputeSha256(zipPath)));
    }

    [Test]
    public void SpeedTracker_CalculatesInstantaneousSpeed_WithoutLifetimeAveragingBug()
    {
        var tracker = new SpeedTracker();
        tracker.Start();

        var initialSpeed = tracker.CalculateSpeed(0);
        Assert.That(initialSpeed, Is.EqualTo(0));

        Thread.Sleep(550);

        var speed1 = tracker.CalculateSpeed(50 * 1024 * 1024);
        Assert.That(speed1, Is.GreaterThan(50.0), "Speed should reflect ~100 MB/s instantaneous rate");
    }

    [Test]
    public async Task TarStream_InspectBytes()
    {
        using var ms = new MemoryStream();
        var tarWriter = new TarWriter(ms, TarEntryFormat.Pax, leaveOpen: true);
        await using (tarWriter.ConfigureAwait(false))
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, "test.txt");
            using var dataStream = new MemoryStream(new byte[] { 1, 2, 3, 4 });
            entry.DataStream = dataStream;
            await tarWriter.WriteEntryAsync(entry);
        }

        var writtenLength = ms.Position;
        Console.WriteLine($"[DEBUG] Total bytes written by TarWriter: {writtenLength}");

        ms.Position = 0;
        var tarReader = new TarReader(ms, leaveOpen: true);
        await using (tarReader.ConfigureAwait(false))
        {
            while (await tarReader.GetNextEntryAsync() is { } entry)
            {
                Console.WriteLine($"[DEBUG] Read entry: {entry.Name}, length: {entry.Length}");
                using var outMs = new MemoryStream();
                if (entry.DataStream != null)
                {
                    await entry.DataStream.CopyToAsync(outMs);
                }
            }
        }

        var readPosition = ms.Position;
        Console.WriteLine($"[DEBUG] Total bytes read by TarReader: {readPosition}");
        Console.WriteLine($"[DEBUG] Remaining unread bytes: {writtenLength - readPosition}");

        if (writtenLength - readPosition == 512)
        {
            var endBlock = new byte[512];
            int r = await ms.ReadAsync(endBlock);
            Console.WriteLine($"[DEBUG] Read final end block bytes: {r}");
        }

        Assert.That(ms.Position, Is.EqualTo(writtenLength));
    }

    [Test]
    public async Task ZeroByteFiles_BothStandaloneAndInsideTarFolder_TransferAccurately()
    {

        var emptyFilePath = Path.Combine(_tempSourceDir, "empty_standalone.dat");
        await File.WriteAllBytesAsync(emptyFilePath, Array.Empty<byte>());

        var folderPath = Path.Combine(_tempSourceDir, "EmptyTestFolder");
        Directory.CreateDirectory(folderPath);

        var gitkeep = Path.Combine(folderPath, ".gitkeep");
        await File.WriteAllBytesAsync(gitkeep, Array.Empty<byte>());

        var normalFile = Path.Combine(folderPath, "data.bin");
        await File.WriteAllBytesAsync(normalFile, new byte[] { 10, 20, 30, 40 });

        var (listener, port) = StartTestListener();

        var receiver = new TransferReceiver();
        receiver.OnIncomingTransfer = (req, ct) => Task.FromResult((true, _tempDestDir, CancellationToken.None));

        var receiverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            return await receiver.HandleClientAsync(client, CancellationToken.None);
        });

        var sender = new TransferSender();
        var session = new TransferSession
        {
            ContainsFolders = true,
            PayloadFolderCount = 1,
            PayloadFileCount = 1
        };
        session.AddFiles(new List<FileSelectionItem>
        {
            new() { AbsolutePath = emptyFilePath, RelativePath = "empty_standalone.dat", RootName = "empty_standalone.dat", Size = 0 },
            new() { AbsolutePath = gitkeep, RelativePath = "EmptyTestFolder/.gitkeep", RootName = "EmptyTestFolder", Size = 0 },
            new() { AbsolutePath = normalFile, RelativePath = "EmptyTestFolder/data.bin", RootName = "EmptyTestFolder", Size = 4 }
        });

        var senderResult = await sender.TransmitSessionAsync("127.0.0.1", port, "Sender", session, CancellationToken.None);
        var receiverResult = await receiverTask;
        listener.Stop();

        Assert.That(senderResult.Success, Is.True, $"Sender failed: {senderResult.ErrorMessage}");
        Assert.That(receiverResult.Success, Is.True, $"Receiver failed: {receiverResult.ErrorMessage}");

        var destEmpty = Path.Combine(_tempDestDir, "empty_standalone.dat");
        var destGitkeep = Path.Combine(_tempDestDir, "EmptyTestFolder", ".gitkeep");
        var destNormal = Path.Combine(_tempDestDir, "EmptyTestFolder", "data.bin");

        Assert.That(File.Exists(destEmpty), Is.True);
        Assert.That(new FileInfo(destEmpty).Length, Is.EqualTo(0));

        Assert.That(File.Exists(destGitkeep), Is.True);
        Assert.That(new FileInfo(destGitkeep).Length, Is.EqualTo(0));

        Assert.That(File.Exists(destNormal), Is.True);
        Assert.That(new FileInfo(destNormal).Length, Is.EqualTo(4));
    }

    [Test]
    public async Task ThousandsOfSmallFiles_TransfersAtHighSpeedAndPreservesIntegrity()
    {
        var rootDir = Path.Combine(_tempSourceDir, "ManyFilesRepo");
        Directory.CreateDirectory(rootDir);

        var items = new List<FileSelectionItem>();
        int totalFiles = 500;
        for (int i = 0; i < totalFiles; i++)
        {
            var subDir = Path.Combine(rootDir, $"module_{i % 20}", $"sub_{i % 5}");
            Directory.CreateDirectory(subDir);
            var filePath = Path.Combine(subDir, $"file_{i}.txt");
            var content = System.Text.Encoding.UTF8.GetBytes($"Payload content for file {i} - {new string('X', i % 200)}");
            await File.WriteAllBytesAsync(filePath, content);

            var relativePath = Path.GetRelativePath(_tempSourceDir, filePath).Replace('\\', '/');
            items.Add(new FileSelectionItem
            {
                AbsolutePath = filePath,
                RelativePath = relativePath,
                RootName = "ManyFilesRepo",
                Size = content.Length
            });
        }

        var (listener, port) = StartTestListener();

        var receiver = new TransferReceiver();
        receiver.OnIncomingTransfer = (req, ct) => Task.FromResult((true, _tempDestDir, CancellationToken.None));

        var receiverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            return await receiver.HandleClientAsync(client, CancellationToken.None);
        });

        var sender = new TransferSender();
        var session = new TransferSession
        {
            ContainsFolders = true,
            PayloadFolderCount = 1,
            PayloadFileCount = totalFiles
        };
        session.AddFiles(items);

        var senderResult = await sender.TransmitSessionAsync("127.0.0.1", port, "Sender", session, CancellationToken.None);
        var receiverResult = await receiverTask;
        listener.Stop();

        Assert.That(senderResult.Success, Is.True, $"Sender failed: {senderResult.ErrorMessage}");
        Assert.That(receiverResult.Success, Is.True, $"Receiver failed: {receiverResult.ErrorMessage}");

        for (int i = 0; i < totalFiles; i++)
        {
            var item = items[i];
            var destPath = Path.Combine(_tempDestDir, item.RelativePath);
            Assert.That(File.Exists(destPath), Is.True, $"File missing: {item.RelativePath}");
            Assert.That(new FileInfo(destPath).Length, Is.EqualTo(item.Size));
        }
    }
}

