using System.Windows;
using System.Windows.Input;
using MiniLocalChat.Infrastructure;
using MiniLocalChat.Models;

namespace MiniLocalChat.Views;

/// <summary>
/// Chat-level actions (favorite, rename, add people, leave, delete), shared by the chat lists' right-click
/// menus and the chat headers of both the full window and the floating panel. Use from XAML as
/// <c>Command="{x:Static v:ChatActions.LeaveGroupCommand}" CommandParameter="{Binding}"</c>.
/// </summary>
public static class ChatActions
{
    public static ICommand ToggleFavoriteCommand { get; } = new ConversationCommand(c => App.Chat.ToggleFavorite(c));

    public static ICommand RenameGroupCommand { get; } = new ConversationCommand(RenameGroup, c => c.IsGroup);

    public static ICommand AddPeopleCommand { get; } = new ConversationCommand(AddPeople, c => c.IsGroup);

    public static ICommand LeaveGroupCommand { get; } = new ConversationCommand(LeaveGroup, c => c.IsGroup);

    public static ICommand DeleteChatCommand { get; } = new ConversationCommand(DeleteChat, c => c.IsGroup);

    public static ICommand ClearHistoryCommand { get; } = new ConversationCommand(ClearHistory, c => c.Messages.Count > 0);

    /// <summary>The window dialogs should sit on: the active one (main window or floating panel).</summary>
    private static Window? ActiveWindow =>
        Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible);

    public static void RenameGroup(Conversation conversation)
    {
        var name = TextPromptWindow.Ask(ActiveWindow, "Rename group", "Group name", conversation.GroupName ?? "", 60);
        if (name is not null)
        {
            App.Chat.RenameGroup(conversation, name);
        }
    }

    public static void AddPeople(Conversation conversation)
    {
        var ips = NewChatWindow.PickPeople(ActiveWindow, $"Add people to {conversation.Title}", conversation.MemberIps);
        if (ips.Count > 0)
        {
            App.Chat.AddPeople(conversation, ips);
        }
    }

    public static void LeaveGroup(Conversation conversation)
    {
        if (Confirm($"Leave “{conversation.Title}”?",
                "You'll stop getting its messages and it disappears from your list. Others see that you left."))
        {
            App.Chat.LeaveGroup(conversation);
        }
    }

    public static void DeleteChat(Conversation conversation)
    {
        if (Confirm($"Delete “{conversation.Title}” from your list?",
                "Its history is removed from this computer only. It comes back if someone writes in it again. To stop getting its messages, leave the group instead."))
        {
            App.Chat.DeleteChat(conversation);
        }
    }

    public static void ClearHistory(Conversation conversation)
    {
        if (Confirm($"Clear all messages in “{conversation.Title}”?",
                "Every message, picture and reaction in this chat is removed from this computer. The others keep their copy. Received files stay in your Files folder."))
        {
            App.Chat.ClearHistory(conversation);
        }
    }

    private static bool Confirm(string title, string text) =>
        DialogGuard.Run(() => ActiveWindow is { } owner
            ? MessageBox.Show(owner, text, title, MessageBoxButton.OKCancel, MessageBoxImage.Question)
            : MessageBox.Show(text, title, MessageBoxButton.OKCancel, MessageBoxImage.Question)) == MessageBoxResult.OK;

    private sealed class ConversationCommand(Action<Conversation> execute, Func<Conversation, bool>? canExecute = null) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter) =>
            parameter is Conversation c && (canExecute?.Invoke(c) ?? true);

        public void Execute(object? parameter)
        {
            if (parameter is Conversation c)
            {
                execute(c);
            }
        }
    }
}
