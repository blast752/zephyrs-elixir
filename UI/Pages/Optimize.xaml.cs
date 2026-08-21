namespace ZephyrsElixir.UI.Pages;

public sealed partial class Optimize : UserControl
{
    private const int TotalSteps = OptimizationEngine.TotalSteps;
    private const int MaxParticlesIdle = 8;
    private const int MaxParticlesActive = 20;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly object _logLock = new();
    private readonly OptimizationReport _report = new();
    
    private CancellationTokenSource? _cts;
    private DateTime _startTime;
    private int _currentStep;
    private bool _isRunning;
    private Storyboard? _pulseStoryboard;
    private DispatcherTimer? _particleSpawnTimer, _particleStateTimer;

    private bool IsExtreme => ExtremeModeToggle.IsChecked == true && Pro.IsAvailable;

    public Optimize()
    {
        InitializeComponent();
        _timer.Tick += OnTimerTick;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        OptimizeProgress.Maximum = TotalSteps;
        this.SubscribeToDeviceUpdates(
            onStatusChanged: OnDeviceConnectionChanged,
            onInfoUpdated: RefreshDeviceUI,
            controls: new UIElement[] { OptimizeButton, DeviceInfoButton });
        RefreshCurrentDevice();
        TranslationManager.Instance.LanguageChanged += OnLanguageChanged;
        UpdateConsoleStatus(false);

        if (TryFindResource("Optimize.Storyboard.PulseGlow") is Storyboard sb)
            _pulseStoryboard = sb;

        // The page instance survives navigation, so the particle field is built once and its two
        // timers are simply parked while the page is away — never torn down, or the ambient
        // animation would be gone for good after the first time the user leaves this page.
        if (_particleSpawnTimer is null) InitializeParticleSystem();
        _particleSpawnTimer?.Start();
        _particleStateTimer?.Start();

        if (_isRunning) _timer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // The run outlives the page: navigating away parks the timers, it never cancels the work the
        // user started, and the report stays where it belongs instead of opening over another screen.
        _timer.Stop();
        StopPulseAnimation();
        _particleSpawnTimer?.Stop();
        _particleStateTimer?.Stop();
        TranslationManager.Instance.LanguageChanged -= OnLanguageChanged;
    }

    private void OnTimerTick(object? sender, EventArgs e) => 
        TimerText.Text = $"{DateTime.Now - _startTime:hh\\:mm\\:ss}";

