# Mini Local Chat

A Teams-style private chat for computers on the same local network (e.g. a `192.168.1.x` Class C subnet). No server: every PC runs the app and talks to the others directly.

- **Identity = IP address.** Each computer keeps a fixed IP; its user picks a nickname, and everyone else sees that nickname for that IP.
- **1:1 and group chats** (pick several people in *New chat*), text, emoji, files and images (attach, drag & drop, or paste).
- **Teams-style messages.** Hover a message for quick reactions (👍 ❤️ 😆 😮 😢 😡) and ⋯ → Reply, Forward, Copy, Edit, Delete (edit/delete your own). Replies quote the original (click to jump to it); edits and deletes update for everyone. **Find in chat** with Ctrl+F.
- **Group chats:** rename, add people, leave (others are told), or delete the chat from your list.
- **Chat list sections:** Favorites, Online, Offline (☆ a chat to pin it), with search in both the full window and the floating panel.
- **Offline delivery.** Messages to someone who is offline are queued and sent automatically when they come back online. Your newest message shows **✓ Delivered**, then **✓✓ Seen** once they open the chat (in groups: *Seen by 2 of 3*).
- **Tray + floating LED.** Closing the window hides it to the tray. A small LED sits at the left or right edge of the screen:
  - dark: nothing new
  - glowing green with a number: unread messages
  - hover it: a mini panel lists only the chats with new messages, so you can read and reply without opening the app. **Pin** it (📌 in its header) to keep it open.
  - drag it up/down or to the other side; double-click it to open the full app
- **Personal colors and picture.** Each user picks two colors in Settings (the first is the avatar background; both are their ripple colors) and optionally a profile picture, which replaces the initials. Pictures are shared over the network automatically and cached.
- **Alarms** (separate from chat; the **Alarms** button in the left rail, or *Alarms…* in the tray menu). Set a time, label, repeat days (or once) and two wave colors. When it rings, the screen waves in those colors every few seconds and an alarm card offers **Snooze 5 min** / **Dismiss**; the Windows alarm sound can loop too. Stops by itself after 5 minutes. Alarms ring while the app runs (also from the tray). The time is set with counter-style ▲▼ boxes (click, mouse wheel, arrow keys or type).
- **Timers** (the *Timers* tab on the Alarms page). Set a duration (hours : minutes : seconds, or a preset like 5 min), start, pause, reset. At zero the screen waves like an alarm, with **Restart** / **Dismiss**.
- **GIFs and emoji stickers like Telegram.** GIFs play inline as silent loops (paused while the window is hidden). The **GIF** button has two tabs: **Emoji**, 24 built-in animated emoji stickers (sent small, without a bubble), and **Saved GIFs**: every GIF you send is saved there, right-click a received one → *Save GIF*, or *Add GIF…* from disk.
- **Screen ripple.** When a message arrives, two Material-style waves in the sender's two colors spread across the whole screen from the LED. Every message ripples, at most once every 3 s so a burst of messages doesn't flood the screen (configurable in Settings). The overlay never blocks clicks or takes focus, and also shows over full-screen apps such as Chrome Remote Desktop (Settings can skip full-screen apps instead). Turn all ripples off or on with **Ripples** in the left rail, the same signal-shaped button in the floating panel header, or the LED's right-click menu; block a single person with *Block ripples* in their chat.
- **Show / hide shortcut:** Ctrl+Alt+M opens the full window from anywhere and hides it again (change it in Settings).
- **Exit** only from the tray icon menu (or the LED's right-click menu).

The built-in animated emoji are [Noto Animated Emoji](https://googlefonts.github.io/noto-emoji-animation/) by Google, licensed under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/), resized to 128 px (`tools/fetch-emoji.ps1` downloads them, `tools/ShrinkGifs.cs` resizes them).

## Build & run

Requires the .NET 10 SDK on Windows.

```powershell
dotnet build MiniLocalChat.slnx -c Release
.\src\MiniLocalChat\bin\Release\net10.0-windows\MiniLocalChat.exe
```

On first start Windows Firewall asks whether to allow the app on the network. Allow it, or the other computers can't see or reach it. If the prompt was dismissed, use **Settings → Allow through firewall** (asks for admin rights) on that computer.

To hand it to other PCs without installing .NET, publish one self-contained, compressed `.exe` (about 70 MB):

```powershell
dotnet publish src/MiniLocalChat/MiniLocalChat.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
```

`publish\MiniLocalChat.exe` is the only file to copy.

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
| 50506 | UDP | Presence heartbeat every 5 s (LAN broadcast + directly to known contacts) carrying the nickname. At startup and once a minute a "hello" also goes to every address on the local subnet, for networks that drop broadcasts. Someone is shown offline after ~17 s of silence. |

Everyone found this way appears in the chat list automatically; nobody needs to be added by hand.

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
