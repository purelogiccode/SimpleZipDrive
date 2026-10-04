using SimpleZipDrive.Core;
using SimpleZipDrive.Core.Models;

namespace SimpleZipDrive.Tests;

/// <summary>
///     Redirects <see cref="AppSettings.SettingsFilePath" /> to a temporary file for the
///     lifetime of the "Settings file" collection and removes it afterwards. Only the file is
///     redirected so path-shape expectations for <c>SettingsDirectory</c>-derived locations
///     (e.g. <see cref="ZipFsHelpers.BaseTempPath" />) stay valid.
/// </summary>
public sealed class SettingsFileFixture : IDisposable
{
    private readonly string? _originalOverride;

    /// <summary>Initializes the fixture and switches the settings file to a temp folder.</summary>
    public SettingsFileFixture()
    {
        _originalOverride = AppSettings.SettingsFilePathOverride;
        TempDirectory = Path.Combine(Path.GetTempPath(), "SimpleZipDrive.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempDirectory);
        AppSettings.SettingsFilePathOverride = Path.Combine(TempDirectory, "settings.dat");
    }

    /// <summary>Gets the temporary directory that holds the redirected settings file.</summary>
    public string TempDirectory { get; }

    /// <summary>Restores the previous settings file path and removes the temporary folder.</summary>
    public void Dispose()
    {
        AppSettings.SettingsFilePathOverride = _originalOverride;

        try
        {
            if (Directory.Exists(TempDirectory)) Directory.Delete(TempDirectory, true);
        }
        catch
        {
            // Best-effort cleanup of the temporary settings folder.
        }
    }
}
