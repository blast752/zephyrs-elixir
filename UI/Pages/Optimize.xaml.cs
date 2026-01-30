
namespace ZephyrsElixir
{
    public sealed partial class Optimize : UserControl
    {
        private const int TotalSteps = 122, CacheIters = 100;
        private const long MemThreshold = 102400;

        private static readonly HashSet<string> Critical = new(StringComparer.OrdinalIgnoreCase)
        {
            "com.android.systemui", "com.android.launcher", "com.android.launcher3", "android",
            "com.google.android.inputmethod", "com.android.inputmethod",
            "com.samsung.android.honeyboard", "com.sec.android.inputmethod",
            "com.google.android.inputmethod.latin",
            "com.android.phone", "com.android.server.telecom", "com.android.providers", "com.android.providers.telephony",
            "com.android.settings", "com.google.android.gms", "com.android.vending", "com.google.android.permissioncontroller", "com.google.android.biometrics", "com.android.biometrics"
        };

        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
        private readonly object _lock = new();
        private readonly OptimizationReport _report = new();

        private CancellationTokenSource? _cts;
        private DateTime _start;
        private int _step;
        private bool _running;
        private bool IsExtreme => ExtremeModeToggle.IsChecked == true && Features.IsAvailable(Features.ExtremeMode);

        public event Action<string>? OptimizationPerformed;

        public Optimize()
        {
            InitializeComponent();
            _timer.Tick += (_, _) => TimerText.Text = $"{DateTime.Now - _start:hh\\:mm\\:ss}";
            Loaded += OnLoad;
            Unloaded += OnUnload;
        }

        private void OnLoad(object s, RoutedEventArgs e)
        {
            OptimizeProgress.Maximum = TotalSteps;
            this.SubscribeToDeviceUpdates(onInfoUpdated: RefreshUI, controls: new UIElement[] { OptimizeButton, DeviceInfoButton });
            RefreshUI(DeviceManager.Instance.DeviceName, DeviceManager.Instance.BatteryLevel);
            InitParticles();
            SetConsole(false);
        }

        private void OnUnload(object s, RoutedEventArgs e) { _cts?.Cancel(); _timer.Stop(); }

        private void RefreshUI(string name, int bat)
        {
            var on = DeviceManager.Instance.IsConnected;
            DeviceNameText.Text = on ? name : Strings.DeviceStatus_NoDevice;
            BatteryText.Text = on ? $"{bat}%" : "—";
            BatteryFill.Width = bat / 100.0 * 48;
            BatteryFill.SetResourceReference(Shape.FillProperty, bat switch { <= 15 => "App.Brush.Battery.Low", <= 40 => "App.Brush.Battery.Medium", _ => "App.Brush.Battery.High" });
            ConnectionIndicator.SetResourceReference(Shape.FillProperty, on ? "App.Brush.Status.Connected" : "App.Brush.Status.Idle");
        }

        private async void OnOptimizeClick(object s, RoutedEventArgs e) { if (_running) Stop(); else await RunAsync(); }

        private async void OnDeviceInfoClick(object s, RoutedEventArgs e)
        {
            Clear();
            Log("Retrieving device information...\n");
            Log($"{await DeviceManager.Instance.GetFullDevicePropertiesAsync()}\nDevice information retrieved.\n");
        }

        private async Task RunAsync()
        {
            Prepare();
            try
            {
                var ct = _cts!.Token;
                await ClearCacheAsync(ct);
                await ManageMemoryAsync(ct);
                await DeepCleanAsync(ct);
                await OptimizeNetAsync(ct);
                await OptimizeSystemAsync(ct);
                await CompileAsync(ct);
                await OptimizeDexAsync(ct);
                Label(Strings.Common_Status_Success);
                Log($"✓ {Strings.Common_Status_Success}\n");
                ShowReport();
            }
            catch (OperationCanceledException) { Label(Strings.Common_Button_Cancel); Log("⚠ Interrupted.\n"); }
            catch (Exception ex) { Label(Strings.Common_Status_Error.Replace("{0}", "")); Log($"✗ {ex.Message}\n"); }
            finally { Cleanup(); }
        }

        private void Prepare()
        {
            _running = true;
            _step = 0;
            _cts = new();
            _start = DateTime.Now;
            _report.Reset();
            Progress(0);
            Label("Initializing...");
            SetBtn(true);
            SetConsole(true);
            Clear();
            Log("▶ Starting AIO optimization...\n");
            _timer.Start();
        }

