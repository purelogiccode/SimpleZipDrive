namespace SimpleZipDrive.Tests;

/// <summary>
///     Serializes tests that read or write the application settings file and redirects the
///     settings file to a per-run temporary location via <see cref="SettingsFileFixture" />,
///     so the developer's real <c>%LOCALAPPDATA%\SimpleZipDrive\settings.dat</c> is never
///     created or overwritten.
/// </summary>
[CollectionDefinition("Settings file")]
public sealed class SettingsFileCollection : ICollectionFixture<SettingsFileFixture>;
