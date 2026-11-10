using FsCheck;
using FsCheck.Xunit;
using SimpleZipDrive.Core;

namespace SimpleZipDrive.Tests;

/// <summary>
///     Property-based (FsCheck) tests for the path and name helpers in <see cref="ZipFsHelpers" />.
///     These complement the example-based tests in <see cref="ZipFsHelpersTests" />.
/// </summary>
public class ZipFsHelpersPropertyTests
{
    private static readonly char[] InvalidNameChars = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    [Property]
    public bool NormalizePath_IsIdempotent(string? path)
    {
        var normalized = ZipFsHelpers.NormalizePath(path);

        return string.Equals(normalized, ZipFsHelpers.NormalizePath(normalized), StringComparison.Ordinal);
    }

    [Property]
    public bool NormalizePath_IsRootedAndHasNoTrailingSeparator(string? path)
    {
        var normalized = ZipFsHelpers.NormalizePath(path);

        return normalized.StartsWith('/') &&
               (string.Equals(normalized, "/", StringComparison.Ordinal) || !normalized.EndsWith('/'));
    }

    [Property]
    public bool ResolveSpecialPaths_IsIdempotent(string? path)
    {
        var resolved = ZipFsHelpers.ResolveSpecialPaths(ZipFsHelpers.NormalizePath(path));

        return string.Equals(resolved, ZipFsHelpers.ResolveSpecialPaths(resolved), StringComparison.Ordinal);
    }

    [Property]
    public bool SanitizeVolumeLabel_StaysWithinNtfsLimit(string? label)
    {
        return ZipFsHelpers.SanitizeVolumeLabel(label).Length <= 32;
    }

    [Property]
    public bool SanitizeFolderName_RemovesInvalidCharacters(string? name)
    {
        var sanitized = ZipFsHelpers.SanitizeFolderName(name);

        return sanitized.Length <= 200 && !sanitized.Any(ch => InvalidNameChars.Contains(ch));
    }
}
