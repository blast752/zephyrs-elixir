namespace ZephyrsElixir.UI.Pages
{
    public partial class Advanced : UserControl
    {
        private static class Ops
        {
            public const string SafetyCore = "safety_core", Dns = "dns", Animations = "animations",
                Compilation = "compilation", Battery = "battery_spoofed", AdId = "ad_id",
                CaptivePortal = "captive_portal", GoogleCore = "google_core", RamExpansion = "ram_expansion";
        }

        private readonly HashSet<string> _ops = new();
        private readonly ObservableCollection<DnsProviderViewModel> _dns = new();
        private readonly UIElement[] _devControls;

        private Timer? _animTimer, _pingTimer;
        private CancellationTokenSource? _pingCts;
        private DateTime? _lastSelect;
        private string? _cfgDns;
        private bool _resetting, _initialized, _interacting, _comboOpen;
        private long _ramMb;
        private bool _hasEnoughRam;

        public Advanced()
        {
            InitializeComponent();
            _devControls = new UIElement[]
            {
                ApplyDnsButton, DnsProviderComboBox,
                ResetAnimationsButton, ApplyAnimationsButton,
                ResetBatteryButton, ResetCompilationButton
            };
            Loaded += OnLoad;
            Unloaded += OnUnload;
        }

        private void OnLoad(object s, RoutedEventArgs e)
        {
            InitDns();
            StartPingMonitor();
            UpdateUI();
            this.SubscribeToDeviceUpdates(onStatusChanged: OnDeviceChanged, controls: _devControls);
            
            foreach (var ctrl in new UIElement[] { SafetyCoreButton, ResetAdIdButton, CaptivePortalButton, GoogleCoreControlButton, RamExpansionButton })
                LicenseGuard.SetRequiredTier(ctrl, LicenseTier.Pro);;
            
            if (DeviceManager.Instance.IsConnected) { LoadAnimSpeed(); StartAnimSync(); _ = CheckRamAsync(); }
            _initialized = true;
        }

        private void OnUnload(object s, RoutedEventArgs e) { _initialized = false; _animTimer?.Dispose(); _pingTimer?.Dispose(); _pingCts?.Cancel(); }

        private void OnDeviceChanged(bool on)
        {
            if (on) { LoadAnimSpeed(); StartAnimSync(); _ = CheckRamAsync(); }
            else { _animTimer?.Dispose(); _animTimer = null; ResetSlider(); }
            UpdateResetBtn();
        }

        private void InitDns()
        {
            if (_dns.Count > 0) return;
            foreach (var (n, h) in new[] { ("NextDNS", "dns.nextdns.io"), ("AdGuard", "dns.adguard-dns.com"), ("Cloudflare", "1dot1dot1dot1.cloudflare-dns.com"), ("Google", "dns.google"), ("Quad9", "dns.quad9.net") })
                _dns.Add(new DnsProviderViewModel { Name = n, Hostname = h });
            if (DnsProviderComboBox != null) { DnsProviderComboBox.ItemsSource = _dns; DnsProviderComboBox.SelectedIndex = 0; }
        }

        private void OnDnsComboBoxDropDownOpened(object s, EventArgs e) { _comboOpen = true; _lastSelect = null; StartPinging(); }
        private void OnDnsComboBoxDropDownClosed(object s, EventArgs e) { _comboOpen = false; _lastSelect = DateTime.Now; }

        private void StartPingMonitor()
        {
            _pingTimer = new Timer(_ =>
            {
                if (_comboOpen || (_lastSelect.HasValue && (DateTime.Now - _lastSelect.Value).TotalSeconds < 10)) StartPinging();
                else _pingCts?.Cancel();
            }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        private void StartPinging()
        {
            _pingCts?.Cancel();
            _pingCts = new();
            var ct = _pingCts.Token;
            Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.WhenAll(_dns.Select(p => PingAsync(p, ct)));
                    try { await Task.Delay(2000, ct); } catch { break; }
                }
            }, ct);
        }

        private async Task PingAsync(DnsProviderViewModel p, CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                await Dispatcher.InvokeAsync(() => p.IsPinging = true);
                using var ping = new Ping();
                var r = await ping.SendPingAsync(p.Hostname, 3000);
                if (!ct.IsCancellationRequested)
                    await Dispatcher.InvokeAsync(() => { p.PingMs = r.Status == IPStatus.Success ? (int)r.RoundtripTime : 0; p.IsPinging = false; });
            }
            catch { if (!ct.IsCancellationRequested) await Dispatcher.InvokeAsync(() => { p.PingMs = 0; p.IsPinging = false; }); }
        }

        private async void OnApplyDnsClick(object s, RoutedEventArgs e)
        {
            if (DnsProviderComboBox?.SelectedItem is not DnsProviderViewModel p || !Confirm(Strings.Advanced_DNS_Confirm, p.Name)) return;
            await Exec(ApplyDnsButton, PrivacyStatusBorder, PrivacyStatusText, async () =>
            {
                await Adb("shell settings put global private_dns_mode hostname");
                await Adb($"shell settings put global private_dns_specifier {p.Hostname}");
                _cfgDns = p.Name;
                Track(Ops.Dns);
                return (true, string.Format(Strings.Advanced_DNS_Success, p.Name));
            });
        }

        private async void OnResetAdIdClick(object s, RoutedEventArgs e)
        {
            if (!Confirm(Strings.Advanced_ResetAdId_Confirm)) return;
            await Exec(ResetAdIdButton, PrivacyStatusBorder, PrivacyStatusText, async () =>
            {
                await Adb($"shell am broadcast -a com.google.android.gms.ads.identifier.service.START --es \"registration_id\" \"{Guid.NewGuid().ToString().ToLowerInvariant()}\"");
                await Task.Delay(500);
                await Adb("shell settings put global ad_id 00000000-0000-0000-0000-000000000000");
                Track(Ops.AdId);
                return (true, Strings.Advanced_ResetAdId_Success);
            });
        }

        private async void OnSafetyCoreClick(object s, RoutedEventArgs e)
        {
            if (!Confirm(Strings.Advanced_SafetyCore_Confirm)) return;
            await Exec(SafetyCoreButton, PrivacyStatusBorder, PrivacyStatusText, async () =>
            {
                var chk = await Adb("shell pm list packages com.google.android.safetycore");
                if (!chk.Contains("com.google.android.safetycore")) return (true, Strings.Advanced_SafetyCore_NotInstalled);
                var o = await Adb("shell pm disable-user --user 0 com.google.android.safetycore");
                if (o.Contains("disabled", StringComparison.OrdinalIgnoreCase) || o.Contains("new state", StringComparison.OrdinalIgnoreCase)) { Track(Ops.SafetyCore); return (true, Strings.Advanced_SafetyCore_Success); }
                return (false, $"{Strings.Advanced_Error}: {o}");
            });
        }

        private async void LoadAnimSpeed()
        {
            if (AnimationSlider == null) return;
            try { UpdateSlider(await GetAnimSpeedAsync()); }
            catch (Exception ex) { Debug.WriteLine($"LoadAnimSpeed: {ex}"); }
        }

        private void StartAnimSync()
        {
            _animTimer = new Timer(async _ =>
            {
                if (!DeviceManager.Instance.IsConnected || _resetting || _interacting) return;
                try
                {
                    var cur = await GetAnimSpeedAsync();
                    await Dispatcher.InvokeAsync(() => { if (AnimationSlider != null && !_interacting && Math.Abs(AnimationSlider.Value - cur) > 0.01) UpdateSlider(cur); });
                }
                catch { }
            }, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        }

        private static async Task<double> GetAnimSpeedAsync()
        {
            var o = await AdbExecutor.ExecuteCommandAsync("shell settings get global animator_duration_scale");
            return double.TryParse(o.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, 0, 2) : 1.0;
        }

        private void UpdateSlider(double v)
        {
            if (AnimationSlider == null || AnimationValueText == null) return;
            _resetting = true;
            AnimationSlider.Value = v;
            AnimationValueText.Text = v == 0 ? "Off" : $"{v:F2}x";
            _resetting = false;
        }

        private void ResetSlider()
        {
            _resetting = true;
            if (AnimationSlider != null) AnimationSlider.Value = 1.0;
            if (AnimationValueText != null) AnimationValueText.Text = "1.0x";
            _resetting = false;
        }

        private void OnAnimationSliderChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_resetting || AnimationValueText == null) return;
            AnimationValueText.Text = e.NewValue == 0 ? "Off" : $"{e.NewValue:F2}x";
            AnimateValue();
        }

        private void OnSliderMouseDown(object s, MouseButtonEventArgs e) => _interacting = true;
        private void OnSliderMouseUp(object s, MouseButtonEventArgs e) => Task.Delay(1000).ContinueWith(_ => Dispatcher.BeginInvoke(() => _interacting = false));

        private void AnimateValue()
        {
            if (AnimationValueText == null) return;
            var a = new DoubleAnimation { From = 1.0, To = 1.15, Duration = TimeSpan.FromMilliseconds(100), AutoReverse = true, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            var t = new ScaleTransform(1, 1);
            AnimationValueText.RenderTransform = t;
            AnimationValueText.RenderTransformOrigin = new Point(0.5, 0.5);
            t.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            t.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        private async void OnApplyAnimationsClick(object s, RoutedEventArgs e)
        {
            if (AnimationSlider == null) return;
            var v = AnimationSlider.Value;
            await Exec(ApplyAnimationsButton, PrivacyStatusBorder, PrivacyStatusText, async () => { await SetAnimSpeedAsync(v); Track(Ops.Animations); return (true, Strings.Advanced_ApplyAnimations_Success); });
        }

        private void OnResetAnimationsClick(object s, RoutedEventArgs e)
        {
            if (ResetAnimationsButton == null || AnimationSlider == null) return;
            ResetAnimationsButton.IsEnabled = false;
            _interacting = true;
            _resetting = true;

            var a = new DoubleAnimation { To = 1.0, Duration = TimeSpan.FromMilliseconds(500), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
            a.Completed += async (_, _) =>
            {
                try
                {
                    await SetAnimSpeedAsync(1.0);
                    Clear(Ops.Animations);
                    Show(PrivacyStatusBorder, PrivacyStatusText, Strings.Advanced_ResetAnimations_Success);
                    await Task.Delay(2000);
                    Hide(PrivacyStatusBorder);
                }
                finally { _resetting = false; _interacting = false; if (ResetAnimationsButton != null) ResetAnimationsButton.IsEnabled = DeviceManager.Instance.IsConnected; }
            };
            AnimationSlider.BeginAnimation(RangeBase.ValueProperty, a);
        }

        private static async Task SetAnimSpeedAsync(double s)
        {
            var v = s.ToString("F1", CultureInfo.InvariantCulture);
            foreach (var n in new[] { "animator_duration_scale", "transition_animation_scale", "window_animation_scale" })
                await AdbExecutor.ExecuteCommandAsync($"shell settings put global {n} {v}");
        }

        private async void OnResetBatteryClick(object s, RoutedEventArgs e)
        {
            if (!Confirm(Strings.Advanced_ResetBattery_Confirm)) return;
            await Exec(ResetBatteryButton, TroubleshootingStatusBorder, TroubleshootingStatusText, async () => { await Adb("shell dumpsys battery reset"); Clear(Ops.Battery); return (true, Strings.Advanced_ResetBattery_Success); });
        }

        private async void OnResetCompilationClick(object s, RoutedEventArgs e)
        {
            if (!Confirm(Strings.Advanced_ResetCompilation_Confirm)) return;
            await Exec(ResetCompilationButton, TroubleshootingStatusBorder, TroubleshootingStatusText, async () => { await Adb("shell cmd package compile --reset -a"); Clear(Ops.Compilation); return (true, Strings.Advanced_ResetCompilation_Success); });
        }

        private async void OnResetAllClick(object s, RoutedEventArgs e)
        {
            if (_ops.Count == 0 || !Confirm(Strings.Advanced_ResetAll_Confirm)) return;
            if (ResetAllButton != null) ResetAllButton.IsEnabled = false;
            Show(TroubleshootingStatusBorder, TroubleshootingStatusText, Strings.Advanced_Status_ResettingAll);

            try
            {
                var ops = new HashSet<string>(_ops);
                var actions = new (string Op, string Status, Func<Task> Act)[]
                {
                    (Ops.Animations, Strings.Advanced_Status_ResetAnimations, async () => { await SetAnimSpeedAsync(1.0); ResetSlider(); }),
                    (Ops.Compilation, Strings.Advanced_Status_ResetCompilation, () => Adb("shell cmd package compile --reset -a")),
                    (Ops.Battery, Strings.Advanced_Status_ResetBattery, () => Adb("shell dumpsys battery reset")),
                    (Ops.Dns, Strings.Advanced_Status_ResetDNS, ResetDnsAsync),
                    (Ops.SafetyCore, Strings.Advanced_Status_ReenableSafetyCore, ReenableSafetyCoreAsync),
                    (Ops.AdId, Strings.Advanced_Status_ResetAdId, ReenableAdIdAsync),
                    (Ops.CaptivePortal, Strings.Advanced_Status_ReenableCaptivePortal, ReenableCaptiveAsync),
                    (Ops.GoogleCore, Strings.Advanced_Status_ReenableGoogleCoreControl, ReenableGoogleAsync),
                    (Ops.RamExpansion, Strings.Advanced_Status_ReenableRamExpansion, ReenableRamAsync)
                };

                foreach (var (op, status, act) in actions)
                {
                    if (!ops.Contains(op)) continue;
                    Show(TroubleshootingStatusBorder, TroubleshootingStatusText, status);
                    await act();
                }

                _ops.Clear();
                _cfgDns = null;
                UpdateUI();
                Show(TroubleshootingStatusBorder, TroubleshootingStatusText, Strings.Advanced_ResetAll_Success);
                await Task.Delay(3000);
                Hide(TroubleshootingStatusBorder);
            }
            catch (Exception ex) { Show(TroubleshootingStatusBorder, TroubleshootingStatusText, $"{Strings.Advanced_Error}: {ex.Message}"); }
            finally { UpdateResetBtn(); }
        }

        private async void OnCaptivePortalClick(object s, RoutedEventArgs e)
        {
            if (!Confirm(Strings.Advanced_CaptivePortal_Confirm)) return;
            await Exec(CaptivePortalButton, PrivacyStatusBorder, PrivacyStatusText, async () =>
            {
                await Adb("shell settings put global captive_portal_detection_enabled 0");
                await Adb("shell settings put global captive_portal_mode 0");
                Track(Ops.CaptivePortal);
                return (true, Strings.Advanced_CaptivePortal_Success);
            });
        }

        private async void OnGoogleCoreControlClick(object s, RoutedEventArgs e)
        {
            if (!Confirm(Strings.Advanced_GoogleCoreControl_Confirm)) return;
            await Exec(GoogleCoreControlButton, PrivacyStatusBorder, PrivacyStatusText, async () =>
            {
                await Adb("shell settings put global google_core_control 0");
                Track(Ops.GoogleCore);
                return (true, Strings.Advanced_GoogleCoreControl_Success);
            });
        }

        private async void OnRamExpansionClick(object s, RoutedEventArgs e)
        {
            if (!_hasEnoughRam) return;
            if (!Confirm(Strings.Advanced_RamExpansion_Confirm)) return;
            await Exec(RamExpansionButton, PrivacyStatusBorder, PrivacyStatusText, async () =>
            {
                var brand = (await Adb("shell getprop ro.product.brand")).Trim().ToLowerInvariant();
                var mfr = (await Adb("shell getprop ro.product.manufacturer")).Trim().ToLowerInvariant();
                foreach (var cmd in GetRamCmds(brand, mfr)) await Adb(cmd);
                Track(Ops.RamExpansion);
                return (true, string.Format(Strings.Advanced_RamExpansion_Success_Brand, brand.ToUpperInvariant()));
            });
        }

        private static IEnumerable<string> GetRamCmds(string brand, string mfr) => (brand, mfr) switch
        {
            ("samsung", _) or (_, "samsung") => new[] { "shell settings put global ram_expand_size_list 0", "shell settings put global ram_expand_size 0", "shell settings put global zram_enabled 0" },
            ("xiaomi" or "redmi" or "poco", _) or (_, "xiaomi") => new[] { "shell settings put global extra_free_kbytes 0", "shell setprop persist.miui.extm.enable 0", "shell settings put global mi_ram_expansion_enabled 0" },
            ("oneplus" or "oppo" or "realme", _) or (_, "oneplus" or "oppo" or "realme") => new[] { "shell settings put global ram_boost_enabled 0", "shell settings put global ram_expand_size 0", "shell setprop persist.sys.oplus.nandswap.condition false" },
            ("vivo" or "iqoo", _) or (_, "vivo") => new[] { "shell settings put global virtual_ram_config 0", "shell settings put global ram_expand_size 0" },
            ("huawei" or "honor", _) or (_, "huawei") => new[] { "shell settings put global ram_expand_enabled 0", "shell settings put global memory_expansion_enabled 0" },
            _ => new[] { "shell settings put global ram_expand_size 0", "shell settings put global zram_enabled 0", "shell settings put global enable_swap 0" }
        };

        private async Task CheckRamAsync()
        {
            try
            {
                var o = await Adb("shell cat /proc/meminfo");
                var m = Regex.Match(o, @"MemTotal:\s*(\d+)\s*kB");
                if (m.Success && long.TryParse(m.Groups[1].Value, out var kb))
                {
                    _ramMb = kb / 1024;
                    _hasEnoughRam = _ramMb >= 5000;

                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (RamExpansionDescription != null) 
                            RamExpansionDescription.Text = _hasEnoughRam
                                ? string.Format(Strings.Advanced_RamExpansion_Description_WithRam, _ramMb / 1024.0)
                                : Strings.Advanced_RamExpansion_Description_LowRam;
                    });
                }
            }
            catch { }
        }

        private async Task ResetDnsAsync() { await Adb("shell settings put global private_dns_mode off"); await Adb("shell settings delete global private_dns_specifier"); _cfgDns = null; }
        private static async Task ReenableSafetyCoreAsync() { var c = await AdbExecutor.ExecuteCommandAsync("shell pm list packages com.google.android.safetycore"); if (c.Contains("com.google.android.safetycore")) await AdbExecutor.ExecuteCommandAsync("shell pm enable com.google.android.safetycore"); }
        private static async Task ReenableAdIdAsync() { await AdbExecutor.ExecuteCommandAsync("shell settings delete global ad_id"); await Task.Delay(100); await AdbExecutor.ExecuteCommandAsync("shell am broadcast -a com.google.android.gms.ads.identifier.service.RESET"); }
        private static async Task ReenableCaptiveAsync() { await AdbExecutor.ExecuteCommandAsync("shell settings put global captive_portal_detection_enabled 1"); await AdbExecutor.ExecuteCommandAsync("shell settings put global captive_portal_mode 1"); }
        private static Task ReenableGoogleAsync() => AdbExecutor.ExecuteCommandAsync("shell settings delete global google_core_control");
        private static async Task ReenableRamAsync() { foreach (var s in new[] { "ram_expand_size_list", "zram_enabled", "extra_free_kbytes", "ram_boost_enabled", "ram_expand_size", "virtual_ram_config", "enable_swap" }) await AdbExecutor.ExecuteCommandAsync($"shell settings delete global {s}"); }

        private void Track(string op) { _ops.Add(op); UpdateUI(); }
        private void Clear(string op) { _ops.Remove(op); UpdateUI(); }
        private void UpdateUI() { UpdateText(); UpdateResetBtn(); }
        private void UpdateText() { if (SessionOperationsText != null) SessionOperationsText.Text = _ops.Count == 0 ? Strings.Advanced_ResetAll_Description : string.Format(Strings.Advanced_ResetAll_Description_WithCount, _ops.Count); }
        private void UpdateResetBtn() { if (ResetAllButton != null) ResetAllButton.IsEnabled = DeviceManager.Instance.IsConnected && _ops.Count > 0; }
        public void TrackExternalOperation(string op) { if (!_initialized) return; if (!Dispatcher.CheckAccess()) Dispatcher.BeginInvoke(() => Track(op)); else Track(op); }

        private bool Confirm(string message, string? p = null)
        {
            try 
            { 
                return DialogService.Instance.ConfirmDirect(
                    p != null ? string.Format(message, p) : message, 
                    Window.GetWindow(this), 
                    Strings.Advanced_Confirm_Title); 
            }
            catch { return false; }
        }

        private async Task Exec(Button? btn, Border? border, TextBlock? text, Func<Task<(bool, string)>> op)
        {
            if (btn != null) btn.IsEnabled = false;
            Show(border, text, Strings.Advanced_Status_Processing);
            try { var (ok, msg) = await op(); Show(border, text, msg); if (ok) { await Task.Delay(3000); Hide(border); } }
            catch (Exception ex) { Show(border, text, $"{Strings.Advanced_Error}: {ex.Message}"); }
            finally { if (btn != null) btn.IsEnabled = DeviceManager.Instance.IsConnected; }
        }

        private static void Show(Border? b, TextBlock? t, string msg) { if (b == null || t == null) return; t.Text = msg; b.Visibility = Visibility.Visible; }
        private static void Hide(Border? b) { if (b != null) b.Visibility = Visibility.Collapsed; }
        private static Task<string> Adb(string cmd) => AdbExecutor.ExecuteCommandAsync(cmd);
    }
}