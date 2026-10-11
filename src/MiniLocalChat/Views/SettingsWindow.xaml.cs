using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MiniLocalChat.Infrastructure;
using MiniLocalChat.Network;
using MiniLocalChat.Services;

namespace MiniLocalChat.Views;

public partial class SettingsWindow : Window
{
    private readonly ChatService _chat = App.Chat;
    private readonly bool _welcome;

    private SettingsWindow(bool welcome)
    {
        InitializeComponent();
        Icon = AppIcons.WindowIcon;
        Title = $"Settings – {AppInfo.Title}";
        _welcome = welcome;
        NicknameBox.MaxLength = ChatService.MaxNicknameLength;
        NicknameBox.Text = _chat.Settings.Nickname;

        var detected = NetUtil.DetectPrimaryIPv4();
        IpCombo.Items.Add(new ComboBoxItem { Content = $"Automatic ({detected ?? "not found"})", Tag = null });
        foreach (var ip in NetUtil.GetSelectableIPv4())
        {
            IpCombo.Items.Add(new ComboBoxItem { Content = ip, Tag = ip });
        }

        IpCombo.SelectedItem = IpCombo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == _chat.Settings.MyIp)
                               ?? IpCombo.Items[0];
        if (_chat.BindAddress is not null)
        {
            IpCombo.IsEnabled = false;
            IpNote.Text = $"Fixed to {_chat.BindAddress} by the --bind command-line option.";
        }

        LedLeft.IsChecked = _chat.Settings.LedSide == nameof(LedSide.Left);
        LedRight.IsChecked = !LedLeft.IsChecked;
        AutoStartBox.IsChecked = _chat.Settings.StartWithWindows;
        RippleBox.IsChecked = _chat.Settings.RippleEnabled;
        RippleIntervalBox.Text = _chat.Settings.RippleIntervalSeconds.ToString();

        if (welcome)
        {
            Title = $"Welcome to {AppInfo.Title}";
            Heading.Text = "Welcome 👋";
            Intro.Visibility = Visibility.Visible;
            MoreSettings.Visibility = Visibility.Collapsed;
            SaveButton.Content = "Get started";
            CancelButton.Content = "Quit";
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        Loaded += (_, _) => NicknameBox.Focus();
    }

    /// <summary>First run: asks for the nickname. Returns false if the user quit.</summary>
    public static bool ShowWelcome() => new SettingsWindow(welcome: true).ShowDialog() == true;

    public static bool ShowFor(Window? owner)
    {
        var window = new SettingsWindow(welcome: false);
        if (owner is { IsVisible: true })
        {
            window.Owner = owner;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            window.Topmost = true;
        }

        return DialogGuard.Run(() => window.ShowDialog()) == true;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var nickname = ChatService.CleanNickname(NicknameBox.Text);
        if (nickname.Length == 0)
        {
            NicknameError.Visibility = Visibility.Visible;
            NicknameBox.Focus();
            return;
        }

        if (!_welcome)
        {
            if (!int.TryParse(RippleIntervalBox.Text.Trim(), out var interval)
                || interval is < RippleController.MinIntervalSeconds or > RippleController.MaxIntervalSeconds)
            {
                RippleError.Text = $"Enter a number of seconds from {RippleController.MinIntervalSeconds} to {RippleController.MaxIntervalSeconds}.";
                RippleError.Visibility = Visibility.Visible;
                RippleIntervalBox.Focus();
                return;
            }

            _chat.Settings.RippleEnabled = RippleBox.IsChecked == true;
            _chat.Settings.RippleIntervalSeconds = interval;
            _chat.Settings.LedSide = LedLeft.IsChecked == true ? nameof(LedSide.Left) : nameof(LedSide.Right);
            var autoStart = AutoStartBox.IsChecked == true;
            if (autoStart != _chat.Settings.StartWithWindows)
            {
                AutoStart.Apply(autoStart);
                _chat.Settings.StartWithWindows = autoStart;
            }
        }

        _chat.ApplyProfile(nickname, (IpCombo.SelectedItem as ComboBoxItem)?.Tag as string);
        DialogResult = true;
    }

    private void OnAllowFirewall(object sender, RoutedEventArgs e)
    {
        var ok = FirewallSetup.AllowThisApp();
        FirewallResult.Text = ok
            ? "✓ Allowed. Other computers can now reach this one."
            : "Not changed – the admin prompt was declined or Windows refused.";
        FirewallResult.Foreground = ok ? (Brush)FindResource("AccentBrush") : Brushes.Firebrick;
        FirewallResult.Visibility = Visibility.Visible;
    }

    private void OnOpenDataFolder(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(AppPaths.DataDir) { UseShellExecute = true });
}
