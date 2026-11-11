using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Ps2Ulix.Gui.Controls;

public enum DeviceKind
{
    Usb,
    Disk,
    Folder,
}

/// <summary>Glossy device icon for the browser screen: a USB stick, a hard disk, or a folder.</summary>
public sealed class DeviceArt : Control
{
    public static readonly StyledProperty<DeviceKind> KindProperty = AvaloniaProperty.Register<DeviceArt, DeviceKind>(nameof(Kind));

    static DeviceArt() => AffectsRender<DeviceArt>(KindProperty);

    public DeviceKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var s = Math.Min(Bounds.Width, Bounds.Height);
        if (s <= 0)
            return;
        var box = new Rect((Bounds.Width - s) / 2, (Bounds.Height - s) / 2, s, s);
        switch (Kind)
        {
            case DeviceKind.Usb: DrawUsb(context, box); break;
            case DeviceKind.Disk: DrawDisk(context, box); break;
            default: DrawFolder(context, box); break;
        }
    }

    private static IBrush Vertical(params (string Color, double Offset)[] stops)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        };
        foreach (var (color, offset) in stops)
            brush.GradientStops.Add(new GradientStop(Color.Parse(color), offset));
        return brush;
    }

    private static Rect Part(Rect box, double x, double y, double w, double h) =>
        new(box.X + box.Width * x, box.Y + box.Height * y, box.Width * w, box.Height * h);

    private static readonly IPen Rim = new Pen(new SolidColorBrush(Color.FromArgb(150, 220, 235, 255)), 1.2);

    private static void Gloss(DrawingContext context, Rect body, double radius)
    {
        var shine = new Rect(body.X + 3, body.Y + 3, body.Width - 6, body.Height * 0.42);
        context.DrawRectangle(Vertical(("#55FFFFFF", 0), ("#00FFFFFF", 1)), null, new RoundedRect(shine, radius * 0.8));
    }

    private static void DrawUsb(DrawingContext context, Rect box)
    {
        // Metal plug, then the body.
        var plug = Part(box, 0.38, 0.06, 0.24, 0.22);
        context.DrawRectangle(Vertical(("#F2F6FF", 0), ("#8E9AB5", 1)), Rim, new RoundedRect(plug, 2));
        context.DrawRectangle(new SolidColorBrush(Color.Parse("#3A4766")), null, Part(box, 0.43, 0.11, 0.05, 0.05));
        context.DrawRectangle(new SolidColorBrush(Color.Parse("#3A4766")), null, Part(box, 0.52, 0.11, 0.05, 0.05));

        var body = Part(box, 0.3, 0.26, 0.4, 0.66);
        var radius = box.Width * 0.06;
        context.DrawRectangle(Vertical(("#7FB6FF", 0), ("#2C63C9", 0.55), ("#14326E", 1)), Rim, new RoundedRect(body, radius));
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), null, new RoundedRect(Part(box, 0.36, 0.52, 0.28, 0.2), 3));
        context.DrawEllipse(new SolidColorBrush(Color.Parse("#9DFFC9")), null, new Point(box.X + box.Width * 0.5, box.Y + box.Height * 0.84), box.Width * 0.022, box.Width * 0.022);
        Gloss(context, body, radius);
    }

    private static void DrawDisk(DrawingContext context, Rect box)
    {
        // Slab seen slightly from above: top face, then front.
        var top = new StreamGeometry();
        using (var g = top.Open())
        {
            g.BeginFigure(new Point(box.X + box.Width * 0.16, box.Y + box.Height * 0.4), true);
            g.LineTo(new Point(box.X + box.Width * 0.26, box.Y + box.Height * 0.26));
            g.LineTo(new Point(box.X + box.Width * 0.94, box.Y + box.Height * 0.26));
            g.LineTo(new Point(box.X + box.Width * 0.84, box.Y + box.Height * 0.4));
            g.EndFigure(true);
        }
        context.DrawGeometry(Vertical(("#C9D6F2", 0), ("#7C8DB4", 1)), Rim, top);

        var front = Part(box, 0.16, 0.4, 0.68, 0.36);
        context.DrawRectangle(Vertical(("#5A6A8F", 0), ("#273350", 0.6), ("#141B2E", 1)), Rim, new RoundedRect(front, 3));
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), null, Part(box, 0.22, 0.47, 0.34, 0.05));
        context.DrawEllipse(new SolidColorBrush(Color.Parse("#7CC4FF")), null, new Point(box.X + box.Width * 0.76, box.Y + box.Height * 0.66), box.Width * 0.025, box.Width * 0.025);
        Gloss(context, front, 3);
    }

    private static void DrawFolder(DrawingContext context, Rect box)
    {
        var tab = new StreamGeometry();
        using (var g = tab.Open())
        {
            g.BeginFigure(new Point(box.X + box.Width * 0.14, box.Y + box.Height * 0.3), true);
            g.LineTo(new Point(box.X + box.Width * 0.42, box.Y + box.Height * 0.3));
            g.LineTo(new Point(box.X + box.Width * 0.48, box.Y + box.Height * 0.37));
            g.LineTo(new Point(box.X + box.Width * 0.14, box.Y + box.Height * 0.37));
            g.EndFigure(true);
        }
        context.DrawGeometry(Vertical(("#FFE29A", 0), ("#E0A43C", 1)), Rim, tab);
        var body = Part(box, 0.14, 0.36, 0.72, 0.46);
        context.DrawRectangle(Vertical(("#FFD27A", 0), ("#E39A2E", 0.6), ("#A9661B", 1)), Rim, new RoundedRect(body, 4));
        Gloss(context, body, 4);
    }
}
