using System.Text.Json.Serialization;
using System.Windows.Media;
using MiniLocalChat.Infrastructure;

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
    public string EffectiveColor1 => Color1 ?? AvatarPalette.DefaultColor1(Ip);

    [JsonIgnore]
    public string EffectiveColor2 => Color2 ?? AvatarPalette.DefaultColor2(Ip);

    [JsonIgnore]
    public Brush AvatarBrush => AvatarPalette.ToBrush(EffectiveColor1);

    [JsonIgnore]
    public Brush RingBrush => AvatarPalette.ToBrush(EffectiveColor2);

    private void OnColorsChanged()
    {
        OnPropertyChanged(nameof(EffectiveColor1));
        OnPropertyChanged(nameof(EffectiveColor2));
        OnPropertyChanged(nameof(AvatarBrush));
        OnPropertyChanged(nameof(RingBrush));
    }
}
