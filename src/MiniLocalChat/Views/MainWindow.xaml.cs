using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using MiniLocalChat.Infrastructure;
using MiniLocalChat.Models;
using MiniLocalChat.Services;

namespace MiniLocalChat.Views;

/// <summary>Full Teams-style window. Closing it only hides it to the tray.</summary>
public partial class MainWindow : Window
{
    private readonly ChatService _chat = App.Chat;
    private readonly ListCollectionView _conversations;

    public MainWindow()
    {
        InitializeComponent();
        Icon = AppIcons.WindowIcon;
        Title = AppInfo.Title;
        DataContext = _chat;

        _conversations = ChatListView.Create(_chat.Conversations, () => SearchBox.Text);
        ConversationList.ItemsSource = _conversations;

        Activated += (_, _) => MarkSelectedRead();
        StateChanged += (_, _) => MarkSelectedRead();
        InputBindings.Add(new KeyBinding(new RelayCommand(StartNewChat), Key.N, ModifierKeys.Control));
    }

    private Conversation? SelectedConversation => ConversationList.SelectedItem as Conversation;

    public bool IsShowing(Conversation conversation) =>
        IsVisible && IsActive && WindowState != WindowState.Minimized && ReferenceEquals(SelectedConversation, conversation);

    public void OpenConversation(Conversation conversation)
    {
        if (!_conversations.PassesFilter(conversation))
        {
            SearchBox.Text = "";
        }

        ConversationList.SelectedItem = conversation;
        ConversationList.ScrollIntoView(conversation);
        ChatPane.FocusInput();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!App.IsExiting)
        {
            e.Cancel = true;
            App.Current.EnterTrayMode();
        }

        base.OnClosing(e);
    }

    private void MarkSelectedRead()
    {
        if (SelectedConversation is { } conversation && IsActive && WindowState != WindowState.Minimized)
        {
            _chat.MarkRead(conversation);
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ChatPane.Conversation = SelectedConversation;
        MarkSelectedRead();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => _conversations.Refresh();

    private void OnNewChat(object sender, RoutedEventArgs e) => StartNewChat();

    private void StartNewChat()
    {
        if (NewChatWindow.Pick(this) is { } conversation)
        {
            OpenConversation(conversation);
        }
    }

    private void OnSettings(object sender, RoutedEventArgs e) => App.Current.ShowSettings();

    private sealed class RelayCommand(Action execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => execute();
    }
}
