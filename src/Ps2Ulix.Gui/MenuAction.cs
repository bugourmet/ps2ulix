using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Ps2Ulix.Gui;

/// <summary>One entry in a console-style menu (game actions, drive options). Label and state are re-read on <see cref="Refresh"/>.</summary>
public sealed class MenuAction(Func<string> label, Geometry? icon, Action run, Func<bool>? canRun = null, bool isDanger = false)
    : ObservableObject
{
    public string Label => label();

    public Geometry? Icon { get; } = icon;

    public bool IsEnabled => canRun?.Invoke() ?? true;

    public bool IsDanger { get; } = isDanger;

    public void Run()
    {
        if (IsEnabled)
            run();
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(IsEnabled));
    }
}
