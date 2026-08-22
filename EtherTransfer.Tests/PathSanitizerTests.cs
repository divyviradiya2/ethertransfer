using System;
using System.IO;
using EtherTransfer.Transfer;
using NUnit.Framework;

namespace EtherTransfer.Tests;

[TestFixture]
public class PathSanitizerTests
{
    private string _sandboxDir = "";

    [SetUp]
    public void SetUp()
    {
        _sandboxDir = Path.Combine(Path.GetTempPath(), "EtherTransfer_Sandbox_" + Guid.NewGuid());
        Directory.CreateDirectory(_sandboxDir);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_sandboxDir))
            {
                Directory.Delete(_sandboxDir, true);
            }
        }
        catch { }
    }

    [Test]
    public void SanitizeRelativePath_SimpleValidFile_ReturnsPathInsideSandbox()
    {
        var result = PathSanitizer.SanitizeRelativePath(_sandboxDir, "document.pdf");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.EqualTo(Path.Combine(_sandboxDir, "document.pdf")));
    }

    [Test]
    public void SanitizeRelativePath_NestedValidPath_ReturnsPathInsideSandbox()
    {
        var result = PathSanitizer.SanitizeRelativePath(_sandboxDir, "Projects/SubFolder/code.cs");

        Assert.That(result, Is.Not.Null);
        var expected = Path.Combine(_sandboxDir, "Projects", "SubFolder", "code.cs");
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase("../secret.txt")]
    [TestCase("../../Windows/System32/cmd.exe")]
    [TestCase("../../../etc/passwd")]
    [TestCase("foo/../../bar/../../secret")]
    public void SanitizeRelativePath_DirectoryTraversal_ReturnsNullOrContainedPath(string maliciousPath)
    {
        var result = PathSanitizer.SanitizeRelativePath(_sandboxDir, maliciousPath);

        if (result != null)
        {
            var normalizedSandbox = Path.GetFullPath(_sandboxDir);
            if (!normalizedSandbox.EndsWith(Path.DirectorySeparatorChar.ToString()))
                normalizedSandbox += Path.DirectorySeparatorChar;

            Assert.That(result.StartsWith(normalizedSandbox, StringComparison.OrdinalIgnoreCase), Is.True,
                $"Path {result} MUST NOT escape the sandbox directory {normalizedSandbox}");
        }
    }

    [Test]
    public void SanitizeRelativePath_NullBytes_AreStripped()
    {
        var result = PathSanitizer.SanitizeRelativePath(_sandboxDir, "test\0file.txt");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Contains('\0'), Is.False);
    }

    [Test]
    public void SanitizeRelativePath_WindowsReservedNames_ArePrefixedSafely()
    {
        var result = PathSanitizer.SanitizeRelativePath(_sandboxDir, "CON.txt");

        Assert.That(result, Is.Not.Null);
        var fileName = Path.GetFileName(result);
        Assert.That(fileName, Is.EqualTo("_CON.txt"));
    }

    [TestCase("COM1")]
    [TestCase("AUX")]
    [TestCase("NUL")]
    [TestCase("PRN")]
    [TestCase("LPT1")]
    public void SanitizeRelativePath_ReservedNamesWithoutExt_ArePrefixedSafely(string reserved)
    {
        var result = PathSanitizer.SanitizeRelativePath(_sandboxDir, reserved);

        Assert.That(result, Is.Not.Null);
        var fileName = Path.GetFileName(result);
        Assert.That(fileName, Is.EqualTo($"_{reserved}"));
    }

    [Test]
    public void SanitizeRelativePath_IllegalCharacters_AreRemoved()
    {
        var result = PathSanitizer.SanitizeRelativePath(_sandboxDir, "my<cool>:file*?.txt");

        Assert.That(result, Is.Not.Null);
        var fileName = Path.GetFileName(result);
        Assert.That(fileName, Is.EqualTo("mycoolfile.txt"));
    }

    [Test]
    public void SanitizeRelativePath_OverlyLongSegment_IsTruncatedTo255Chars()
    {
        var longName = new string('a', 300) + ".txt";
        var result = PathSanitizer.SanitizeRelativePath(_sandboxDir, longName);

        Assert.That(result, Is.Not.Null);
        var fileName = Path.GetFileName(result);
        Assert.That(fileName, Is.Not.Null);
        Assert.That(fileName!.Length, Is.LessThanOrEqualTo(255));
    }

    [TestCase(".gitignore", ".gitignore")]
    [TestCase(".env", ".env")]
    [TestCase(".dockerignore", ".dockerignore")]
    [TestCase("my_file.txt.", "my_file.txt")]
    [TestCase("my_file.txt   ", "my_file.txt")]
    public void SanitizeRelativePath_LeadingDotFilesAndTrailingDots_HandledCorrectly(string input, string expectedFileName)
    {
        var result = PathSanitizer.SanitizeRelativePath(_sandboxDir, input);

        Assert.That(result, Is.Not.Null);
        var fileName = Path.GetFileName(result);
        Assert.That(fileName, Is.EqualTo(expectedFileName));
    }

    [Test]
    public void SanitizeRelativePath_EmptyOrWhitespace_ReturnsNull()
    {
        Assert.That(PathSanitizer.SanitizeRelativePath(_sandboxDir, ""), Is.Null);
        Assert.That(PathSanitizer.SanitizeRelativePath(_sandboxDir, "   "), Is.Null);
        Assert.That(PathSanitizer.SanitizeRelativePath(_sandboxDir, "///"), Is.Null);
    }

    [Test]
    public void ResolveCollision_WhenNoFileExists_ReturnsOriginalPath()
    {
        var targetFile = Path.Combine(_sandboxDir, "unique_file.txt");

        var resolved = PathSanitizer.ResolveCollision(targetFile);

        Assert.That(resolved, Is.EqualTo(targetFile));
    }

    [Test]
    public void ResolveCollision_WhenFileExists_AppendsIncrementingNumber()
    {
        var targetFile = Path.Combine(_sandboxDir, "photo.jpg");
        File.WriteAllText(targetFile, "original");

        var resolved = PathSanitizer.ResolveCollision(targetFile);
        Assert.That(resolved, Is.EqualTo(Path.Combine(_sandboxDir, "photo (1).jpg")));

        File.WriteAllText(resolved, "first copy");
        var resolved2 = PathSanitizer.ResolveCollision(targetFile);
        Assert.That(resolved2, Is.EqualTo(Path.Combine(_sandboxDir, "photo (2).jpg")));
    }
}