    private void OnDeviceConnectionChanged(bool isConnected)
    {
        if (!isConnected)
            RefreshDeviceUI(string.Empty, 0);
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => RefreshCurrentDevice();

    private void RefreshCurrentDevice() =>
        RefreshDeviceUI(DeviceManager.Instance.DeviceName, DeviceManager.Instance.BatteryLevel);

    private void RefreshDeviceUI(string deviceName, int batteryLevel)
    {
        var hasDevice = !string.IsNullOrWhiteSpace(deviceName);

        DeviceNameText.Text = hasDevice ? deviceName : Strings.DeviceStatus_NoDevice;
        BatteryText.Text    = hasDevice ? $"{batteryLevel}%" : "—";
        BatteryFill.Width   = Math.Clamp(batteryLevel, 0, 100) / 100.0 * 48;

        BatteryFill.SetResourceReference(Shape.FillProperty, batteryLevel switch
        {
            <= 15 => "App.Brush.Battery.Low",
            <= 40 => "App.Brush.Battery.Medium",
            _     => "App.Brush.Battery.High"
        });

        ConnectionIndicator.SetResourceReference(Shape.FillProperty,
            hasDevice ? "App.Brush.Status.Connected" : "App.Brush.Status.Idle");
    }

    private void UpdateProgress(int step)
    {
        _currentStep = step;
        Dispatcher.Invoke(() =>
        {
            var clampedStep = Math.Min(step, TotalSteps);
            OptimizeProgress.Value = clampedStep;
            var percentage = Math.Min(step * 100.0 / TotalSteps, 100);
            ProgressText.Text = $"{percentage:0}%";

            UpdateLiveStats();
        });
    }

    private void UpdateStepLabel(string text) => 
        Dispatcher.Invoke(() => StepLabel.Text = text);

    private void UpdateConsoleStatus(bool isActive)
    {
        Dispatcher.Invoke(() =>
        {
            ConsoleStatusDot.SetResourceReference(Shape.FillProperty, 
                isActive ? "App.Brush.Status.Active" : "App.Brush.Status.Idle");
            ConsoleStatusText.Text = isActive 
                ? Strings.Optimize_Console_Status_Running 
                : Strings.Optimize_Console_Status_Idle;
            
            if (ConsoleStatusGlow != null)
                ConsoleStatusGlow.Color = UIHelpers.PaletteColor(
                    isActive ? "App.Color.Status.Active" : "App.Color.Status.Idle") ?? ConsoleStatusGlow.Color;
        });
    }

    private void SetButtonState(bool isStopMode)
    {
        Dispatcher.Invoke(() =>
        {
            OptimizeButton.Style = (Style)FindResource(
                isStopMode ? "App.Style.Button.Destructive" : "App.Style.Button");
            OptimizeButton.Content = isStopMode 
                ? Strings.Dialog_StopOptimization_StopButton 
                : Strings.Optimize_Button_Start;
            OptimizeButton.Tag = isStopMode ? "close" : "play";
            
            OptimizeIcon.Kind = isStopMode ? "play" : "settings";
        });
    }

    private void UpdateLiveStats()
    {
        Dispatcher.Invoke(() =>
        {
            StatMemory.Text = UIHelpers.FormatSize(_report.MemoryFreedKb);
            StatStorage.Text = UIHelpers.FormatSize(_report.StorageCleanedKb);
            StatApps.Text = _report.AppsForceKilled.Count.ToString();
            var (statusText, statusIcon) = _isRunning
                ? (Strings.Optimize_LiveStatus_Running, "sync")
                : _report.Outcome switch
                {
                    OptimizationOutcome.Success => (Strings.Optimize_LiveStatus_Completed, "check"),
                    OptimizationOutcome.Partial => (Strings.Optimize_LiveStatus_Interrupted, "warning"),
                    OptimizationOutcome.Error   => (Strings.Optimize_LiveStatus_Failed,      "error-circle"),
                    _                           => (Strings.Optimize_LiveStatus_Completed,   "check")
                };

            StatStatus.Text = statusText;
            StatStatusIcon.Kind = statusIcon;
        });
    }

    private void ShowStatsFooter()
    {
        Dispatcher.Invoke(() =>
        {
            StatsFooter.Visibility = Visibility.Visible;
            
            var storyboard = new Storyboard();
            
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(fadeIn, StatsFooter);
            Storyboard.SetTargetProperty(fadeIn, new PropertyPath("Opacity"));
            
            var slideUp = new DoubleAnimation(20, 0, TimeSpan.FromMilliseconds(400))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(slideUp, StatsFooter);
            Storyboard.SetTargetProperty(slideUp, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));
            
            storyboard.Children.Add(fadeIn);
            storyboard.Children.Add(slideUp);
            storyboard.Begin();
        });
    }

    private void StartPulseAnimation()
    {
        Dispatcher.Invoke(() =>
        {
            ActiveGlowRing.Visibility = Visibility.Visible;
            _pulseStoryboard?.Begin(ActiveGlowRing, true);
        });
    }

    private void StopPulseAnimation()
    {
        Dispatcher.Invoke(() =>
        {
            _pulseStoryboard?.Stop(ActiveGlowRing);
            ActiveGlowRing.Visibility = Visibility.Collapsed;
        });
    }

    private async void OnOptimizeClick(object sender, RoutedEventArgs e)
    {
        if (_isRunning)
            StopOptimization();
        else
            await RunOptimizationAsync();
    }

    private async void OnDeviceInfoClick(object sender, RoutedEventArgs e)
    {
        ClearConsole();
        LogToConsole($"{Strings.Optimize_Console_RetrievingInfo}\n");
        var info = await DeviceManager.Instance.GetFullDevicePropertiesAsync();
        LogToConsole($"{info}\n{Strings.Optimize_Console_InfoRetrieved}\n");
    }

