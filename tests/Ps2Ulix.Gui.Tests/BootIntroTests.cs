using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Ps2Ulix.Gui.Controls;

namespace Ps2Ulix.Gui.Tests;

public sealed class BootIntroTests
{
    private static (MainWindow Window, BootIntro Intro, Panel Layer) Open()
    {
        var window = new MainWindow();
        window.DataContext = new MainViewModel(new FakeDialogs());
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, window.FindControl<BootIntro>("Intro")!, window.FindControl<Panel>("IntroLayer")!);
    }

    private static void Settle()
    {
        for (var i = 0; i < 40; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void AnyKey_SkipsIntro()
    {
        var (window, intro, layer) = Open();
        Assert.True(intro.IsPlaying);

        window.KeyPress(Key.A, RawInputModifiers.None, PhysicalKey.A, "a");
        Settle();

        Assert.False(intro.IsPlaying);
        Assert.Equal(0, layer.Opacity);
        window.Close();
    }

    [AvaloniaFact]
    public void Click_SkipsIntro()
    {
        var (window, intro, _) = Open();
        Assert.True(intro.IsPlaying);

        window.MouseDown(new Point(400, 300), MouseButton.Left);
        window.MouseUp(new Point(400, 300), MouseButton.Left);
        Settle();

        Assert.False(intro.IsPlaying);
        window.Close();
    }

    [Fact]
    public void Title_ShowsInTheMiddle_GoneAtEnd()
    {
        Assert.Equal(0, BootIntro.TitleOpacityAt(0.5));
        Assert.Equal(1, BootIntro.TitleOpacityAt(BootIntro.Duration * 0.75), 3);
        Assert.Equal(0, BootIntro.TitleOpacityAt(BootIntro.Duration), 3);
    }
}
