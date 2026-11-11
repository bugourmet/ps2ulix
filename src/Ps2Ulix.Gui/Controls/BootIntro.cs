using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Ps2Ulix.Gui.Controls;

/// <summary>
/// Power-on scene: from black, a tilted cloud of light motes swirls in and collapses into a bright
/// core, which flares as the title appears; then everything fades. Original take on a console boot,
/// drawn from scratch each frame. <see cref="Time"/> can also be set directly (tests, previews).
/// </summary>
public sealed class BootIntro : Control
{
    public static readonly StyledProperty<double> TimeProperty = AvaloniaProperty.Register<BootIntro, double>(nameof(Time));

    /// <summary>Whole scene, fade-out included.</summary>
    public const double Duration = 3.6;

    /// <summary>The choreography below is timed in these units and stretched to <see cref="Duration"/>.</summary>
    private const double SceneLength = 2.4;

    private const int MoteCount = 1100;
    private const double Tilt = 0.32; // vertical squash: the swirl lies in a plane seen from slightly above
    private const int AlphaLevels = 24;

    private static readonly Mote[] Motes = CreateMotes();
    private static readonly IBrush[] MoteBrushes = Enumerable.Range(0, AlphaLevels)
        .Select(i => (IBrush)new SolidColorBrush(Color.FromRgb(190, 220, 255), (i + 1.0) / AlphaLevels).ToImmutable())
        .ToArray();

    private Stopwatch? _clock;

    static BootIntro() => AffectsRender<BootIntro>(TimeProperty);

    public BootIntro() => IsHitTestVisible = false;

    public event EventHandler? Completed;

    public double Time
    {
        get => GetValue(TimeProperty);
        set => SetValue(TimeProperty, value);
    }

    public bool IsPlaying => _clock is not null;

    /// <summary>Title comes in with the flare and leaves with the scene.</summary>
    public static double TitleOpacityAt(double time)
    {
        var t = SceneTime(time);
        return Smooth(1.05, 1.45, t) * (1 - Smooth(SceneLength - 0.5, SceneLength - 0.1, t));
    }

    private static double SceneTime(double time) => time * SceneLength / Duration;

    public void Play()
    {
        _clock = Stopwatch.StartNew();
        RequestFrame();
    }

    public void Skip()
    {
        if (_clock is null)
            return;
        _clock = null;
        Time = Duration;
        Completed?.Invoke(this, EventArgs.Empty);
    }

    private void RequestFrame() => TopLevel.GetTopLevel(this)?.RequestAnimationFrame(_ =>
    {
        if (_clock is null)
            return;
        Time = _clock.Elapsed.TotalSeconds;
        if (Time >= Duration)
            Skip();
        else
            RequestFrame();
    });

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0)
            return;
        var t = SceneTime(Time);
        context.FillRectangle(Brushes.Black, new Rect(0, 0, w, h));

        var fade = Smooth(0, 0.25, t) * (1 - Smooth(SceneLength - 0.45, SceneLength, t));
        using var _ = context.PushOpacity(fade);

        var center = new Point(w / 2, h * 0.44);
        var scale = Math.Min(w, h) * 0.62;
        var gather = Smooth(0.1, 1.15, t);           // motes fall inward
        var flare = Smooth(0.95, 1.15, t) * (1 - Smooth(1.15, 1.9, t)); // brief burst when they meet

        // Deep blue haze behind everything, brightening as the core forms.
        DrawGlow(context, center, scale * (1.1 - 0.3 * gather), Color.FromRgb(20, 50, 130), 0.25 + 0.35 * gather);

        foreach (var m in Motes)
        {
            var radius = m.Radius * (1 - 0.86 * gather) + 0.035;
            // Inner motes orbit faster, like matter falling into a well.
            var angle = m.Angle + t * m.Spin / (0.25 + radius);
            var x = Math.Cos(angle) * radius;
            var y = Math.Sin(angle) * radius * Tilt + m.Lift * (1 - gather);
            var depth = 0.6 + 0.4 * Math.Sin(angle); // nearer side of the disc is brighter
            var alpha = Math.Clamp(m.Brightness * depth * (0.35 + 0.65 * gather) * Smooth(m.Delay, m.Delay + 0.35, t), 0, 1);
            if (alpha < 0.02)
                continue;
            var size = m.Size * (0.7 + 0.5 * depth);
            context.DrawEllipse(MoteBrushes[(int)(alpha * (AlphaLevels - 1))], null,
                new Point(center.X + x * scale, center.Y + y * scale), size, size);
        }

        // The core, and its flare when the swirl collapses into it.
        DrawGlow(context, center, scale * (0.10 + 0.05 * gather + 0.5 * flare), Color.FromRgb(150, 200, 255), 0.6 * gather + 0.5 * flare);
        DrawGlow(context, center, scale * (0.03 + 0.02 * gather + 0.08 * flare), Colors.White, 0.9 * gather);
        if (flare > 0.01)
        {
            // Horizontal lens streak.
            var streak = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0, 160, 210, 255), 0),
                    new GradientStop(Color.FromArgb((byte)(200 * flare), 220, 235, 255), 0.5),
                    new GradientStop(Color.FromArgb(0, 160, 210, 255), 1),
                },
            };
            var half = w * 0.45 * (0.4 + flare);
            context.FillRectangle(streak, new Rect(center.X - half, center.Y - 1.5, half * 2, 3));
        }
    }

    private static void DrawGlow(DrawingContext context, Point center, double radius, Color color, double strength)
    {
        if (radius <= 0 || strength <= 0.01)
            return;
        var brush = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromArgb((byte)(255 * Math.Clamp(strength, 0, 1)), color.R, color.G, color.B), 0),
                new GradientStop(Color.FromArgb((byte)(90 * Math.Clamp(strength, 0, 1)), color.R, color.G, color.B), 0.35),
                new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1),
            },
        };
        context.DrawEllipse(brush, null, center, radius, radius);
    }

    private static double Smooth(double a, double b, double x)
    {
        var v = Math.Clamp((x - a) / (b - a), 0, 1);
        return v * v * (3 - 2 * v);
    }

    private static Mote[] CreateMotes()
    {
        var random = new Random(2000);
        return Enumerable.Range(0, MoteCount).Select(_ =>
        {
            // Denser toward the middle, a few strays far out.
            var r = 0.12 + Math.Pow(random.NextDouble(), 1.6) * 1.1;
            return new Mote(
                Radius: r,
                Angle: random.NextDouble() * Math.Tau,
                Spin: 0.9 + random.NextDouble() * 0.8,
                Lift: (random.NextDouble() - 0.5) * 0.25,
                Size: 0.6 + random.NextDouble() * 1.3,
                Brightness: 0.35 + random.NextDouble() * 0.65,
                Delay: random.NextDouble() * 0.35);
        }).ToArray();
    }

    private sealed record Mote(double Radius, double Angle, double Spin, double Lift, double Size, double Brightness, double Delay);
}
