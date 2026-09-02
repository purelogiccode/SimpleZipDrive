using SimpleZipDrive.Core;

namespace SimpleZipDrive.Tests;

/// <summary>
///     Tests for the shared <see cref="ArchiveFormats" /> helper used by both the Dokan
///     and WinFsp mount services.
/// </summary>
public class ArchiveFormatsTests
{
    [Theory]
    [InlineData("comic.cbz", "zip")]
    [InlineData("comic.CBZ", "zip")]
    [InlineData("comic.cbr", "rar")]
    [InlineData("comic.cb7", "7z")]
    [InlineData("book.zip", "zip")]
    [InlineData("book.7z", "7z")]
    [InlineData("book.rar", "rar")]
    [InlineData("backup.tar.gz", "tar")]
    [InlineData("backup.tgz", "tar")]
    public void GetArchiveType_ComicAndKnownExtensions_MapsCorrectly(string filePath, string expected)
    {
        Assert.Equal(expected, ArchiveFormats.GetArchiveType(filePath));
    }

    [Theory]
    [InlineData("comic.cbz")]
    [InlineData("comic.CBZ")]
    [InlineData("comic.cbr")]
    [InlineData("comic.cb7")]
    [InlineData("book.zip")]
    [InlineData("book.tar.xz")]
    public void IsSupportedArchive_ComicAndKnownExtensions_ReturnsTrue(string filePath)
    {
        Assert.True(ArchiveFormats.IsSupportedArchive(filePath));
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("image.png")]
    [InlineData("document.pdf")]
    [InlineData("noextension")]
    public void IsSupportedArchive_UnsupportedExtensions_ReturnsFalse(string filePath)
    {
        Assert.False(ArchiveFormats.IsSupportedArchive(filePath));
    }

    [Fact]
    public void DialogFilter_IncludesComicExtensions()
    {
        Assert.Contains("*.cbz", ArchiveFormats.DialogFilter, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("*.cbr", ArchiveFormats.DialogFilter, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("*.cb7", ArchiveFormats.DialogFilter, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SupportedExtensionsDescription_IncludesComicExtensions()
    {
        Assert.Contains(".cbz", ArchiveFormats.SupportedExtensionsDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".cbr", ArchiveFormats.SupportedExtensionsDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".cb7", ArchiveFormats.SupportedExtensionsDescription, StringComparison.OrdinalIgnoreCase);
    }
}