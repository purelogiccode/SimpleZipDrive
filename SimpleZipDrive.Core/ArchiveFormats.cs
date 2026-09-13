namespace SimpleZipDrive.Core;

/// <summary>
///     Central definition of the archive formats supported by SimpleZipDrive.
///     Comic-book archive containers are mapped to their underlying format:
///     <c>.cbz</c> (ZIP), <c>.cbr</c> (RAR) and <c>.cb7</c> (7-Zip).
/// </summary>
public static class ArchiveFormats
{
    /// <summary>Archive type identifier for ZIP files.</summary>
    public const string Zip = "zip";

    /// <summary>Archive type identifier for 7-Zip files.</summary>
    public const string SevenZip = "7z";

    /// <summary>Archive type identifier for RAR files.</summary>
    public const string Rar = "rar";

    /// <summary>Archive type identifier for TAR files (including compressed variants).</summary>
    public const string Tar = "tar";

    /// <summary>Archive type identifier for ZArchive (.zar) files.</summary>
    public const string Zar = "zar";

    /// <summary>Archive type identifier for Xbox XISO disc images (.iso, .xiso, .cso).</summary>
    public const string Xiso = "xiso";

    /// <summary>
    ///     Human-readable list of supported extensions, used in error messages and dialogs.
    /// </summary>
    public const string SupportedExtensionsDescription =
        ".zip, .7z, .rar, .tar, .tar.gz, .tar.bz2, .tar.xz, .tgz, .tbz2, .txz, .cbz, .cbr, .cb7, .zar, .iso, .xiso, .cso";

    /// <summary>
    ///     Human-readable summary of the supported format families, used in the startup banner.
    /// </summary>
    public const string SupportedFormatsDescription =
        "ZIP, 7Z, RAR, and TAR archives (incl. .tar.gz, .tar.bz2, .tar.xz, .tgz, .tbz2, .txz), " +
        "comic-book archives (.cbz, .cbr, .cb7), Zstd-seekable ZArchive (.zar), and Xbox XISO disc images (.iso, .xiso, .cso)";

    /// <summary>
    ///     File-dialog filter covering all supported archive extensions.
    /// </summary>
    public const string DialogFilter =
        "Archive files (*.zip;*.7z;*.rar;*.tar;*.tar.gz;*.tar.bz2;*.tar.xz;*.tgz;*.tbz2;*.txz;*.cbz;*.cbr;*.cb7;*.zar;*.iso;*.xiso;*.cso)|*.zip;*.7z;*.rar;*.tar;*.tar.gz;*.tar.bz2;*.tar.xz;*.tgz;*.tbz2;*.txz;*.cbz;*.cbr;*.cb7;*.zar;*.iso;*.xiso;*.cso|" +
        "ZIP files (*.zip;*.cbz)|*.zip;*.cbz|" +
        "7Z files (*.7z;*.cb7)|*.7z;*.cb7|" +
        "RAR files (*.rar;*.cbr)|*.rar;*.cbr|" +
        "TAR files (*.tar;*.tar.gz;*.tar.bz2;*.tar.xz;*.tgz;*.tbz2;*.txz)|*.tar;*.tar.gz;*.tar.bz2;*.tar.xz;*.tgz;*.tbz2;*.txz|" +
        "ZAR files (*.zar)|*.zar|" +
        "Xbox XISO images (*.iso;*.xiso;*.cso)|*.iso;*.xiso;*.cso|" +
        "All files (*.*)|*.*";

    private static readonly string[] ContainerExtensions = [".zip", ".7z", ".rar", ".tar", ".zar", ".xiso"];

    private static readonly string[] TarCompressedSuffixes =
        [".tar.gz", ".tar.bz2", ".tar.xz", ".tgz", ".tbz2", ".txz"];

    /// <summary>
    ///     Returns the archive type identifier for a file path (e.g. <c>"zip"</c>, <c>"7z"</c>, <c>"rar"</c>, <c>"tar"</c>).
    ///     Comic-book extensions are mapped to their container format so they can be opened by the
    ///     existing readers (a <c>.cbz</c> is a ZIP file, a <c>.cbr</c> is a RAR file, a <c>.cb7</c> is a 7-Zip file).
    ///     Xbox disc-image extensions (<c>.iso</c>, <c>.xiso</c>, <c>.cso</c>) all map to the XISO reader,
    ///     which validates the Xbox XDVDFS header during mount.
    ///     Unknown extensions are returned without the leading dot.
    /// </summary>
    /// <param name="filePath">The archive file path.</param>
    /// <returns>The archive type identifier.</returns>
    public static string GetArchiveType(string filePath)
    {
        var fileName = Path.GetFileName(filePath).ToLowerInvariant();

        foreach (var suffix in TarCompressedSuffixes)
        {
            if (fileName.EndsWith(suffix, StringComparison.Ordinal))
                return Tar;
        }

        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        return extension switch
        {
            ".cbz" => Zip,
            ".cbr" => Rar,
            ".cb7" => SevenZip,
            ".iso" or ".xiso" or ".cso" => Xiso,
            _ => extension.TrimStart('.')
        };
    }

    /// <summary>
    ///     Returns true when the file extension corresponds to a supported archive format.
    /// </summary>
    /// <param name="filePath">The archive file path.</param>
    /// <returns>True when the archive type is supported; otherwise false.</returns>
    public static bool IsSupportedArchive(string filePath)
    {
        var type = GetArchiveType(filePath);
        return ContainerExtensions.Contains("." + type, StringComparer.OrdinalIgnoreCase);
    }
}