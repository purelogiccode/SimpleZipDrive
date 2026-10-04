namespace SimpleZipDrive.Core.Interfaces;

/// <summary>
/// Represents a single file or directory entry exposed by an <see cref="IFuseVolume"/>.
/// </summary>
public interface IFuseEntry
{
    /// <summary>
    /// Gets the name of the file or directory, without path separators.
    /// </summary>
    string FileName { get; }

    /// <summary>
    /// Gets a value indicating whether the entry represents a directory.
    /// </summary>
    bool IsDirectory { get; }

    /// <summary>
    /// Gets the size of the file in bytes, or zero for directories.
    /// </summary>
    long Size { get; }
}
