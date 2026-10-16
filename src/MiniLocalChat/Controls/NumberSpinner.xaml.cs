using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MiniLocalChat.Controls;

/// <summary>
/// A counter-style number (clock digits): ▲ / ▼, the mouse wheel, the arrow keys, or typing.
/// Wraps around (59 → 00) like a clock.
/// </summary>
public partial class NumberSpinner : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(int), typeof(NumberSpinner),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((NumberSpinner)d).OnValueChanged()));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(int), typeof(NumberSpinner), new PropertyMetadata(59));

    public NumberSpinner()
    {
        InitializeComponent();
        Box.Text = Value.ToString("00");
    }

    public event EventHandler? ValueChanged;

    public int Value
    {
        get => (int)GetValue(ValueProperty);
        set => SetValue(ValueProperty, Wrap(value));
    }

    /// <summary>Largest value (minimum is 0), e.g. 23 for hours, 59 for minutes.</summary>
    public int Maximum
    {
        get => (int)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    private int Wrap(int value)
    {
        var range = Maximum + 1;
        return ((value % range) + range) % range;
    }

    private void OnValueChanged()
    {
        if (!Box.IsKeyboardFocused)
        {
            Box.Text = Value.ToString("00");
        }

        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Step(int delta)
    {
        Commit();
        Value = Value + delta;
        Box.Text = Value.ToString("00");
        Box.SelectAll();
    }

    private void OnUp(object sender, RoutedEventArgs e) => Step(+1);

    private void OnDown(object sender, RoutedEventArgs e) => Step(-1);

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        Step(e.Delta > 0 ? 1 : -1);
        e.Handled = true;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up:
                Step(+1);
                e.Handled = true;
                break;
            case Key.Down:
                Step(-1);
                e.Handled = true;
                break;
            case Key.PageUp:
                Step(+10);
                e.Handled = true;
                break;
            case Key.PageDown:
                Step(-10);
                e.Handled = true;
                break;
            case Key.Enter:
                Commit();
                break;
        }
    }

    // Digits only.
    private void OnTextInput(object sender, TextCompositionEventArgs e) => e.Handled = !e.Text.All(char.IsDigit);

    private void OnGotFocus(object sender, KeyboardFocusChangedEventArgs e) => Dispatcher.BeginInvoke(Box.SelectAll);

    private void OnLostFocus(object sender, KeyboardFocusChangedEventArgs e) => Commit();

    /// <summary>Takes what was typed (clamped to the range).</summary>
    private void Commit()
    {
        if (int.TryParse(Box.Text, out var typed))
        {
            SetValue(ValueProperty, Math.Clamp(typed, 0, Maximum));
        }

        Box.Text = Value.ToString("00");
    }
}
