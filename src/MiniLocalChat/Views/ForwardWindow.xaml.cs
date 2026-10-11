using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MiniLocalChat.Infrastructure;
using MiniLocalChat.Models;

namespace MiniLocalChat.Views;

/// <summary>Picks the chats a message is forwarded to.</summary>
public partial class ForwardWindow : Window
{
    private readonly ObservableCollection<Target> _targets = [];
    private readonly ListCollectionView _view;

    private ForwardWindow(ChatMessage message)
    {
        InitializeComponent();
        PreviewText.Text = message.Preview;
        foreach (var conversation in App.Chat.Conversations
                     .OrderBy(c => c.SectionOrder)
                     .ThenByDescending(c => c.LastActivityUtc)
                     .ThenBy(c => c.Title, StringComparer.CurrentCultureIgnoreCase))
        {
            var target = new Target(conversation);
            target.PropertyChanged += (_, _) => UpdateState();
            _targets.Add(target);
        }

        _view = new ListCollectionView(_targets)
        {
            Filter = o => o is Target t && (t.IsSelected || t.Conversation.Matches(SearchBox.Text.Trim())),
        };
        TargetList.ItemsSource = _view;
        Loaded += (_, _) => SearchBox.Focus();
    }

    /// <summary>Returns the chats to forward to (empty if cancelled).</summary>
    public static List<Conversation> Pick(Window? owner, ChatMessage message)
    {
        var window = new ForwardWindow(message);
        if (owner is { IsVisible: true })
        {
            window.Owner = owner;
            window.Topmost = owner.Topmost;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return DialogGuard.Run(() => window.ShowDialog()) == true
            ? window._targets.Where(t => t.IsSelected).Select(t => t.Conversation).ToList()
            : [];
    }

    private void UpdateState()
    {
        var count = _targets.Count(t => t.IsSelected);
        SendButton.IsEnabled = count > 0;
        SendButton.Content = count > 1 ? $"Forward ({count})" : "Forward";
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => _view?.Refresh();

    private void OnSend(object sender, RoutedEventArgs e) => DialogResult = true;

    private sealed class Target(Conversation conversation) : ObservableObject
    {
        private bool _isSelected;

        public Conversation Conversation { get; } = conversation;

        public bool IsSelected
        {
            get => _isSelected;
            set => Set(ref _isSelected, value);
        }
    }
}
