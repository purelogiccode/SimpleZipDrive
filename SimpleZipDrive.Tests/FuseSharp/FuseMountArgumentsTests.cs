using FuseSharp;

namespace SimpleZipDrive.Tests.FuseSharp;

/// <summary>
/// Tests the libfuse mount argument builder, including how the volume label is
/// exposed on each platform.
/// </summary>
public class FuseMountArgumentsTests
{
    /// <summary>
    /// Verifies Linux exposes the volume label through <c>fsname</c> because the
    /// <c>volname</c> option is macOS-only.
    /// </summary>
    [Fact]
    public void BuildMountArguments_OnLinux_UsesFsnameWithLabel()
    {
        var arguments = FuseFileSystem.BuildMountArguments(isMacOs: false, debug: false, "XBOX_ISO");

        Assert.Contains("fsname=XBOX_ISO", arguments, StringComparer.Ordinal);
        Assert.DoesNotContain(arguments, static a => a.StartsWith("volname=", StringComparison.Ordinal));
        Assert.Contains("ro", arguments, StringComparer.Ordinal);
    }

    /// <summary>
    /// Verifies macOS keeps exposing the label through <c>volname</c>.
    /// </summary>
    [Fact]
    public void BuildMountArguments_OnMacOs_UsesVolnameWithLabel()
    {
        var arguments = FuseFileSystem.BuildMountArguments(isMacOs: true, debug: false, "XBOX_ISO");

        Assert.Contains("volname=XBOX_ISO", arguments, StringComparer.Ordinal);
    }

    /// <summary>
    /// Verifies debug mode adds the libfuse debug flag.
    /// </summary>
    [Fact]
    public void BuildMountArguments_WithDebug_AddsDebugFlag()
    {
        var arguments = FuseFileSystem.BuildMountArguments(isMacOs: false, debug: true, "XBOX_ISO");

        Assert.Contains("-d", arguments, StringComparer.Ordinal);
    }

    /// <summary>
    /// Verifies an empty label falls back to the application name.
    /// </summary>
    [Fact]
    public void BuildMountArguments_WithEmptyLabel_FallsBackToApplicationName()
    {
        var arguments = FuseFileSystem.BuildMountArguments(isMacOs: false, debug: false, string.Empty);

        Assert.Contains("fsname=SimpleZipDrive", arguments, StringComparer.Ordinal);
    }

    /// <summary>
    /// Verifies option separators and control characters are stripped from the label.
    /// </summary>
    [Fact]
    public void BuildMountArguments_SanitizesLabel()
    {
        var arguments = FuseFileSystem.BuildMountArguments(isMacOs: false, debug: false, "bad,label/with\nchars");

        Assert.Contains("fsname=badlabelwithchars", arguments, StringComparer.Ordinal);
    }

    /// <summary>
    /// Verifies overly long labels are truncated before they reach the mount options.
    /// </summary>
    [Fact]
    public void BuildMountArguments_TruncatesLongLabels()
    {
        var arguments = FuseFileSystem.BuildMountArguments(isMacOs: false, debug: false, new string('A', 40));

        Assert.Contains($"fsname={new string('A', 32)}", arguments, StringComparer.Ordinal);
    }

    /// <summary>
    /// Verifies a label made entirely of separators and control characters falls back
    /// to the application name.
    /// </summary>
    [Fact]
    public void BuildMountArguments_AllInvalidLabel_FallsBackToApplicationName()
    {
        var arguments = FuseFileSystem.BuildMountArguments(isMacOs: true, debug: false, "///,,\n");

        Assert.Contains("volname=SimpleZipDrive", arguments, StringComparer.Ordinal);
    }

    /// <summary>
    /// Verifies the first argument is the file system name libfuse uses.
    /// </summary>
    [Fact]
    public void BuildMountArguments_FirstArgumentIsApplicationName()
    {
        var arguments = FuseFileSystem.BuildMountArguments(isMacOs: false, debug: false, "XBOX_ISO");

        Assert.Equal("SimpleZipDrive", arguments[0]);
    }

    /// <summary>
    /// Verifies debug mode on macOS exposes both the label and the debug flag.
    /// </summary>
    [Fact]
    public void BuildMountArguments_MacWithDebug_IncludesVolnameAndDebug()
    {
        var arguments = FuseFileSystem.BuildMountArguments(isMacOs: true, debug: true, "XBOX_ISO");

        Assert.Contains("volname=XBOX_ISO", arguments, StringComparer.Ordinal);
        Assert.Contains("-d", arguments, StringComparer.Ordinal);
    }

    /// <summary>
    /// Verifies no argument is empty, which would corrupt the native argv vector.
    /// </summary>
    [Fact]
    public void BuildMountArguments_HasNoEmptyArguments()
    {
        var arguments = FuseFileSystem.BuildMountArguments(isMacOs: false, debug: true, "XBOX_ISO");

        Assert.DoesNotContain(arguments, static argument => argument.Length == 0);
    }
}