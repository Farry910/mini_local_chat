using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using MiniLocalChat.Models;

namespace MiniLocalChat.Views;

/// <summary>
/// The chat list as shown in both the full window and the floating panel: sections Favorites, Online,
/// Offline (top to bottom); inside each, chats with new messages first, then most recent, then by name.
/// Kept up to date live as people come online, favorites change and messages arrive.
/// </summary>
public static class ChatListView
{
    public static ListCollectionView Create(ObservableCollection<Conversation> conversations, Func<string> searchText)
    {
        var view = new ListCollectionView(conversations)
        {
            Filter = o => o is Conversation c && c.Matches(searchText().Trim()),
            IsLiveGrouping = true,
            IsLiveSorting = true,
        };

        // Predefined group names keep the sections in this order even when one starts out empty.
        var sections = new PropertyGroupDescription(nameof(Conversation.Section));
        foreach (var name in Conversation.Sections)
        {
            sections.GroupNames.Add(name);
        }

        view.GroupDescriptions.Add(sections);
        view.LiveGroupingProperties.Add(nameof(Conversation.Section));

        view.SortDescriptions.Add(new SortDescription(nameof(Conversation.SectionOrder), ListSortDirection.Ascending));
        view.SortDescriptions.Add(new SortDescription(nameof(Conversation.HasUnread), ListSortDirection.Descending));
        view.SortDescriptions.Add(new SortDescription(nameof(Conversation.LastActivityUtc), ListSortDirection.Descending));
        view.SortDescriptions.Add(new SortDescription(nameof(Conversation.Title), ListSortDirection.Ascending));
        foreach (var property in new[] { nameof(Conversation.SectionOrder), nameof(Conversation.HasUnread), nameof(Conversation.LastActivityUtc), nameof(Conversation.Title) })
        {
            view.LiveSortingProperties.Add(property);
        }

        return view;
    }
}
