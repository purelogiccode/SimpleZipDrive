namespace SimpleZipDrive.Core.Models;

/// <summary>
///     Selects which file-system driver is used to expose the archive as a drive/folder.
/// </summary>
public enum MountBackend
{
    /// <summary>Pick the best available backend for the current platform (WinFsp → Dokan on Windows, FUSE elsewhere).</summary>
    Auto,

    /// <summary>Use the Dokan driver (Windows only).</summary>
    Dokan,

    /// <summary>Use the WinFsp driver (Windows only).</summary>
    WinFsp,

    /// <summary>Use FUSE (Linux and macOS).</summary>
    Fuse
}
