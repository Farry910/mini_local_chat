using System.ComponentModel;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using MiniLocalChat.Infrastructure;
using MiniLocalChat.Models;
using MiniLocalChat.Services;
using MiniLocalChat.Views;

namespace MiniLocalChat;

/// <summary>
/// Command line:
///   --tray          start hidden in the tray (used by "Start with Windows")
///   --data &lt;dir&gt;    use another data folder
///   --bind &lt;ip&gt;     listen/send only on this IP and use it as identity (e.g. two test instances on 127.0.0.1 / 127.0.0.2)
/// </summary>
public partial class App : Application
{
    private MainWindow? _main;
    private FloatingController? _floating;
    private RippleController? _ripples;
    private TrayIcon? _tray;
    private Mutex? _instanceMutex;
    private EventWaitHandle? _showSignal;

    public static ChatService Chat { get; private set; } = null!;

    public static bool IsExiting { get; private set; }

    public static new App Current => (App)Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        var startInTray = e.Args.Contains("--tray", StringComparer.OrdinalIgnoreCase);
        AppPaths.Initialize(ArgValue(e.Args, "--data"));
        if (!AcquireSingleInstance())
        {
            Shutdown();
            return;
        }

        Chat = new ChatService(Dispatcher);
        Chat.Load(ArgValue(e.Args, "--bind"));

        if (string.IsNullOrWhiteSpace(Chat.Settings.Nickname) && !SettingsWindow.ShowWelcome())
        {
            Shutdown();
            return;
        }

        try
        {
            Chat.Start();
        }
        catch (SocketException ex)
        {
            Chat.Shutdown();
            MessageBox.Show(
                $"Mini Local Chat couldn't open its network ports ({Chat.Settings.TcpPort}/{Chat.Settings.UdpPort}).\n\n{ex.Message}\n\nIs another copy already running?",
                "Mini Local Chat", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        _main = new MainWindow();
        _floating = new FloatingController();
        _tray = new TrayIcon(() => ShowMain(), ShowSettings, ExitApp);
        _tray.SetUnread(Chat.TotalUnread);

        Chat.IsConversationVisible = c => _main.IsShowing(c) || _floating.IsShowing(c);
        _ripples = new RippleController(Chat, () =>
            _floating.LedCenter ?? new Point(SystemParameters.WorkArea.Left + SystemParameters.WorkArea.Width / 2,
                                             SystemParameters.WorkArea.Top + SystemParameters.WorkArea.Height / 2));
        Chat.PropertyChanged += OnChatPropertyChanged;
        Chat.MessageReceived += (_, _) =>
        {
            if (_main.IsVisible && !_main.IsActive)
            {
                Win32.FlashTaskbar(_main);
            }
        };
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(() => _floating.ApplySettings());

        if (startInTray)
        {
            EnterTrayMode();
        }
        else
        {
            ShowMain();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showSignal?.Dispose();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    public void ShowMain(Conversation? open = null)
    {
        if (_main is null)
        {
            return;
        }

        _floating?.Exit();
        if (!_main.IsVisible)
        {
            _main.Show();
        }

        if (_main.WindowState == WindowState.Minimized)
        {
            _main.WindowState = WindowState.Normal;
        }

        _main.Activate();
        if ((open ?? Chat.MostRecentUnread()) is { } conversation)
        {
            _main.OpenConversation(conversation);
        }
    }

    /// <summary>Hide the window; the tray icon and the floating LED take over.</summary>
    public void EnterTrayMode()
    {
        _main?.Hide();
        _floating?.Enter();

        if (!Chat.Settings.TrayHintShown)
        {
            Chat.Settings.TrayHintShown = true;
            Chat.SaveSettings();
            _tray?.ShowBalloon("Mini Local Chat is still running",
                "The LED at the edge of your screen glows green when messages arrive. Hover it to reply. To quit, right-click this icon and choose Exit.");
        }
    }

    public void ShowSettings()
    {
        var owner = _main is { IsVisible: true } ? _main : null;
        if (SettingsWindow.ShowFor(owner))
        {
            _floating?.ApplySettings();
            _ripples?.ApplySettings();
        }
    }

    public void ExitApp()
    {
        if (IsExiting)
        {
            return;
        }

        IsExiting = true;
        _floating?.Exit();
        Chat.Shutdown();
        _tray?.Dispose();
        _main?.Close();
        Shutdown();
    }

    private void OnChatPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatService.TotalUnread))
        {
            _tray?.SetUnread(Chat.TotalUnread);
        }
    }

    /// <summary>One instance per data folder. A second launch just brings the running one to the front.</summary>
    private bool AcquireSingleInstance()
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(AppPaths.DataDir.ToLowerInvariant()));
        var name = "MiniLocalChat_" + Convert.ToHexString(hash)[..16];

        _instanceMutex = new Mutex(initiallyOwned: true, name, out var createdNew);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, name + "_show");
        if (!createdNew)
        {
            Win32.AllowSetForegroundWindow(-1);
            _showSignal.Set();
            return false;
        }

        var signal = _showSignal;
        new Thread(() =>
        {
            while (signal.WaitOne())
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (!IsExiting)
                    {
                        ShowMain();
                    }
                });
            }
        })
        { IsBackground = true, Name = "Single instance signal" }.Start();
        return true;
    }

    private static string? ArgValue(string[] args, string name)
    {
        var index = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled", e.Exception);
        MessageBox.Show("Something went wrong:\n\n" + e.Exception.Message, "Mini Local Chat", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
