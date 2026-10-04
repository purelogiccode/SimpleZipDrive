using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;

namespace SimpleZipDrive.Views;

/// <summary>
///     A small cross-platform modal message box styled to match the application theme.
///     The API mirrors the WPF <c>System.Windows.MessageBox</c> surface used throughout the app.
/// </summary>
public partial class MessageBoxWindow : Window
{
    public MessageBoxWindow()
    {
        InitializeComponent();
    }

    public MessageBoxWindow(string message, string caption, MessageBoxButton button, MessageBoxImage icon)
    {
        InitializeComponent();

        Title = caption;
        MessageText.Text = message;

        (IconText.Text, IconText.Foreground) = icon switch
        {
            MessageBoxImage.Information => ("\u2139", Avalonia.Media.Brushes.DodgerBlue),
            MessageBoxImage.Warning => ("\u26A0", Avalonia.Media.Brushes.Orange),
            MessageBoxImage.Error => ("\u26D4", Avalonia.Media.Brushes.IndianRed),
            MessageBoxImage.Question => ("\u2753", Avalonia.Media.Brushes.DodgerBlue),
            _ => (string.Empty, Avalonia.Media.Brushes.Transparent)
        };

        switch (button)
        {
            case MessageBoxButton.OkCancel:
                AddButton("OK", MessageBoxResult.Ok, isDefault: true);
                AddButton("Cancel", MessageBoxResult.Cancel);
                break;
            case MessageBoxButton.YesNo:
                AddButton("Yes", MessageBoxResult.Yes, isDefault: true);
                AddButton("No", MessageBoxResult.No);
                break;
            default:
                AddButton("OK", MessageBoxResult.Ok, isDefault: true);
                break;
        }
    }

    public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

    private void AddButton(string content, MessageBoxResult result, bool isDefault = false)
    {
        ControlTheme? theme = null;
        if (this.TryFindResource(isDefault ? "PrimaryButton" : "SecondaryButton", out var resource))
            theme = resource as ControlTheme;

        var button = new Button
        {
            Content = content,
            Width = 85,
            Theme = theme,
            Tag = result
        };

        button.Click += OnButtonClick;
        ButtonsPanel.Children.Add(button);
    }

    private void OnButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MessageBoxResult result }) Result = result;
        Close();
    }
}
