using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace SimpleZipDrive.Views;

/// <summary>
///     Dialog for editing application settings such as RAM cache limit, mount type, mount backend,
///     cross-integrity security, and auto-open behavior.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly ISettingsService _settingsService;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SettingsWindow" /> class,
    ///     populating controls with the current persisted settings.
    /// </summary>
    public SettingsWindow()
    {
        InitializeComponent();
        _settingsService = ServiceProvider.Get<ISettingsService>();

        var settings = _settingsService.Settings;
        RamLimitTextBox.Text = settings.MaxMemoryPerFileMb.ToString(CultureInfo.InvariantCulture);

        MountTypeComboBox.Items.Add("Drive Letter");
        MountTypeComboBox.Items.Add("Folder");
        MountTypeComboBox.SelectedIndex = settings.DefaultMountType == MountType.Folder ? 1 : 0;

        PopulateBackends(settings.MountBackend);

        AutoOpenCheckBox.IsChecked = settings.AutoOpenMountedDrive;
        CrossIntegrityCheckBox.IsChecked = settings.CrossIntegrityMount;
        MountFolderTextBox.Text = settings.CrossIntegrityMountFolder;
    }

    private void PopulateBackends(MountBackend current)
    {
        var options = new (string Label, MountBackend Value, bool Enabled)[]
        {
            ("Auto (recommended)", MountBackend.Auto, true),
            ("Dokan (Windows)", MountBackend.Dokan, OperatingSystem.IsWindows()),
            ("WinFsp (Windows)", MountBackend.WinFsp, OperatingSystem.IsWindows()),
            ("FUSE (Linux/macOS)", MountBackend.Fuse, !OperatingSystem.IsWindows())
        };

        var selectedIndex = 0;
        for (var i = 0; i < options.Length; i++)
        {
            var (label, value, enabled) = options[i];
            MountBackendComboBox.Items.Add(new ComboBoxItem { Content = label, Tag = value, IsEnabled = enabled });
            if (value == current && enabled) selectedIndex = i;
        }

        MountBackendComboBox.SelectedIndex = selectedIndex;
        BackendHintText.Text = OperatingSystem.IsWindows()
            ? "Auto picks WinFsp when installed, otherwise Dokan. FUSE is used on Linux/macOS."
            : "FUSE (libfuse3/macFUSE) is used on Linux/macOS.";
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (long.TryParse(RamLimitTextBox.Text, CultureInfo.InvariantCulture, out var value) && value > 0)
            {
                var settings = _settingsService.Settings;
                settings.MaxMemoryPerFileMb = value;
                settings.DefaultMountType =
                    MountTypeComboBox.SelectedIndex == 1 ? MountType.Folder : MountType.DriveLetter;
                settings.MountBackend = MountBackendComboBox.SelectedItem is ComboBoxItem { Tag: MountBackend backend }
                    ? backend
                    : MountBackend.Auto;
                settings.AutoOpenMountedDrive = AutoOpenCheckBox.IsChecked == true;
                settings.CrossIntegrityMount = CrossIntegrityCheckBox.IsChecked == true;
                settings.CrossIntegrityMountFolder = MountFolderTextBox.Text?.Trim() ?? string.Empty;
                _settingsService.SaveSettings();

                var actualValue = settings.MaxMemoryPerFileMb;
                var loggingService = ServiceProvider.TryGet<ILoggingService>();
                loggingService?.Log($"{AppTheme.Section("SETTINGS")}");

                if (actualValue < value)
                {
                    var availableMemoryMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024;
                    loggingService?.Log(
                        $"RAM limit per file capped to {actualValue} MB (90% of available {availableMemoryMb} MB system memory).");
                    loggingService?.Log($"Entered value {value} MB exceeded safe limits and was adjusted.");
                }
                else
                {
                    loggingService?.Log(
                        $"RAM limit per file updated to {actualValue} MB. New mounts will use this value.");
                }

                loggingService?.Log(
                    $"Default mount type set to: {(settings.DefaultMountType == MountType.Folder ? "Folder" : "Drive Letter")}.");

                loggingService?.Log($"Mount backend set to: {settings.MountBackend}.");

                loggingService?.Log(
                    $"Auto-open mounted drive: {(settings.AutoOpenMountedDrive ? "Enabled" : "Disabled")}.");

                loggingService?.Log(
                    $"Cross-integrity mount: {(settings.CrossIntegrityMount ? "Enabled (folder mount with permissive DACL)" : "Disabled")}.");

                var mountFolder = settings.CrossIntegrityMountFolder;
                if (settings.CrossIntegrityMount)
                {
                    loggingService?.Log(
                        $"Cross-integrity mount folder: {(string.IsNullOrWhiteSpace(mountFolder) ? @"Default (%LOCALAPPDATA%\SimpleZipDrive\Mounts)" : mountFolder)}.");
                }

                Close();
            }
            else
            {
                MessageBox.Show("Please enter a valid positive number for the RAM limit.", "Invalid Input",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            const string context = "SettingsWindow.Save_Click: Error saving settings";
            ErrorLoggerStatic.LogErrorSync(ex, context);
            MessageBox.Show($"Error saving settings: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BrowseMountFolder_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var currentPath = MountFolderTextBox.Text?.Trim();
            IStorageFolder? startLocation = null;
            if (!string.IsNullOrEmpty(currentPath) && Directory.Exists(currentPath))
                startLocation = await StorageProvider.TryGetFolderFromPathAsync(currentPath);

            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Cross-Integrity Mount Folder",
                AllowMultiple = false,
                SuggestedStartLocation = startLocation
            });

            if (folders.Count > 0) MountFolderTextBox.Text = folders[0].Path.LocalPath;
        }
        catch (Exception ex)
        {
            ErrorLoggerStatic.ReportSilentException(ex, "SettingsWindow.BrowseMountFolder_Click failed", true);
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