        private void Cleanup()
        {
            _timer.Stop();
            _running = false;
            SetBtn(false);
            SetConsole(false);
            OptimizeButton.IsEnabled = DeviceManager.Instance.IsConnected;
            _cts?.Dispose();
            _cts = null;
        }

        private async Task ClearCacheAsync(CancellationToken ct)
        {
            for (int i = 1; i <= CacheIters; i++)
            {
                ct.ThrowIfCancellationRequested();
                Label($"Clearing cache ({i}/{CacheIters})");
                if (i % 20 == 0) Log($"Cache progress: {i}%\n");
                await Adb("shell pm trim-caches 1000G", ct);
                Progress(++_step);
                await Task.Delay(30, ct);
            }
            _report.CacheCleared = true;
            Log("✓ Cache cleared.\n");
        }

        private async Task ManageMemoryAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Label("Analyzing memory...");
            Log("Analyzing background processes...\n");

            var heavy = await GetHeavyAppsAsync(ct);
            await Adb("shell am kill-all", ct);
            _report.ProcessesKilled++;
            Progress(++_step);

            foreach (var (pkg, mem) in heavy.Take(10))
            {
                ct.ThrowIfCancellationRequested();
                if (IsCrit(pkg)) continue;
                Label($"Stopping {pkg.Split('.').Last()}...");
                Log($"Force stopping {pkg} ({mem / 1024.0:F1} MB)...\n");
                await Adb($"shell am force-stop {pkg}", ct);
                _report.AppsForceKilled.Add((pkg, mem));
                _report.MemoryFreedKb += mem;
            }
            Progress(++_step);
            Log($"✓ Memory optimized. ~{_report.MemoryFreedKb / 1024.0:F1} MB freed.\n");

            if (IsExtreme)
            {
                await Adb("shell settings put global cached_apps_freezer enabled", ct);
                Log("✓ Cached apps freezer enabled.\n");
            }
            Progress(++_step);
            
            OptimizationPerformed?.Invoke("memory_optimized");
        }

        private async Task<List<(string, long)>> GetHeavyAppsAsync(CancellationToken ct)
        {
            var memInfo = await AdbOut("shell dumpsys meminfo", ct);
            var result = ParseMemInfo(memInfo);
            if (result.Count == 0) result = ParseActivity(await AdbOut("shell dumpsys activity processes", ct));
            if (result.Count == 0) result = ParsePs(await AdbOut("shell ps -A -o RSS,NAME", ct));
            return result.Where(x => x.Item2 > MemThreshold && !IsCrit(x.Item1)).OrderByDescending(x => x.Item2).ToList();
        }

        private static List<(string, long)> ParseMemInfo(string output)
        {
            var result = new List<(string, long)>();
            bool inPss = false;
            foreach (var line in output.Split('\n'))
            {
                var t = line.Trim();
                if (t.StartsWith("Total PSS by process", StringComparison.OrdinalIgnoreCase) || t.StartsWith("Total RSS by process", StringComparison.OrdinalIgnoreCase)) { inPss = true; continue; }
                if (inPss && string.IsNullOrWhiteSpace(t)) break;
                if (inPss)
                {
                    var m = Regex.Match(t, @"^([\d,]+)\s*K[B]?:\s*([\w\.]+)", RegexOptions.IgnoreCase);
                    if (m.Success) { var kb = long.Parse(m.Groups[1].Value.Replace(",", "")); var pkg = m.Groups[2].Value; if (pkg.Contains('.') && !pkg.StartsWith("pid")) result.Add((pkg, kb)); }
                }
                if (!inPss)
                {
                    var m = Regex.Match(t, @"([\d,]+)\s*K[B]?.*?(com\.[\w\.]+|org\.[\w\.]+|net\.[\w\.]+)");
                    if (m.Success && !result.Any(r => r.Item1 == m.Groups[2].Value)) result.Add((m.Groups[2].Value, long.Parse(m.Groups[1].Value.Replace(",", ""))));
                }
            }
            return result;
        }

