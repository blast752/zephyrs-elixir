namespace ZephyrsElixir.UI.Pages;
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

    #region Lifecycle

    public Advanced()
    {
        InitializeComponent();
        _devControls =
        [
            ApplyDnsButton, DnsProviderComboBox,
            ResetAnimationsButton, ApplyAnimationsButton,
            ResetBatteryButton, ResetCompilationButton
        ];
        Loaded += OnLoad;
        Unloaded += OnUnload;
    }

    private void OnLoad(object s, RoutedEventArgs e)
    {
        InitDns();
        StartPingMonitor();
        UpdateUI();
        this.SubscribeToDeviceUpdates(onStatusChanged: OnDeviceChanged, controls: _devControls);
        
        foreach (var ctrl in (UIElement[])[SafetyCoreButton, ResetAdIdButton, CaptivePortalButton, GoogleCoreControlButton, RamExpansionButton])
            LicenseGuard.SetRequiredTier(ctrl, LicenseTier.Pro);
        
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

    #endregion

    #region Privacy & DNS

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

    private async void OnSafetyCoreClick(object s, RoutedEventArgs e)
    {
        if (!Confirm(Strings.Advanced_SafetyCore_Confirm)) return;
        if (SafetyCoreButton != null) SafetyCoreButton.IsEnabled = false;
        Show(PrivacyStatusBorder, PrivacyStatusText, Strings.Advanced_Status_Processing);
        try
        {
            var result = await Pro.ExecuteAsync(ProCommandIds.SafetyCore);
            if (result.Success)
            {
                var isNotInstalled = result.Message.Contains("not installed", StringComparison.OrdinalIgnoreCase);
                Track(Ops.SafetyCore);
                Show(PrivacyStatusBorder, PrivacyStatusText, isNotInstalled ? Strings.Advanced_SafetyCore_NotInstalled : Strings.Advanced_SafetyCore_Success);
                await Task.Delay(3000);
                Hide(PrivacyStatusBorder);
            }
            else
            {
                Show(PrivacyStatusBorder, PrivacyStatusText, $"{Strings.Advanced_Error}: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            Show(PrivacyStatusBorder, PrivacyStatusText, $"{Strings.Advanced_Error}: {ex.Message}");
        }
        finally
        {
            if (SafetyCoreButton != null) SafetyCoreButton.IsEnabled = DeviceManager.Instance.IsConnected;
        }
    }

    private async void OnResetAdIdClick(object s, RoutedEventArgs e)
    {
        if (!Confirm(Strings.Advanced_ResetAdId_Confirm)) return;
        await ExecPro(ResetAdIdButton, PrivacyStatusBorder, PrivacyStatusText,
            ProCommandIds.ResetAdId, Ops.AdId, Strings.Advanced_ResetAdId_Success);
    }

    private async void OnCaptivePortalClick(object s, RoutedEventArgs e)
    {
        if (!Confirm(Strings.Advanced_CaptivePortal_Confirm)) return;
        await ExecPro(CaptivePortalButton, PrivacyStatusBorder, PrivacyStatusText,
            ProCommandIds.CaptivePortal, Ops.CaptivePortal, Strings.Advanced_CaptivePortal_Success);
    }

    private async void OnGoogleCoreControlClick(object s, RoutedEventArgs e)
    {
        if (!Confirm(Strings.Advanced_GoogleCoreControl_Confirm)) return;
        await ExecPro(GoogleCoreControlButton, PrivacyStatusBorder, PrivacyStatusText,
            ProCommandIds.GoogleCoreControl, Ops.GoogleCore, Strings.Advanced_GoogleCoreControl_Success);
    }

    private async void OnRamExpansionClick(object s, RoutedEventArgs e)
    {
        if (!_hasEnoughRam) return;
        if (!Confirm(Strings.Advanced_RamExpansion_Confirm)) return;
        var brand = (await Adb("shell getprop ro.product.brand")).Trim().ToUpperInvariant();
        await ExecPro(RamExpansionButton, PrivacyStatusBorder, PrivacyStatusText,
            ProCommandIds.RamExpansion, Ops.RamExpansion, string.Format(Strings.Advanced_RamExpansion_Success_Brand, brand));
    }

    private async Task ExecPro(Button? btn, Border? border, TextBlock? text, string commandId, string opKey, string? successMessage = null)
    {
        if (btn != null) btn.IsEnabled = false;
        Show(border, text, Strings.Advanced_Status_Processing);

        try
        {
            var result = await Pro.ExecuteAsync(commandId);

            if (result.Success)
            {
                Track(opKey);
                Show(border, text, successMessage ?? result.Message);
                await Task.Delay(3000);
                Hide(border);
            }
            else
            {
                Show(border, text, $"{Strings.Advanced_Error}: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            Show(border, text, $"{Strings.Advanced_Error}: {ex.Message}");
        }
        finally
        {
            if (btn != null) btn.IsEnabled = DeviceManager.Instance.IsConnected;
        }
    }

    #endregion

    #region Animations

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
        AnimationValueText.Text = v == 0 ? Strings.Advanced_Animation_Value_Off : $"{v:F2}x";
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
        AnimationValueText.Text = e.NewValue == 0 ? Strings.Advanced_Animation_Value_Off : $"{e.NewValue:F2}x";
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

    #endregion

    #region Troubleshooting

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

            var freeReverts = new (string Op, string Status, Func<Task> Act)[]
            {
                (Ops.Animations, Strings.Advanced_Status_ResetAnimations, async () => { await SetAnimSpeedAsync(1.0); ResetSlider(); }),
                (Ops.Compilation, Strings.Advanced_Status_ResetCompilation, () => Adb("shell cmd package compile --reset -a")),
                (Ops.Battery, Strings.Advanced_Status_ResetBattery, () => Adb("shell dumpsys battery reset")),
                (Ops.Dns, Strings.Advanced_Status_ResetDNS, ResetDnsAsync),
            };

            foreach (var (op, status, act) in freeReverts)
            {
                if (!ops.Contains(op)) continue;
                Show(TroubleshootingStatusBorder, TroubleshootingStatusText, status);
                await act();
            }

            var proReverts = new (string Op, string Status, string CommandId)[]
            {
                (Ops.SafetyCore, Strings.Advanced_Status_ReenableSafetyCore, ProCommandIds.SafetyCore),
                (Ops.AdId, Strings.Advanced_Status_ResetAdId, ProCommandIds.ResetAdId),
                (Ops.CaptivePortal, Strings.Advanced_Status_ReenableCaptivePortal, ProCommandIds.CaptivePortal),
                (Ops.GoogleCore, Strings.Advanced_Status_ReenableGoogleCoreControl, ProCommandIds.GoogleCoreControl),
                (Ops.RamExpansion, Strings.Advanced_Status_ReenableRamExpansion, ProCommandIds.RamExpansion),
            };

            foreach (var (op, status, cmdId) in proReverts)
            {
                if (!ops.Contains(op)) continue;
                Show(TroubleshootingStatusBorder, TroubleshootingStatusText, status);
                await Pro.RevertAsync(cmdId);
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

    #endregion

    #region Helpers

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

    #endregion
}
