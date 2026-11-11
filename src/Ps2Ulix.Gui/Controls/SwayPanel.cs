using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Ps2Ulix.Gui.Controls;

/// <summary>
/// Floats its content like a menu icon: a slow turn in real 3D and a gentle bob. The active
/// (highlighted) one turns further, faster, and grows. All panels share one 30 fps clock.
/// </summary>
public sealed class SwayPanel : Panel
{
    public static readonly StyledProperty<bool> IsActiveProperty = AvaloniaProperty.Register<SwayPanel, bool>(nameof(IsActive));

    public static readonly StyledProperty<double> AmplitudeProperty = AvaloniaProperty.Register<SwayPanel, double>(nameof(Amplitude), 8);

    public static readonly StyledProperty<double> ActiveAmplitudeProperty = AvaloniaProperty.Register<SwayPanel, double>(nameof(ActiveAmplitude), 26);

    public static readonly StyledProperty<double> ActiveScaleProperty = AvaloniaProperty.Register<SwayPanel, double>(nameof(ActiveScale), 1.14);

    private static readonly HashSet<SwayPanel> Live = [];
    private static readonly DateTime Epoch = DateTime.UtcNow;
    private static DispatcherTimer? _timer;

    private readonly MatrixTransform _transform = new();
    private readonly Rotate3DTransform _turn = new() { Depth = 760 };
    private readonly double _phase = Random.Shared.NextDouble() * Math.Tau;
    private double _scale = 1;
    private double _amplitude;

    public SwayPanel()
    {
        RenderTransform = _transform;
        RenderTransformOrigin = RelativePoint.Center;
    }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public double Amplitude
    {
        get => GetValue(AmplitudeProperty);
        set => SetValue(AmplitudeProperty, value);
    }

    public double ActiveAmplitude
    {
        get => GetValue(ActiveAmplitudeProperty);
        set => SetValue(ActiveAmplitudeProperty, value);
    }

    public double ActiveScale
    {
        get => GetValue(ActiveScaleProperty);
        set => SetValue(ActiveScaleProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _amplitude = Amplitude;
        Live.Add(this);
        if (_timer is null)
        {
            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Background, (_, _) => TickAll());
            _timer.Start();
        }
        Update((DateTime.UtcNow - Epoch).TotalSeconds);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Live.Remove(this);
        if (Live.Count == 0 && _timer is not null)
        {
            _timer.Stop();
            _timer = null;
        }
        base.OnDetachedFromVisualTree(e);
    }

    private static void TickAll()
    {
        var t = (DateTime.UtcNow - Epoch).TotalSeconds;
        foreach (var panel in Live)
            panel.Update(t);
    }

    private void Update(double t)
    {
        // Ease toward the target so selection changes swell instead of snapping.
        var active = IsActive;
        _scale += ((active ? ActiveScale : 1) - _scale) * 0.2;
        _amplitude += ((active ? ActiveAmplitude : Amplitude) - _amplitude) * 0.12;
        var speed = active ? 1.25 : 0.7;
        _turn.AngleY = _amplitude * Math.Sin(t * speed + _phase);
        var bob = (active ? 5 : 3) * Math.Sin(t * 1.1 + _phase * 1.7);
        _transform.Matrix = Matrix.CreateScale(_scale, _scale) * _turn.Value * Matrix.CreateTranslation(0, bob);
    }
}
