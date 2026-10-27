using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MiniLocalChat.Infrastructure;
using MiniLocalChat.Models;
using MiniLocalChat.Services;

namespace MiniLocalChat.Views;

/// <summary>One chat in the "By chat" list.</summary>
public sealed class ChatStorageRow(Conversation conversation, long fileBytes)
{
    public Conversation Conversation { get; } = conversation;

    public long FileBytes { get; } = fileBytes;

    public string Title => Conversation.Title;

    public bool HasFiles => FileBytes > 0;

    public string Detail =>
        StorageWindow.Messages(Conversation.Messages.Count) + " · "
        + (HasFiles ? FileSizeConverter.Format(FileBytes) + " in pictures and files" : "no pictures or files");
}

/// <summary>
/// Settings → Manage storage: what uses space on this computer, the trash of unused files, deleting old
/// pictures and files (now or every day), and per chat: delete its files or clear its history.
/// </summary>
public partial class StorageWindow : Window
{
    private const double MaxWindowHeight = 720;

    private static readonly (string Label, int Days)[] Ages =
        [("1 week", 7), ("1 month", 30), ("3 months", 90), ("6 months", 180), ("1 year", 365)];

    private readonly ChatService _chat = App.Chat;
    private readonly bool _ready;

    private StorageWindow()
    {
        InitializeComponent();
        Icon = AppIcons.WindowIcon;
        Title = $"Storage and history – {AppInfo.Title}";

        var chosen = _chat.Settings.DeleteFilesAfterDays;
        foreach (var (label, days) in Ages.Any(a => a.Days == chosen) || chosen <= 0 ? Ages : [.. Ages, ($"{chosen} days", chosen)])
        {
            AgeCombo.Items.Add(new ComboBoxItem { Content = label, Tag = days });
        }

        AgeCombo.SelectedItem = AgeCombo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == chosen) ?? AgeCombo.Items[2];
        AutoDeleteBox.IsChecked = chosen > 0;
        AutoEmptyBox.IsChecked = _chat.Settings.AutoEmptyTrash;
        _ready = true;
        Loaded += async (_, _) => await RefreshAsync();
    }

    public static void ShowFor(Window? owner)
    {
        var window = new StorageWindow();
        if (owner is { IsVisible: true })
        {
            window.Owner = owner;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            window.Topmost = true;
        }

        WindowSizing.FitHeight(window, window.Owner, MaxWindowHeight);
        DialogGuard.Run(() => window.ShowDialog());
    }

    public static string Messages(int count) => count == 1 ? "1 message" : $"{count:N0} messages";

    private static string Files(int count) => count == 1 ? "1 picture or file" : $"{count:N0} pictures and files";

    private static string Size(long bytes) => FileSizeConverter.Format(bytes);

    private (string Label, int Days) SelectedAge =>
        AgeCombo.SelectedItem is ComboBoxItem { Content: string label, Tag: int days } ? (label, days) : Ages[2];

    private async Task RefreshAsync()
    {
        var report = await _chat.MeasureStorageAsync();
        TotalText.Text = $"Mini Local Chat uses {Size(report.Total)} on this computer.";
        UnusedSize.Text = Size(report.Unused);
        EmptyTrashButton.IsEnabled = report.Unused > 0;
        ChatFilesSize.Text = Size(report.ChatFiles);
        LibrarySize.Text = Size(report.Library);
        AvatarsSize.Text = Size(report.Avatars);
        HistorySize.Text = Size(report.History);
        OtherSize.Text = Size(report.Other);

        var rows = _chat.Conversations
            .Where(c => c.Messages.Count > 0)
            .Select(c => new ChatStorageRow(c, report.ChatFilesByConversation.GetValueOrDefault(c.Id)))
            .OrderByDescending(r => r.FileBytes)
            .ThenByDescending(r => r.Conversation.Messages.Count)
            .ToList();
        ChatList.ItemsSource = rows;
        NoChats.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Runs a cleanup with the window locked, then shows its result and the new numbers.</summary>
    private async Task RunAsync(Func<Task<string>> work)
    {
        Body.IsEnabled = false;
        StatusText.Text = "Working…";
        try
        {
            var result = await work();
            await RefreshAsync();
            StatusText.Text = result;
        }
        catch (Exception ex)
        {
            Log.Error("Storage cleanup", ex);
            StatusText.Text = "Something went wrong; some files may not have been deleted.";
        }
        finally
        {
            Body.IsEnabled = true;
        }
    }

    private async void OnEmptyTrash(object sender, RoutedEventArgs e) =>
        await RunAsync(async () => $"Freed {Size(await _chat.EmptyTrashAsync())}.");

    private async void OnDeleteOld(object sender, RoutedEventArgs e)
    {
        var (label, days) = SelectedAge;
        var plan = _chat.PlanFileCleanup(null, DateTime.UtcNow.AddDays(-days));
        if (plan.IsEmpty)
        {
            StatusText.Text = $"No pictures or files older than {label}.";
            return;
        }

        if (plan.Paths.Count == 0 || Confirm($"Delete pictures and files older than {label}?",
                $"This deletes {Files(plan.Paths.Count)} ({Size(plan.Bytes)}) in all chats from this computer. "
                + "The messages stay, marked as removed. The others keep their copy."))
        {
            await RunAsync(async () => $"Freed {Size(await _chat.DeleteFilesAsync(plan))}.");
        }
    }

    private async void OnDeleteChatFiles(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row)
        {
            return;
        }

        var plan = _chat.PlanFileCleanup(row.Conversation, null);
        if (plan.IsEmpty)
        {
            StatusText.Text = "Nothing to delete there: files still being sent, or also shown in another chat, are kept.";
            return;
        }

        if (plan.Paths.Count == 0 || Confirm($"Delete the pictures and files in “{row.Title}”?",
                $"This deletes {Files(plan.Paths.Count)} ({Size(plan.Bytes)}) from this computer. "
                + "The messages stay, marked as removed. The others keep their copy."))
        {
            await RunAsync(async () => $"Freed {Size(await _chat.DeleteFilesAsync(plan))}.");
        }
    }

    private async void OnClearChat(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row)
        {
            return;
        }

        if (Confirm($"Clear all messages in “{row.Title}”?",
                $"{Messages(row.Conversation.Messages.Count)} and their pictures and files are removed from this computer. "
                + "The others keep their copy. This can't be undone."))
        {
            await RunAsync(async () => $"Cleared “{row.Title}” and freed {Size(await _chat.ClearHistoryAndFilesAsync(row.Conversation))}.");
        }
    }

    private void OnAutoEmptyChanged(object sender, RoutedEventArgs e)
    {
        if (_ready)
        {
            _chat.Settings.AutoEmptyTrash = AutoEmptyBox.IsChecked == true;
            _chat.SaveSettings();
        }
    }

    private void OnAutoDeleteChanged(object sender, RoutedEventArgs e) => SaveAutoDelete();

    private void OnAgeChanged(object sender, SelectionChangedEventArgs e) => SaveAutoDelete();

    private void SaveAutoDelete()
    {
        var days = AutoDeleteBox.IsChecked == true ? SelectedAge.Days : 0;
        if (_ready && days != _chat.Settings.DeleteFilesAfterDays)
        {
            _chat.Settings.DeleteFilesAfterDays = days;
            _chat.SaveSettings();
        }
    }

    private static ChatStorageRow? RowOf(object sender) => (sender as FrameworkElement)?.DataContext as ChatStorageRow;

    private bool Confirm(string title, string text) =>
        DialogGuard.Run(() => MessageBox.Show(this, text, title, MessageBoxButton.OKCancel, MessageBoxImage.Question)) == MessageBoxResult.OK;

    private void OnScrollerMouseWheel(object sender, MouseWheelEventArgs e) => WindowSizing.ScrollOnWheel(sender, e);

    private void OnOpenDataFolder(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(AppPaths.DataDir) { UseShellExecute = true });
}
