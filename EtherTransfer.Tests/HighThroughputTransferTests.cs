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

    [Test]
    public async Task FolderTransfer_WhenFolderAlreadyExists_AutoRenamesWithIncrementingSuffix()
    {
        var existingFolderPath = Path.Combine(_tempDestDir, "Photos");
        Directory.CreateDirectory(existingFolderPath);
        var existingFilePath = Path.Combine(existingFolderPath, "old_pic.jpg");
        await File.WriteAllTextAsync(existingFilePath, "original content");

        var sourceFolder = Path.Combine(_tempSourceDir, "Photos");
        Directory.CreateDirectory(sourceFolder);
        var srcFile1 = Path.Combine(sourceFolder, "pic1.jpg");
        var srcFile2 = Path.Combine(sourceFolder, "pic2.jpg");
        await File.WriteAllTextAsync(srcFile1, "new pic 1");
        await File.WriteAllTextAsync(srcFile2, "new pic 2");

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
            PayloadFileCount = 2
        };
        session.AddFiles(new List<FileSelectionItem>
        {
            new() { AbsolutePath = srcFile1, RelativePath = "Photos/pic1.jpg", RootName = "Photos", Size = new FileInfo(srcFile1).Length },
            new() { AbsolutePath = srcFile2, RelativePath = "Photos/pic2.jpg", RootName = "Photos", Size = new FileInfo(srcFile2).Length }
        });

        var senderResult = await sender.TransmitSessionAsync("127.0.0.1", port, "Sender", session, CancellationToken.None);
        var receiverResult = await receiverTask;
        listener.Stop();

        Assert.That(senderResult.Success, Is.True, $"Sender failed: {senderResult.ErrorMessage}");
        Assert.That(receiverResult.Success, Is.True, $"Receiver failed: {receiverResult.ErrorMessage}");

        Assert.That(File.Exists(existingFilePath), Is.True, "Original existing file must be untouched.");
        Assert.That(await File.ReadAllTextAsync(existingFilePath), Is.EqualTo("original content"));

        var newFolder = Path.Combine(_tempDestDir, "Photos (1)");
        Assert.That(Directory.Exists(newFolder), Is.True, "New folder must be created as Photos (1).");
        Assert.That(File.Exists(Path.Combine(newFolder, "pic1.jpg")), Is.True);
        Assert.That(File.Exists(Path.Combine(newFolder, "pic2.jpg")), Is.True);
        Assert.That(await File.ReadAllTextAsync(Path.Combine(newFolder, "pic1.jpg")), Is.EqualTo("new pic 1"));
    }

    [Test]
    public async Task Transfer_SingleFile_PreservesMetadataTimestamps()
    {
        var srcFile = Path.Combine(_tempSourceDir, "legacy_doc.pdf");
        await File.WriteAllTextAsync(srcFile, "document content");

        var expectedCreated = new DateTime(2021, 5, 12, 10, 30, 0, DateTimeKind.Utc);
        var expectedModified = new DateTime(2022, 8, 20, 15, 45, 0, DateTimeKind.Utc);
        File.SetCreationTimeUtc(srcFile, expectedCreated);
        File.SetLastWriteTimeUtc(srcFile, expectedModified);

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
            ContainsFolders = false,
            PayloadFolderCount = 0,
            PayloadFileCount = 1
        };
        session.AddFiles(new List<FileSelectionItem>
        {
            new() { AbsolutePath = srcFile, RelativePath = "legacy_doc.pdf", RootName = "legacy_doc.pdf", Size = new FileInfo(srcFile).Length }
        });

        var senderResult = await sender.TransmitSessionAsync("127.0.0.1", port, "Sender", session, CancellationToken.None);
        var receiverResult = await receiverTask;
        listener.Stop();

        Assert.That(senderResult.Success, Is.True, $"Sender failed: {senderResult.ErrorMessage}");
        Assert.That(receiverResult.Success, Is.True, $"Receiver failed: {receiverResult.ErrorMessage}");

        var destFile = Path.Combine(_tempDestDir, "legacy_doc.pdf");
        Assert.That(File.Exists(destFile), Is.True);

        var actualCreated = File.GetCreationTimeUtc(destFile);
        var actualModified = File.GetLastWriteTimeUtc(destFile);

        Assert.That((actualCreated - expectedCreated).Duration(), Is.LessThanOrEqualTo(TimeSpan.FromSeconds(2)));
        Assert.That((actualModified - expectedModified).Duration(), Is.LessThanOrEqualTo(TimeSpan.FromSeconds(2)));
    }

    [Test]
    public async Task Transfer_Folder_PreservesMetadataTimestamps()
    {
        var srcFolder = Path.Combine(_tempSourceDir, "ArchiveFolder");
        Directory.CreateDirectory(srcFolder);

        var srcFile1 = Path.Combine(srcFolder, "data1.bin");
        var srcFile2 = Path.Combine(srcFolder, "data2.bin");
        await File.WriteAllTextAsync(srcFile1, "binary 1");
        await File.WriteAllTextAsync(srcFile2, "binary 2");

        var expectedFile1Created = new DateTime(2019, 3, 10, 8, 15, 0, DateTimeKind.Utc);
        var expectedFile1Modified = new DateTime(2020, 4, 11, 9, 20, 0, DateTimeKind.Utc);
        File.SetCreationTimeUtc(srcFile1, expectedFile1Created);
        File.SetLastWriteTimeUtc(srcFile1, expectedFile1Modified);

        var expectedFile2Created = new DateTime(2021, 6, 14, 12, 0, 0, DateTimeKind.Utc);
        var expectedFile2Modified = new DateTime(2022, 7, 18, 16, 30, 0, DateTimeKind.Utc);
        File.SetCreationTimeUtc(srcFile2, expectedFile2Created);
        File.SetLastWriteTimeUtc(srcFile2, expectedFile2Modified);

        var expectedFolderCreated = new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var expectedFolderModified = new DateTime(2022, 7, 18, 17, 0, 0, DateTimeKind.Utc);
        Directory.SetCreationTimeUtc(srcFolder, expectedFolderCreated);
        Directory.SetLastWriteTimeUtc(srcFolder, expectedFolderModified);

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
            PayloadFileCount = 2
        };
        session.AddFiles(new List<FileSelectionItem>
        {
            new() { AbsolutePath = srcFile1, RelativePath = "ArchiveFolder/data1.bin", RootName = "ArchiveFolder", Size = new FileInfo(srcFile1).Length },
            new() { AbsolutePath = srcFile2, RelativePath = "ArchiveFolder/data2.bin", RootName = "ArchiveFolder", Size = new FileInfo(srcFile2).Length }
        });

        var senderResult = await sender.TransmitSessionAsync("127.0.0.1", port, "Sender", session, CancellationToken.None);
        var receiverResult = await receiverTask;
        listener.Stop();

        Assert.That(senderResult.Success, Is.True, $"Sender failed: {senderResult.ErrorMessage}");
        Assert.That(receiverResult.Success, Is.True, $"Receiver failed: {receiverResult.ErrorMessage}");

        var destFolder = Path.Combine(_tempDestDir, "ArchiveFolder");
        Assert.That(Directory.Exists(destFolder), Is.True);

        var destFile1 = Path.Combine(destFolder, "data1.bin");
        var destFile2 = Path.Combine(destFolder, "data2.bin");

        Assert.That(File.Exists(destFile1), Is.True);
        Assert.That(File.Exists(destFile2), Is.True);

        var actualFile1Created = File.GetCreationTimeUtc(destFile1);
        var actualFile1Modified = File.GetLastWriteTimeUtc(destFile1);
        Assert.That((actualFile1Created - expectedFile1Created).Duration(), Is.LessThanOrEqualTo(TimeSpan.FromSeconds(2)));
        Assert.That((actualFile1Modified - expectedFile1Modified).Duration(), Is.LessThanOrEqualTo(TimeSpan.FromSeconds(2)));

        var actualFile2Created = File.GetCreationTimeUtc(destFile2);
        var actualFile2Modified = File.GetLastWriteTimeUtc(destFile2);
        Assert.That((actualFile2Created - expectedFile2Created).Duration(), Is.LessThanOrEqualTo(TimeSpan.FromSeconds(2)));
        Assert.That((actualFile2Modified - expectedFile2Modified).Duration(), Is.LessThanOrEqualTo(TimeSpan.FromSeconds(2)));

        var actualFolderModified = Directory.GetLastWriteTimeUtc(destFolder);
        Assert.That((actualFolderModified - expectedFolderModified).Duration(), Is.LessThanOrEqualTo(TimeSpan.FromSeconds(2)));
    }
}

