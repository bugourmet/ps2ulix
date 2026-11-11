using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using SkiaSharp;
using Vector3 = System.Numerics.Vector3;

namespace Ps2Ulix.Gui.Controls;

/// <summary>
/// Living backdrop: domain-warped fog, pillars of light and drifting motes, drawn on the composition
/// render thread so file jobs and layout can't stutter it. <see cref="Tint"/> follows the selected cover.
/// The fog is low frequency, so it is computed small on the CPU and upscaled. Not an SkSL shader: Skia's
/// raster path dies with an illegal instruction on runtime effects, which would kill the app without a GPU.
/// </summary>
public sealed class AmbientBackground : Control
{
    public static readonly StyledProperty<Color> TintProperty =
        AvaloniaProperty.Register<AmbientBackground, Color>(nameof(Tint), Color.FromRgb(0x4C, 0x8D, 0xFF));

    public static readonly StyledProperty<bool> EnergizedProperty =
        AvaloniaProperty.Register<AmbientBackground, bool>(nameof(Energized));

    private CompositionCustomVisual? _visual;
    private Window? _window;

    public AmbientBackground() => IsHitTestVisible = false;

    public Color Tint
    {
        get => GetValue(TintProperty);
        set => SetValue(TintProperty, value);
    }

    /// <summary>Fog moves faster and glows brighter while a job runs.</summary>
    public bool Energized
    {
        get => GetValue(EnergizedProperty);
        set => SetValue(EnergizedProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        var host = ElementComposition.GetElementVisual(this);
        if (host is null)
            return;
        _visual = host.Compositor.CreateCustomVisual(new Handler());
        ElementComposition.SetElementChildVisual(this, _visual);
        _visual.Size = new Vector(Bounds.Width, Bounds.Height);
        SendState();

        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is not null)
        {
            _window.Activated += OnWindowActivated;
            _window.Deactivated += OnWindowDeactivated;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_window is not null)
        {
            _window.Activated -= OnWindowActivated;
            _window.Deactivated -= OnWindowDeactivated;
            _window = null;
        }
        _visual?.SendHandlerMessage(Handler.Stop);
        ElementComposition.SetElementChildVisual(this, null);
        _visual = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty && _visual is not null)
            _visual.Size = new Vector(Bounds.Width, Bounds.Height);
        else if (change.Property == TintProperty || change.Property == EnergizedProperty)
            SendState();
    }

    private void SendState() => _visual?.SendHandlerMessage(new State(Tint, Energized));

    // No point burning CPU behind other windows.
    private void OnWindowActivated(object? sender, EventArgs e) => _visual?.SendHandlerMessage(Handler.Resume);

    private void OnWindowDeactivated(object? sender, EventArgs e) => _visual?.SendHandlerMessage(Handler.Pause);

    private sealed record State(Color Tint, bool Energized);

    private sealed class Handler : CompositionCustomVisualHandler
    {
        public const string Pause = "pause";
        public const string Resume = "resume";
        public const string Stop = "stop";

        private const double FrameSeconds = 1.0 / 24;
        private const int FogEveryFrames = 2; // slow enough that 12 fps of fog reads as smooth
        private const int FogScale = 6;
        private const int MaxFogWidth = 180;
        private const int MaxFogHeight = 112;
        private const int MoteCount = 56;
        private const int NoiseTile = 128;
        private const int HashSize = 256;

        private static readonly float[] Hash = CreateHash();

        private readonly (float X, float Y, float Size, float Speed, float Phase)[] _motes = CreateMotes();
        private SKBitmap? _fog;
        private int[] _pixels = [];
        private SKShader? _grain;
        private int _frame;
        private bool _paused;
        private bool _stopped;

        private Vector3 _tint = new(0.3f, 0.55f, 1f);
        private Vector3 _targetTint = new(0.3f, 0.55f, 1f);
        private float _energy;
        private float _targetEnergy;
        private double _phase = Random.Shared.NextDouble() * 500;
        private TimeSpan _lastTick;
        private TimeSpan _lastFrame;

        public override void OnMessage(object message)
        {
            switch (message)
            {
                case State state:
                    _targetTint = new Vector3(state.Tint.R, state.Tint.G, state.Tint.B) / 255f;
                    _targetEnergy = state.Energized ? 1 : 0;
                    Start();
                    break;
                case Pause:
                    _paused = true;
                    break;
                case Resume:
                    _paused = false;
                    Start();
                    break;
                case Stop:
                    _stopped = true;
                    _fog?.Dispose();
                    _grain?.Dispose();
                    _fog = null;
                    _grain = null;
                    break;
            }
        }

        private void Start()
        {
            if (!_stopped)
            {
                Invalidate();
                RegisterForNextAnimationFrameUpdate();
            }
        }

        public override void OnAnimationFrameUpdate()
        {
            if (_paused || _stopped)
                return;
            if ((CompositionNow - _lastFrame).TotalSeconds >= FrameSeconds)
            {
                _lastFrame = CompositionNow;
                Invalidate();
            }
            RegisterForNextAnimationFrameUpdate();
        }

        public override void OnRender(ImmediateDrawingContext context)
        {
            var feature = context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) as ISkiaSharpApiLeaseFeature;
            var bounds = GetRenderBounds();
            if (_stopped || feature is null || bounds.Width < 1 || bounds.Height < 1)
                return;

            var dt = Math.Min((CompositionNow - _lastTick).TotalSeconds, 0.25);
            _lastTick = CompositionNow;
            var blend = (float)Math.Min(1, dt * 2.2);
            _tint = Vector3.Lerp(_tint, _targetTint, blend);
            _energy += (_targetEnergy - _energy) * blend;
            _phase += dt * (1 + 2.5 * _energy);

            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;
            var w = (float)bounds.Width;
            var h = (float)bounds.Height;
            DrawFog(canvas, w, h);
            DrawMotes(canvas, w, h);
            DrawGrain(canvas, w, h);
        }

        private void DrawFog(SKCanvas canvas, float w, float h)
        {
            var fogW = Math.Clamp((int)(w / FogScale), 16, MaxFogWidth);
            var fogH = Math.Clamp((int)(h / FogScale), 16, MaxFogHeight);
            var fresh = false;
            if (_fog is null || _fog.Width != fogW || _fog.Height != fogH)
            {
                _fog?.Dispose();
                _fog = new SKBitmap(new SKImageInfo(fogW, fogH, SKColorType.Rgba8888, SKAlphaType.Opaque));
                _pixels = new int[fogW * fogH];
                fresh = true;
            }
            if (fresh || _frame++ % FogEveryFrames == 0)
            {
                ComputeFog(_pixels, fogW, fogH);
                Marshal.Copy(_pixels, 0, _fog.GetPixels(), _pixels.Length);
                _fog.NotifyPixelsChanged();
            }
            using var scale = new SKPaint { FilterQuality = SKFilterQuality.High };
            canvas.DrawBitmap(_fog, new SKRect(0, 0, w, h), scale);
        }

        private void ComputeFog(int[] pixels, int width, int height)
        {
            var t = (float)_phase * 0.035f;
            var aspect = (float)width / height;
            var fogGain = 0.34f + 0.16f * _energy;
            var pillarGain = 0.11f + 0.10f * _energy;
            var i = 0;
            for (var y = 0; y < height; y++)
            {
                var v = (y + 0.5f) / height;
                var floor = Smooth(0.05f, 1f, v);
                var glow = Smooth(0.6f, 1f, v);
                var py = v * 2.2f;
                for (var x = 0; x < width; x++)
                {
                    var u = (x + 0.5f) / width;
                    var px = u * aspect * 2.2f;

                    // Domain-warped fog.
                    var qx = Fbm(px + t, py);
                    var qy = Fbm(px + 3.1f, py - t * 1.3f + 3.1f);
                    var fog = Fbm(px + qx * 1.6f - t * 0.6f, py + qy * 1.6f + t * 0.4f);
                    fog *= fog;

                    // Pillars of light rising from the floor, fading upward.
                    var pillars = Smooth(0.58f, 1f, Noise(u * 15f + qx * 1.5f, t * 1.6f)) * floor;

                    var amount = fog * fogGain + pillars * pillarGain * (0.5f + fog);
                    var dx = u - 0.5f;
                    var dy = v - 0.55f;
                    var vignette = 1f - 0.9f * (dx * dx + dy * dy);
                    var r = (0.016f + _tint.X * amount + 0.02f * glow) * vignette;
                    var g = (0.024f + _tint.Y * amount + 0.03f * glow) * vignette;
                    var b = (0.055f + _tint.Z * amount + 0.07f * glow) * vignette;
                    pixels[i++] = unchecked((int)0xFF000000) | (ToByte(b) << 16) | (ToByte(g) << 8) | ToByte(r);
                }
            }
        }

        private static int ToByte(float value) => (int)(Math.Clamp(value, 0f, 1f) * 255f + 0.5f);

        private static float Smooth(float a, float b, float x)
        {
            var t = Math.Clamp((x - a) / (b - a), 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        private static float Fbm(float x, float y)
        {
            var value = 0.5f * Noise(x, y);
            value += 0.25f * Noise(x * 2.03f + 17f, y * 2.03f + 9f);
            return value + 0.125f * Noise(x * 4.12f + 43.5f, y * 4.12f + 27.3f);
        }

        /// <summary>Value noise over a hash table.</summary>
        private static float Noise(float x, float y)
        {
            var fx = MathF.Floor(x);
            var fy = MathF.Floor(y);
            var ix = (int)fx;
            var iy = (int)fy;
            var u = x - fx;
            var v = y - fy;
            u = u * u * (3f - 2f * u);
            v = v * v * (3f - 2f * v);
            var a = Lattice(ix, iy);
            var b = Lattice(ix + 1, iy);
            var c = Lattice(ix, iy + 1);
            var d = Lattice(ix + 1, iy + 1);
            return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
        }

        private static float Lattice(int x, int y) => Hash[((x * 73) ^ (y * 179)) & (HashSize - 1)];

        private void DrawMotes(SKCanvas canvas, float w, float h)
        {
            var light = Vector3.Lerp(_tint, Vector3.One, 0.55f) * 255;
            using var paint = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus };
            foreach (var m in _motes)
            {
                var t = (float)_phase * m.Speed;
                var y = 1 - (m.Y + t * 0.012f) % 1;
                var x = m.X + 0.012f * MathF.Sin(t * 0.3f + m.Phase);
                var twinkle = 0.5f + 0.5f * MathF.Sin(t * 0.9f + m.Phase);
                // Fade near the top so motes don't pop when they wrap.
                var alpha = 8 + 44 * twinkle * MathF.Min(1, y * 4);
                paint.Color = new SKColor((byte)light.X, (byte)light.Y, (byte)light.Z, (byte)alpha);
                canvas.DrawCircle(x * w, y * h, m.Size, paint);
            }
        }

        private void DrawGrain(SKCanvas canvas, float w, float h)
        {
            // Fine grain breaks up 8-bit banding in the dark gradients.
            _grain ??= CreateGrain();
            using var paint = new SKPaint { Shader = _grain, BlendMode = SKBlendMode.Plus };
            canvas.DrawRect(new SKRect(0, 0, w, h), paint);
        }

        private static SKShader CreateGrain()
        {
            using var bitmap = new SKBitmap(new SKImageInfo(NoiseTile, NoiseTile, SKColorType.Rgba8888, SKAlphaType.Premul));
            var random = new Random(7);
            for (var y = 0; y < NoiseTile; y++)
            {
                for (var x = 0; x < NoiseTile; x++)
                {
                    var v = (byte)random.Next(0, 5);
                    bitmap.SetPixel(x, y, new SKColor(v, v, v, 255));
                }
            }
            using var image = SKImage.FromBitmap(bitmap);
            return image.ToShader(SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
        }

        private static float[] CreateHash()
        {
            var random = new Random(3);
            return Enumerable.Range(0, HashSize).Select(_ => random.NextSingle()).ToArray();
        }

        private static (float, float, float, float, float)[] CreateMotes()
        {
            var random = new Random(11);
            return Enumerable.Range(0, MoteCount)
                .Select(_ => (random.NextSingle(), random.NextSingle(), 0.6f + random.NextSingle() * 1.5f,
                    0.4f + random.NextSingle() * 1.2f, random.NextSingle() * MathF.Tau))
                .ToArray();
        }
    }
}
