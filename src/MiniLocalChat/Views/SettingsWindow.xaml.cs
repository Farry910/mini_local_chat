using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MiniLocalChat.Infrastructure;
using MiniLocalChat.Models;
using MiniLocalChat.Network;
using MiniLocalChat.Services;

namespace MiniLocalChat.Views;

public partial class SettingsWindow : Window
{
    private readonly ChatService _chat = App.Chat;
    private readonly bool _welcome;
    private string? _avatarId;
    private string _hotkey = "";

    private SettingsWindow(bool welcome)
    {
        InitializeComponent();
        Icon = AppIcons.WindowIcon;
        Title = $"Settings – {AppInfo.Title}";
        _welcome = welcome;
        NicknameBox.MaxLength = ChatService.MaxNicknameLength;
        NicknameBox.Text = _chat.Settings.Nickname;
        NicknameBox.TextChanged += (_, _) => UpdateColorPreview();

        Color1List.ItemsSource = AvatarPalette.Choices;
        Color2List.ItemsSource = AvatarPalette.Choices;
        Color1List.SelectedItem = _chat.Me.EffectiveColor1;
        Color2List.SelectedItem = _chat.Me.EffectiveColor2;
        _avatarId = _chat.Me.AvatarId;
        PreviewRippleButton.Visibility = App.Current.Ripples is null ? Visibility.Collapsed : Visibility.Visible;
        UpdateColorPreview();

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
        RippleGapBox.Text = _chat.Settings.RippleMinGapSeconds.ToString();
        SkipFullScreenBox.IsChecked = _chat.Settings.RippleSkipFullScreen;
        ReminderBox.IsChecked = _chat.Settings.RippleReminder;
        AwayBox.Text = _chat.Settings.AwayAfterMinutes.ToString();
        _hotkey = _chat.Settings.Hotkey;
        HotkeyBox.Text = string.IsNullOrEmpty(_hotkey) ? "None" : _hotkey;

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
            if (!int.TryParse(RippleGapBox.Text.Trim(), out var gap)
                || gap is < RippleController.MinGapSeconds or > RippleController.MaxGapSeconds)
            {
                RippleError.Text = $"Enter a number of seconds from {RippleController.MinGapSeconds} to {RippleController.MaxGapSeconds}.";
                RippleError.Visibility = Visibility.Visible;
                RippleGapBox.Focus();
                return;
            }

            if (!int.TryParse(AwayBox.Text.Trim(), out var awayMinutes) || awayMinutes is < 0 or > 240)
            {
                AwayError.Text = "Enter a number of minutes from 0 to 240.";
                AwayError.Visibility = Visibility.Visible;
                AwayBox.Focus();
                return;
            }

            if (_hotkey != _chat.Settings.Hotkey && !App.Current.ApplyHotkey(_hotkey, out var hotkeyError))
            {
                HotkeyHint.Text = hotkeyError;
                HotkeyHint.Foreground = Brushes.Firebrick;
                HotkeyBox.Focus();
                return;
            }

            _chat.Settings.Hotkey = _hotkey;
            _chat.SetRippleEnabled(RippleBox.IsChecked == true);
            _chat.Settings.RippleSkipFullScreen = SkipFullScreenBox.IsChecked == true;
            _chat.Settings.RippleReminder = ReminderBox.IsChecked == true;
            _chat.Settings.AwayAfterMinutes = awayMinutes;
            _chat.Settings.RippleMinGapSeconds = gap;
            _chat.Settings.LedSide = LedLeft.IsChecked == true ? nameof(LedSide.Left) : nameof(LedSide.Right);
            var autoStart = AutoStartBox.IsChecked == true;
            if (autoStart != _chat.Settings.StartWithWindows)
            {
                AutoStart.Apply(autoStart);
                _chat.Settings.StartWithWindows = autoStart;
            }
        }

        _chat.ApplyProfile(nickname, (IpCombo.SelectedItem as ComboBoxItem)?.Tag as string, SelectedColor1, SelectedColor2, _avatarId);
        DialogResult = true;
    }

    private string SelectedColor1 => Color1List.SelectedItem as string ?? _chat.Me.EffectiveColor1;

    private string SelectedColor2 => Color2List.SelectedItem as string ?? _chat.Me.EffectiveColor2;

    private void OnColorChanged(object sender, SelectionChangedEventArgs e) => UpdateColorPreview();

    private void UpdateColorPreview()
    {
        if (!IsInitialized)
        {
            return;
        }

        ColorPreview.Fill = AvatarPalette.ToBrush(SelectedColor1);
        ColorPreview.Image = AvatarStore.ImageFor(_avatarId);
        RemovePictureLink.Visibility = _avatarId is null ? Visibility.Collapsed : Visibility.Visible;
        var name = ChatService.CleanNickname(NicknameBox.Text);
        ColorPreview.Text = AvatarPalette.Initials(name.Length > 0 ? name : "?");
    }

    private void OnChoosePicture(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose your picture",
            Filter = "Pictures|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp;*.tif;*.tiff|All files|*.*",
        };
        if (DialogGuard.Run(() => dialog.ShowDialog(this)) != true)
        {
            return;
        }

        try
        {
            _avatarId = AvatarStore.Import(dialog.FileName);
            UpdateColorPreview();
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or FileFormatException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "That file couldn't be opened as a picture.", "Choose picture", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnRemovePicture(object sender, RoutedEventArgs e)
    {
        _avatarId = null;
        UpdateColorPreview();
    }

    private void OnPreviewRipple(object sender, RoutedEventArgs e) =>
        App.Current.Ripples?.Play(SelectedColor1, SelectedColor2);

    private void OnAllowFirewall(object sender, RoutedEventArgs e)
    {
        var ok = FirewallSetup.AllowThisApp();
        FirewallResult.Text = ok
            ? "✓ Allowed. Other computers can now reach this one."
            : "Not changed – the admin prompt was declined or Windows refused.";
        FirewallResult.Foreground = ok ? (Brush)FindResource("AccentBrush") : Brushes.Firebrick;
        FirewallResult.Visibility = Visibility.Visible;
    }

    /// <summary>Records the pressed combination, e.g. Ctrl+Alt+M (needs Ctrl, Alt or Win).</summary>
    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.Tab)
        {
            return;
        }

        var text = GlobalHotkey.Format(Keyboard.Modifiers, key);
        if (!GlobalHotkey.TryParse(text, out _, out _))
        {
            HotkeyHint.Text = "Use Ctrl, Alt or Win together with a key, e.g. Ctrl+Alt+M.";
            HotkeyHint.Foreground = Brushes.Firebrick;
            return;
        }

        _hotkey = text;
        HotkeyBox.Text = text;
        HotkeyHint.Text = "Press Save to use it.";
        HotkeyHint.Foreground = (Brush)FindResource("SubtleTextBrush");
    }

    private void OnClearHotkey(object sender, RoutedEventArgs e)
    {
        _hotkey = "";
        HotkeyBox.Text = "None";
    }

    private void OnOpenEmojiCredit(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(EmojiStickers.CreditUrl) { UseShellExecute = true });

    private void OnOpenDataFolder(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(AppPaths.DataDir) { UseShellExecute = true });
}