    private async Task RunOptimizationAsync()
    {
        PrepareForOptimization();

        void OnDeviceStatus(object? s, bool connected)
        {
            if (!connected) _cts?.Cancel();
        }

        DeviceManager.Instance.DeviceStatusChanged += OnDeviceStatus;

        // Pin the whole run to the device that is active at start: a mid-run active-device switch
        // must never mix optimization commands across phones, and the engine's disconnect guard
        // then watches this specific serial instead of the global connection state.
        var serial = DeviceManager.Instance.ActiveSerial;
        var engine = new OptimizationEngine(serial.Length > 0 ? serial : null, _report)
        {
            Extreme = IsExtreme,
            Log = LogToConsole,
            StepChanged = UpdateStepLabel,
            ProgressChanged = UpdateProgress
        };

        try
        {
            await engine.RunAsync(_cts!.Token);
        }
        finally
        {
            DeviceManager.Instance.DeviceStatusChanged -= OnDeviceStatus;
            CleanupAfterOptimization();
        }

        ShowOptimizationReport();
    }

    private void PrepareForOptimization()
    {
        _isRunning = true;
        _currentStep = 0;
        _cts = new CancellationTokenSource();
        _startTime = DateTime.Now;
        _report.Reset();
        
        UpdateProgress(0);
        UpdateStepLabel(Strings.Optimize_Status_Initializing);
        SetButtonState(true);
        UpdateConsoleStatus(true);
        ClearConsole();
        LogToConsole($"▶ {Strings.Optimize_Console_Starting}\n");
        _timer.Start();

        UpdateLiveStats();
        ShowStatsFooter();
        StartPulseAnimation();
    }

    private void CleanupAfterOptimization()
    {
        _timer.Stop();
        _isRunning = false;
        SetButtonState(false);
        UpdateConsoleStatus(false);
        OptimizeButton.IsEnabled = DeviceManager.Instance.IsConnected;
        StopPulseAnimation();
        UpdateLiveStats();
        _cts?.Dispose();
        _cts = null;
    }

    private void StopOptimization()
    {
        if (_cts is null || _cts.IsCancellationRequested) return;

        if (!DialogService.Instance.ConfirmStopOptimization(Application.Current.MainWindow))
            return;

        LogToConsole($"{Strings.Optimize_Console_Stopping}\n");
        _cts.Cancel();
    }

    private void ClearConsole() => 
        Dispatcher.Invoke(() => { lock (_logLock) TerminalBox.Clear(); });

    private void LogToConsole(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        Dispatcher.Invoke(() =>
        {
            lock (_logLock)
            {
                TerminalBox.AppendText(text);
                TerminalBox.ScrollToEnd();
            }
        });
    }

    private void ShowOptimizationReport()
    {
        if (Application.Current.MainWindow is not MainWindow owner) return;

        try
        {
            new OptimizationReportDialog(_report) { Owner = owner }.ShowDialog();
        }
        catch (Exception ex)
        {
            LogToConsole($"✗ Report error: {ex.Message}\n");
        }
    }

    private void InitializeParticleSystem()
    {
        var particles = new HashSet<Rectangle>();
        var rng = new Random();
        var wasRunning = false;
        var maxParticles = MaxParticlesIdle;

        var spawnTimer = _particleSpawnTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        var stateTimer = _particleStateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };

        stateTimer.Tick += (_, _) =>
        {
            if (_isRunning == wasRunning) return;
            wasRunning = _isRunning;
            spawnTimer.Interval = TimeSpan.FromMilliseconds(_isRunning ? 150 : 800);
            maxParticles = _isRunning ? MaxParticlesActive : MaxParticlesIdle;
            if (_isRunning)
                for (int i = 0; i < 10; i++) SpawnParticle();
        };

        spawnTimer.Tick += (_, _) =>
        {
            if (particles.Count < maxParticles)
            {
                var count = _isRunning ? rng.Next(1, 4) : 1;
                for (int i = 0; i < count; i++) SpawnParticle();
            }
        };