        private static List<(string, long)> ParseActivity(string output)
        {
            var result = new List<(string, long)>();
            foreach (Match m in Regex.Matches(output, @"(com\.[\w\.]+|org\.[\w\.]+|net\.[\w\.]+).*?(?:lastPss|pss|mem)[=:\s]*([\d,]+)", RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                var pkg = m.Groups[1].Value;
                if (long.TryParse(m.Groups[2].Value.Replace(",", ""), out var kb))
                {
                    var i = result.FindIndex(r => r.Item1 == pkg);
                    if (i >= 0) result[i] = (pkg, Math.Max(result[i].Item2, kb)); else result.Add((pkg, kb));
                }
            }
            return result;
        }

        private static List<(string, long)> ParsePs(string output) => output.Split('\n').Skip(1)
            .Select(l => l.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            .Where(p => p.Length >= 2 && long.TryParse(p[0], out _) && p[^1].Contains('.') && (p[^1].StartsWith("com.") || p[^1].StartsWith("org.") || p[^1].StartsWith("net.")))
            .Select(p => (p[^1], long.Parse(p[0]))).ToList();

        private async Task DeepCleanAsync(CancellationToken ct)
        {
            Label("Deep cleaning...");
            Log("Starting deep storage cleanup...\n");

            var ops = new (string? p, string c, string d)[]
            {
                ("/data/local/tmp", "shell rm -rf /data/local/tmp/*", "Temp files"),
                (null, "shell cmd package clear-caches 1000G", "Package caches"),
                ("/data/anr", "shell rm -rf /data/anr/*", "ANR traces"),
                ("/data/tombstones", "shell rm -rf /data/tombstones/*", "Crash dumps"),
                (null, "shell logcat -c", "Logcat buffer"),
                ("/data/system/dropbox", "shell rm -rf /data/system/dropbox/*", "System dropbox")
            };

            long before = await GetStorageAsync(ct), total = 0;
            foreach (var (path, cmd, desc) in ops)
            {
                ct.ThrowIfCancellationRequested();
                Label($"Cleaning: {desc}");
                var size = path != null ? await GetDirSizeAsync(path, ct) : 0;
                var r = await AdbOut(cmd, ct);
                if (!r.Contains("Permission denied", StringComparison.OrdinalIgnoreCase))
                {
                    if (path != null && size > 0) { total += size; Log($"✓ {desc} ({size / 1024.0:F1} MB)\n"); }
                    else Log($"✓ {desc}\n");
                    _report.CleanedItems.Add(desc);
                }
                Progress(++_step);
            }

            long after = await GetStorageAsync(ct);
            if (after - before > 0) total = after - before;

            Label("Running TRIM...");
            await Adb("shell sm fstrim /data", ct);
            await Adb("shell sm fstrim /cache", ct);
            Progress(++_step);
            _report.TrimExecuted = true;
            _report.StorageCleanedKb = total;
            Log($"✓ Storage cleanup complete. ~{total / 1024.0:F1} MB freed.\n");
            OptimizationPerformed?.Invoke("storage_cleaned");
        }

        private async Task<long> GetDirSizeAsync(string path, CancellationToken ct)
        {
            try { var o = await AdbOut($"shell du -sk {path} 2>/dev/null", ct); var p = o.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries); return p.Length > 0 && long.TryParse(p[0], out var kb) ? kb : 0; }
            catch { return 0; }
        }

        private async Task<long> GetStorageAsync(CancellationToken ct)
        {
            try { var o = await AdbOut("shell df /data | tail -1", ct); var p = o.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries); return p.Length >= 4 && long.TryParse(p[3], out var kb) ? kb : 0; }
            catch { return 0; }
        }

        private async Task OptimizeNetAsync(CancellationToken ct)
        {
            Label("Optimizing network...");
            Log("Applying network optimizations...\n");
            foreach (var (key, val) in new (string, string)[]
            {
                ("wifi_watchdog_poor_network_test_enabled", "0"),
                ("network_recommendations_enabled", "0"),
                ("wifi_scan_always_enabled", "0"),
                ("ble_scan_always_enabled", "0"),
            })
            {
                ct.ThrowIfCancellationRequested();
                await Adb($"shell settings put global {key} {val}", ct);
                Progress(++_step);
            }
            
            Log("Refreshing network caches...\n");
            await Adb("shell cmd connectivity airplane-mode enable", ct);
            Progress(++_step);
            await Task.Delay(1200, ct);
            await Adb("shell cmd connectivity airplane-mode disable", ct);
            Progress(++_step);

            _report.NetworkOptimized = true;
            Log("✓ Network optimization complete.\n");
            OptimizationPerformed?.Invoke("network_optimized");
        }

        private async Task OptimizeSystemAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Label("Optimizing system...");
            
            if (IsExtreme)
            {
                Log("Enabling multi-core packet scheduler...\n");
                await Adb("shell settings put system multicore_packet_scheduler 1", ct);
            }
            Progress(++_step);
            
            Log("Setting animations to 0.5x...\n");
            foreach (var s in new[] { "animator_duration_scale", "transition_animation_scale", "window_animation_scale" })
            {
                await Adb($"shell settings put global {s} 0.5", ct);
                Progress(++_step);
            }
            
