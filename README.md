# Mini Local Chat

A Teams-style private chat for computers on the same local network (e.g. a `192.168.1.x` Class C subnet). No server: every PC runs the app and talks to the others directly.

- **Identity = IP address.** Each computer keeps a fixed IP; its user picks a nickname, and everyone else sees that nickname for that IP.
- **1:1 and group chats** (pick several people in *New chat*), text, emoji, files and images (attach, drag & drop, or paste).
- **Teams-style messages.** Hover a message for quick reactions (👍 ❤️ 😂 😮 😢 🙏), **+** for 56 more, **Reply**, and ⋯ → Forward, Copy, Edit, Delete (edit/delete your own). Replies quote the original (click to jump to it); edits and deletes update for everyone. **Find in chat** with Ctrl+F.
- **Select and copy part of a message:** drag across the text (double-click a word, triple-click for all), then Ctrl+C or right-click → *Copy selected text*.
- **Typing indicator:** *Alice is typing…* with bouncing dots above the message box, and *typing…* in the chat list.
- **Color emoji** everywhere: messages, reactions, the emoji picker and chat previews (the message box itself still shows them in black and white while you type; that's a WPF limit).
- **Clear chat history** (⋯ in the chat header, or right-click the chat): removes all messages on your computer only.
- **Group chats:** rename, add people, leave (others are told), or delete the chat from your list.
- **Chat list sections:** Favorites, Online, Offline (☆ a chat to pin it), with search in both the full window and the floating panel.
- **Offline delivery.** Messages to someone who is offline are queued and sent automatically when they come back online. Your newest message shows **✓ Delivered**, then **✓✓ Seen** once they open the chat (in groups: *Seen by 2 of 3*).
- **Tray + floating LED.** Closing the window hides it to the tray. A small LED sits at the left or right edge of the screen:
  - dark: nothing new
  - glowing green with a number: unread messages
  - hover it: a mini panel lists only the chats with new messages, so you can read and reply without opening the app. **Pin** it (📌 in its header) to keep it open.
  - drag it up/down or to the other side; double-click it to open the full app
- **Personal colors and picture.** Each user picks two colors in Settings (the first is the avatar background; both are their ripple colors) and optionally a profile picture, which replaces the initials. Pictures are shared over the network automatically and cached.
- **View profile pictures full size, like Telegram:** click someone's picture in the chat header or next to their messages. Zoom with the mouse wheel, double-click or + / −, drag to move around, Esc or click outside to close. Pictures are now kept at up to 640 px (pick yours again in Settings to upload a sharp one; older ones are 128 px).
- **Online, Away, Offline:** green, amber or gray dot. You show as **Away** after 5 minutes without moving the mouse (change it in Settings) or while your screen is locked; Offline when the app isn't running.
- **Alarms** (separate from chat; the **Alarms** button in the left rail, or *Alarms…* in the tray menu). Set a time, label, repeat days (or once) and two wave colors. When it rings, the screen waves in those colors every few seconds and an alarm card offers **Snooze 5 min** / **Dismiss**; the Windows alarm sound can loop too. Stops by itself after 5 minutes. Alarms ring while the app runs (also from the tray). The time is set with counter-style ▲▼ boxes (click, mouse wheel, arrow keys or type).
- **Timers** (the *Timers* tab on the Alarms page). Set a duration (hours : minutes : seconds, or a preset like 5 min), start, pause, reset. At zero the screen waves like an alarm, with **Restart** / **Dismiss**.
- **Telegram stickers.** The **GIF** button opens a panel whose first tab is **Stickers**: real Telegram animated stickers (.tgs files), played smoothly with transparency. One comes built in (`Assets\Stickers`); add more with *Add sticker…* (e.g. .tgs files downloaded from Telegram), drop a .tgs into a chat, or right-click a sticker someone sent → *Save sticker*. Stickers you send are saved too. In chats a sticker plays a few times and then rests (to save CPU); hover it to play it again. The receiver needs version 1.3 or later.
- **GIFs and emoji stickers like Telegram.** GIFs play inline as silent loops (paused while the window is hidden). The panel's other tabs: **Emoji**, 24 built-in animated emoji stickers (sent small, without a bubble), and **Saved GIFs**: every GIF you send is saved there, right-click a received one → *Save GIF*, or *Add GIF…* from disk.
- **Screen ripple.** When a message arrives, two Material-style waves in the sender's two colors spread across the whole screen from the LED. Every message ripples, at most once every 3 s so a burst of messages doesn't flood the screen (configurable in Settings). The overlay never blocks clicks or takes focus, and also shows over full-screen apps such as Chrome Remote Desktop (Settings can skip full-screen apps instead). Turn all ripples off or on with **Ripples** in the left rail, the same signal-shaped button in the floating panel header, or the LED's right-click menu; block a single person with *Block ripples* in their chat.
- **Reminder ripples:** while messages are unread, a small ripple in the sender's colors spreads from your mouse pointer every 10 seconds, so a missed ripple isn't the end of it. Turning ripples off, or blocking ripples from someone, also stops their reminders; none while you're Away. (Settings → *New message alert*.)
- **Show / hide shortcut:** Ctrl+Alt+M opens the full window from anywhere and hides it again (change it in Settings).
- **Exit** only from the tray icon menu (or the LED's right-click menu).

Telegram stickers are played with [SkiaSharp](https://github.com/mono/SkiaSharp) / Skottie (MIT). Color emoji are drawn with [Emoji.Wpf](https://github.com/samhocevar/emoji.wpf) (WTFPL) from the Segoe UI Emoji font.

The built-in animated emoji are [Noto Animated Emoji](https://googlefonts.github.io/noto-emoji-animation/) by Google, licensed under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/), resized to 128 px (`tools/fetch-emoji.ps1` downloads them, `tools/ShrinkGifs.cs` resizes them).

## Build & run

Requires the .NET 10 SDK on Windows.

```powershell
dotnet build MiniLocalChat.slnx -c Release
.\src\MiniLocalChat\bin\Release\net10.0-windows\MiniLocalChat.exe
```

On first start Windows Firewall asks whether to allow the app on the network. Allow it, or the other computers can't see or reach it. If the prompt was dismissed, use **Settings → Allow through firewall** (asks for admin rights) on that computer.

To hand it to other PCs without installing .NET, publish one self-contained, compressed `.exe` (about 85 MB), signed for the auto-update:

```powershell
powershell -ExecutionPolicy Bypass -File tools\publish.ps1
```

It writes `publish\MiniLocalChat.exe` and `publish\MiniLocalChat.exe.sig`. For a first install, copy both into a folder the user can write to (e.g. `%LOCALAPPDATA%\MiniLocalChat`), not Program Files, so the app can update itself.

### Auto-update over the LAN (no internet)

Every computer tells the others which version it has. When one sees a higher version, it downloads it from that computer, checks it, and restarts into it as soon as nobody is using the app (main window closed to the tray, floating panel not in use, no alarm ringing, or you're Away); or when you quit. Each updated computer passes the new version on, so a release spreads by itself. Settings → *Updates* turns it off for one computer.

To release a new version:

1. Raise `<Version>` in `src/MiniLocalChat/MiniLocalChat.csproj` (only a **higher** version is installed).
2. Run `tools\publish.ps1`.
3. Start the new `publish\MiniLocalChat.exe` (with its `.sig` next to it) on any one computer that's on the network. The rest follows.

**Signing.** Only exes signed with the release key are accepted, so nobody else on the network can push a program (or an old version) to everyone. The private key was created once with `dotnet run tools/UpdateKey.cs -- new` and is in `%USERPROFILE%\.minilocalchat\update-signing-key.pem` on the publishing computer. **Back it up and keep it private**: without it no further updates can be signed (a new key means copying a build with the new public key to every computer by hand once). The public key is built into `Services/Updater.cs`. Development builds (`dotnet run` / `bin\`) neither update nor hand out updates.

Computers on version 1.4 or older don't have the updater yet: give them 1.5 by hand once.
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
