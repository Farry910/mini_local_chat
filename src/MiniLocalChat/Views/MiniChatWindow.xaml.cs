using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MiniLocalChat.Infrastructure;
using MiniLocalChat.Models;

namespace MiniLocalChat.Views;

/// <summary>
/// Hover panel next to the LED: every chat and person in sections (Favorites, Online, Offline), with new
/// messages on top of each, and a search box. Pick one to read and reply without opening the full app.
/// </summary>
public partial class MiniChatWindow : Window
{
    private readonly ListCollectionView _chats;
    private Conversation? _open;

    public MiniChatWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => Win32.MakeToolWindow(this);

        _chats = ChatListView.Create(App.Chat.Conversations, () => SearchBox.Text);
        ((INotifyCollectionChanged)_chats).CollectionChanged += (_, _) => UpdateEmptyState();
        ChatList.ItemsSource = _chats;

        // The open chat was deleted or left (e.g. from its menu): go back to the list.
        App.Chat.Conversations.CollectionChanged += (_, _) =>
        {
            if (_open is not null && !App.Chat.Conversations.Contains(_open))
            {
                ShowList();
            }
        };
        UpdateEmptyState();
    }

    public event Action<Conversation?>? OpenAppRequested;

    public bool IsShowing(Conversation conversation) => IsVisible && ReferenceEquals(_open, conversation);

    public void ShowList()
    {
        _open = null;
        ChatPane.Conversation = null;
        ChatPane.Visibility = Visibility.Collapsed;
        ListPanel.Visibility = Visibility.Visible;
        BackButton.Visibility = Visibility.Collapsed;
        NewMessageButton.Visibility = Visibility.Visible;
        FindButton.Visibility = Visibility.Collapsed;
        BindingOperations.ClearBinding(HeaderTitle, TextBlock.TextProperty);
        BindingOperations.ClearBinding(HeaderStatus, TextBlock.TextProperty);
        HeaderTitle.Text = "Chats";
        HeaderStatus.Visibility = Visibility.Collapsed;
        RippleBlock.Conversation = null;
        RippleBlock.Visibility = Visibility.Collapsed;
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
        ListPanel.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Collapsed;
        BackButton.Visibility = Visibility.Visible;
        NewMessageButton.Visibility = Visibility.Collapsed;
        FindButton.Visibility = Visibility.Visible;
        HeaderTitle.SetBinding(TextBlock.TextProperty, new Binding(nameof(Conversation.Title)) { Source = conversation });
        HeaderStatus.SetBinding(TextBlock.TextProperty, conversation.IsGroup
            ? new Binding(nameof(Conversation.Subtitle)) { Source = conversation }
            : new Binding("Peer.IsOnline") { Source = conversation, Converter = (IValueConverter)FindResource("PresenceText") });
        HeaderStatus.Visibility = Visibility.Visible;
        RippleBlock.Conversation = conversation;
        RippleBlock.Visibility = Visibility.Visible;
        if (IsActive)
        {
            ChatPane.FocusInput();
        }
    }

    private void UpdateEmptyState() =>
        EmptyState.Visibility = _open is null && App.Chat.Conversations.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => _chats?.Refresh();

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

    private void OnFindInChat(object sender, RoutedEventArgs e)
    {
        Activate();
        ChatPane.ToggleSearch();
    }

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
