namespace SimpleZipDrive.Core.Interfaces;

/// <summary>
/// A read-only volume that can be exposed as a FUSE file system. Paths passed to the
/// volume are POSIX-style: <c>/</c> is the root and components are separated by <c>/</c>.
/// </summary>
public interface IFuseVolume
{
    /// <summary>
    /// Gets the volume label reported to the operating system.
    /// </summary>
    string VolumeLabel { get; }

    /// <summary>
    /// Gets the volume creation time.
    /// </summary>
    DateTime VolumeCreationTime { get; }

    /// <summary>
    /// Gets the total size of the volume in bytes.
    /// </summary>
    ulong VolumeSize { get; }

    /// <summary>
    /// Gets the entry for the specified path.
    /// </summary>
    /// <param name="path">The POSIX path to look up (<c>/</c> for the root).</param>
    /// <returns>The matching entry, or <see langword="null"/> if no entry exists at the path.</returns>
    IFuseEntry? GetEntry(string path);

    /// <summary>
    /// Enumerates the child entries of the directory at the specified path.
    /// </summary>
    /// <param name="path">The POSIX directory path to list (<c>/</c> for the root).</param>
    /// <returns>The entries contained in the directory; empty if the path is not a valid directory.</returns>
    IEnumerable<IFuseEntry> GetFolderList(string path);

    /// <summary>
    /// Reads file data for the specified entry into the buffer.
    /// </summary>
    /// <param name="entry">The file entry to read from.</param>
    /// <param name="buffer">The buffer that receives the data.</param>
    /// <param name="offset">The byte offset within the file at which to start reading.</param>
    /// <returns>The number of bytes read.</returns>
    /// <exception cref="IOException">Thrown when the underlying data cannot be read.</exception>
    int ReadFile(IFuseEntry entry, Span<byte> buffer, long offset);
}
