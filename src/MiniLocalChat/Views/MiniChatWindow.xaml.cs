using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MiniLocalChat.Infrastructure;
using MiniLocalChat.Models;

namespace MiniLocalChat.Views;

/// <summary>
/// Hover panel next to the LED: every chat and person (offline ones too, messages to them are queued),
/// with new messages on top. Pick one to read and reply without opening the full app.
/// </summary>
public partial class MiniChatWindow : Window
{
    private readonly ListCollectionView _chats;
    private Conversation? _open;

    public MiniChatWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => Win32.MakeToolWindow(this);

        _chats = new ListCollectionView(App.Chat.Conversations) { IsLiveSorting = true };
        _chats.SortDescriptions.Add(new SortDescription(nameof(Conversation.HasUnread), ListSortDirection.Descending));
        _chats.SortDescriptions.Add(new SortDescription(nameof(Conversation.LastActivityUtc), ListSortDirection.Descending));
        _chats.SortDescriptions.Add(new SortDescription(nameof(Conversation.Title), ListSortDirection.Ascending));
        _chats.LiveSortingProperties.Add(nameof(Conversation.HasUnread));
        _chats.LiveSortingProperties.Add(nameof(Conversation.LastActivityUtc));
        _chats.LiveSortingProperties.Add(nameof(Conversation.Title));
        ((INotifyCollectionChanged)_chats).CollectionChanged += (_, _) => UpdateEmptyState();
        ChatList.ItemsSource = _chats;
        UpdateEmptyState();
    }

    public event Action<Conversation?>? OpenAppRequested;

    public bool IsShowing(Conversation conversation) => IsVisible && ReferenceEquals(_open, conversation);

    public void ShowList()
    {
        _open = null;
        ChatPane.Conversation = null;
        ChatPane.Visibility = Visibility.Collapsed;
        ChatList.Visibility = Visibility.Visible;
        BackButton.Visibility = Visibility.Collapsed;
        BindingOperations.ClearBinding(HeaderTitle, TextBlock.TextProperty);
        BindingOperations.ClearBinding(HeaderStatus, TextBlock.TextProperty);
        HeaderTitle.Text = "Chats";
        HeaderStatus.Visibility = Visibility.Collapsed;
        if (_chats.Cast<object>().FirstOrDefault() is { } first)
        {
            ChatList.ScrollIntoView(first);
        }

        UpdateEmptyState();
    }

    public void OpenConversation(Conversation conversation)
    {
        _open = conversation;
        App.Chat.MarkRead(conversation);

        ChatPane.Conversation = conversation;
        ChatPane.Visibility = Visibility.Visible;
        ChatList.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Collapsed;
        BackButton.Visibility = Visibility.Visible;
        HeaderTitle.SetBinding(TextBlock.TextProperty, new Binding(nameof(Conversation.Title)) { Source = conversation });
        HeaderStatus.SetBinding(TextBlock.TextProperty, conversation.IsGroup
            ? new Binding(nameof(Conversation.Subtitle)) { Source = conversation }
            : new Binding("Peer.IsOnline") { Source = conversation, Converter = (IValueConverter)FindResource("PresenceText") });
        HeaderStatus.Visibility = Visibility.Visible;
        if (IsActive)
        {
            ChatPane.FocusInput();
        }
    }

    private void UpdateEmptyState() =>
        EmptyState.Visibility = _open is null && _chats.IsEmpty ? Visibility.Visible : Visibility.Collapsed;

    private void OnChatSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ChatList.SelectedItem is Conversation conversation)
        {
            ChatList.SelectedItem = null;
            Activate();
            OpenConversation(conversation);
        }
    }

    private void OnBack(object sender, RoutedEventArgs e) => ShowList();

    private void OnOpenApp(object sender, RoutedEventArgs e) => OpenAppRequested?.Invoke(_open);

    private void OnNewMessage(object sender, RoutedEventArgs e)
    {
        if (NewChatWindow.Pick(this) is { } conversation)
        {
            Activate();
            OpenConversation(conversation);
        }
    }
}
