using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Ps2Ulix.Gui.Controls;

public enum SegmentKind
{
    Ul,
    Iso,
    Zso,
    Other,
}

/// <param name="Item">Game the segment belongs to; matched against <see cref="CapacityBar.Highlight"/>.</param>
public sealed record CapacitySegment(SegmentKind Kind, long Bytes, object? Item = null);

/// <summary>The drive as one bar: a slice per game, other files, and free space. The selected game lights up.</summary>
public sealed class CapacityBar : Control
{
    public static readonly StyledProperty<long> TotalProperty = AvaloniaProperty.Register<CapacityBar, long>(nameof(Total));

    public static readonly StyledProperty<IReadOnlyList<CapacitySegment>?> SegmentsProperty =
        AvaloniaProperty.Register<CapacityBar, IReadOnlyList<CapacitySegment>?>(nameof(Segments));

    public static readonly StyledProperty<object?> HighlightProperty = AvaloniaProperty.Register<CapacityBar, object?>(nameof(Highlight));

    private static readonly Dictionary<SegmentKind, Color> Colors = new()
    {
        [SegmentKind.Ul] = Color.Parse("#7DB6FF"),
        [SegmentKind.Iso] = Color.Parse("#B596FF"),
        [SegmentKind.Zso] = Color.Parse("#5EE6A8"),
        [SegmentKind.Other] = Color.Parse("#3A4360"),
    };

    static CapacityBar() => AffectsRender<CapacityBar>(TotalProperty, SegmentsProperty, HighlightProperty);

    public long Total
    {
        get => GetValue(TotalProperty);
        set => SetValue(TotalProperty, value);
    }

    public IReadOnlyList<CapacitySegment>? Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public object? Highlight
    {
        get => GetValue(HighlightProperty);
        set => SetValue(HighlightProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        var radius = bounds.Height / 2;
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(26, 255, 255, 255)), null, new RoundedRect(bounds, radius));
        if (Total <= 0 || Segments is null)
            return;

        using (context.PushClip(new RoundedRect(bounds, radius)))
        {
            var x = 0.0;
            foreach (var segment in Segments)
            {
                var width = bounds.Width * segment.Bytes / Total;
                if (width <= 0)
                    continue;
                var color = Colors[segment.Kind];
                var lit = Highlight is not null && ReferenceEquals(segment.Item, Highlight);
                var faded = Highlight is not null && !lit && segment.Item is not null;
                var brush = new SolidColorBrush(lit ? Avalonia.Media.Colors.White : color, faded ? 0.45 : 1);
                // A hairline gap keeps slices of the same format apart, but tiny games stay visible.
                context.DrawRectangle(brush, null, new Rect(x, 0, Math.Max(width - 1, 1.5), bounds.Height));
                x += width;
            }
        }
    }
}
