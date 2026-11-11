using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace Ps2Ulix.Gui.Tests;

public sealed class RefreshTests
{
    /// <summary>Refresh used to scan drives on the UI thread, freezing the window on a slow drive.</summary>
    [AvaloniaFact]
    public async Task Refresh_RunsOffUiThread_AndKeepsList()
    {
        var window = new MainWindow { ShowIntro = false };
        var vm = new MainViewModel(new FakeDialogs());
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();

        await vm.RefreshDrivesAsync();
        var first = vm.Drives.ToList();
        await vm.RefreshDrivesAsync();

        Assert.NotEmpty(vm.Drives);
        Assert.Equal(first.Select(d => d.Path), vm.Drives.Select(d => d.Path));
        window.Close();
    }
}
