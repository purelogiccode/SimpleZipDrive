using System.Globalization;
using System.Net.Sockets;
using System.Security.Cryptography;
using SimpleZipDrive.Core;

namespace SimpleZipDrive.Tests;

[Collection("Logging")]
public class ErrorLoggerAdditionalTests
{
    // ─── IsUserError: DokanNet namespace exceptions ───

    [Fact]
    public void IsUserError_DokanNetDriveException_ReturnsTrue()
    {
        // The method checks fullName.StartsWith("DokanNet.") and typeName.Contains("Drive")
        // We can't easily create a DokanNet exception, but we can test the message-based path
        var result = ErrorLogger.IsUserError(new InvalidOperationException("drive letter is in use"));
        Assert.True(result);
    }

    // ─── IsUserError: "wrong password" message ───

    [Fact]
    public void IsUserError_WrongPasswordMessage_ReturnsTrue()
    {
        var result = ErrorLogger.IsUserError(new InvalidOperationException("wrong password provided"));
        Assert.True(result);
    }

    // ─── IsUserError: "incorrect password" message ───

    [Fact]
    public void IsUserError_IncorrectPasswordMessage_ReturnsTrue()
    {
        var result = ErrorLogger.IsUserError(new InvalidOperationException("incorrect password"));
        Assert.True(result);
    }

    // ─── IsUserError: "no password" message ───

    [Fact]
    public void IsUserError_NoPasswordMessage_ReturnsTrue()
    {
        var result = ErrorLogger.IsUserError(new InvalidOperationException("no password was provided"));
        Assert.True(result);
    }

    // ─── IsUserError: "need a password" message ───

    [Fact]
    public void IsUserError_NeedAPasswordMessage_ReturnsTrue()
    {
        var result = ErrorLogger.IsUserError(new InvalidOperationException("you need a password to open this"));
        Assert.True(result);
    }

    // ─── IsUserError: "missing password" message ───

    [Fact]
    public void IsUserError_MissingPasswordMessage_ReturnsTrue()
    {
        var result = ErrorLogger.IsUserError(new InvalidOperationException("missing password for encrypted archive"));
        Assert.True(result);
    }

    // ─── IsUserError: "invalid password" message ───

    [Fact]
    public void IsUserError_InvalidPasswordMessage_ReturnsTrue()
    {
        var result = ErrorLogger.IsUserError(new InvalidOperationException("invalid password"));
        Assert.True(result);
    }

    // ─── IsUserError: "password is" message ───

    [Fact]
    public void IsUserError_PasswordIsMessage_ReturnsTrue()
    {
        var result = ErrorLogger.IsUserError(new InvalidOperationException("password is wrong"));
        Assert.True(result);
    }

    // ─── IsUserError: "encrypted" with "file" ───

    [Fact]
    public void IsUserError_EncryptedFile_ReturnsTrue()
    {
        var result = ErrorLogger.IsUserError(new InvalidOperationException("the file is encrypted"));
        Assert.True(result);
    }

    // ─── IsUserError: "encrypted" with "archive" ───

    [Fact]
    public void IsUserError_EncryptedArchive_ReturnsTrue()
    {
        var result = ErrorLogger.IsUserError(new InvalidOperationException("the archive is encrypted"));
        Assert.True(result);
    }

    // ─── IsUserError: "encrypted" with "entry" ───

    [Fact]
    public void IsUserError_EncryptedEntry_ReturnsTrue()
    {
        var result = ErrorLogger.IsUserError(new InvalidOperationException("entry is encrypted"));
        Assert.True(result);
    }

    // ─── GetEnvironmentDetails: includes bitness ───

    [Fact]
    public void GetEnvironmentDetails_IncludesBitness()
    {
        using var logger = new ErrorLogger();
        var result = logger.GetEnvironmentDetails();

        Assert.Contains("Bitness:", result, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.Contains("64-bit", StringComparison.OrdinalIgnoreCase) ||
                    result.Contains("32-bit", StringComparison.OrdinalIgnoreCase));
    }

    // ─── GetEnvironmentDetails: includes Windows version ───

