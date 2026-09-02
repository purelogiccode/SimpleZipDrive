using SharpCompress.Archives;

namespace SimpleZipDrive.Core.Models;

/// <summary>
///     Represents a resolved filesystem entry (file or directory) within the archive.
/// </summary>
public sealed class EntryNode
{
    /// <summary>Gets or sets the normalized forward-slash-separated path (e.g., "/folder/file.txt").</summary>
    public string NormalizedPath { get; set; } = string.Empty;

    /// <summary>Gets or sets the canonical path as it appears in the archive entry key.</summary>
    public string CanonicalPath { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether this node represents a directory.</summary>
    public bool IsDir { get; set; }

    /// <summary>Gets or sets the underlying archive entry, or <see langword="null" /> for implicit directories.</summary>
    public IArchiveEntry? Entry { get; set; }

    /// <summary>Gets or sets the uncompressed file size in bytes (0 for directories).</summary>
    public long FileSize { get; set; }

    /// <summary>Gets or sets the creation timestamp of this entry.</summary>
    public DateTime CreationTime { get; set; }

    /// <summary>Gets or sets the last-modified timestamp of this entry.</summary>
    public DateTime LastWriteTime { get; set; }

    /// <summary>Gets or sets the last-accessed timestamp of this entry.</summary>
    public DateTime LastAccessTime { get; set; }
}