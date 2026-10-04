using SimpleZipDrive.Core.Models;

namespace SimpleZipDrive.Tests;

/// <summary>
///     Tests for the <see cref="ScreenshotResult" /> record.
/// </summary>
public class ScreenshotResultTests
{
    [Fact]
    public void Success_WithPath_HasNoError()
    {
        var result = new ScreenshotResult(true, @"C:\shots\Screenshot_1.png", null);

        Assert.True(result.Success);
        Assert.Equal(@"C:\shots\Screenshot_1.png", result.FilePath);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void Failure_WithError_HasNoPath()
    {
        var result = new ScreenshotResult(false, null, "write permission issues");

        Assert.False(result.Success);
        Assert.Null(result.FilePath);
        Assert.Equal("write permission issues", result.ErrorMessage);
    }

    [Fact]
    public void ValueEquality_SameValues_AreEqual()
    {
        var first = new ScreenshotResult(true, "path.png", null);
        var second = new ScreenshotResult(true, "path.png", null);

        Assert.Equal(first, second);
    }

    [Fact]
    public void ValueEquality_DifferentValues_AreNotEqual()
    {
        var first = new ScreenshotResult(true, "a.png", null);
        var second = new ScreenshotResult(true, "b.png", null);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void With_CreatesModifiedCopy()
    {
        var original = new ScreenshotResult(false, null, "failed");

        var modified = new ScreenshotResult(Success: true, FilePath: "saved.png", ErrorMessage: null);

        Assert.False(original.Success);
        Assert.True(modified.Success);
        Assert.Equal("saved.png", modified.FilePath);
        Assert.Null(modified.ErrorMessage);
    }
}
