namespace SimpleZipDrive.Core.Interfaces;

/// <summary>
///     Prompts the user for the password of an encrypted archive. Implemented by the UI layer
///     so that the mount backends stay independent of any particular UI framework.
/// </summary>
public interface IPasswordPromptService
{
    /// <summary>
    ///     Shows a modal password prompt and blocks the calling thread until the user responds.
    /// </summary>
    /// <param name="archivePath">Full path to the archive file (used to display the file name).</param>
    /// <param name="archiveType">Archive format identifier (e.g., "zip", "7z", "rar").</param>
    /// <returns>The entered password, or <see langword="null" /> if the user cancelled.</returns>
    string? Prompt(string archivePath, string archiveType);
}
