using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Ps2Ulix.Gui.Controls;

/// <summary>
/// A disc drawn with a conic gradient, standing in where a game has no cover and spinning during jobs.
/// CDs are silver, DVDs carry the purple dye of a burned disc; <see cref="Seed"/> gives each game its own sheen.
/// </summary>
public sealed class DiscArt : Control
{
    public static readonly StyledProperty<string?> SeedProperty = AvaloniaProperty.Register<DiscArt, string?>(nameof(Seed));

    public static readonly StyledProperty<bool> IsDvdProperty = AvaloniaProperty.Register<DiscArt, bool>(nameof(IsDvd));

    static DiscArt() => AffectsRender<DiscArt>(SeedProperty, IsDvdProperty);

    public string? Seed
    {
        get => GetValue(SeedProperty);
        set => SetValue(SeedProperty, value);
    }

    public bool IsDvd
    {
        get => GetValue(IsDvdProperty);
        set => SetValue(IsDvdProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var radius = Math.Min(Bounds.Width, Bounds.Height) / 2;
        if (radius <= 0)
            return;
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);

        // FNV-1a: string.GetHashCode changes between runs and the disc should not.
        var hash = 2166136261u;
        foreach (var c in Seed ?? "")
            hash = (hash ^ c) * 16777619u;
        var hue = hash % 360;

        var brush = new ConicGradientBrush { Center = RelativePoint.Center, Angle = hash % 90 };
        const int stops = 12;
        for (var i = 0; i <= stops; i++)
        {
            // Two bright flares per turn over a dark base, like light across a data layer.
            var flare = Math.Pow(Math.Abs(Math.Cos(Math.PI * 2 * i / stops)), 3);
            var stopHue = (hue + i * 30) % 360;
            var color = IsDvd
                ? HsvColor.ToRgb(260 + 40 * Math.Sin(i), 0.55 - 0.3 * flare, 0.28 + 0.6 * flare)
                : HsvColor.ToRgb(stopHue, 0.08 + 0.42 * flare, 0.42 + 0.56 * flare);
            brush.GradientStops.Add(new GradientStop(color, (double)i / stops));
        }

        context.DrawEllipse(brush, null, center, radius, radius);
        var groove = new Pen(new SolidColorBrush(Color.FromArgb(22, 255, 255, 255)), Math.Max(0.5, radius / 90));
        foreach (var ring in new[] { 0.94, 0.8, 0.62, 0.44 })
            context.DrawEllipse(null, groove, center, radius * ring, radius * ring);
        context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), Math.Max(0.5, radius / 70)), center, radius - 0.5, radius - 0.5);

        // Clear hub ring and spindle hole.
        context.DrawEllipse(new SolidColorBrush(Color.FromArgb(150, 200, 214, 240)), null, center, radius * 0.3, radius * 0.3);
        context.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x0A, 0x0E, 0x1C)), new Pen(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), Math.Max(0.5, radius / 80)), center, radius * 0.13, radius * 0.13);
    }
}
