using System.Diagnostics.CodeAnalysis;

namespace SimpleZipDrive.Core.Models;

/// <summary>
///     Event arguments for mount status changes.
/// </summary>
[SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global",
    Justification = "Populated by the mount services and part of the public event contract; the UI currently reads the service state directly.")]
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
