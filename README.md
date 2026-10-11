# Mini Local Chat

A Teams-style private chat for computers on the same local network (e.g. a `192.168.1.x` Class C subnet). No server: every PC runs the app and talks to the others directly.

- **Identity = IP address.** Each computer keeps a fixed IP; its user picks a nickname, and everyone else sees that nickname for that IP.
- **1:1 and group chats** (pick several people in *New chat*), text, emoji, files and images (attach, drag & drop, or paste).
- **Offline delivery.** Messages to someone who is offline are queued and sent automatically when they come back online.
- **Tray + floating LED.** Closing the window hides it to the tray. A small LED sits at the left or right edge of the screen:
  - dark: nothing new
  - glowing green with a number: unread messages
  - hover it: a mini panel lists only the chats with new messages, so you can read and reply without opening the app
  - drag it up/down or to the other side; double-click it to open the full app
- **Exit** only from the tray icon menu (or the LED's right-click menu).

## Build & run

Requires the .NET 10 SDK on Windows.

```powershell
dotnet build MiniLocalChat.slnx -c Release
.\src\MiniLocalChat\bin\Release\net10.0-windows\MiniLocalChat.exe
```

On first start Windows Firewall asks whether to allow the app on the network. Allow it on **private networks**, or the other computers can't reach it.

To hand it to other PCs without installing .NET, publish a self-contained build:

```powershell
dotnet publish src/MiniLocalChat -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

### Command-line options

| Option | Purpose |
| --- | --- |
| `--tray` | Start hidden in the tray (used by *Start with Windows*). |
| `--data <dir>` | Use a different data folder (default `%AppData%\MiniLocalChat`). |
| `--bind <ip>` | Listen and send only on this IP and use it as identity. Lets you run two copies on one PC for testing, e.g. `--bind 127.0.0.1 --data A` and `--bind 127.0.0.2 --data B`. |

## How it works

| Port | Protocol | Use |
| --- | --- | --- |
| 50505 | TCP | Messages and files. One connection per message: `"MLC1"`, int32 header length, JSON header, then file bytes. The receiver replies with one ACK byte once it has stored the message; no ACK means the sender retries later. |
| 50506 | UDP | Presence heartbeat every 5 s (LAN broadcast + directly to known contacts) carrying the nickname. Someone is shown offline after ~17 s of silence. |

- The sender of a message is always taken from the TCP connection's remote IP, never from the message itself.
- Group chats have an id shared by all members; every group message carries the member list, so a recipient's app creates the same group automatically.
- Message ids make retries idempotent: a message received twice is stored once.
- Received files go to `<data folder>\Files\<message id>\` with the file name sanitized.

Data files in the data folder: `settings.json`, `contacts.json`, `conversations.json`, `Files\`, and `log.txt` for errors.

## Project layout

```
src/MiniLocalChat/
  App.xaml(.cs)        startup, single instance, tray mode, exit
  Models/              Contact, Conversation, ChatMessage, AppSettings
  Network/             wire format, TCP server/client, UDP presence, delivery queue
  Services/            ChatService (all chat state), tray icon, autostart
  Controls/            ChatView (messages + composer), Avatar
  Views/               MainWindow, LedWindow, MiniChatWindow, FloatingController, NewChatWindow, SettingsWindow
  Themes/Theme.xaml    Teams-like colors and styles
```
