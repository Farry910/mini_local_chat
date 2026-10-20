using System.Text.Json.Serialization;
using System.Windows.Media;
using MiniLocalChat.Infrastructure;
using MiniLocalChat.Services;

namespace MiniLocalChat.Models;

/// <summary>
/// A person on the LAN. The IP address is the permanent identity; the nickname and the two
/// avatar colors are chosen by that person and shared over the network.
/// </summary>
public sealed class Contact : ObservableObject
{
    private string _ip = "";
    private string _nickname = "";
    private string? _color1;
    private string? _color2;
    private bool _isOnline;
    private bool _isAway;
    private bool _rippleBlocked;
    private string? _avatarId;

    public string Ip
    {
        get => _ip;
        set
        {
            if (Set(ref _ip, value))
            {
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(Initials));
                OnColorsChanged();
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

    /// <summary>Chosen avatar fill / first ripple wave ("#RRGGBB"); null = default from the IP.</summary>
    public string? Color1
    {
        get => _color1;
        set
        {
            if (Set(ref _color1, AvatarPalette.NormalizeHex(value)))
            {
                OnColorsChanged();
            }
        }
    }

    /// <summary>Chosen avatar ring / second ripple wave ("#RRGGBB"); null = default from the IP.</summary>
    public string? Color2
    {
        get => _color2;
        set
        {
            if (Set(ref _color2, AvatarPalette.NormalizeHex(value)))
            {
                OnColorsChanged();
            }
        }
    }

    public DateTime LastSeenUtc { get; set; }

    /// <summary>The person's profile picture (hash of the image); null = initials. The image itself is cached by id.</summary>
    public string? AvatarId
    {
        get => _avatarId;
        set
        {
            if (Set(ref _avatarId, AvatarStore.IsValidId(value) ? value : null))
            {
                OnPropertyChanged(nameof(AvatarImage));
            }
        }
    }

    /// <summary>The picture, once it's on this computer; null shows initials on the first color.</summary>
    [JsonIgnore]
    public ImageSource? AvatarImage => AvatarStore.ImageFor(AvatarId);

    /// <summary>Call when the picture for <see cref="AvatarId"/> has just arrived.</summary>
    public void RefreshAvatar() => OnPropertyChanged(nameof(AvatarImage));

    /// <summary>
    /// My local choice: messages from this person never trigger the screen ripple (e.g. someone spamming).
    /// Their messages still arrive and light the LED. They aren't told.
    /// </summary>
    public bool RippleBlocked
    {
        get => _rippleBlocked;
        set => Set(ref _rippleBlocked, value);
    }

    [JsonIgnore]
    public bool IsOnline
    {
        get => _isOnline;
        set
        {
            if (Set(ref _isOnline, value))
            {
                if (!value)
                {
                    IsAway = false;
                }

                OnPropertyChanged(nameof(PresenceText));
            }
        }
    }

    /// <summary>Online but away: their mouse hasn't moved for a while, or their screen is locked.</summary>
    [JsonIgnore]
    public bool IsAway
    {
        get => _isAway;
        set
        {
            if (Set(ref _isAway, value))
            {
                OnPropertyChanged(nameof(PresenceText));
            }
        }
    }

    /// <summary>For chat headers: "Online", "Away" or "Offline · …".</summary>
    [JsonIgnore]
    public string PresenceText => !IsOnline ? "Offline · messages are delivered when they come online" : IsAway ? "Away" : "Online";

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Nickname) ? Ip : Nickname;

    [JsonIgnore]
    public string Initials => AvatarPalette.Initials(DisplayName);

    [JsonIgnore]
    public string EffectiveColor1 => Color1 ?? AvatarPalette.DefaultColor1(Ip);

    [JsonIgnore]
    public string EffectiveColor2 => Color2 ?? AvatarPalette.DefaultColor2(Ip);

    [JsonIgnore]
    public Brush AvatarBrush => AvatarPalette.ToBrush(EffectiveColor1);

    private void OnColorsChanged()
    {
        OnPropertyChanged(nameof(EffectiveColor1));
        OnPropertyChanged(nameof(EffectiveColor2));
        OnPropertyChanged(nameof(AvatarBrush));
    }
}