            Log("✓ System optimized.\n");
        }

        private async Task CompileAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var ext = ExtremeModeToggle.IsChecked == true;
            var mode = ext ? "everything" : "speed";
            Label(ext ? "Extreme compilation" : "Compiling packages");
            Log($"Starting {(ext ? "EXTREME " : "")}compilation ({mode})...\n");

            await AdbExecutor.ExecuteCommandAsync(
                $"shell cmd package compile -m {mode} -f -a",
                ct,
                line =>
                {
                    Log($"{line}\n");
                    var m = Regex.Match(line, @"on\s+([\w\.]+)$");
                    if (m.Success) Label($"Compiling {m.Groups[1].Value.Split('.').Last()}...");
                });

            Progress(++_step);
            _report.CompilationMode = mode;
            OptimizationPerformed?.Invoke("compilation_optimized");
        }

        private async Task OptimizeDexAsync(CancellationToken ct)
        {
            try
            {
                Label("DEX optimization");
                Log("Spoofing battery...\n");
                await Adb("shell dumpsys battery set level 100", ct);
                OptimizationPerformed?.Invoke("battery_spoofed");

                ct.ThrowIfCancellationRequested();
                Log("Running background DEX...\n");
                await Adb("shell cmd package bg-dexopt-job", ct);
                Progress(++_step);
                _report.DexOptimized = true;
            }
            finally
            {
                Log("Resetting battery...\n");
                await Adb("shell dumpsys battery reset", CancellationToken.None);
                Log("✓ Battery reset.\n");
            }
        }

        private void Stop()
        {
            if (!DialogService.Instance.ConfirmStopOptimization(Application.Current.MainWindow)) return;
            Log("Stopping...\n");
            _cts?.Cancel();
        }

        private static bool IsCrit(string p) => Critical.Any(c => p.StartsWith(c, StringComparison.OrdinalIgnoreCase));

        private async Task Adb(string a, CancellationToken ct) { var o = await AdbExecutor.ExecuteCommandAsync(a, ct); if (!string.IsNullOrWhiteSpace(o)) Log($"{o}\n"); }
        private Task<string> AdbOut(string a, CancellationToken ct) => AdbExecutor.ExecuteCommandAsync(a, ct);

        private void SetBtn(bool stop) => Dispatcher.Invoke(() =>
        {
            OptimizeButton.Style = (Style)FindResource(stop ? "App.Style.Button.Destructive" : "App.Style.Button");
            OptimizeButton.Content = stop ? Strings.Dialog_StopOptimization_StopButton : Strings.Optimize_Button_Start;
            OptimizeButton.Tag = stop ? "\uE711" : "\uE768";
        });

        private void Progress(int s) => Dispatcher.Invoke(() => { OptimizeProgress.Value = Math.Min(s, TotalSteps); ProgressText.Text = $"{Math.Min(s * 100.0 / TotalSteps, 100):0}%"; });
        private void Label(string t) => Dispatcher.Invoke(() => StepLabel.Text = t);
        private void SetConsole(bool on) => Dispatcher.Invoke(() => 
        { 
            ConsoleStatusDot.SetResourceReference(Shape.FillProperty, on ? "App.Brush.Status.Active" : "App.Brush.Status.Idle"); 
            ConsoleStatusText.Text = on ? Strings.Optimize_Console_Status_Running : Strings.Optimize_Console_Status_Idle; 
        });
        private void Clear() => Dispatcher.Invoke(() => { lock (_lock) TerminalBox.Clear(); });
        private void Log(string t) { if (string.IsNullOrEmpty(t)) return; Dispatcher.Invoke(() => { lock (_lock) { TerminalBox.AppendText(t); TerminalBox.ScrollToEnd(); } }); }
        private void ShowReport() { if (Application.Current.MainWindow is MainWindow owner) Dispatcher.Invoke(() => new OptimizationReportDialog(_report) { Owner = owner }.ShowDialog()); }

        private void InitParticles()
        {
            var particles = new HashSet<Rectangle>();
            var rng = new Random();
            var wasRunning = false;
            var max = 8;

            var spawn = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            var state = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };

            state.Tick += (_, _) =>
            {
                if (_running == wasRunning) return;
                wasRunning = _running;
                spawn.Interval = TimeSpan.FromMilliseconds(_running ? 150 : 800);
                max = _running ? 20 : 8;
                if (_running) for (int i = 0; i < 10; i++) Spawn();
            };

            spawn.Tick += (_, _) => { if (particles.Count < max) for (int i = 0; i < (_running ? rng.Next(1, 4) : 1); i++) Spawn(); };

            void Spawn()
            {
                var done = _step >= TotalSteps * 0.95;
                var p = new Rectangle
                {
                    Width = rng.Next(_running ? 80 : 40, _running ? 200 : 100),
                    Height = _running ? rng.Next(2, 4) : 1,
                    Fill = MakeBrush(done, _running),
                    Effect = new BlurEffect { Radius = _running ? 5 : 2 },
                    Opacity = 0,
                    RenderTransform = new TranslateTransform()
                };
                var h = Math.Max(1, (int)(ParticleCanvas.ActualHeight > 0 ? ParticleCanvas.ActualHeight : 600));
                Canvas.SetLeft(p, -p.Width - 50);
                Canvas.SetTop(p, rng.Next(0, h));
                ParticleCanvas.Children.Add(p);
                particles.Add(p);
                Animate(p, particles, rng);
            }

            Dispatcher.BeginInvoke(() => { for (int i = 0; i < 3; i++) Spawn(); }, DispatcherPriority.Loaded);
            spawn.Start();
            state.Start();
            ParticleCanvas.Unloaded += (_, _) => { spawn.Stop(); state.Stop(); ParticleCanvas.Children.Clear(); particles.Clear(); };
        }

        private void Animate(Rectangle p, HashSet<Rectangle> particles, Random rng)
        {
            var w = ParticleCanvas.ActualWidth > 0 ? ParticleCanvas.ActualWidth : 1200;
            var dur = TimeSpan.FromMilliseconds(rng.Next(1500, 3000) * (_running ? 0.5 : 1.5));
            var sb = new Storyboard();
            sb.Children.Add(Anim(p, "(UIElement.Opacity)", 0, 0.8, TimeSpan.FromMilliseconds(200)));
            sb.Children.Add(Anim(p, "(UIElement.Opacity)", 0.8, 0, dur, TimeSpan.FromMilliseconds(200)));
            sb.Children.Add(Anim(p, "(UIElement.RenderTransform).(TranslateTransform.X)", 0, w + p.Width + 100, dur));
            if (_running) sb.Children.Add(Anim(p, "(UIElement.RenderTransform).(TranslateTransform.Y)", 0, rng.Next(-30, 31), dur));
            sb.Completed += (_, _) => { ParticleCanvas.Children.Remove(p); particles.Remove(p); };
            sb.Begin();
        }

        private static LinearGradientBrush MakeBrush(bool done, bool running)
        {
            var (c1, c2, c3) = done ? ("#00FFD700", "#FFFFD700", "#00FF6400") : running ? ("#0000BFFF", "#FF7D64FF", "#00FF00BF") : ("#00007FFF", "#6400BFFF", "#00007FFF");
            return new() { StartPoint = new(0, 0), EndPoint = new(1, 0), GradientStops = { new((Color)ColorConverter.ConvertFromString(c1), 0), new((Color)ColorConverter.ConvertFromString(c2), 0.5), new((Color)ColorConverter.ConvertFromString(c3), 1) } };
        }

        private static DoubleAnimation Anim(UIElement t, string path, double from, double to, TimeSpan dur, TimeSpan? begin = null)
        {
            var a = new DoubleAnimation(from, to, dur) { BeginTime = begin ?? TimeSpan.Zero };
            Storyboard.SetTarget(a, t);
            Storyboard.SetTargetProperty(a, new PropertyPath(path));
            return a;
        }
    }

    public sealed class OptimizationReport
    {
        public bool CacheCleared { get; set; }
        public long MemoryFreedKb { get; set; }
        public int ProcessesKilled { get; set; }
        public List<(string pkg, long kb)> AppsForceKilled { get; } = new();
        public long StorageCleanedKb { get; set; }
        public List<string> CleanedItems { get; } = new();
        public bool TrimExecuted { get; set; }
        public bool NetworkOptimized { get; set; }
        public string? CompilationMode { get; set; }
        public bool DexOptimized { get; set; }

        public void Reset()
        {
            CacheCleared = false;
            MemoryFreedKb = 0;
            ProcessesKilled = 0;
            AppsForceKilled.Clear();
            StorageCleanedKb = 0;
            CleanedItems.Clear();
            TrimExecuted = false;
            NetworkOptimized = false;
            CompilationMode = null;
            DexOptimized = false;
        }

        public double TotalFreedMb => (MemoryFreedKb + StorageCleanedKb) / 1024.0;
    }
}