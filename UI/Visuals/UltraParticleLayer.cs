namespace ZephyrsElixir.UI.Visuals;

public sealed class UltraParticleLayer : FrameworkElement
{
    private sealed class Particle
    {
        public double X;
        public double Y;
        public double VX;
        public double VY;
        public double Size;
        public double Drift;
        public Brush Brush = null!;
    }

    private readonly DrawingVisual _visual = new();
    private readonly List<Particle> _particles = new();
    private readonly Random _rng = new();
    private readonly Stopwatch _sw = new();
    private bool _running;

    public UltraParticleLayer()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = false;
        Loaded += (s, e) => Start();
        Unloaded += (s, e) => Stop();
        SizeChanged += (s, e) => ResetParticles();
    }

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => _visual;

    private void Start()
    {
        if (_running) return;
        AddVisualChild(_visual);
        AddLogicalChild(_visual);
        ResetParticles();
        _sw.Restart();
        CompositionTarget.Rendering += OnFrame;
        _running = true;
    }

    private void Stop()
    {
        if (!_running) return;
        CompositionTarget.Rendering -= OnFrame;
        _sw.Stop();
        _running = false;
        RemoveVisualChild(_visual);
        RemoveLogicalChild(_visual);
        _particles.Clear();
    }

    private void ResetParticles()
    {
        _particles.Clear();
        double area = Math.Max(ActualWidth * ActualHeight, 1);
        int count = Math.Clamp((int)(area / 18000) + 24, 24, 96);
        for (int i = 0; i < count; i++) _particles.Add(CreateParticle(true));
        Redraw();
    }

    private Particle CreateParticle(bool initial)
    {
        double size = _rng.NextDouble() * 8 + 3;
        Color c = _rng.Next(0, 5) == 0
            ? Color.FromArgb((byte)_rng.Next(90, 160), 255, 215, 0)
            : Color.FromArgb((byte)_rng.Next(60, 140), 0, 191, 255);

        var brush = new RadialGradientBrush(c, Colors.Transparent)
        {
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5
        };
        brush.Freeze();

        double x = _rng.NextDouble() * Math.Max(ActualWidth, 1);
        double y = initial ? _rng.NextDouble() * Math.Max(ActualHeight, 1) : ActualHeight + size;
        double vy = -20 - _rng.NextDouble() * 40;
        double vx = (_rng.NextDouble() - 0.5) * 14;

        return new Particle
        {
            X = x,
            Y = y,
            VX = vx,
            VY = vy,
            Size = size,
            Drift = (_rng.NextDouble() - 0.5) * 0.4,
            Brush = brush
        };
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        double dt = Math.Min(_sw.Elapsed.TotalSeconds, 1.0 / 30.0);
        _sw.Restart();

        using var dc = _visual.RenderOpen();
        for (int i = 0; i < _particles.Count; i++)
        {
            var p = _particles[i];
            p.VX += p.Drift * dt;
            p.X += p.VX * dt;
            p.Y += p.VY * dt;

            if (p.Y < -p.Size)
            {
                p = CreateParticle(false);
            }
            else
            {
                if (p.X < -p.Size) p.X = ActualWidth + p.Size;
                if (p.X > ActualWidth + p.Size) p.X = -p.Size;
            }

            _particles[i] = p;
            dc.DrawEllipse(p.Brush, null, new Point(p.X, p.Y), p.Size, p.Size);
        }
    }

    private void Redraw()
    {
        using var dc = _visual.RenderOpen();
        for (int i = 0; i < _particles.Count; i++)
        {
            var p = _particles[i];
            dc.DrawEllipse(p.Brush, null, new Point(p.X, p.Y), p.Size, p.Size);
        }
    }
}
