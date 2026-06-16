namespace ZephyrsElixir.UI.Pages;

public sealed partial class Optimize : UserControl
{
    #region Constants & Configuration

    private const int TotalSteps = 120;
    private const int CacheIterations = 100;
    private const long MemoryThresholdKb = 102400;
    private const int MaxParticlesIdle = 8;
    private const int MaxParticlesActive = 20;

    #endregion

    #region Fields

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly object _logLock = new();
    private readonly OptimizationReport _report = new();
    
    private CancellationTokenSource? _cts;
    private DateTime _startTime;
    private int _currentStep;
    private bool _particlesInit;
    private bool _isRunning;
    private Storyboard? _pulseStoryboard;

    #endregion

    #region Properties

    private bool IsExtreme => ExtremeModeToggle.IsChecked == true && Pro.IsAvailable;

    #endregion

    #region Constructor & Lifecycle

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
        RefreshDeviceUI(DeviceManager.Instance.DeviceName, DeviceManager.Instance.BatteryLevel);
        UpdateConsoleStatus(false);

        if (TryFindResource("Optimize.Storyboard.PulseGlow") is Storyboard sb)
            _pulseStoryboard = sb;

        if (!_particlesInit) { InitializeParticleSystem(); _particlesInit = true; }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        _timer.Stop();
        StopPulseAnimation();
    }

    private void OnTimerTick(object? sender, EventArgs e) => 
        TimerText.Text = $"{DateTime.Now - _startTime:hh\\:mm\\:ss}";

    #endregion

    #region UI Update Methods

    private void OnDeviceConnectionChanged(bool isConnected)
    {
        if (!isConnected)
            RefreshDeviceUI(Strings.DeviceStatus_NoDevice, 0);
    }

    private void RefreshDeviceUI(string deviceName, int batteryLevel)
    {
        var hasDevice = !string.IsNullOrWhiteSpace(deviceName)
                    && !string.Equals(deviceName, Strings.DeviceStatus_NoDevice, StringComparison.Ordinal);

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
            {
                ConsoleStatusGlow.Color = isActive 
                    ? Color.FromRgb(0x00, 0xE6, 0x76) 
                    : Color.FromRgb(0x60, 0x7D, 0x8B);
            }
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
            OptimizeButton.Tag = isStopMode ? "\uE711" : "\uE768";
            
            OptimizeIcon.Text = isStopMode ? "\uE768" : "\uE9F5";
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
                ? (Strings.Optimize_LiveStatus_Running, "\uE895")
                : _report.Outcome switch
                {
                    OptimizationOutcome.Success => (Strings.Optimize_LiveStatus_Completed, "\uE73E"),
                    OptimizationOutcome.Partial => (Strings.Optimize_LiveStatus_Interrupted, "\uE7BA"),
                    OptimizationOutcome.Error   => (Strings.Optimize_LiveStatus_Failed,      "\uEA39"),
                    _                           => (Strings.Optimize_LiveStatus_Completed,   "\uE73E")
                };

            StatStatus.Text = statusText;
            StatStatusIcon.Text = statusIcon;
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

    #endregion

    #region Event Handlers

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

    #endregion

    #region Optimization Core

    private async Task RunOptimizationAsync()
    {
        PrepareForOptimization();

        void OnDeviceStatus(object? s, bool connected)
        {
            if (!connected) _cts?.Cancel();
        }

        DeviceManager.Instance.DeviceStatusChanged += OnDeviceStatus;

        try
        {
            var ct = _cts!.Token;

            await RunStep(ClearCacheAsync, ct);
            await RunStep(ManageMemoryAsync, ct);
            await RunStep(DeepCleanStorageAsync, ct);
            await RunStep(OptimizeNetworkAsync, ct);
            await RunStep(OptimizeSystemAsync, ct);
            await RunStep(CompilePackagesAsync, ct);
            await RunStep(OptimizeDexAsync, ct);

            _report.Outcome = OptimizationOutcome.Success;
            UpdateStepLabel(Strings.Common_Status_Success);
            LogToConsole($"✓ {Strings.Common_Status_Success}\n");
        }
        catch (OperationCanceledException)
        {
            _report.Outcome = OptimizationOutcome.Partial;
            UpdateStepLabel(Strings.Common_Button_Cancel);
            LogToConsole($"⚠ {Strings.Optimize_Console_Interrupted}\n");
        }
        catch (Exception ex)
        {
            _report.Outcome = OptimizationOutcome.Error;
            _report.ErrorMessage = ex.Message;
            UpdateStepLabel(Strings.Common_Status_Error.Replace("{0}", ""));
            LogToConsole($"✗ {ex.Message}\n");
        }
        finally
        {
            DeviceManager.Instance.DeviceStatusChanged -= OnDeviceStatus;
            CleanupAfterOptimization();
        }

        ShowOptimizationReport();
    }

    private async Task RunStep(Func<CancellationToken, Task> step, CancellationToken ct)
    {
        GuardRunning(ct);
        await step(ct);
        _report.CompletedSteps++;
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

    #endregion

    #region Optimization Tasks

    private async Task ClearCacheAsync(CancellationToken ct)
    {
        for (int i = 1; i <= CacheIterations; i++)
        {
            UpdateStepLabel(string.Format(Strings.Optimize_Status_ClearingCache, i, CacheIterations));
            
            if (i % 20 == 0) 
                LogToConsole($"{string.Format(Strings.Optimize_Console_CacheProgress, i)}\n");
            
            await RunAdbAsync("shell pm trim-caches 1000G", ct);
            UpdateProgress(++_currentStep);
            await Task.Delay(30, ct);
        }
        
        _report.CacheCleared = true;
        LogToConsole($"✓ {Strings.Optimize_Console_CacheCleared}\n");
    }

    private async Task ManageMemoryAsync(CancellationToken ct)
    {
        UpdateStepLabel(Strings.Optimize_Status_AnalyzingMemory);
        LogToConsole($"{Strings.Optimize_Console_AnalyzingProcesses}\n");

        var heavyApps = await GetHeavyAppsAsync(ct);
        await RunAdbAsync("shell am kill-all", ct);
        _report.ProcessesKilled++;
        UpdateProgress(++_currentStep);

        foreach (var (pkg, mem) in heavyApps.Take(10))
        {
            if (CloudIntelligenceManager.IsCriticalPackage(pkg)) continue;
            
            var appName = pkg.Split('.').Last();
            UpdateStepLabel(string.Format(Strings.Optimize_Status_Stopping, appName));
            LogToConsole($"{string.Format(Strings.Optimize_Console_ForceStoppingApp, pkg, $"{mem / 1024.0:F1}")}\n");
            await RunAdbAsync($"shell am force-stop {pkg}", ct);
            _report.AppsForceKilled.Add((pkg, mem));
            _report.MemoryFreedKb += mem;
        }
        
        UpdateProgress(++_currentStep);
        LogToConsole($"✓ {string.Format(Strings.Optimize_Console_MemoryOptimized, $"{_report.MemoryFreedKb / 1024.0:F1}")}\n");

        if (IsExtreme)
        {
            var result = await Pro.ExecuteAsync(ProCommandIds.ExtremeCachedAppsFreezer, ct: ct);
            if (result.Success)
                LogToConsole($"✓ {Strings.Optimize_Console_CachedAppsFreezer}\n");
        }
        
        UpdateProgress(++_currentStep);
    }

    private async Task DeepCleanStorageAsync(CancellationToken ct)
    {
        UpdateStepLabel(Strings.Optimize_Status_DeepCleaning);
        LogToConsole($"{Strings.Optimize_Console_StartingDeepClean}\n");

        var cleanupOps = new (string? Path, string Command, string Description)[]
        {
            ("/data/local/tmp", "shell rm -rf /data/local/tmp/*", Strings.Optimize_Clean_TempFiles),
            (null, "shell pm trim-caches 1000G", Strings.Optimize_Clean_PackageCaches),
            ("/data/anr", "shell rm -rf /data/anr/*", Strings.Optimize_Clean_AnrTraces),
            ("/data/tombstones", "shell rm -rf /data/tombstones/*", Strings.Optimize_Clean_CrashDumps),
            (null, "shell logcat -c", Strings.Optimize_Clean_LogcatBuffer),
            ("/data/system/dropbox", "shell rm -rf /data/system/dropbox/*", Strings.Optimize_Clean_SystemDropbox)
        };

        long storageBefore = await GetAvailableStorageAsync(ct);
        long totalCleaned = 0;

        foreach (var (path, cmd, desc) in cleanupOps)
        {
            UpdateStepLabel(string.Format(Strings.Optimize_Status_Cleaning, desc));
            
            var dirSize = path != null ? await GetDirectorySizeAsync(path, ct) : 0;
            var result = await RunAdbAsync(cmd, ct);
            
            if (!result.Contains("Permission denied", StringComparison.OrdinalIgnoreCase))
            {
                if (path != null && dirSize > 0)
                {
                    totalCleaned += dirSize;
                    LogToConsole($"✓ {desc} ({dirSize / 1024.0:F1} MB)\n");
                }
                else
                {
                    LogToConsole($"✓ {desc}\n");
                }
                _report.CleanedItems.Add(desc);
            }
            UpdateProgress(++_currentStep);
        }

        long storageAfter = await GetAvailableStorageAsync(ct);
        if (storageAfter - storageBefore > 0) 
            totalCleaned = storageAfter - storageBefore;

        UpdateStepLabel(Strings.Optimize_Status_RunningTrim);
        await RunAdbAsync("shell sm fstrim /data", ct);
        
        var sdkStr = await RunAdbAsync("shell getprop ro.build.version.sdk", ct);
        if (int.TryParse(sdkStr.Trim(), out var sdk) && sdk < 29)
            await RunAdbAsync("shell sm fstrim /cache", ct);
        
        UpdateProgress(++_currentStep);
        
        _report.TrimExecuted = true;
        _report.StorageCleanedKb = totalCleaned;
        LogToConsole($"✓ {string.Format(Strings.Optimize_Console_StorageComplete, $"{totalCleaned / 1024.0:F1}")}\n");
    }

    private async Task OptimizeNetworkAsync(CancellationToken ct)
    {
        UpdateStepLabel(Strings.Optimize_Status_OptimizingNetwork);
        LogToConsole($"{Strings.Optimize_Console_ApplyingNetwork}\n");
        
        var networkSettings = new (string Key, string Value)[]
        {
            ("wifi_watchdog_poor_network_test_enabled", "0"),
            ("network_recommendations_enabled", "0"),
            ("wifi_scan_always_enabled", "0"),
            ("ble_scan_always_enabled", "0")
        };

        foreach (var (key, val) in networkSettings)
        {
            await RunAdbAsync($"shell settings put global {key} {val}", ct);
            UpdateProgress(++_currentStep);
        }

        _report.NetworkOptimized = true;
        LogToConsole($"✓ {Strings.Optimize_Console_NetworkComplete}\n");
    }

    private async Task OptimizeSystemAsync(CancellationToken ct)
    {
        UpdateStepLabel(Strings.Optimize_Status_OptimizingSystem);

        if (IsExtreme)
        {
            LogToConsole($"{Strings.Optimize_Console_MulticoreScheduler}\n");
            await Pro.ExecuteAsync(ProCommandIds.ExtremeMulticoreScheduler, ct: ct);
        }
        UpdateProgress(++_currentStep);

        LogToConsole($"{Strings.Optimize_Console_SettingAnimations}\n");
        var animationSettings = new[] { "animator_duration_scale", "transition_animation_scale", "window_animation_scale" };
        
        foreach (var setting in animationSettings)
        {
            await RunAdbAsync($"shell settings put global {setting} 0.5", ct);
            UpdateProgress(++_currentStep);
        }

        LogToConsole($"✓ {Strings.Optimize_Console_SystemOptimized}\n");
    }

    private async Task CompilePackagesAsync(CancellationToken ct)
    {
        var mode = "speed";
        if (IsExtreme)
        {
            var result = await Pro.ExecuteAsync(ProCommandIds.ExtremeCompilationMode, ct: ct);
            if (result.Success) mode = result.Message;
        }
        
        UpdateStepLabel(IsExtreme ? Strings.Optimize_Status_ExtremeCompilation : Strings.Optimize_Status_CompilingPackages);
        LogToConsole($"{string.Format(IsExtreme ? Strings.Optimize_Console_ExtremeCompilationStart : Strings.Optimize_Console_CompilationStart, mode)}\n");

        GuardRunning(ct);
        await AdbExecutor.ExecuteCommandAsync(
            $"shell cmd package compile -m {mode} -f -a",
            ct,
            line =>
            {
                LogToConsole($"{line}\n");
                var match = CompileOutputRegex().Match(line);
                if (match.Success)
                    UpdateStepLabel(string.Format(Strings.Optimize_Status_Compiling, match.Groups[1].Value.Split('.').Last()));
            });
        GuardRunning(ct);

        UpdateProgress(++_currentStep);
        _report.CompilationMode = mode;
    }

    private async Task OptimizeDexAsync(CancellationToken ct)
    {
        try
        {
            UpdateStepLabel(Strings.Optimize_Status_DexOptimization);
            LogToConsole($"{Strings.Optimize_Console_SpoofingBattery}\n");
            await RunAdbAsync("shell dumpsys battery set level 100", ct);

            LogToConsole($"{Strings.Optimize_Console_RunningDex}\n");
            await RunAdbAsync("shell cmd package bg-dexopt-job", ct);
            UpdateProgress(++_currentStep);
            _report.DexOptimized = true;
        }
        finally
        {
            if (DeviceManager.Instance.IsConnected)
            {
                LogToConsole($"{Strings.Optimize_Console_ResettingBattery}\n");
                try
                {
                    await AdbExecutor.ExecuteCommandAsync("shell dumpsys battery reset", CancellationToken.None);
                    LogToConsole($"✓ {Strings.Optimize_Console_BatteryReset}\n");
                }
                catch { }
            }
        }
    }

    #endregion

    #region Memory Analysis Helpers

    [GeneratedRegex(@"(com\.[\w\.]+|org\.[\w\.]+|net\.[\w\.]+).*?(?:lastPss|pss|mem)[=:\s]*([\d,]+)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ActivityProcessRegex();

    [GeneratedRegex(@"on\s+([\w\.]+)$")]
    private static partial Regex CompileOutputRegex();

    private async Task<List<(string Package, long MemoryKb)>> GetHeavyAppsAsync(CancellationToken ct)
    {
        var memInfo = await RunAdbAsync("shell dumpsys meminfo", ct);
        var result = MemInfoParser.ParseMemInfo(memInfo);

        if (result.Count == 0)
            result = ParseActivityProcesses(await RunAdbAsync("shell dumpsys activity processes", ct));

        if (result.Count == 0)
            result = ParsePsOutput(await RunAdbAsync("shell ps -A -o RSS,NAME", ct));

        return result
            .Where(x => x.MemoryKb > MemoryThresholdKb && !CloudIntelligenceManager.IsCriticalPackage(x.Package))
            .OrderByDescending(x => x.MemoryKb)
            .ToList();
    }

    private static List<(string Package, long MemoryKb)> ParseActivityProcesses(string output)
    {
        var result = new List<(string, long)>();
        
        foreach (Match match in ActivityProcessRegex().Matches(output))
        {
            var pkg = match.Groups[1].Value;
            if (long.TryParse(match.Groups[2].Value.Replace(",", ""), out var kb))
            {
                var existing = result.FindIndex(r => r.Item1 == pkg);
                if (existing >= 0)
                    result[existing] = (pkg, Math.Max(result[existing].Item2, kb));
                else
                    result.Add((pkg, kb));
            }
        }
        return result;
    }

    private static List<(string Package, long MemoryKb)> ParsePsOutput(string output) =>
        output.Split('\n')
            .Skip(1)
            .Select(line => line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length >= 2 && 
                           long.TryParse(parts[0], out _) && 
                           parts[^1].Contains('.') &&
                           (parts[^1].StartsWith("com.") || parts[^1].StartsWith("org.") || parts[^1].StartsWith("net.")))
            .Select(parts => (parts[^1], long.Parse(parts[0])))
            .ToList();

    #endregion

    #region Utility Methods
    
    private void GuardRunning(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!DeviceManager.Instance.IsConnected)
            throw new OperationCanceledException(Strings.Optimize_Console_DeviceDisconnected, ct);
    }

    private async Task<string> RunAdbAsync(string command, CancellationToken ct, bool log = false)
    {
        GuardRunning(ct);
        var output = await AdbExecutor.ExecuteCommandAsync(command, ct);
        GuardRunning(ct);
        if (log && !string.IsNullOrWhiteSpace(output))
            LogToConsole($"{output}\n");
        return output;
    }

    private async Task<long> GetDirectorySizeAsync(string path, CancellationToken ct)
    {
        try
        {
            var output = await RunAdbAsync($"shell du -sk {path} 2>/dev/null", ct);
            var parts = output.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 && long.TryParse(parts[0], out var kb) ? kb : 0;
        }
        catch { return 0; }
    }

    private async Task<long> GetAvailableStorageAsync(CancellationToken ct)
    {
        try
        {
            var output = await RunAdbAsync("shell df /data | tail -1", ct);
            var parts = output.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 4 && long.TryParse(parts[3], out var kb) ? kb : 0;
        }
        catch { return 0; }
    }

    #endregion

    #region Console Logging

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

    #endregion

    #region Particle System

    private void InitializeParticleSystem()
    {
        var particles = new HashSet<Rectangle>();
        var rng = new Random();
        var wasRunning = false;
        var maxParticles = MaxParticlesIdle;

        var spawnTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        var stateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };

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
                Effect = new BlurEffect { Radius = _isRunning ? 5 : 2 },
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

        spawnTimer.Start();
        stateTimer.Start();

        ParticleCanvas.Unloaded += (_, _) =>
        {
            spawnTimer.Stop();
            stateTimer.Stop();
            ParticleCanvas.Children.Clear();
            particles.Clear();
        };
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

    private static LinearGradientBrush CreateParticleGradient(bool isComplete, bool isRunning) =>
        isComplete ? ParticleGradientComplete : isRunning ? ParticleGradientRunning : ParticleGradientIdle;

    private static DoubleAnimation CreateAnimation(UIElement target, string path, double from, double to, TimeSpan duration, TimeSpan? beginTime = null)
    {
        var animation = new DoubleAnimation(from, to, duration) { BeginTime = beginTime ?? TimeSpan.Zero };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath(path));
        return animation;
    }

    #endregion
}