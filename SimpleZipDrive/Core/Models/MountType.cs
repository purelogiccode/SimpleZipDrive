namespace SimpleZipDrive.Core.Models;

/// <summary>
///     Specifies how the archive virtual drive is mounted in the filesystem.
/// </summary>
public enum MountType
{
    /// <summary>Mount as a standard drive letter (e.g., <c>Z:\</c>).</summary>
    DriveLetter,

    /// <summary>Mount as a directory within an existing drive.</summary>
    Folder
}