using System.Windows;
using MiniLocalChat.Infrastructure;

namespace MiniLocalChat.Views;

/// <summary>Asks for one line of text, e.g. a group name.</summary>
public partial class TextPromptWindow : Window
{
    private TextPromptWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Input.Focus();
            Input.SelectAll();
        };
    }

    /// <summary>Returns the entered text (possibly empty), or null if cancelled.</summary>
    public static string? Ask(Window? owner, string title, string label, string initial, int maxLength)
    {
        var window = new TextPromptWindow { Title = title };
        window.Heading.Text = title;
        window.Label.Text = label;
        window.Input.MaxLength = maxLength;
        window.Input.Text = initial;
        if (owner is { IsVisible: true })
        {
            window.Owner = owner;
            window.Topmost = owner.Topmost;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return DialogGuard.Run(() => window.ShowDialog()) == true ? window.Input.Text.Trim() : null;
    }

    private void OnSave(object sender, RoutedEventArgs e) => DialogResult = true;
}
