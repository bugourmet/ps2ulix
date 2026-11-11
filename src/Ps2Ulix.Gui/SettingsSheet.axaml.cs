using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Ps2Ulix.Core;

namespace Ps2Ulix.Gui;

/// <summary>
/// Edits an <see cref="OplGameConfig"/> in place like a system configuration list; result is true on Save.
/// Up/Down moves between rows, Left/Right changes the value, Esc cancels.
/// </summary>
public partial class SettingsSheet : UserControl
{
    private readonly TaskCompletionSource<bool> _result = new();
    private readonly OplGameConfig _config;
    private readonly CheckBox[] _modes;
    private readonly Control[] _rows;
    private int _dma;

    // Needed by the XAML loader and previewer.
    public SettingsSheet() : this("", new OplGameConfig())
    {
    }

    public SettingsSheet(string title, OplGameConfig config)
    {
        _config = config;
        InitializeComponent();
        TitleText.Text = title;
        _modes = [Mode1, Mode2, Mode3, Mode4, Mode5, Mode6];
        for (var i = 0; i < _modes.Length; i++)
            _modes[i].IsChecked = config.Compatibility.HasFlag((CompatModes)(1 << i));
        _dma = config.Dma;
        ShowDma();
        Vmc0Box.Text = config.Vmc0;
        Vmc1Box.Text = config.Vmc1;
        AltStartupBox.Text = config.AltStartup;
        _rows = [.. _modes, DmaRow, Vmc0Box, Vmc1Box, AltStartupBox, SaveButton];
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        Loaded += (_, _) => Mode1.Focus();
    }

    public Task<bool> Result => _result.Task;

    private void ShowDma() => DmaValue.Text = OplGameConfig.DmaModeNames[_dma];

    private void StepDma(int step)
    {
        var count = OplGameConfig.DmaModeNames.Length;
        _dma = (_dma + step + count) % count;
        ShowDma();
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        var row = Array.FindIndex(_rows, r => ReferenceEquals(r, focused));
        switch (e.Key)
        {
            case Key.Escape:
                _result.TrySetResult(false);
                break;
            case Key.Up or Key.Down when row >= 0 || CancelButton.IsFocused:
                var from = row >= 0 ? row : _rows.Length - 1;
                _rows[Math.Clamp(from + (e.Key == Key.Down ? 1 : -1), 0, _rows.Length - 1)].Focus();
                break;
            case Key.Left or Key.Right when focused is CheckBox box:
                box.IsChecked = box.IsChecked != true;
                break;
            case Key.Left or Key.Right when ReferenceEquals(focused, DmaRow):
                StepDma(e.Key == Key.Right ? 1 : -1);
                break;
            case Key.Left or Key.Right when SaveButton.IsFocused || CancelButton.IsFocused:
                (SaveButton.IsFocused ? CancelButton : SaveButton).Focus();
                break;
            case Key.Enter when focused is CheckBox box:
                box.IsChecked = box.IsChecked != true;
                break;
            case Key.Enter when CancelButton.IsFocused:
                _result.TrySetResult(false);
                break;
            case Key.Enter when ReferenceEquals(focused, DmaRow):
                StepDma(1);
                break;
            case Key.Enter:
                Save();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void OnDmaClick(object? sender, RoutedEventArgs e) => StepDma(1);

    private void Save()
    {
        var modes = CompatModes.None;
        for (var i = 0; i < _modes.Length; i++)
        {
            if (_modes[i].IsChecked == true)
                modes |= (CompatModes)(1 << i);
        }
        _config.Compatibility = modes;
        _config.Dma = _dma;
        _config.Vmc0 = Vmc0Box.Text?.Trim();
        _config.Vmc1 = Vmc1Box.Text?.Trim();
        _config.AltStartup = AltStartupBox.Text?.Trim();
        _result.TrySetResult(true);
    }

    private void OnSave(object? sender, RoutedEventArgs e) => Save();

    private void OnCancel(object? sender, RoutedEventArgs e) => _result.TrySetResult(false);
}
