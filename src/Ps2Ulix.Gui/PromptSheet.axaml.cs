using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Ps2Ulix.Core;

namespace Ps2Ulix.Gui;

/// <summary>
/// Console-style dialog for confirm / text input / format choice / report text, driven by <see cref="PromptOptions"/>.
/// Enter answers with the focused option, Esc cancels, Left/Right moves between the answers,
/// Up/Down in the text box moves the format choice.
/// </summary>
public partial class PromptSheet : UserControl
{
    private readonly TaskCompletionSource<PromptResult?> _result = new();

    // Needed by the XAML loader and previewer.
    public PromptSheet() : this(new PromptOptions("", ""))
    {
    }

    public PromptSheet(PromptOptions options)
    {
        Options = options;
        InitializeComponent();
        DataContext = this;
        InputBox.Text = options.Input;
        CdRadio.IsChecked = options.Media == UlMedia.Cd;
        DvdRadio.IsChecked = options.Media == UlMedia.Dvd;
        ChoiceBox.SelectedIndex = options.ChoiceIndex;
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        Loaded += (_, _) =>
        {
            if (HasInput)
            {
                InputBox.Focus();
                InputBox.SelectAll();
            }
            // Destructive questions start on Cancel, so a stray Enter does no harm.
            else if (options.ShowCancel && options.OkText == "Delete")
                CancelButton.Focus();
            else
                OkButton.Focus();
        };
    }

    public PromptOptions Options { get; }

    public bool HasInput => Options.Input is not null;

    public bool HasMedia => Options.Media is not null;

    public bool HasDetails => Options.Details is not null;

    public bool HasChoices => Options.Choices is not null;

    public Task<PromptResult?> Result => _result.Task;

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                _result.TrySetResult(null);
                break;
            case Key.Enter when CancelButton.IsFocused:
                _result.TrySetResult(null);
                break;
            case Key.Enter:
                Ok();
                break;
            case Key.Left or Key.Right when OkButton.IsFocused || CancelButton.IsFocused:
                if (Options.ShowCancel)
                    (OkButton.IsFocused ? CancelButton : OkButton).Focus();
                break;
            case Key.Up or Key.Down when HasChoices && InputBox.IsFocused:
                var count = Options.Choices!.Count;
                ChoiceBox.SelectedIndex = (Math.Max(0, ChoiceBox.SelectedIndex) + (e.Key == Key.Down ? 1 : count - 1)) % count;
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void Ok()
    {
        var media = HasMedia ? (DvdRadio.IsChecked == true ? UlMedia.Dvd : UlMedia.Cd) : (UlMedia?)null;
        _result.TrySetResult(new PromptResult(InputBox.Text?.Trim() ?? "", media, Math.Max(0, ChoiceBox.SelectedIndex)));
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Ok();

    private void OnCancel(object? sender, RoutedEventArgs e) => _result.TrySetResult(null);
}
