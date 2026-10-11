using System.Text.Json.Serialization;
using System.Windows.Media;
using MiniLocalChat.Infrastructure;

namespace MiniLocalChat.Models;

/// <summary>A person on the LAN. The IP address is the permanent identity; the nickname is chosen by that person.</summary>
public sealed class Contact : ObservableObject
{
    private string _ip = "";
    private string _nickname = "";
    private bool _isOnline;

    public string Ip
    {
        get => _ip;
        set
        {
            if (Set(ref _ip, value))
            {
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(Initials));
                OnPropertyChanged(nameof(AvatarBrush));
            }
        }
    }

    public string Nickname
    {
        get => _nickname;
        set
        {
            if (Set(ref _nickname, value?.Trim() ?? ""))
            {
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(Initials));
            }
        }
    }

    public DateTime LastSeenUtc { get; set; }

    [JsonIgnore]
    public bool IsOnline
    {
        get => _isOnline;
        set => Set(ref _isOnline, value);
    }

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Nickname) ? Ip : Nickname;

    [JsonIgnore]
    public string Initials => AvatarPalette.Initials(DisplayName);

    [JsonIgnore]
    public Brush AvatarBrush => AvatarPalette.For(Ip);
}
