using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MiniLocalChat.Infrastructure;
using MiniLocalChat.Models;
using MiniLocalChat.Services;
using MiniLocalChat.Views;
using XamlAnimatedGif;

namespace MiniLocalChat.Controls;

/// <summary>
/// Messages + composer for one conversation, Teams-style: hover a message for reactions and
/// Reply / Forward / Copy / Edit / Delete; find in chat (Ctrl+F). Used by the main window and
/// (compact) by the floating mini panel.
/// </summary>
public partial class ChatView : UserControl
{
    public static readonly DependencyProperty ConversationProperty = DependencyProperty.Register(
        nameof(Conversation), typeof(Conversation), typeof(ChatView),
        new PropertyMetadata(null, (d, e) => ((ChatView)d).OnConversationChanged((Conversation?)e.OldValue, (Conversation?)e.NewValue)));

    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(
        nameof(IsCompact), typeof(bool), typeof(ChatView),
        new PropertyMetadata(false, (d, _) => ((ChatView)d).ApplyLayoutMode()));

    public static readonly DependencyProperty BubbleMaxWidthProperty = DependencyProperty.Register(
        nameof(BubbleMaxWidth), typeof(double), typeof(ChatView), new PropertyMetadata(560.0));

    public static readonly DependencyProperty ImageMaxWidthProperty = DependencyProperty.Register(
        nameof(ImageMaxWidth), typeof(double), typeof(ChatView), new PropertyMetadata(320.0));

    private static readonly string[] Emojis =
    [
        "😀", "😃", "😄", "😁", "😆", "😅", "😂", "🤣", "😊",
        "😇", "🙂", "😉", "😍", "😘", "😋", "😎", "🤔", "😐",
        "😴", "😮", "😢", "😭", "😡", "🥳", "👍", "👎", "👏",
        "🙏", "💪", "👌", "✌️", "👋", "🙌", "❤️", "💔", "🔥",
        "⭐", "🎉", "✅", "❌", "⚠️", "☕", "🍕", "🍺", "🎂",
        "📌", "📎", "💡", "⏰", "🚀", "💯", "👀", "🤝", "🙈",
    ];

    private readonly Dictionary<Conversation, string> _drafts = new();
    private readonly Dictionary<Conversation, List<PendingAttachment>> _attachmentDrafts = new();
    private readonly ObservableCollection<PendingAttachment> _attachments = [];
    private ListCollectionView? _messages;
    private ScrollViewer? _scroller;
    private ChatMessage? _replyTo;
    private ChatMessage? _editing;
    private string _draftBeforeEdit = "";
    private bool _watchingWindow;
    private bool _pinnedToBottom = true;
    private ListCollectionView? _savedGifs;
    private ListCollectionView? _stickers;
    private ListCollectionView? _tgsStickers;
    private bool _restoringDraft;
    private Storyboard? _typingDots;

