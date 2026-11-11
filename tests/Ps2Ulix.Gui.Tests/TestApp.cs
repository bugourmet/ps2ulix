using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(Ps2Ulix.Gui.Tests.TestApp))]

namespace Ps2Ulix.Gui.Tests;

public static class TestApp
{
    // Skia drawing (not headless stubs) so CaptureRenderedFrame gives a real picture.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .WithInterFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
