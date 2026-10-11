using System.Windows;
using System.Windows.Threading;
using MiniLocalChat.Infrastructure;
using MiniLocalChat.Models;

namespace MiniLocalChat.Views;

/// <summary>
/// Floating mode: the LED plus the mini panel that appears while hovering it.
/// The panel stays open while the mouse is over the LED or the panel, or while the panel has focus (typing).
/// </summary>
public sealed class FloatingController
{
    private readonly LedWindow _led = new();
    private readonly MiniChatWindow _mini = new();
    private readonly DispatcherTimer _showTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };

    public FloatingController()
    {
        _showTimer.Tick += (_, _) =>
        {
            _showTimer.Stop();
            ShowMini(activate: false);
        };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            TryHideMini();
        };

        _led.MouseEnter += (_, _) =>
        {
            _hideTimer.Stop();
            if (!_mini.IsVisible && !_led.IsDragging)
            {
                _showTimer.Start();
            }
        };
        _led.MouseLeave += (_, _) =>
        {
            _showTimer.Stop();
            _hideTimer.Start();
        };
        _led.Clicked += (_, _) => ShowMini(activate: true);
        _led.DoubleClicked += (_, _) => App.Current.ShowMain();
        _led.DragStarted += (_, _) =>
        {
            _showTimer.Stop();
            HideMini();
        };
        _led.OpenRequested += (_, _) => App.Current.ShowMain();
        _led.ExitRequested += (_, _) => App.Current.ExitApp();

        _mini.MouseEnter += (_, _) => _hideTimer.Stop();
        _mini.MouseLeave += (_, _) => _hideTimer.Start();
        _mini.Deactivated += (_, _) => _hideTimer.Start();
        _mini.OpenAppRequested += conversation => App.Current.ShowMain(conversation);
    }

    public bool IsActive { get; private set; }

    public bool IsShowing(Conversation conversation) => _mini.IsShowing(conversation);

    public void Enter()
    {
        IsActive = true;
        _led.ShowAtSavedPosition();
    }

    public void Exit()
    {
        IsActive = false;
        _showTimer.Stop();
        _hideTimer.Stop();
        HideMini();
        _led.Hide();
    }

    public void ApplySettings()
    {
        if (IsActive)
        {
            HideMini();
            _led.PlaceFromSettings();
        }
    }

    private void ShowMini(bool activate)
    {
        if (!IsActive)
        {
            return;
        }

        if (!_mini.IsVisible)
        {
            _mini.ShowList();
            PositionMini();
            _mini.Show();
        }

        if (activate)
        {
            _mini.Activate();
        }
    }

    private void TryHideMini()
    {
        if (DialogGuard.IsOpen || _led.IsMouseOver || _mini.IsMouseOver || _mini.IsActive)
        {
            return;
        }

        HideMini();
    }

    private void HideMini()
    {
        if (_mini.IsVisible)
        {
            _mini.Hide();
            _mini.ShowList();
        }
    }

    // Beside the LED, toward the middle of the screen, kept inside the work area.
    private void PositionMini()
    {
        const double overlap = 6;
        var area = _led.WorkArea;
        _mini.Left = _led.Side == LedSide.Left
            ? _led.Left + _led.Width - overlap
            : _led.Left - _mini.Width + overlap;
        var maxTop = Math.Max(area.Top, area.Bottom - _mini.Height);
        _mini.Top = Math.Clamp(_led.Top - 6, area.Top, maxTop);
    }
}