    public ChatView()
    {
        InitializeComponent();
        EmojiGrid.ItemsSource = Emojis;
        CommandManager.AddPreviewExecutedHandler(Input, OnInputPreviewExecuted);
        CommandManager.AddPreviewCanExecuteHandler(Input, OnInputPreviewCanExecute);
        AttachmentTray.ItemsSource = _attachments;
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Find, (_, _) => OpenSearch()));

        // GIFs only play while they can be seen: not in the tray, not minimized.
        IsVisibleChanged += (_, _) => UpdateGifPlayback();
        Loaded += (_, _) =>
        {
            if (Window.GetWindow(this) is { } window && !_watchingWindow)
            {
                _watchingWindow = true;
                window.StateChanged += (_, _) => UpdateGifPlayback();
            }
        };
        ApplyLayoutMode();
    }

    public Conversation? Conversation
    {
        get => (Conversation?)GetValue(ConversationProperty);
        set => SetValue(ConversationProperty, value);
    }

    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    public double BubbleMaxWidth
    {
        get => (double)GetValue(BubbleMaxWidthProperty);
        set => SetValue(BubbleMaxWidthProperty, value);
    }

    public double ImageMaxWidth
    {
        get => (double)GetValue(ImageMaxWidthProperty);
        set => SetValue(ImageMaxWidthProperty, value);
    }

    private bool IsSearching => SearchBar.Visibility == Visibility.Visible;

    private ScrollViewer? Scroller
    {
        get
        {
            if (_scroller is null)
            {
                MessageList.ApplyTemplate();
                _scroller = MessageList.Template.FindName("Scroller", MessageList) as ScrollViewer;
                if (_scroller is not null)
                {
                    _scroller.ScrollChanged += OnScrollChanged;
                }
            }

            return _scroller;
        }
    }

    /// <summary>
    /// Keeps the chat pinned to the newest message while content above it grows, e.g. a GIF or picture
    /// that finishes loading after the chat already scrolled down. Scrolling up yourself unpins it.
    /// </summary>
    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scroller)
        {
            return;
        }

        if (e.ExtentHeightChange > 0 && _pinnedToBottom)
        {
            scroller.ScrollToEnd();
        }
        else if (e.ExtentHeightChange == 0)
        {
            _pinnedToBottom = scroller.VerticalOffset >= scroller.ScrollableHeight - 2;
        }
    }

    public void FocusInput()
    {
        if (Conversation is not null)
        {
            Input.Focus();
            Keyboard.Focus(Input);
        }
    }

    public void ToggleSearch()
    {
        if (IsSearching)
        {
            CloseSearch();
        }
        else
        {
            OpenSearch();
        }
    }

    private void OnConversationChanged(Conversation? oldValue, Conversation? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.Messages.CollectionChanged -= OnMessagesChanged;
            oldValue.PropertyChanged -= OnConversationPropertyChanged;
            App.Chat.NotifyTyping(oldValue, false);
            _drafts[oldValue] = _editing is null ? Input.Text : _draftBeforeEdit;
            _attachmentDrafts[oldValue] = [.. _attachments];
        }

        _attachments.Clear();
        if (newValue is not null && _attachmentDrafts.Remove(newValue, out var waiting))
        {
            foreach (var attachment in waiting)
            {
                _attachments.Add(attachment);
            }
        }

        UpdateAttachmentTray();

        _editing = null;
        _replyTo = null;
        UpdateComposerMode();
        CloseSearch(scroll: false);

        ChatRoot.DataContext = newValue;
        ChatRoot.Visibility = newValue is null ? Visibility.Collapsed : Visibility.Visible;
        EmptyState.Visibility = newValue is null && !IsCompact ? Visibility.Visible : Visibility.Collapsed;
        EmojiToggle.IsChecked = false;

        _messages = newValue is null ? null : new ListCollectionView(newValue.Messages) { Filter = MatchesSearch };
        MessageList.ItemsSource = _messages;

        if (newValue is not null)
        {
            newValue.Messages.CollectionChanged += OnMessagesChanged;
            newValue.PropertyChanged += OnConversationPropertyChanged;
            _restoringDraft = true;
            Input.Text = _drafts.GetValueOrDefault(newValue, "");
            _restoringDraft = false;
            Input.CaretIndex = Input.Text.Length;
            ScrollToEndSoon();
        }

        UpdateTypingLine();
    }

    // ---- "Alice is typing…" ---------------------------------------------------------------------

    private void OnConversationPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Models.Conversation.IsTyping))
        {
            UpdateTypingLine();
        }
    }

    private void UpdateTypingLine()
    {
        var typing = Conversation?.IsTyping == true;
        TypingLine.Visibility = typing ? Visibility.Visible : Visibility.Hidden;
        if (typing)
        {
            _typingDots ??= CreateTypingDots();
            _typingDots.Begin(this, isControllable: true);
        }
        else
        {
            _typingDots?.Stop(this);
        }
    }

    /// <summary>Three dots hopping one after another (at a low frame rate: it's tiny).</summary>
    private Storyboard CreateTypingDots()
    {
        var storyboard = new Storyboard { RepeatBehavior = RepeatBehavior.Forever, Duration = TimeSpan.FromSeconds(1.2) };
        Timeline.SetDesiredFrameRate(storyboard, 30);
        var dots = new[] { TypingDot1, TypingDot2, TypingDot3 };
        for (var i = 0; i < dots.Length; i++)
        {
            var hop = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromSeconds(0.15 * i) };
            hop.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            hop.KeyFrames.Add(new EasingDoubleKeyFrame(-3, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.25)), new SineEase()));
            hop.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.5)), new SineEase()));
            Storyboard.SetTarget(hop, dots[i]);
            Storyboard.SetTargetProperty(hop, new PropertyPath("RenderTransform.Y"));
            storyboard.Children.Add(hop);
        }

        return storyboard;
    }

    /// <summary>Tells the others I'm typing (or stopped) — only for real typing, not restoring a draft or editing.</summary>
    private void OnInputTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_restoringDraft && _editing is null && Conversation is { } conversation)
        {
            App.Chat.NotifyTyping(conversation, !string.IsNullOrWhiteSpace(Input.Text));
        }
    }

    private void ApplyLayoutMode()
    {
        var compact = IsCompact;
        Header.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        ComposerHint.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        MessageList.Padding = compact ? new Thickness(10, 2, 10, 8) : new Thickness(20, 4, 20, 12);
        Composer.Margin = compact ? new Thickness(8, 2, 8, 8) : new Thickness(20, 4, 20, 16);
        TypingLine.Margin = compact ? new Thickness(12, 0, 8, 0) : new Thickness(24, 0, 20, 0);
        TypingLine.Margin = compact ? new Thickness(12, 0, 8, 0) : new Thickness(24, 0, 20, 0);
        BubbleMaxWidth = compact ? 270 : 560;
        ImageMaxWidth = compact ? 230 : 320;
        GifPanel.Width = compact ? 316 : 340;
        EmptyState.Visibility = compact || Conversation is not null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add)
        {
            return;
        }

        var scroller = Scroller;
        var nearBottom = scroller is null || scroller.VerticalOffset >= scroller.ScrollableHeight - 80;
        var mine = e.NewItems?.OfType<ChatMessage>().Any(m => m.IsMine) == true;
        if (nearBottom || mine)
        {
            ScrollToEndSoon();
        }

        UpdateSearchCount();
    }

    // The virtualized list only estimates its height until the items near the end are measured,
    // so one ScrollToEnd can stop short. Repeat after each layout pass until we're really at the bottom.
    private void ScrollToEndSoon(int attemptsLeft = 10)
    {
        _pinnedToBottom = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (Scroller is not { } scroller)
            {
                return;
            }

            scroller.ScrollToEnd();
            scroller.UpdateLayout();
            if (attemptsLeft > 0 && scroller.VerticalOffset < scroller.ScrollableHeight - 1)
            {
                ScrollToEndSoon(attemptsLeft - 1);
            }
        });
    }

    // ---- Composer: send, reply, edit ------------------------------------------------------------

    private void Send()
    {
        if (Conversation is not { } conversation)
        {
            return;
        }

        if (_editing is { } editing)
        {
            if (!string.IsNullOrWhiteSpace(Input.Text))
            {
                App.Chat.EditMessage(editing, Input.Text);
                EndComposerMode();
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(Input.Text) && _attachments.Count == 0)
        {
            return;
        }

        // Pictures and files first, then the text (which carries the reply, if any).
        foreach (var attachment in _attachments.Where(a => File.Exists(a.Path)))
        {
            App.Chat.SendFile(conversation, attachment.Path);
        }

        _attachments.Clear();
        UpdateAttachmentTray();
        if (!string.IsNullOrWhiteSpace(Input.Text))
        {
            App.Chat.SendText(conversation, Input.Text, _replyTo);
        }

        Input.Clear();
        _drafts.Remove(conversation);
        _replyTo = null;
        UpdateComposerMode();
    }

    private void StartReply(ChatMessage message)
    {
        if (_editing is not null)
        {
            EndComposerMode();
        }

        _replyTo = message;
        UpdateComposerMode();
        FocusInput();
    }

    private void StartEdit(ChatMessage message)
    {
        if (_editing is null)
        {
            _draftBeforeEdit = Input.Text;
        }

        _replyTo = null;
        _editing = message;
        Input.Text = message.Text;
        Input.CaretIndex = Input.Text.Length;
        UpdateComposerMode();
        FocusInput();
    }

    /// <summary>Leaves reply/edit mode; after an edit the draft that was there before comes back.</summary>
    private void EndComposerMode()
    {
        if (_editing is not null)
        {
            Input.Text = _draftBeforeEdit;
            Input.CaretIndex = Input.Text.Length;
            _draftBeforeEdit = "";
        }

        _editing = null;
        _replyTo = null;
        UpdateComposerMode();
    }

    private void UpdateComposerMode()
    {
        if (_editing is not null)
        {
            BannerTitle.Text = "Editing message";
            BannerText.Text = "Enter to save · Esc to cancel";
        }
        else if (_replyTo is { } reply)
        {
            BannerTitle.Text = "Replying to " + (reply.IsMine ? "yourself" : reply.Sender?.DisplayName ?? "message");
            BannerText.Text = reply.Preview;
        }

        ComposerBanner.Visibility = _editing is null && _replyTo is null ? Visibility.Collapsed : Visibility.Visible;
        SendButton.ToolTip = _editing is null ? "Send (Enter)" : "Save (Enter)";
        AttachButton.IsEnabled = _editing is null;
    }

    private void OnCancelComposerMode(object sender, RoutedEventArgs e)
    {
        EndComposerMode();
        FocusInput();
    }

    /// <summary>Adds files to the tray above the input; they're sent with the next message (Teams-style).</summary>
    private void AddAttachments(IEnumerable<string> paths)
    {
        if (Conversation is null || _editing is not null)
        {
            return;
        }

        foreach (var path in paths.Where(File.Exists))
        {
            if (!_attachments.Any(a => string.Equals(a.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                _attachments.Add(new PendingAttachment(path));
            }
        }

        UpdateAttachmentTray();
        FocusInput();
    }

    private void UpdateAttachmentTray()
    {
        AttachmentTray.Visibility = _attachments.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SendButton.IsEnabled = true;
    }

    private void OnRemoveAttachment(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PendingAttachment attachment })
        {
            _attachments.Remove(attachment);
            UpdateAttachmentTray();
            FocusInput();
        }
    }

    /// <summary>
    /// Ctrl+V / Shift+Insert / Paste with a screenshot or copied files on the clipboard: attach them.
    /// Handled here because a TextBox won't even run Paste when the clipboard has no text.
    /// Returns true if it attached something (so the normal text paste must not run).
    /// </summary>
    private bool PasteAttachments()
    {
        if (Conversation is null || _editing is not null)
        {
            return false;
        }

        try
        {
            var data = Clipboard.GetDataObject();
            if (data is null)
            {
                return false;
            }

            if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files)
            {
                AddAttachments(files);
                return true;
            }

            if (ClipboardImage.HasPicture(data) && ClipboardImage.ReadPng(data) is { } png)
            {
                var dir = Path.Combine(AppPaths.FilesDir, "pasted");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, $"Screenshot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss_fff}.png");
                File.WriteAllBytes(path, png);
                AddAttachments([path]);
                return true;
            }
        }
        catch (Exception ex) when (ex is ExternalException or IOException or UnauthorizedAccessException)
        {
            Log.Error("Paste", ex);
        }

        return false;
    }

    private void OnSend(object sender, RoutedEventArgs e)
    {
        Send();
        FocusInput();
    }

    private void OnInputPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            Send();
        }
        else if (e.Key == Key.Escape && (_editing is not null || _replyTo is not null))
        {
            e.Handled = true;
            EndComposerMode();
        }
        else if (IsPasteGesture(e) && PasteAttachments())
        {
            e.Handled = true;
        }
        else if (e.Key == Key.Back && Input.Text.Length == 0 && _attachments.Count > 0)
        {
            // Backspace in an empty box removes the last attachment.
            e.Handled = true;
            _attachments.RemoveAt(_attachments.Count - 1);
            UpdateAttachmentTray();
        }
    }

    private static bool IsPasteGesture(KeyEventArgs e) =>
        (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        || (e.Key == Key.Insert && Keyboard.Modifiers == ModifierKeys.Shift);

    // Right-click → Paste: allow it when the clipboard holds a picture or files (the TextBox alone wouldn't).
    private void OnInputPreviewCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        if (e.Command == ApplicationCommands.Paste && Conversation is not null && _editing is null && ClipboardHasAttachment())
        {
            e.CanExecute = true;
            e.Handled = true;
        }
    }

    private void OnInputPreviewExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Command == ApplicationCommands.Paste && PasteAttachments())
        {
            e.Handled = true;
        }
    }

    private static bool ClipboardHasAttachment()
    {
        try
        {
            return Clipboard.GetDataObject() is { } data
                   && (data.GetDataPresent(DataFormats.FileDrop) || ClipboardImage.HasPicture(data));
        }
        catch (ExternalException)
        {
            return false;
        }
    }

    private void OnAttach(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = "Attach files or pictures" };
        if (DialogGuard.Run(() => dialog.ShowDialog(Window.GetWindow(this))) == true)
        {
            AddAttachments(dialog.FileNames);
        }

        FocusInput();
    }

    private void OnEmojiClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Content: string emoji })
        {
            var caret = Input.CaretIndex;
            Input.Text = Input.Text.Insert(caret, emoji);
            Input.CaretIndex = caret + emoji.Length;
        }

        EmojiToggle.IsChecked = false;
        FocusInput();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = Conversation is not null && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            AddAttachments(files);
        }
    }

    // ---- Message actions ------------------------------------------------------------------------

    /// <summary>The message a clicked element belongs to (toolbar buttons and reaction chips sit inside its template).</summary>
    private static ChatMessage? MessageOf(object sender)
    {
        var node = sender as DependencyObject;
        while (node is not null)
        {
            switch (node)
            {
                case FrameworkElement { DataContext: ChatMessage message }:
                    return message;
                case FrameworkContentElement { DataContext: ChatMessage contentMessage }:
                    return contentMessage;
            }

            node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }

        return null;
    }

    private void OnBubbleRightClick(object sender, MouseButtonEventArgs e)
    {
        if (MessageOf(sender) is { HasActions: true } message)
        {
            e.Handled = true;
            ShowMessageMenu(message, (UIElement)sender, PlacementMode.MousePoint);
        }
    }

    private void OnMoreActions(object sender, RoutedEventArgs e)
    {
        if (MessageOf(sender) is { HasActions: true } message)
        {
            ShowMessageMenu(message, (UIElement)sender, PlacementMode.Bottom);
        }
    }

    private void ShowMessageMenu(ChatMessage message, UIElement target, PlacementMode placement)
    {
        var menu = new ContextMenu { PlacementTarget = target, Placement = placement };
        AddItem(menu, "Reply", "", () => StartReply(message));
        AddItem(menu, "Forward", "", () => Forward(message));
        if (MessageText.SelectionIn(message) is { } selected)
        {
            AddItem(menu, "Copy selected text", "", () => CopyText(selected));
            AddItem(menu, "Copy whole message", "", () => Copy(message));
        }
        else
        {
            AddItem(menu, "Copy", "", () => Copy(message));
        }

        if (message.IsGif && !message.IsSticker && message.LocalPath is { } gifPath && File.Exists(gifPath) && !GifLibrary.Contains(gifPath))
        {
            AddItem(menu, "Save GIF", "", () => GifLibrary.Add(gifPath));
        }

        if (message.IsTgs && message.LocalPath is { } stickerPath && File.Exists(stickerPath) && !StickerLibrary.Contains(stickerPath))
        {
            AddItem(menu, "Save sticker", "", () => StickerLibrary.Add(stickerPath));
        }

        if (message.CanEdit || message.CanDelete)
        {
            menu.Items.Add(new Separator());
        }

        if (message.CanEdit)
        {
            AddItem(menu, "Edit", "", () => StartEdit(message));
        }

        if (message.CanDelete)
        {
            AddItem(menu, "Delete", "", () => Delete(message), Brushes.Firebrick);
        }

        menu.IsOpen = true;
    }

    private void AddItem(ContextMenu menu, string header, string glyph, Action action, Brush? foreground = null)
    {
        var item = new MenuItem
        {
            Header = header,
            Icon = new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("IconFont"), Foreground = foreground ?? Brushes.Black },
        };
        if (foreground is not null)
        {
            item.Foreground = foreground;
        }

        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private void OnQuickReact(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Content: string emoji } && MessageOf(sender) is { } message)
        {
            App.Chat.ToggleReaction(message, emoji);
        }
    }

    private void OnReplyClick(object sender, RoutedEventArgs e)
    {
        if (MessageOf(sender) is { HasActions: true } message)
        {
            StartReply(message);
        }
    }

    /// <summary>The + on the hover bar: every reaction, in a grid next to the mouse.</summary>
    private void OnMoreReactions(object sender, RoutedEventArgs e)
    {
        if (MessageOf(sender) is not { HasActions: true } message)
        {
            return;
        }

        var grid = new UniformGrid { Columns = 8 };
        var popup = new Popup
        {
            // At the mouse: the hover bar hides as soon as the mouse moves into the popup.
            PlacementTarget = MessageList,
            Placement = PlacementMode.MousePoint,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
            Child = new Border
            {
                Margin = new Thickness(0, 4, 8, 8),
                Padding = new Thickness(4),
                Background = Brushes.White,
                BorderBrush = (Brush)FindResource("DividerBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Opacity = 0.18 },
                Child = grid,
            },
        };
        var style = (Style)FindResource("ToolbarEmojiButton");
        foreach (var emoji in ChatMessage.MoreReactions)
        {
            var button = new Button { Style = style, Content = emoji, Width = 32, Height = 32, FontSize = 17 };
            if (message.Reactions.TryGetValue(emoji, out var people) && people.Contains(App.Chat.Me.Ip))
            {
                button.Background = (Brush)FindResource("AccentLightBrush"); // already mine: clicking takes it back
            }

            button.Click += (_, _) =>
            {
                popup.IsOpen = false;
                App.Chat.ToggleReaction(message, emoji);
            };
            grid.Children.Add(button);
        }

        popup.IsOpen = true;
    }

    private void OnReactionChipClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ReactionChip chip } && MessageOf(VisualTreeHelper.GetParent((Button)sender)) is { } message)
        {
            App.Chat.ToggleReaction(message, chip.Emoji);
        }
    }

    private void Forward(ChatMessage message)
    {
        var targets = ForwardWindow.Pick(Window.GetWindow(this), message);
        if (targets.Count > 0 && !App.Chat.Forward(message, targets))
        {
            MessageBox.Show(Window.GetWindow(this)!, "This file is no longer on this computer, so it can't be forwarded.",
                "Forward", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void Delete(ChatMessage message)
    {
        var owner = Window.GetWindow(this)!;
        var answer = DialogGuard.Run(() => MessageBox.Show(owner,
            "Delete this message? It will be removed for everyone in the chat.", "Delete message",
            MessageBoxButton.OKCancel, MessageBoxImage.Question));
        if (answer != MessageBoxResult.OK)
        {
            return;
        }

        if (ReferenceEquals(_editing, message))
        {
            EndComposerMode();
        }

        App.Chat.DeleteMessage(message);
    }

    private static void CopyText(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (ExternalException ex)
        {
            Log.Error("Copy", ex);
        }
    }

    private static void Copy(ChatMessage message)
    {
        try
        {
            if (message.Kind == MessageKind.Text)
            {
                Clipboard.SetText(message.Text);
            }
            else if (message.LocalPath is { } path && File.Exists(path))
            {
                Clipboard.SetFileDropList(new StringCollection { path });
            }
        }
        catch (ExternalException ex)
        {
            Log.Error("Copy", ex);
        }
    }

    /// <summary>Clicking a reply's quote jumps to the original message and flashes it.</summary>
    private void OnReplyQuoteClick(object sender, MouseButtonEventArgs e)
    {
        if (MessageOf(sender)?.ReplyTo is not { } reply || Conversation is not { } conversation
            || conversation.Messages.FirstOrDefault(m => m.Id == reply.Id) is not { } original)
        {
            return;
        }

        e.Handled = true;
        if (IsSearching && !MatchesSearch(original))
        {
            CloseSearch(scroll: false);
        }

        ScrollToMessage(original);
    }

    private void ScrollToMessage(ChatMessage message)
    {
        var index = _messages?.IndexOf(message) ?? -1;
        if (index < 0)
        {
            return;
        }

        FindDescendant<VirtualizingStackPanel>(MessageList)?.BringIndexIntoViewPublic(index);
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (MessageList.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement container)
            {
                container.BringIntoView();
                container.BeginAnimation(OpacityProperty, new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(450))
                {
                    RepeatBehavior = new RepeatBehavior(2),
                    FillBehavior = FillBehavior.Stop,
                });
            }
        });
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            if (FindDescendant<T>(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    // ---- Chat header ----------------------------------------------------------------------------

    /// <summary>Favorites, and for groups rename / add people / leave / delete (same menu as right-clicking the chat).</summary>
    private void OnChatMenu(object sender, RoutedEventArgs e)
    {
        if (Conversation is not { } conversation)
        {
            return;
        }

        var menu = (ContextMenu)Application.Current.FindResource("ConversationMenu");
        menu.DataContext = conversation;
        menu.PlacementTarget = (UIElement)sender;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    // ---- Find in chat ---------------------------------------------------------------------------

    private void OnToggleSearch(object sender, RoutedEventArgs e) => ToggleSearch();

    private void OpenSearch()
    {
        if (Conversation is null)
        {
            return;
        }

        SearchBar.Visibility = Visibility.Visible;
        FindBox.Focus();
        FindBox.SelectAll();
    }

    private void CloseSearch(bool scroll = true)
    {
        if (!IsSearching)
        {
            return;
        }

        SearchBar.Visibility = Visibility.Collapsed;
        FindBox.Text = "";
        _messages?.Refresh();
        UpdateSearchCount();
        if (scroll)
        {
            ScrollToEndSoon();
            FocusInput();
        }
    }

    private void OnCloseSearch(object sender, RoutedEventArgs e) => CloseSearch();

    private void OnFindKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseSearch();
        }
    }

    private void OnFindChanged(object sender, TextChangedEventArgs e)
    {
        _messages?.Refresh();
        UpdateSearchCount();
        ScrollToEndSoon();
    }

    private bool MatchesSearch(object item)
    {
        var query = FindBox.Text.Trim();
        if (!IsSearching || query.Length == 0)
        {
            return true;
        }

        return item is ChatMessage { IsDeleted: false } message && message.Kind != MessageKind.System
            && (message.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || (message.FileName?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false)
                || (message.Sender?.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false));
    }

    private void UpdateSearchCount()
    {
        var searching = IsSearching && FindBox.Text.Trim().Length > 0;
        var count = searching ? _messages?.Count ?? 0 : 0;
        SearchCount.Text = searching ? count == 1 ? "1 result" : $"{count} results" : "";
        NoResults.Visibility = searching && count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- GIFs -----------------------------------------------------------------------------------

    private void UpdateGifPlayback()
    {
        var play = IsVisible && Window.GetWindow(this) is not { WindowState: WindowState.Minimized };
        foreach (var image in FindDescendants<Image>(MessageList))
        {
            if (AnimationBehavior.GetAnimator(image) is { } animator)
            {
                if (play)
                {
                    animator.Play();
                }
                else
                {
                    animator.Pause();
                }
            }
        }
    }

    private void OnGifError(object sender, AnimationErrorEventArgs e) =>
        Log.Error($"GIF ({e.Kind})", e.Exception);

    private string GifQuery => GifFilter.Text.Trim();

    private void OnGifPanelOpened(object? sender, EventArgs e)
    {
        GifFilter.Text = "";
        _stickers = new ListCollectionView(EmojiStickers.Available().ToList())
        {
            Filter = o => o is EmojiSticker s && (GifQuery.Length == 0 || s.Keywords.Contains(GifQuery, StringComparison.CurrentCultureIgnoreCase)),
        };
        _savedGifs = new ListCollectionView(GifLibrary.Items)
        {
            Filter = o => o is SavedGif gif && (GifQuery.Length == 0 || gif.Name.Contains(GifQuery, StringComparison.CurrentCultureIgnoreCase)),
        };
        _tgsStickers = new ListCollectionView(StickerLibrary.Items)
        {
            Filter = o => o is SavedSticker s && (GifQuery.Length == 0 || s.Name.Contains(GifQuery, StringComparison.CurrentCultureIgnoreCase)),
        };
        TgsGrid.ItemsSource = _tgsStickers;
        StickerGrid.ItemsSource = _stickers;
        GifGrid.ItemsSource = _savedGifs;
        UpdateGifTab();
        GifFilter.Focus();
    }

    // Dropping the tiles stops their animations while the panel is closed.
    private void OnGifPanelClosed(object? sender, EventArgs e)
    {
        TgsGrid.ItemsSource = null;
        StickerGrid.ItemsSource = null;
        GifGrid.ItemsSource = null;
        _tgsStickers = null;
        _stickers = null;
        _savedGifs = null;
    }

    private void OnGifTabChanged(object sender, RoutedEventArgs e)
    {
        if (IsInitialized)
        {
            UpdateGifTab();
        }
    }

    private void OnGifFilterChanged(object sender, TextChangedEventArgs e)
    {
        _tgsStickers?.Refresh();
        _stickers?.Refresh();
        _savedGifs?.Refresh();
        UpdateGifTab();
    }

    private void UpdateGifTab()
    {
        var stickers = StickersTab.IsChecked == true;
        var emoji = EmojiTab.IsChecked == true;
        TgsGrid.Visibility = stickers ? Visibility.Visible : Visibility.Collapsed;
        StickerGrid.Visibility = emoji ? Visibility.Visible : Visibility.Collapsed;
        GifGrid.Visibility = !stickers && !emoji ? Visibility.Visible : Visibility.Collapsed;
        AddGifButton.Visibility = emoji ? Visibility.Collapsed : Visibility.Visible;
        AddGifButton.Content = stickers ? "Add sticker…" : "Add GIF…";
        StickerCredit.Visibility = emoji ? Visibility.Visible : Visibility.Collapsed;
        GifFilterHint.Text = stickers ? "Search stickers" : emoji ? "Search emoji" : "Search saved GIFs";

        var view = stickers ? _tgsStickers : emoji ? _stickers : _savedGifs;
        GifEmpty.Visibility = view is { IsEmpty: true } ? Visibility.Visible : Visibility.Collapsed;
        GifEmpty.Text = stickers ? "No stickers match."
            : emoji ? "No emoji match."
            : GifLibrary.Items.Count == 0 ? "No saved GIFs yet. GIFs you send are saved here automatically, or use “Add GIF…”."
            : "No saved GIFs match.";
    }

    private void OnStickerPicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: EmojiSticker sticker } && Conversation is { } conversation)
        {
            GifToggle.IsChecked = false;
            App.Chat.SendSticker(conversation, sticker);
            FocusInput();
        }
    }

    private void OnTgsStickerPicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SavedSticker sticker } && Conversation is { } conversation)
        {
            GifToggle.IsChecked = false;
            App.Chat.SendSticker(conversation, sticker.Path);
            FocusInput();
        }
    }

    private void OnRemoveTgsSticker(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SavedSticker sticker })
        {
            e.Handled = true;
            StickerLibrary.Remove(sticker);
            UpdateGifTab();
        }
    }

    // Panel tiles show a still frame and animate while hovered.
    private void OnTileMouseEnter(object sender, MouseEventArgs e) => TileAnimator(sender)?.Play();

    private void OnTileMouseLeave(object sender, MouseEventArgs e)
    {
        if (TileAnimator(sender) is { } animator)
        {
            animator.Pause();
            animator.Rewind();
        }
    }

    private static Animator? TileAnimator(object sender) =>
        sender is DependencyObject tile && FindDescendants<Image>(tile).FirstOrDefault() is { } image
            ? AnimationBehavior.GetAnimator(image)
            : null;

    private void OnGifPicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SavedGif gif } && Conversation is { } conversation)
        {
            GifToggle.IsChecked = false;
            App.Chat.SendFile(conversation, gif.Path);
            FocusInput();
        }
    }

    private void OnRemoveGif(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SavedGif gif })
        {
            GifLibrary.Remove(gif);
            UpdateGifTab();
        }
    }

    private void OnAddGif(object sender, RoutedEventArgs e)
    {
        if (StickersTab.IsChecked == true)
        {
            AddStickers();
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = "Add GIFs", Filter = "GIF animations|*.gif" };
        if (DialogGuard.Run(() => dialog.ShowDialog(Window.GetWindow(this))) == true)
        {
            foreach (var file in dialog.FileNames)
            {
                try
                {
                    GifLibrary.Add(file);
                }
                catch (IOException ex)
                {
                    Log.Error("Adding GIF", ex);
                }
            }
        }

        // The file dialog closed the panel; bring it back with the new GIFs on top.
        GifToggle.IsChecked = true;
    }

    /// <summary>Telegram sticker files (.tgs), e.g. downloaded from Telegram; anything that isn't one is skipped.</summary>
    private void AddStickers()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = "Add stickers", Filter = "Telegram stickers|*.tgs" };
        if (DialogGuard.Run(() => dialog.ShowDialog(Window.GetWindow(this))) == true)
        {
            var skipped = new List<string>();
            foreach (var file in dialog.FileNames)
            {
                try
                {
                    if (StickerLibrary.Add(file) is null)
                    {
                        skipped.Add(System.IO.Path.GetFileName(file));
                    }
                }
                catch (IOException ex)
                {
                    Log.Error("Adding sticker", ex);
                    skipped.Add(System.IO.Path.GetFileName(file));
                }
            }

            if (skipped.Count > 0)
            {
                DialogGuard.Run(() => MessageBox.Show(Window.GetWindow(this)!,
                    "These aren't Telegram animated stickers, so they weren't added:\n" + string.Join("\n", skipped),
                    "Add stickers", MessageBoxButton.OK, MessageBoxImage.Information));
            }
        }

        // The file dialog closed the panel; bring it back with the new stickers.
        GifToggle.IsChecked = true;
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindDescendants<T>(child))
            {
                yield return nested;
            }
        }
    }

    // ---- Attachments ----------------------------------------------------------------------------

    private static void OpenFile(ChatMessage? message)
    {
        if (message?.LocalPath is { } path && File.Exists(path))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        else if (message is not null)
        {
            MessageBox.Show("This file is no longer on this computer.", "Mini Local Chat", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OnImageClick(object sender, MouseButtonEventArgs e) => OpenFile(MessageOf(sender));

    private void OnOpenAttachment(object sender, RoutedEventArgs e) => OpenFile(MessageOf(sender));

    private void OnShowInFolder(object sender, RoutedEventArgs e)
    {
        if (MessageOf(sender)?.LocalPath is { } path && File.Exists(path))
        {
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
    }
}

/// <summary>A pasted screenshot or attached file waiting in the message box until the message is sent.</summary>
public sealed class PendingAttachment(string path)
{
    public string Path { get; } = path;

    public string Name => System.IO.Path.GetFileName(Path);

    public bool IsPicture => ChatService.IsImageFile(Path);
}

public sealed class MessageTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Mine { get; set; }

    public DataTemplate? Other { get; set; }

    public DataTemplate? System { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) => item switch
    {
        ChatMessage { Kind: MessageKind.System } => System,
        ChatMessage { IsMine: true } => Mine,
        _ => Other,
    };
}
