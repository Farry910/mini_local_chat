using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
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
        _chat.RippleSettingChanged += UpdateRippleButton;
        UpdateRippleButton();
        StateChanged += (_, _) => MarkSelectedRead();
        InputBindings.Add(new KeyBinding(new RelayCommand(StartNewChat), Key.N, ModifierKeys.Control));
    }

    private Conversation? SelectedConversation => ConversationList.SelectedItem as Conversation;

    public bool IsShowing(Conversation conversation) =>
        IsVisible && IsActive && WindowState != WindowState.Minimized && ChatPane.Visibility == Visibility.Visible
        && ReferenceEquals(SelectedConversation, conversation);

    public void OpenConversation(Conversation conversation)
    {
        ShowPage(alarms: false);
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
        if (SelectedConversation is { } conversation && IsActive && WindowState != WindowState.Minimized
            && ChatPane.Visibility == Visibility.Visible)
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

    private void OnToggleRipples(object sender, RoutedEventArgs e) => _chat.SetRippleEnabled(!_chat.Settings.RippleEnabled);

    private void UpdateRippleButton()
    {
        var on = _chat.Settings.RippleEnabled;
        RippleOffMark.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        RippleRailLabel.Text = on ? "Ripples" : "Ripples off";
        RippleRailButton.Foreground = on ? (Brush)FindResource("SubtleTextBrush") : Brushes.Firebrick;
        RippleRailButton.ToolTip = on
            ? "Screen ripples are on. Click to turn them off for everyone."
            : "Screen ripples are off. Click to turn them back on.";
    }

    private void OnShowChat(object sender, RoutedEventArgs e) => ShowPage(alarms: false);

    private void OnShowAlarms(object sender, RoutedEventArgs e) => ShowPage(alarms: true);

    /// <summary>Switches between the chat (list + conversation) and the Alarms page.</summary>
    public void ShowPage(bool alarms)
    {
        AlarmsPage.Visibility = alarms ? Visibility.Visible : Visibility.Collapsed;
        ChatListPane.Visibility = alarms ? Visibility.Collapsed : Visibility.Visible;
        ChatPane.Visibility = alarms ? Visibility.Collapsed : Visibility.Visible;
        ChatRailButton.Tag = alarms ? null : "Active";
        AlarmsRailButton.Tag = alarms ? "Active" : null;
        if (!alarms)
        {
            MarkSelectedRead();
        }
    }

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
