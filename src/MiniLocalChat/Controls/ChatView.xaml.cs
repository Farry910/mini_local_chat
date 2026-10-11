using System.Collections.Specialized;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MiniLocalChat.Infrastructure;
using MiniLocalChat.Models;

namespace MiniLocalChat.Controls;

/// <summary>Messages + composer for one conversation. Used by the main window and (compact) by the floating mini panel.</summary>
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
    private ScrollViewer? _scroller;

    public ChatView()
    {
        InitializeComponent();
        EmojiGrid.ItemsSource = Emojis;
        CommandManager.AddPreviewExecutedHandler(Input, OnInputPreviewExecuted);
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

    private ScrollViewer? Scroller
    {
        get
        {
            if (_scroller is null)
            {
                MessageList.ApplyTemplate();
                _scroller = MessageList.Template.FindName("Scroller", MessageList) as ScrollViewer;
            }

            return _scroller;
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

    private void OnConversationChanged(Conversation? oldValue, Conversation? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.Messages.CollectionChanged -= OnMessagesChanged;
            _drafts[oldValue] = Input.Text;
        }

        ChatRoot.DataContext = newValue;
        ChatRoot.Visibility = newValue is null ? Visibility.Collapsed : Visibility.Visible;
        EmptyState.Visibility = newValue is null ? Visibility.Visible : Visibility.Collapsed;
        EmojiToggle.IsChecked = false;

        if (newValue is not null)
        {
            newValue.Messages.CollectionChanged += OnMessagesChanged;
            Input.Text = _drafts.GetValueOrDefault(newValue, "");
            Input.CaretIndex = Input.Text.Length;
            ScrollToEndSoon();
        }
    }

    private void ApplyLayoutMode()
    {
        var compact = IsCompact;
        Header.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        ComposerHint.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        MessageList.Padding = compact ? new Thickness(10, 2, 10, 8) : new Thickness(20, 4, 20, 12);
        Composer.Margin = compact ? new Thickness(8, 2, 8, 8) : new Thickness(20, 4, 20, 16);
        BubbleMaxWidth = compact ? 270 : 560;
        ImageMaxWidth = compact ? 230 : 320;
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
    }

    private void ScrollToEndSoon()
    {
        // Twice: once after layout, once more after images and virtualized items have measured.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => Scroller?.ScrollToEnd());
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => Scroller?.ScrollToEnd());
    }

    // ---- Sending --------------------------------------------------------------------------------

    private void Send()
    {
        if (Conversation is { } conversation && !string.IsNullOrWhiteSpace(Input.Text))
        {
            App.Chat.SendText(conversation, Input.Text);
            Input.Clear();
            _drafts.Remove(conversation);
        }
    }

    private void SendFiles(IEnumerable<string> paths)
    {
        if (Conversation is not { } conversation)
        {
            return;
        }

        foreach (var path in paths.Where(File.Exists))
        {
            App.Chat.SendFile(conversation, path);
        }
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
    }

    // Pasting an image or copied files sends them instead of inserting text.
    private void OnInputPreviewExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Command != ApplicationCommands.Paste || Conversation is null)
        {
            return;
        }

        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                e.Handled = true;
                SendFiles(Clipboard.GetFileDropList().Cast<string>());
            }
            else if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } image)
            {
                e.Handled = true;
                var dir = Path.Combine(AppPaths.FilesDir, "pasted");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, $"Image_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
                using (var stream = File.Create(path))
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(image));
                    encoder.Save(stream);
                }

                App.Chat.SendFile(Conversation, path);
            }
        }
        catch (Exception ex) when (ex is ExternalException or IOException)
        {
            Log.Error("Paste", ex);
        }
    }

    private void OnAttach(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = "Send files or images" };
        if (DialogGuard.Run(() => dialog.ShowDialog(Window.GetWindow(this))) == true)
        {
            SendFiles(dialog.FileNames);
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
            SendFiles(files);
        }
    }

    // ---- Attachments ----------------------------------------------------------------------------

    private static ChatMessage? MessageOf(object sender) => sender switch
    {
        FrameworkElement fe => fe.DataContext as ChatMessage,
        FrameworkContentElement fce => fce.DataContext as ChatMessage,
        _ => null,
    };

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

    private void OnCopyMessage(object sender, RoutedEventArgs e)
    {
        var message = sender is MenuItem { Parent: ContextMenu { PlacementTarget: FrameworkElement target } }
            ? target.DataContext as ChatMessage
            : MessageOf(sender);
        if (message is null)
        {
            return;
        }

        if (message.Kind == MessageKind.Text)
        {
            Clipboard.SetText(message.Text);
        }
        else if (message.LocalPath is { } path && File.Exists(path))
        {
            Clipboard.SetFileDropList(new StringCollection { path });
        }
    }
}

public sealed class MessageTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Mine { get; set; }

    public DataTemplate? Other { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        item is ChatMessage { IsMine: true } ? Mine : Other;
}
