using MiniLocalChat.Infrastructure;
using MiniLocalChat.Models;
using MiniLocalChat.Network;

namespace MiniLocalChat.Services;

/// <summary>
/// Auto-update over the LAN, no internet needed. Every heartbeat says which version this computer can
/// hand out. Seeing a higher one, I ask that computer for its exe ("update-get"), it sends it ("update",
/// the exe as payload), I check it (see <see cref="Updater"/>) and stage it; the app installs it and
/// restarts when nobody is using it. Once updated, I hand the new version out too, so a release spreads
/// from computer to computer.
/// </summary>
public sealed partial class ChatService
{
    private const int MaxUploads = 2;
    private static readonly TimeSpan UpdateRequestTimeout = TimeSpan.FromMinutes(2);

    private int _uploads;
    private (string Ip, Version Version, DateTime AtUtc)? _updateRequest;
    private readonly HashSet<(string Ip, Version Version)> _badUpdates = [];
    private bool _updatesBlocked;

    /// <summary>The version I can hand out (my own exe), for the heartbeat; null for development builds.</summary>
    private static string? ServableVersion => Updater.IsInstalledBuild ? Updater.CurrentVersion.ToString(3) : null;

    /// <summary>A newer version, checked and ready to install (on the next restart).</summary>
    public Version? StagedUpdate { get; private set; }

    /// <summary>A newer version was downloaded and checked; the app decides when to restart into it.</summary>
    public event Action? UpdateStaged;

    private static void StartUpdates() => Updater.CleanUp();

    /// <summary>A heartbeat said this person can hand out <paramref name="advertised"/>.</summary>
    private void ConsiderUpdate(Contact contact, string? advertised)
    {
        if (!Settings.AutoUpdate || _updatesBlocked || StagedUpdate is not null || !Updater.IsInstalledBuild
            || Updater.Parse(advertised) is not { } version || version <= Updater.CurrentVersion
            || _badUpdates.Contains((contact.Ip, version)))
        {
            return;
        }

        if (_updateRequest is { } pending && DateTime.UtcNow - pending.AtUtc < UpdateRequestTimeout)
        {
            return; // already asking someone
        }

        _updateRequest = (contact.Ip, version, DateTime.UtcNow);
        var header = new WireHeader { Type = WireTypes.UpdateGet, Id = Guid.NewGuid(), Nick = Me.Nickname, Colors = MyColors(), Avatar = Me.AvatarId, Text = version.ToString(3) };
        SendDirect(contact.Ip, header, null);
    }

    /// <summary>Someone asks for my exe. A couple of uploads at a time; the others ask again later (or someone else).</summary>
    private void ReceiveUpdateRequest(WireHeader header, Contact sender)
    {
        if (ServableVersion is null || header.Text != ServableVersion || _uploads >= MaxUploads)
        {
            return;
        }

        var reply = new WireHeader
        {
            Type = WireTypes.Update,
            Id = Guid.NewGuid(),
            Nick = Me.Nickname,
            Colors = MyColors(),
            Avatar = Me.AvatarId,
            Kind = MessageKind.File,
            FileName = Path.GetFileName(Updater.ExePath),
            FileSize = new FileInfo(Updater.ExePath!).Length,
            Text = ServableVersion,
        };
        _knownMessageIds.Add(reply.Id);
        Interlocked.Increment(ref _uploads);
        var bind = BindAddress;
        var port = Settings.TcpPort;
        var ip = sender.Ip;
        _ = Task.Run(async () =>
        {
            try
            {
                await PeerClient.SendAsync(bind, ip, port, reply, Updater.ExePath);
            }
            catch (Exception ex)
            {
                Log.Error("Sending update", ex);
            }
            finally
            {
                Interlocked.Decrement(ref _uploads);
            }
        });
    }

    /// <summary>The exe I asked for arrived: check it in the background, then stage it.</summary>
    private void ReceiveUpdate(WireHeader header, Contact sender, string? filePath)
    {
        if (filePath is null)
        {
            return;
        }

        if (_updateRequest is not { } request || request.Ip != sender.Ip || StagedUpdate is not null)
        {
            DeleteDownload(filePath); // not asked for
            return;
        }

        _ = Task.Run(() =>
        {
            Version? staged = null;
            var blocked = false;
            try
            {
                staged = Updater.Stage(filePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error("Staging update (is the app folder read-only?)", ex);
                blocked = true;
            }
            finally
            {
                DeleteDownload(filePath);
            }

            _ui.InvokeAsync(() =>
            {
                _updateRequest = null;
                if (staged is not null)
                {
                    StagedUpdate = staged;
                    UpdateStaged?.Invoke();
                }
                else if (blocked)
                {
                    _updatesBlocked = true;
                }
                else
                {
                    _badUpdates.Add((request.Ip, request.Version)); // not a Mini Local Chat exe, or not newer: don't ask them again
                }
            });
        });
    }

    private static void DeleteDownload(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            if (Path.GetDirectoryName(filePath) is { } dir && dir.StartsWith(AppPaths.FilesDir, StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
