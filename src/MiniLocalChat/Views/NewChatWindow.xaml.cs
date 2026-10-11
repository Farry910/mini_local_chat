using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using MiniLocalChat.Infrastructure;
using MiniLocalChat.Models;

namespace MiniLocalChat.Views;

/// <summary>
/// Pick one person (1:1 chat) or several (group chat, Teams-style). Also used to pick people to add to a group.
/// </summary>
public partial class NewChatWindow : Window
{
    private readonly ObservableCollection<PersonOption> _people = [];
    private readonly ListCollectionView _view;
    private readonly bool _addPeopleMode;

    private NewChatWindow(bool addPeopleMode, IEnumerable<string>? excludeIps = null)
    {
        InitializeComponent();
        _addPeopleMode = addPeopleMode;
        var exclude = excludeIps?.ToHashSet() ?? [];
        foreach (var contact in App.Chat.Contacts
                     .Where(c => !exclude.Contains(c.Ip))
                     .OrderByDescending(c => c.IsOnline)
                     .ThenBy(c => c.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            AddOption(contact, selected: false);
        }

        _view = new ListCollectionView(_people)
        {
            Filter = o => o is PersonOption p && (p.IsSelected || Matches(p.Contact, SearchBox.Text.Trim())),
        };
        PeopleList.ItemsSource = _view;
        Loaded += (_, _) => SearchBox.Focus();
        UpdateState();
    }

    public Conversation? Result { get; private set; }

    private List<string> SelectedIps => _people.Where(p => p.IsSelected).Select(p => p.Contact.Ip).ToList();

    /// <summary>Shows the dialog and returns the (new or existing) conversation, or null if cancelled.</summary>
    public static Conversation? Pick(Window? owner)
    {
        var window = new NewChatWindow(addPeopleMode: false);
        return Show(window, owner) ? window.Result : null;
    }

    /// <summary>Picks people to add to a group. Returns their IPs (empty if cancelled).</summary>
    public static List<string> PickPeople(Window? owner, string title, IEnumerable<string> alreadyIn)
    {
        var window = new NewChatWindow(addPeopleMode: true, alreadyIn)
        {
            Title = "Add people",
        };
        window.Heading.Text = title;
        window.Description.Text = "Everyone in the chat will see who you added. New people don't see earlier messages.";
        window.UpdateState();
        return Show(window, owner) ? window.SelectedIps : [];
    }

    private static bool Show(NewChatWindow window, Window? owner)
    {
        if (owner is { IsVisible: true })
        {
            window.Owner = owner;
            window.Topmost = owner.Topmost;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return DialogGuard.Run(() => window.ShowDialog()) == true;
    }

    private static bool Matches(Contact contact, string search) =>
        search.Length == 0
        || contact.DisplayName.Contains(search, StringComparison.CurrentCultureIgnoreCase)
        || contact.Ip.Contains(search);

    private void AddOption(Contact contact, bool selected)
    {
        var option = new PersonOption(contact) { IsSelected = selected };
        option.PropertyChanged += (_, _) => UpdateState();
        _people.Add(option);
    }

    private void UpdateState()
    {
        var count = _people.Count(p => p.IsSelected);
        StartButton.IsEnabled = count > 0;
        if (_addPeopleMode)
        {
            StartButton.Content = count > 0 ? $"Add ({count})" : "Add";
            GroupPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            StartButton.Content = count > 1 ? $"Start group chat ({count + 1})" : "Start chat";
            GroupPanel.Visibility = count > 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        NoPeopleText.Visibility = _people.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => _view?.Refresh();

    private void OnIpKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            AddByIp();
        }
    }

    private void OnAddByIp(object sender, RoutedEventArgs e) => AddByIp();

    private void AddByIp()
    {
        if (string.IsNullOrWhiteSpace(IpBox.Text))
        {
            return;
        }

        var contact = App.Chat.AddContactByIp(IpBox.Text);
        if (contact is null)
        {
            IpError.Text = "Enter a valid IPv4 address of another computer, e.g. 192.168.1.25";
            IpError.Visibility = Visibility.Visible;
            return;
        }

        IpError.Visibility = Visibility.Collapsed;
        IpBox.Clear();
        if (_people.FirstOrDefault(p => p.Contact == contact) is { } existing)
        {
            existing.IsSelected = true;
        }
        else
        {
            AddOption(contact, selected: true);
        }

        _view.Refresh();
        UpdateState();
    }

    private void OnStart(object sender, RoutedEventArgs e)
    {
        var ips = SelectedIps;
        if (ips.Count == 0)
        {
            return;
        }

        if (!_addPeopleMode)
        {
            Result = ips.Count == 1
                ? App.Chat.GetOrCreateDirect(ips[0])
                : App.Chat.CreateGroup(ips, GroupNameBox.Text);
        }

        DialogResult = true;
    }

    private sealed class PersonOption(Contact contact) : ObservableObject
    {
        private bool _isSelected;

        public Contact Contact { get; } = contact;

        public string Status => Contact.Ip + (Contact.IsOnline ? " · online" : " · offline");

        public bool IsSelected
        {
            get => _isSelected;
            set => Set(ref _isSelected, value);
        }
    }
}
