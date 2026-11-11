using Avalonia;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;

namespace Ps2Ulix.Gui.Controls;

/// <summary>Composition-thread animations for list items: staggered entry, and gliding when the layout reflows.</summary>
public static class Motion
{
    private static readonly TimeSpan GlideDuration = TimeSpan.FromMilliseconds(260);
    private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(320);
    private const int StaggerMs = 22;
    private const int MaxStaggerMs = 420;

    /// <summary>Fades an item in, delayed by its index, then lets later moves animate.</summary>
    public static void Enter(Control item, int index)
    {
        if (ElementComposition.GetElementVisual(item) is { } visual)
        {
            Start(visual, index);
            return;
        }

        void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            item.AttachedToVisualTree -= OnAttached;
            if (ElementComposition.GetElementVisual(item) is { } attached)
                Start(attached, index);
        }

        item.AttachedToVisualTree += OnAttached;
    }

    private static void Start(CompositionVisual visual, int index)
    {
        var compositor = visual.Compositor;
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0f, 0f);
        fade.InsertKeyFrame(1f, 1f);
        fade.Duration = FadeDuration;
        fade.DelayTime = TimeSpan.FromMilliseconds(Math.Min(index * StaggerMs, MaxStaggerMs));
        fade.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        visual.StartAnimation("Opacity", fade);

        var glide = compositor.CreateVector3DKeyFrameAnimation();
        glide.Target = "Offset";
        glide.InsertExpressionKeyFrame(1f, "this.FinalValue");
        glide.Duration = GlideDuration;
        var implicitAnimations = compositor.CreateImplicitAnimationCollection();
        implicitAnimations["Offset"] = glide;

        // Only after first layout, or new items would fly in from the corner.
        Avalonia.Threading.Dispatcher.UIThread.Post(() => visual.ImplicitAnimations = implicitAnimations,
            Avalonia.Threading.DispatcherPriority.Background);
    }
}
