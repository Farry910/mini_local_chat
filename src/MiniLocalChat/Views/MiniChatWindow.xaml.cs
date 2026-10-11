using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MiniLocalChat.Infrastructure;
using MiniLocalChat.Models;

namespace MiniLocalChat.Views;

/// <summary>
/// Hover panel next to the LED: lists only the chats with unread messages (1:1 and group);
/// pick one to read and reply without opening the full app.
/// </summary>
public partial class MiniChatWindow : Window
{
    private readonly ListCollectionView _unread;
    private Conversation? _open;

    public MiniChatWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => Win32.MakeToolWindow(this);

        _unread = new ListCollectionView(App.Chat.Conversations)
        {
            // The open chat stays listed after it's read, until the user goes back.
            Filter = o => o is Conversation c && (c.UnreadCount > 0 || ReferenceEquals(c, _open)),
            IsLiveFiltering = true,
            IsLiveSorting = true,
        };
        _unread.LiveFilteringProperties.Add(nameof(Conversation.UnreadCount));
        _unread.LiveSortingProperties.Add(nameof(Conversation.LastActivityUtc));
        _unread.SortDescriptions.Add(new SortDescription(nameof(Conversation.LastActivityUtc), ListSortDirection.Descending));
        ((INotifyCollectionChanged)_unread).CollectionChanged += (_, _) => UpdateEmptyState();
        UnreadList.ItemsSource = _unread;
        UpdateEmptyState();
    }

    public event Action<Conversation?>? OpenAppRequested;

    public bool IsShowing(Conversation conversation) => IsVisible && ReferenceEquals(_open, conversation);

    public void ShowList()
    {
        _open = null;
        ChatPane.Conversation = null;
        ChatPane.Visibility = Visibility.Collapsed;
        UnreadList.Visibility = Visibility.Visible;
        BackButton.Visibility = Visibility.Collapsed;
        BindingOperations.ClearBinding(HeaderTitle, TextBlock.TextProperty);
        HeaderTitle.Text = "New messages";
        _unread.Refresh();
        UpdateEmptyState();
    }

    public void OpenConversation(Conversation conversation)
    {
        _open = conversation;
        App.Chat.MarkRead(conversation);
        _unread.Refresh();

        ChatPane.Conversation = conversation;
        ChatPane.Visibility = Visibility.Visible;
        UnreadList.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Collapsed;
        BackButton.Visibility = Visibility.Visible;
        HeaderTitle.SetBinding(TextBlock.TextProperty, new Binding(nameof(Conversation.Title)) { Source = conversation });
        if (IsActive)
        {
            ChatPane.FocusInput();
        }
    }

    private void UpdateEmptyState() =>
        EmptyState.Visibility = _open is null && _unread.IsEmpty ? Visibility.Visible : Visibility.Collapsed;

    private void OnUnreadSelected(object sender, SelectionChangedEventArgs e)
    {
        if (UnreadList.SelectedItem is Conversation conversation)
        {
            UnreadList.SelectedItem = null;
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
