namespace SimpleZipDrive.Core.Models;

/// <summary>
///     Event arguments for mount status changes.
/// </summary>
public class MountStatusChangedEventArgs : EventArgs
{
    /// <summary>
    ///     Gets a value indicating whether a drive is currently mounted.
    /// </summary>
    public bool IsMounted { get; init; }

    /// <summary>
    ///     Gets the mount point (e.g., "M:\").
    /// </summary>
    public string? MountPoint { get; init; }

    /// <summary>
    ///     Gets the archive path.
    /// </summary>
    public string? ArchivePath { get; init; }
}