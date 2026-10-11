using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using MiniLocalChat.Models;

namespace MiniLocalChat.Controls;

/// <summary>
/// "Block ripples" for the people in a chat: a toggle in 1:1 chats, a per-member menu in group chats.
/// Blocking only stops the screen ripple; messages still arrive.
/// </summary>
public partial class RippleBlockButton : UserControl
{
    public static readonly DependencyProperty ConversationProperty = DependencyProperty.Register(
        nameof(Conversation), typeof(Conversation), typeof(RippleBlockButton),
        new PropertyMetadata(null, (d, e) => ((RippleBlockButton)d).OnConversationChanged((Conversation?)e.OldValue)));

    public static readonly DependencyProperty OnAccentProperty = DependencyProperty.Register(
        nameof(OnAccent), typeof(bool), typeof(RippleBlockButton),
        new PropertyMetadata(false, (d, _) => ((RippleBlockButton)d).ApplyStyle()));

    public RippleBlockButton()
    {
        InitializeComponent();
        ApplyStyle();
    }

    public Conversation? Conversation
    {
        get => (Conversation?)GetValue(ConversationProperty);
        set => SetValue(ConversationProperty, value);
    }

    /// <summary>Icon-only, white, for the purple floating-panel header.</summary>
    public bool OnAccent
    {
        get => (bool)GetValue(OnAccentProperty);
        set => SetValue(OnAccentProperty, value);
    }

    private void ApplyStyle()
    {
        var style = (Style)Resources[OnAccent ? "AccentPill" : "LightPill"];
        Toggle.Style = style;
        GroupButton.Style = style;
        ToggleText.Visibility = GroupText.Visibility = OnAccent ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnConversationChanged(Conversation? old)
    {
        if (old is not null)
        {
            foreach (var member in old.Members)
            {
                member.PropertyChanged -= OnMemberChanged;
            }
        }

        BindingOperations.ClearBinding(Toggle, ToggleButton.IsCheckedProperty);
        Toggle.Visibility = GroupButton.Visibility = Visibility.Collapsed;

        if (Conversation is not { } conversation)
        {
            return;
        }

        foreach (var member in conversation.Members)
        {
            member.PropertyChanged += OnMemberChanged;
        }

        if (!conversation.IsGroup && conversation.Peer is { } peer)
        {
            Toggle.SetBinding(ToggleButton.IsCheckedProperty,
                new Binding(nameof(Contact.RippleBlocked)) { Source = peer, Mode = BindingMode.TwoWay });
            Toggle.Visibility = Visibility.Visible;
        }
        else
        {
            GroupButton.Visibility = Visibility.Visible;
        }

        UpdateLabels();
    }

    private void OnMemberChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Contact.RippleBlocked) or nameof(Contact.DisplayName))
        {
            UpdateLabels();
        }
    }

    private void OnToggleChanged(object sender, RoutedEventArgs e) => UpdateLabels();

    private void UpdateLabels()
    {
        if (Conversation is not { } conversation)
        {
            return;
        }

        if (!conversation.IsGroup && conversation.Peer is { } peer)
        {
            ToggleText.Text = peer.RippleBlocked ? "Ripples blocked" : "Block ripples";
            Toggle.ToolTip = peer.RippleBlocked
                ? $"Messages from {peer.DisplayName} don't ripple your screen. Click to allow again."
                : $"Stop messages from {peer.DisplayName} from rippling your screen. Their messages still arrive.";
        }
        else
        {
            var blocked = conversation.Members.Count(m => m.RippleBlocked);
            GroupText.Text = blocked > 0 ? $"Ripples blocked ({blocked}) ▾" : "Block ripples ▾";
            GroupButton.ToolTip = "Choose whose messages shouldn't ripple your screen";
        }
    }

    private void OnGroupMenu(object sender, RoutedEventArgs e)
    {
        if (Conversation is not { } conversation)
        {
            return;
        }

        var menu = new ContextMenu { PlacementTarget = GroupButton, Placement = PlacementMode.Bottom };
        menu.Items.Add(new MenuItem { Header = "Don't ripple my screen for messages from:", IsEnabled = false });
        foreach (var member in conversation.Members.OrderBy(m => m.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            var item = new MenuItem { Header = member.DisplayName, IsCheckable = true, StaysOpenOnClick = true };
            item.SetBinding(MenuItem.IsCheckedProperty,
                new Binding(nameof(Contact.RippleBlocked)) { Source = member, Mode = BindingMode.TwoWay });
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }
}