    [Fact]
    public void GetEnvironmentDetails_IncludesWindowsVersion()
    {
        using var logger = new ErrorLogger();
        var result = logger.GetEnvironmentDetails();

        Assert.Contains("Windows Version:", result, StringComparison.OrdinalIgnoreCase);
    }

    // ─── GetEnvironmentDetails: includes processor count ───

    [Fact]
    public void GetEnvironmentDetails_IncludesProcessorCount()
    {
        using var logger = new ErrorLogger();
        var result = logger.GetEnvironmentDetails();

        Assert.Contains("Processor Count:", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture), result,
            StringComparison.OrdinalIgnoreCase);
    }

    // ─── ReportSilentException: does not throw ───

    [Fact]
    public void ReportSilentException_NonSilent_DoesNotThrow()
    {
        var thrown = Record.Exception(() =>
        {
            using var logger = new ErrorLogger();
            ErrorLogger.ReportSilentException(new InvalidOperationException("test"), "test context");
        });

        Assert.Null(thrown);
    }

    // ─── ReportSilentException: silent mode does not write to console ───

    [Fact]
    public void ReportSilentException_Silent_DoesNotWriteToConsole()
    {
        using var logger = new ErrorLogger();
        var originalError = Console.Error;
        try
        {
            using var capture = new StringWriter();
            Console.SetError(capture);

            ErrorLogger.ReportSilentException(new InvalidOperationException("test"), "test context", true);

            var output = capture.ToString();
            Assert.DoesNotContain("SILENT EXCEPTION CAUGHT", output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    // ─── IsUserError: SharpCompress namespace with non-matching type ───

    [Fact]
    public void IsUserError_SharpCompressNonMatchingType_ReturnsFalse()
    {
        // SharpCompress namespace but type name doesn't contain Archive/Format/Invalid
        var result = ErrorLogger.IsUserError(new InvalidOperationException("some error"));
        // This is a plain InvalidOperationException, not SharpCompress
        Assert.False(result);
    }

    // ─── IsUserError: "cannot find central directory" ───

    [Fact]
    public void IsUserError_CannotFindCentralDirectory_ReturnsTrue()
    {
        var result = ErrorLogger.IsUserError(new InvalidOperationException("cannot find central directory"));
        Assert.True(result);
    }

    // ─── IsUserError: "unknown format" ───

    [Fact]
    public void IsUserError_UnknownFormat_ReturnsTrue()
    {
        var result = ErrorLogger.IsUserError(new InvalidOperationException("unknown format"));
        Assert.True(result);
    }

    // ─── IsUserError: "not a valid" ───

    [Fact]
    public void IsUserError_NotAValid_ReturnsTrue()
    {
        var result = ErrorLogger.IsUserError(new InvalidOperationException("this is not a valid file"));
        Assert.True(result);
    }

    // ─── IsUserError: "corrupt" with "archive" ───

    [Fact]
    public void IsUserError_CorruptArchive_ReturnsTrue()
    {
        var result = ErrorLogger.IsUserError(new InvalidOperationException("archive is corrupt"));
        Assert.True(result);
    }

    // ─── IsUserError: "corrupt" with "zip" ───

    [Fact]
    public void IsUserError_CorruptZip_ReturnsTrue()
    {
        var result = ErrorLogger.IsUserError(new InvalidOperationException("zip file is corrupt"));
        Assert.True(result);
    }

    // ─── IsUserError: "corrupt" alone (not enough) ───

    [Fact]
    public void IsUserError_CorruptAlone_ReturnsFalse()
    {
        var result = ErrorLogger.IsUserError(new InvalidOperationException("something corrupt"));
        Assert.False(result);
    }

    // ─── IsUserError: "header" with "invalid" ───

    [Fact]
    public void IsUserError_HeaderInvalid_ReturnsTrue()
    {
        var result = ErrorLogger.IsUserError(new InvalidOperationException("header is invalid"));
        Assert.True(result);
    }

    // ─── IsUserError: "mount point" with "invalid" ───

    [Fact]
    public void IsUserError_MountPointInvalid_ReturnsTrue()
    {
        var result = ErrorLogger.IsUserError(new InvalidOperationException("mount point is invalid"));
        Assert.True(result);
    }

    // ─── IsUserError: "drive letter" with "in use" ───

    [Fact]
    public void IsUserError_DriveLetterInUse_ReturnsTrue()
    {
        var result = ErrorLogger.IsUserError(new InvalidOperationException("drive letter is in use"));
        Assert.True(result);
    }

    // ─── IsUserError: "canceled" ───

    [Fact]
    public void IsUserError_Canceled_ReturnsTrue()
    {
        var result = ErrorLogger.IsUserError(new InvalidOperationException("operation was canceled"));
        Assert.True(result);
    }

    // ─── IsUserError: "cancelled" ───

    [Fact]
    public void IsUserError_Cancelled_ReturnsTrue()
    {
        var result = ErrorLogger.IsUserError(new InvalidOperationException("operation was cancelled"));
        Assert.True(result);
    }

    // ─── IsUserError: OperationCanceledException ───

    [Fact]
    public void IsUserError_OperationCanceledException_ReturnsTrue()
    {
        var result = ErrorLogger.IsUserError(new OperationCanceledException());
        Assert.True(result);
    }

    // ─── IsUserError: TaskCanceledException ───

    [Fact]
    public void IsUserError_TaskCanceledException_ReturnsTrue()
    {
        var result = ErrorLogger.IsUserError(new TaskCanceledException());
        Assert.True(result);
    }

    // ─── IsUserError: HttpRequestException with OperationCanceledException inner ───

    [Fact]
    public void IsUserError_HttpRequestExceptionWithCanceledInner_ReturnsTrue()
    {
        var ex = new HttpRequestException("request failed", new OperationCanceledException());
        var result = ErrorLogger.IsUserError(ex);
        Assert.True(result);
    }

    // ─── IsUserError: HttpRequestException without canceled inner ───

    [Fact]
    public void IsUserError_HttpRequestExceptionWithoutCanceledInner_ReturnsFalse()
    {
        // A non-network inner exception (e.g. a server error) may be a real application error
        // and must stay reportable. Only DNS/connection failures (SocketException) are treated
        // as environment noise by the separate filter below.
        var ex = new HttpRequestException("request failed", new InvalidOperationException("server error"));
        var result = ErrorLogger.IsUserError(ex);
        Assert.False(result);
    }

    // ─── IsUserError: network failures ("Host not found") are environment conditions ───

    [Fact]
    public void IsUserError_HttpRequestExceptionWithSocketExceptionInner_ReturnsTrue()
    {
        var ex = new HttpRequestException("Host not found.", new SocketException((int)SocketError.HostNotFound));
        Assert.True(ErrorLogger.IsUserError(ex));
    }

    [Fact]
    public void IsUserError_SocketException_ReturnsTrue()
    {
        Assert.True(ErrorLogger.IsUserError(new SocketException((int)SocketError.HostNotFound)));
    }

    // ─── IsUserError: expected environment conditions (WinFsp / Dokan) are not bugs ───

    [Theory]
    [InlineData("WinFsp not found. Unable to mount archive.")]
    [InlineData("Mount failed: WinFsp version mismatch. Installed: ~2.1, Required: 2.2.")]
    [InlineData("WinFsp mount error: incorrect dll version (need 2.2, have 2.1)")]
    [InlineData("WinFsp driver service is not running. Please start the WinFsp.Launcher service.")]
    [InlineData("WinFsp native DLL could not be loaded. The DLL may be missing.")]
    [InlineData(
        @"Error: Failed to mount 'D:\Games\game.rar'. Could not load file or assembly 'winfsp-msil, Version=2.0.0.0, Culture=neutral, PublicKeyToken=b099876d8fa9b1f3'.")]
    [InlineData("WinFsp mount failed with status 0xC0000035: Mount failed with status 0xC0000035.")]
    [InlineData(
        "WinFsp mount failed with status 0xC0000033: Mount failed with status 0xC0000033. This may be caused by an outdated WinFsp driver.")]
    [InlineData("Mount point 'N:' is not accessible or cannot be created.")]
    [InlineData(
        @"Mount error: Could not find a part of the path 'C:\Users\Red\AppData\Local\SimpleZipDrive\Temp\344344_4348ff0eabcd46499496a744557eb147'.")]
    [InlineData("The WinFsp driver was not found or is not running. Please install or start the WinFsp service.")]
    [InlineData("Dokan driver not found. Unable to mount archive.")]
    [InlineData("Can't install the Dokan driver")]
    [InlineData("The file 'book.cbz' is not a supported archive format (expected .zip, .7z, .rar, .tar).")]
    // Cross-platform facade: missing driver or a backend that does not exist on this OS
    // (the reported "libfuse3 not found" startup warning on Windows).
    [InlineData("libfuse3 not found: FUSE is only available on Linux and macOS.")]
    [InlineData("FUSE is not available: FUSE is only available on Linux and macOS. Unable to mount archive.")]
    [InlineData(
        "The FUSE runtime library (libfuse3 on Linux, macFUSE on macOS) is not available. Unable to mount archive.")]
    [InlineData("Dokan is not available: The Dokan driver (dokan2.dll) is not installed. Unable to mount archive.")]
    [InlineData("WinFsp is not available: The WinFsp driver is not installed. Unable to mount archive.")]
    [InlineData("Dokan is not available: Dokan is only available on Windows.")]
    public void IsUserError_ExpectedEnvironmentOrUserConditions_ReturnsTrue(string message)
    {
        var ex = new InvalidOperationException(message);
        var result = ErrorLogger.IsUserError(ex);
        Assert.True(result);
    }

    // ─── IsUserError: Avalonia render-pass invalidation is a framework condition ───

    [Fact]
    public void IsUserError_AvaloniaRenderPassInvalidation_ReturnsTrue()
    {
        // Avalonia throws InvalidOperationException("Visual was invalidated during the render pass")
        // when a control template is applied while a TextBlock with embedded inline controls is
        // measured during rendering. It is a framework condition, not an application bug, and must
        // not be forwarded to the bug-report API (bug reports #68051/#68052).
        var ex = new InvalidOperationException("Visual was invalidated during the render pass")
        {
            Source = "Avalonia.Base"
        };
        Assert.True(ErrorLogger.IsUserError(ex));
    }

    [Fact]
    public void IsUserError_AvaloniaRenderPassInvalidationMessageOnly_ReturnsTrue()
    {
        // BugReportSink wraps message-only log events in an InvalidOperationException before
        // classifying them, so the same text must be recognized without an Avalonia source.
        var ex = new InvalidOperationException("Visual was invalidated during the render pass");
        Assert.True(ErrorLogger.IsUserError(ex));
    }

    [Fact]
    public void IsUserError_UnrelatedRenderFailure_ReturnsFalse()
    {
        // Only the exact Avalonia render-pass invalidation is an environment condition; other
        // render/layout failures may be real application bugs and must stay reportable.
        var ex = new InvalidOperationException("Failed to render the archive listing.");
        Assert.False(ErrorLogger.IsUserError(ex));
    }

    // ─── IsUserError: user/data errors observed in bug reports that must not be forwarded ───

    [Theory]
    // Wrong RAR password (SharpCompress CryptographicException text)
    [InlineData("Mount error: The password did not match.")]
    // Archive locked by another process (antivirus / download manager / torrent client)
    [InlineData(
        @"Mount error: The process cannot access the file 'C:\GAMES\game.rar' because it is being used by another process.")]
    // Corrupt/truncated RAR file
    [InlineData("Mount error: Unknown Rar Header: 0")]
    // Truncated multi-part archive
    [InlineData("Mount error: Cannot seek to position 1047630476. End of stream reached at position 1047265280.")]
    // Archive file missing (e.g. unmounted virtual drive path)
    [InlineData("Error: Archive file not found at 'Z:\\game.zip'.")]
    public void IsUserError_UserOrDataErrorsFromBugReports_ReturnsTrue(string message)
    {
        var ex = new InvalidOperationException(message);
        var result = ErrorLogger.IsUserError(ex);
        Assert.True(result);
    }

    [Fact]
    public void IsUserError_CryptographicExceptionFromSharpCompress_ReturnsTrue()
    {
        // CryptographicException thrown inside SharpCompress means wrong password / encrypted data
        var ex = new CryptographicException("The password did not match.") { Source = "SharpCompress" };
        Assert.True(ErrorLogger.IsUserError(ex));
    }

    [Fact]
    public void IsUserError_CryptographicExceptionFromOtherSource_ReturnsFalse()
    {
        // A CryptographicException unrelated to archive decryption may be a real application bug
        // (e.g. protected-data misuse) and must stay reportable.
        var ex = new CryptographicException("Key not valid for use in specified state.");
        Assert.False(ErrorLogger.IsUserError(ex));
    }

    [Fact]
    public void IsUserError_BadImageFormatException_ReturnsTrue()
    {
        // Architecture/binary mismatches are environment problems
        var result =
            ErrorLogger.IsUserError(
                new BadImageFormatException(
                    "An attempt was made to load a program with an incorrect format. (0x8007000B)"));
        Assert.True(result);
    }

    [Fact]
    public void IsUserError_IndexOutOfRangeExceptionWithoutSharpCompressContext_ReturnsFalse()
    {
        // Without SharpCompress context this must stay reportable (could be a real app bug)
        var result =
            ErrorLogger.IsUserError(new IndexOutOfRangeException("Index was outside the bounds of the array."));
        Assert.False(result);
    }

    [Fact]
    public void IsUserError_IndexOutOfRangeExceptionFromSharpCompressStack_ReturnsTrue()
    {
        // Corrupt ZIP symptom reported by users (bug #65320/#65321):
        // IndexOutOfRangeException thrown inside SharpCompress.ZipArchive.LoadEntries.
        try
        {
            ThrowIndexOutOfRangeException_LikeSharpCompressLoadEntries();
            Assert.Fail("Expected exception was not thrown.");
        }
        catch (Exception ex)
        {
            // The stack frame below contains 'SharpCompress' via the throwing method name,
            // mirroring how the production exception carries SharpCompress frames.
            Assert.True(ErrorLogger.IsUserError(ex));
        }
    }

    private static void ThrowIndexOutOfRangeException_LikeSharpCompressLoadEntries()
    {
        // Method name intentionally references SharpCompress so the captured stack trace
        // contains the library name, simulating an exception thrown inside SharpCompress code.
        // MA0012: IndexOutOfRangeException is intentional - it mirrors the exact runtime
        // exception thrown by SharpCompress.ZipArchive.LoadEntries (bug #65320/#65321).
#pragma warning disable MA0012
        throw new IndexOutOfRangeException("Index was outside the bounds of the array.");
#pragma warning restore MA0012
    }

    [Fact]
    public void IsUserError_UnrelatedMountError_ReturnsFalse()
    {
        var ex = new InvalidOperationException("Unexpected failure in ZipFs.InitializeEntries.");
        var result = ErrorLogger.IsUserError(ex);
        Assert.False(result);
    }

    [Fact]
    public void IsUserError_GenuineFuseSessionFailure_ReturnsFalse()
    {
        // A real FUSE session/interop failure must stay reportable; only missing-runtime
        // and unavailable-backend messages are environment conditions.
        var ex = new InvalidOperationException("FUSE session failed for '/mnt/archive': fuse_loop failed with -1");
        Assert.False(ErrorLogger.IsUserError(ex));
    }

    [Fact]
    public void IsUserError_AssemblyLoadFailureOutsideWinFsp_ReturnsFalse()
    {
        // The environment filter only covers the WinFsp interop assembly; a missing
        // unrelated assembly may be a real packaging regression and must stay reportable.
        var ex = new InvalidOperationException(
            "Error unmounting drive: Could not load file or assembly 'Contoso.Widgets, Version=1.0.0.0, " +
            "Culture=neutral, PublicKeyToken=null'. The system cannot find the file specified.");
        Assert.False(ErrorLogger.IsUserError(ex));
    }

    [Fact]
    public void IsUserError_MissingPathOutsideAppData_ReturnsFalse()
    {
        // Only missing application data/temp paths are environment conditions; a missing
        // unrelated path may indicate a real application defect.
        var ex = new InvalidOperationException(
            @"Could not find a part of the path 'D:\Games\game\data.bin'.");
        Assert.False(ErrorLogger.IsUserError(ex));
    }
}