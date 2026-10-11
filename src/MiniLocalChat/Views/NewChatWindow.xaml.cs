using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using MiniLocalChat.Infrastructure;
using MiniLocalChat.Models;

namespace MiniLocalChat.Views;

/// <summary>Pick one person (1:1 chat) or several (group chat, Teams-style).</summary>
public partial class NewChatWindow : Window
{
    private readonly ObservableCollection<PersonOption> _people = [];

    private NewChatWindow()
    {
        InitializeComponent();
        foreach (var contact in App.Chat.Contacts.OrderByDescending(c => c.IsOnline).ThenBy(c => c.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            AddOption(contact, selected: false);
        }

        PeopleList.ItemsSource = _people;
        UpdateState();
    }

    public Conversation? Result { get; private set; }

    /// <summary>Shows the dialog and returns the (new or existing) conversation, or null if cancelled.</summary>
    public static Conversation? Pick(Window? owner)
    {
        var window = new NewChatWindow();
        if (owner is { IsVisible: true })
        {
            window.Owner = owner;
            window.Topmost = owner.Topmost;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return DialogGuard.Run(() => window.ShowDialog()) == true ? window.Result : null;
    }

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
        StartButton.Content = count > 1 ? $"Start group chat ({count + 1})" : "Start chat";
        GroupPanel.Visibility = count > 1 ? Visibility.Visible : Visibility.Collapsed;
        NoPeopleText.Visibility = _people.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

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

        UpdateState();
    }

    private void OnStart(object sender, RoutedEventArgs e)
    {
        var ips = _people.Where(p => p.IsSelected).Select(p => p.Contact.Ip).ToList();
        if (ips.Count == 0)
        {
            return;
        }

        Result = ips.Count == 1
            ? App.Chat.GetOrCreateDirect(ips[0])
            : App.Chat.CreateGroup(ips, GroupNameBox.Text);
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