        void SpawnParticle()
        {
            var isComplete = _currentStep >= TotalSteps * 0.95;
            var particle = new Rectangle
            {
                Width = rng.Next(_isRunning ? 80 : 40, _isRunning ? 200 : 100),
                Height = _isRunning ? rng.Next(2, 4) : 1,
                Fill = CreateParticleGradient(isComplete, _isRunning),
                Effect = _isRunning ? ParticleBlurRunning : ParticleBlurIdle,
                Opacity = 0,
                RenderTransform = new TranslateTransform()
            };

            var canvasHeight = Math.Max(1, (int)(ParticleCanvas.ActualHeight > 0 ? ParticleCanvas.ActualHeight : 600));
            Canvas.SetLeft(particle, -particle.Width - 50);
            Canvas.SetTop(particle, rng.Next(0, canvasHeight));
            ParticleCanvas.Children.Add(particle);
            particles.Add(particle);
            AnimateParticle(particle, particles, rng);
        }

        Dispatcher.BeginInvoke(() =>
        {
            for (int i = 0; i < 3; i++) SpawnParticle();
        }, DispatcherPriority.Loaded);
    }

    private void AnimateParticle(Rectangle particle, HashSet<Rectangle> particles, Random rng)
    {
        var canvasWidth = ParticleCanvas.ActualWidth > 0 ? ParticleCanvas.ActualWidth : 1200;
        var duration = TimeSpan.FromMilliseconds(rng.Next(1500, 3000) * (_isRunning ? 0.5 : 1.5));
        
        var storyboard = new Storyboard();
        storyboard.Children.Add(CreateAnimation(particle, "(UIElement.Opacity)", 0, 0.8, TimeSpan.FromMilliseconds(200)));
        storyboard.Children.Add(CreateAnimation(particle, "(UIElement.Opacity)", 0.8, 0, duration, TimeSpan.FromMilliseconds(200)));
        storyboard.Children.Add(CreateAnimation(particle, "(UIElement.RenderTransform).(TranslateTransform.X)", 0, canvasWidth + particle.Width + 100, duration));
        
        if (_isRunning)
            storyboard.Children.Add(CreateAnimation(particle, "(UIElement.RenderTransform).(TranslateTransform.Y)", 0, rng.Next(-30, 31), duration));
        
        storyboard.Completed += (_, _) =>
        {
            ParticleCanvas.Children.Remove(particle);
            particles.Remove(particle);
        };
        
        storyboard.Begin();
    }

    private static readonly LinearGradientBrush ParticleGradientComplete = CreateFrozenParticleGradient(
        Color.FromArgb(0x00, 0xFF, 0xD7, 0x00), Color.FromArgb(0xFF, 0xFF, 0xD7, 0x00), Color.FromArgb(0x00, 0xFF, 0x64, 0x00));
    private static readonly LinearGradientBrush ParticleGradientRunning = CreateFrozenParticleGradient(
        Color.FromArgb(0x00, 0x00, 0xBF, 0xFF), Color.FromArgb(0xFF, 0x7D, 0x64, 0xFF), Color.FromArgb(0x00, 0xFF, 0x00, 0xBF));
    private static readonly LinearGradientBrush ParticleGradientIdle = CreateFrozenParticleGradient(
        Color.FromArgb(0x00, 0x00, 0x7F, 0xFF), Color.FromArgb(0x64, 0x00, 0xBF, 0xFF), Color.FromArgb(0x00, 0x00, 0x7F, 0xFF));

    private static LinearGradientBrush CreateFrozenParticleGradient(Color c1, Color c2, Color c3)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
            GradientStops = { new GradientStop(c1, 0), new GradientStop(c2, 0.5), new GradientStop(c3, 1) }
        };
        brush.Freeze();
        return brush;
    }

    private static readonly BlurEffect ParticleBlurRunning = CreateFrozenBlur(5);
    private static readonly BlurEffect ParticleBlurIdle = CreateFrozenBlur(2);

    private static BlurEffect CreateFrozenBlur(double radius)
    {
        var effect = new BlurEffect { Radius = radius };
        effect.Freeze();
        return effect;
    }

    private static LinearGradientBrush CreateParticleGradient(bool isComplete, bool isRunning) =>
        isComplete ? ParticleGradientComplete : isRunning ? ParticleGradientRunning : ParticleGradientIdle;

    private static DoubleAnimation CreateAnimation(UIElement target, string path, double from, double to, TimeSpan duration, TimeSpan? beginTime = null)
    {
        var animation = new DoubleAnimation(from, to, duration) { BeginTime = beginTime ?? TimeSpan.Zero };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath(path));
        return animation;
    }
}