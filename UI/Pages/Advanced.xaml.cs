namespace ZephyrsElixir.UI.Pages;
public partial class Advanced : UserControl
{
    private readonly ObservableCollection<DnsProviderViewModel> _dns = new();
    private readonly ObservableCollection<VialChangeRow> _vialChanges = new();
    private readonly UIElement[] _devControls;

    private Timer? _animTimer, _pingTimer;
    private int _animSyncBusy;
    private CancellationTokenSource? _pingCts;
    private volatile bool _pingLoopRunning;
    private const long RamExpansionMinimumMb = 5000;

    private DateTime? _lastSelect;
    private bool _resetting, _initialized, _interacting, _comboOpen;
    private long _ramMb;
    private bool _hasEnoughRam;
    private SettingsSnapshot? _openVial;
    private bool _suppressVialSelectAll;

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
        this.SubscribeToActiveDevice(_ => OnDeviceChanged(DeviceManager.Instance.IsConnected));

        foreach (var ctrl in (UIElement[])[SafetyCoreButton, ResetAdIdButton, CaptivePortalButton, GoogleCoreControlButton, RamExpansionButton])
            LicenseGuard.SetRequiredTier(ctrl, LicenseTier.Pro);

        if (DeviceManager.Instance.IsConnected) { LoadAnimSpeed(); StartAnimSync(); _ = CheckRamAsync(); }

        VialChangesList.ItemsSource = _vialChanges;
        SettingsTimeMachine.VialsChanged += OnVialsStoreChanged;
        OperationLedger.Changed += OnLedgerChanged;
        RefreshVials();
        _initialized = true;
    }

    private void OnUnload(object s, RoutedEventArgs e)
    {
        _initialized = false;
        _animTimer?.Dispose();
        _pingTimer?.Dispose();
        _pingCts?.Cancel();
        SettingsTimeMachine.VialsChanged -= OnVialsStoreChanged;
        OperationLedger.Changed -= OnLedgerChanged;
    }

    private void OnDeviceChanged(bool on)
    {
        if (on) { LoadAnimSpeed(); StartAnimSync(); _ = CheckRamAsync(); }
        else { _animTimer?.Dispose(); _animTimer = null; ResetSlider(); }
        // The ledger is per-device: switching phones must re-read the pending count for the new one.
        UpdateUI();
        RefreshVials();
    }

    private void OnRootPreviewKeyDown(object s, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || VialOverlayContainer.Visibility != Visibility.Visible) return;
        CloseVialOverlay();
        e.Handled = true;
    }

    private void InitDns()
    {
        if (_dns.Count > 0) return;
        foreach (var (n, h) in AppConfiguration.Dns.Providers)
            _dns.Add(new DnsProviderViewModel { Name = n, Hostname = h });
        _dns.Add(new DnsProviderViewModel { Name = Strings.Advanced_DNS_Custom, IsCustom = true });

        try { if (File.Exists(AppConfiguration.Paths.CustomDnsMarker)) CustomDnsBox.Text = File.ReadAllText(AppConfiguration.Paths.CustomDnsMarker).Trim(); } catch { }

        if (DnsProviderComboBox != null) { DnsProviderComboBox.ItemsSource = _dns; DnsProviderComboBox.SelectedIndex = 0; }
    }

    private void OnDnsSelectionChanged(object s, SelectionChangedEventArgs e)
    {
        if (CustomDnsPanel == null) return;
        CustomDnsPanel.Visibility = DnsProviderComboBox?.SelectedItem is DnsProviderViewModel { IsCustom: true }
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnCustomDnsTextChanged(object s, TextChangedEventArgs e)
    {
        if (CustomDnsValidBadge == null) return;
        CustomDnsValidBadge.Visibility = RecipeValidator.IsValidHostname(CustomDnsBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
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
        // The 1s monitor tick calls this repeatedly while the dropdown is open: without the guard
        // each call would cancel and respawn the loop, hammering the providers every second.
        if (_pingLoopRunning) return;
        _pingLoopRunning = true;

        _pingCts?.Cancel();
        _pingCts = new();
        var ct = _pingCts.Token;
        Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.WhenAll(_dns.Where(p => !p.IsCustom).Select(p => PingAsync(p, ct)));
                    try { await Task.Delay(2000, ct); } catch { break; }
                }
            }
            finally { _pingLoopRunning = false; }
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
        if (DnsProviderComboBox?.SelectedItem is not DnsProviderViewModel p) return;

        var hostname = p.IsCustom ? CustomDnsBox.Text.Trim() : p.Hostname;
        if (p.IsCustom && !RecipeValidator.IsValidHostname(hostname))
        {
            Show(PrivacyStatusBorder, PrivacyStatusText, Strings.Advanced_DNS_InvalidHostname);
            return;
        }

        var displayName = p.IsCustom ? hostname : p.Name;
        if (!Confirm(Strings.Advanced_DNS_Confirm, displayName)) return;

        await Exec(ApplyDnsButton, PrivacyStatusBorder, PrivacyStatusText, async () =>
        {
            await Adb("shell settings put global private_dns_mode hostname");
            await Adb($"shell settings put global private_dns_specifier {hostname}");
            if (p.IsCustom)
                try { File.WriteAllText(AppConfiguration.Paths.CustomDnsMarker, hostname); } catch { }
            Track(OperationLedger.Ops.Dns);
            return (true, string.Format(Strings.Advanced_DNS_Success, displayName));
        });
    }

    private async void OnSafetyCoreClick(object s, RoutedEventArgs e)
    {
        if (!Confirm(Strings.Advanced_SafetyCore_Confirm)) return;
        if (SafetyCoreButton != null) SafetyCoreButton.IsEnabled = false;
        Show(PrivacyStatusBorder, PrivacyStatusText, Strings.Advanced_Status_Processing);
        try
        {
            await CaptureAdvancedVialAsync();
            var result = await Pro.ExecuteAsync(ProCommandIds.SafetyCore);
            if (result.Success)
            {
                var isNotInstalled = result.Message.Contains("not installed", StringComparison.OrdinalIgnoreCase);
                Track(OperationLedger.Ops.SafetyCore);
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
            ProCommandIds.ResetAdId, Strings.Advanced_ResetAdId_Success);
    }

    private async void OnCaptivePortalClick(object s, RoutedEventArgs e)
    {
        if (!Confirm(Strings.Advanced_CaptivePortal_Confirm)) return;
        await ExecPro(CaptivePortalButton, PrivacyStatusBorder, PrivacyStatusText,
            ProCommandIds.CaptivePortal, Strings.Advanced_CaptivePortal_Success);
    }

    private async void OnGoogleCoreControlClick(object s, RoutedEventArgs e)
    {
        if (!Confirm(Strings.Advanced_GoogleCoreControl_Confirm)) return;
        await ExecPro(GoogleCoreControlButton, PrivacyStatusBorder, PrivacyStatusText,
            ProCommandIds.GoogleCoreControl, Strings.Advanced_GoogleCoreControl_Success);
    }

    private static readonly string[] RamExpansionKeys =
    {
        "ram_expand_size_list", "ram_expand_size", "zram_enabled", "extra_free_kbytes",
        "mi_ram_expansion_enabled", "ram_boost_enabled", "virtual_ram_config",
        "ram_expand_enabled", "memory_expansion_enabled", "enable_swap"
    };

    private enum RamVerifyOutcome { Applied, NotApplied, NotSupported }

    private async void OnRamExpansionClick(object s, RoutedEventArgs e)
    {
        if (!_hasEnoughRam)
        {
            Show(PrivacyStatusBorder, PrivacyStatusText, Strings.Advanced_RamExpansion_Description_LowRam);
            return;
        }
        if (!Confirm(Strings.Advanced_RamExpansion_Confirm)) return;

        if (RamExpansionButton != null) RamExpansionButton.IsEnabled = false;
        Show(PrivacyStatusBorder, PrivacyStatusText, Strings.Advanced_Status_Processing);
        try
        {
            await CaptureAdvancedVialAsync();
            var brand = (await Adb("shell getprop ro.product.brand")).Trim().ToUpperInvariant();
            var result = await Pro.ExecuteAsync(ProCommandIds.RamExpansion);
            if (!result.Success)
            {
                Show(PrivacyStatusBorder, PrivacyStatusText, $"{Strings.Advanced_Error}: {result.Message}");
                return;
            }

            switch (await VerifyRamExpansionAsync())
            {
                case RamVerifyOutcome.Applied:
                    Track(OperationLedger.Ops.RamExpansion);
                    Show(PrivacyStatusBorder, PrivacyStatusText,
                        $"{string.Format(Strings.Advanced_RamExpansion_Success_Brand, brand)} {Strings.Advanced_RamExpansion_Verified}");
                    await Task.Delay(4000);
                    Hide(PrivacyStatusBorder);
                    break;
                case RamVerifyOutcome.NotSupported:
                    Show(PrivacyStatusBorder, PrivacyStatusText, Strings.Advanced_RamExpansion_NotSupported);
                    await Task.Delay(5000);
                    Hide(PrivacyStatusBorder);
                    break;
                default:
                    Show(PrivacyStatusBorder, PrivacyStatusText, Strings.Advanced_RamExpansion_NotApplied);
                    break;
            }
        }
        catch (Exception ex)
        {
            Show(PrivacyStatusBorder, PrivacyStatusText, $"{Strings.Advanced_Error}: {ex.Message}");
        }
        finally
        {
            if (RamExpansionButton != null) RamExpansionButton.IsEnabled = DeviceManager.Instance.IsConnected;
        }
    }

    private async Task<RamVerifyOutcome> VerifyRamExpansionAsync()
    {
        var output = await Adb(
            "shell \"settings list global; echo zem=$(getprop persist.miui.extm.enable); echo zeo=$(getprop persist.sys.oplus.nandswap.condition)\"");

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.SplitLines())
        {
            var idx = line.IndexOf('=');
            if (idx <= 0) continue;
            values[line[..idx].Trim()] = line[(idx + 1)..].Trim();
        }

        var present = RamExpansionKeys.Where(values.ContainsKey).ToList();
        var miui = values.GetValueOrDefault("zem", string.Empty);
        var oplus = values.GetValueOrDefault("zeo", string.Empty);

        bool anyDisabled =
            present.Any(k => values[k] is "0" or "null") ||
            miui == "0" ||
            oplus == "false";

        if (anyDisabled) return RamVerifyOutcome.Applied;
        if (present.Count > 0 || miui.Length > 0 || oplus.Length > 0) return RamVerifyOutcome.NotApplied;
        return RamVerifyOutcome.NotSupported;
    }

    private async Task ExecPro(Button? btn, Border? border, TextBlock? text, string commandId, string? successMessage = null)
    {
        if (btn != null) btn.IsEnabled = false;
        Show(border, text, Strings.Advanced_Status_Processing);

        try
        {
            await CaptureAdvancedVialAsync();
            var result = await Pro.ExecuteAsync(commandId);

            if (result.Success)
            {
                if (OperationLedger.OpForProCommand(commandId) is { } op) Track(op);
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

    private async void LoadAnimSpeed()
    {
        if (AnimationSlider == null) return;
        try { UpdateSlider(await GetAnimSpeedAsync()); }
        catch (Exception ex) { Debug.WriteLine($"LoadAnimSpeed: {ex}"); }
    }

    private void StartAnimSync()
    {
        _animTimer?.Dispose();
        _animTimer = new Timer(async _ =>
        {
            if (!DeviceManager.Instance.IsConnected || _resetting || _interacting) return;

            // On a slow link the poll can outlast the 2s period; without this a backlog of stale
            // reads would pile up, each holding one of the shared adb slots.
            if (Interlocked.Exchange(ref _animSyncBusy, 1) == 1) return;
            try
            {
                var cur = await GetAnimSpeedAsync();
                await Dispatcher.InvokeAsync(() => { if (AnimationSlider != null && !_interacting && Math.Abs(AnimationSlider.Value - cur) > 0.01) UpdateSlider(cur); });
            }
            catch { }
            finally { Interlocked.Exchange(ref _animSyncBusy, 0); }
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
        await Exec(ApplyAnimationsButton, PrivacyStatusBorder, PrivacyStatusText, async () => { await SetAnimSpeedAsync(v); Track(OperationLedger.Ops.Animations); return (true, Strings.Advanced_ApplyAnimations_Success); });
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
                Clear(OperationLedger.Ops.Animations);
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
        await Exec(ResetBatteryButton, TroubleshootingStatusBorder, TroubleshootingStatusText, async () => { await Adb("shell dumpsys battery reset"); Clear(OperationLedger.Ops.Battery); return (true, Strings.Advanced_ResetBattery_Success); });
    }

    private async void OnResetCompilationClick(object s, RoutedEventArgs e)
    {
        if (!Confirm(Strings.Advanced_ResetCompilation_Confirm)) return;
        await Exec(ResetCompilationButton, TroubleshootingStatusBorder, TroubleshootingStatusText, async () => { await Adb("shell cmd package compile --reset -a"); Clear(OperationLedger.Ops.Compilation); return (true, Strings.Advanced_ResetCompilation_Success); });
    }

    private async void OnResetAllClick(object s, RoutedEventArgs e)
    {
        var serial = Serial;
        var ops = new HashSet<string>(OperationLedger.Get(serial), StringComparer.Ordinal);
        if (ops.Count == 0 || !Confirm(Strings.Advanced_ResetAll_Confirm)) return;
        if (ResetAllButton != null) ResetAllButton.IsEnabled = false;
        Show(TroubleshootingStatusBorder, TroubleshootingStatusText, Strings.Advanced_Status_ResettingAll);

        // Pins the whole undo to the device it started on — including the Pro module's reverts,
        // whose adb bridge takes no serial. Switching devices mid-reset can't misfire commands.
        AdbExecutor.AmbientSerial = serial;
        try
        {
            // Bottled first: the undo itself is an operation, so it can be undone too.
            await CaptureAdvancedVialAsync();

            var freeReverts = new (string Op, string Status, Func<Task> Act)[]
            {
                (OperationLedger.Ops.Animations, Strings.Advanced_Status_ResetAnimations, () => SetAnimSpeedAsync(1.0)),
                (OperationLedger.Ops.Compilation, Strings.Advanced_Status_ResetCompilation, () => Adb("shell cmd package compile --reset -a")),
                (OperationLedger.Ops.Battery, Strings.Advanced_Status_ResetBattery, () => Adb("shell dumpsys battery reset")),
                (OperationLedger.Ops.Dns, Strings.Advanced_Status_ResetDNS, ResetDnsAsync),
            };
            // Ops.Optimization needs no command of its own: everything an optimization leaves
            // behind is either its compilation (above) or a setting the baseline restore rewinds.

            foreach (var (op, status, act) in freeReverts)
            {
                if (!ops.Contains(op)) continue;
                Show(TroubleshootingStatusBorder, TroubleshootingStatusText, status);
                await act();
            }

            var proReverts = new (string CommandId, string Status)[]
            {
                (ProCommandIds.SafetyCore, Strings.Advanced_Status_ReenableSafetyCore),
                (ProCommandIds.ResetAdId, Strings.Advanced_Status_ResetAdId),
                (ProCommandIds.CaptivePortal, Strings.Advanced_Status_ReenableCaptivePortal),
                (ProCommandIds.GoogleCoreControl, Strings.Advanced_Status_ReenableGoogleCoreControl),
                (ProCommandIds.RamExpansion, Strings.Advanced_Status_ReenableRamExpansion),
            };

            foreach (var (cmdId, status) in proReverts)
            {
                if (OperationLedger.OpForProCommand(cmdId) is not { } op || !ops.Contains(op)) continue;
                Show(TroubleshootingStatusBorder, TroubleshootingStatusText, status);
                await Pro.RevertAsync(cmdId);
            }

            // Last word: the values the device actually had before the first change, poured back
            // from the pinned vial. This is what undoes Optimize's network and animation tuning —
            // and it beats the fixed defaults above wherever the two disagree.
            await RestoreBaselineAsync(serial);

            OperationLedger.Clear(serial);
            LoadAnimSpeed();
            UpdateUI();
            Show(TroubleshootingStatusBorder, TroubleshootingStatusText, Strings.Advanced_ResetAll_Success);
            await Task.Delay(3000);
            Hide(TroubleshootingStatusBorder);
        }
        catch (Exception ex) { Show(TroubleshootingStatusBorder, TroubleshootingStatusText, $"{Strings.Advanced_Error}: {ex.Message}"); }
        finally { AdbExecutor.AmbientSerial = null; UpdateResetBtn(); }
    }

    /// <summary>
    /// Pours back the settings the app itself wrote, taking their values from the vial bottled
    /// before the first tracked change. Only keys the app owns are touched, so a setting the user
    /// changed on the phone in the meantime survives untouched.
    /// </summary>
    private async Task RestoreBaselineAsync(string? serial)
    {
        if (string.IsNullOrEmpty(serial)) return;

        var vial = SettingsTimeMachine.LoadVial(OperationLedger.BaselineVialPath(serial));
        if (vial is null) return;

        var changes = await SettingsTimeMachine.DiffAsync(vial);
        if (changes is null) return;

        var owned = changes.Where(c => OperationLedger.Owns(c.Key)).ToList();
        if (owned.Count == 0) return;

        Show(TroubleshootingStatusBorder, TroubleshootingStatusText, Strings.Advanced_Status_ResettingAll);
        await SettingsTimeMachine.RestoreAsync(owned, serial);
    }

    private async Task CheckRamAsync()
    {
        // Cleared first: an unreadable meminfo on the device we just switched to must not leave the
        // previous phone's capacity in place, or the gate below would open on hardware that fails it.
        _ramMb = 0;
        _hasEnoughRam = false;
        try
        {
            var o = await Adb("shell cat /proc/meminfo");
            var m = Regex.Match(o, @"MemTotal:\s*(\d+)\s*kB");
            if (m.Success && long.TryParse(m.Groups[1].Value, out var kb))
            {
                _ramMb = kb / 1024;
                _hasEnoughRam = _ramMb >= RamExpansionMinimumMb;
            }
        }
        catch { }

        await Dispatcher.InvokeAsync(() =>
        {
            if (RamExpansionDescription != null)
                RamExpansionDescription.Text = _hasEnoughRam
                    ? string.Format(Strings.Advanced_RamExpansion_Description_WithRam, _ramMb / 1024.0)
                    : Strings.Advanced_RamExpansion_Description_LowRam;
        });
    }

    private async Task ResetDnsAsync() { await Adb("shell settings put global private_dns_mode off"); await Adb("shell settings delete global private_dns_specifier"); }

    private static string? Serial => DeviceManager.Instance.ActiveSerial;
    private static int PendingOps => OperationLedger.Count(Serial);

    private void Track(string op) { OperationLedger.Track(Serial, op); UpdateUI(); }
    private void Clear(string op) { OperationLedger.Forget(Serial, op); UpdateUI(); }
    private void UpdateUI() { UpdateText(); UpdateResetBtn(); }
    private void UpdateText() { if (SessionOperationsText != null) { var n = PendingOps; SessionOperationsText.Text = n == 0 ? Strings.Advanced_ResetAll_Description : string.Format(Strings.Advanced_ResetAll_Description_WithCount, n); } }
    private void UpdateResetBtn() { if (ResetAllButton != null) ResetAllButton.IsEnabled = DeviceManager.Instance.IsConnected && PendingOps > 0; }
    private void OnLedgerChanged() { if (_initialized) Dispatcher.BeginInvoke(UpdateUI); }

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
        try
        {
            await CaptureAdvancedVialAsync();
            var (ok, msg) = await op();
            Show(border, text, msg);
            if (ok) { await Task.Delay(3000); Hide(border); }
        }
        catch (Exception ex) { Show(border, text, $"{Strings.Advanced_Error}: {ex.Message}"); }
        finally { if (btn != null) btn.IsEnabled = DeviceManager.Instance.IsConnected; }
    }

    private static Task CaptureAdvancedVialAsync() => SettingsTimeMachine.CaptureAsync(
        SettingsTimeMachine.TriggerAdvanced,
        DeviceManager.Instance.ActiveSerial,
        deviceName: DeviceManager.Instance.DeviceName);

    private static void Show(Border? b, TextBlock? t, string msg) { if (b == null || t == null) return; t.Text = msg; b.Visibility = Visibility.Visible; }
    private static void Hide(Border? b) { if (b != null) b.Visibility = Visibility.Collapsed; }
    private static Task<string> Adb(string cmd) => AdbExecutor.ExecuteCommandAsync(cmd);

    private void OnVialsStoreChanged() => Dispatcher.BeginInvoke(RefreshVials);

    private async void RefreshVials()
    {
        if (VialsList == null || VialsEmptyState == null) return;

        var serial = DeviceManager.Instance.ActiveSerial;
        if (string.IsNullOrEmpty(serial))
        {
            VialsList.ItemsSource = null;
            VialsEmptyText.Text = Strings.Advanced_Vials_EmptyDisconnected;
            VialsEmptyState.Visibility = Visibility.Visible;
            BottleNowButton.IsEnabled = false;
            return;
        }

        BottleNowButton.IsEnabled = true;
        var vials = await SettingsTimeMachine.LoadAsync(serial);
        VialsList.ItemsSource = vials.Select(v => new VialItemViewModel(v)).ToList();
        VialsEmptyText.Text = Strings.Advanced_Vials_Empty;
        VialsEmptyState.Visibility = vials.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnBottleNowClick(object s, RoutedEventArgs e)
    {
        if (!DeviceManager.Instance.IsConnected) return;
        BottleNowButton.IsEnabled = false;
        try
        {
            var vial = await SettingsTimeMachine.CaptureAsync(SettingsTimeMachine.TriggerManual,
                DeviceManager.Instance.ActiveSerial, deviceName: DeviceManager.Instance.DeviceName);
            Show(VialsStatusBorder, VialsStatusText,
                vial is null ? Strings.Advanced_Vials_CaptureFailed : Strings.Advanced_Vials_Captured);
            await Task.Delay(2500);
            Hide(VialsStatusBorder);
        }
        finally { BottleNowButton.IsEnabled = DeviceManager.Instance.IsConnected; }
    }

    private void OnDeleteVialClick(object s, RoutedEventArgs e)
    {
        if (s is not FrameworkElement { DataContext: VialItemViewModel item }) return;
        if (!DialogService.Instance.ConfirmDirect(Strings.Advanced_Vials_DeleteQuestion, Window.GetWindow(this), Strings.Advanced_Confirm_Title)) return;
        SettingsTimeMachine.Delete(item.Vial);
    }

    private async void OnOpenVialClick(object s, RoutedEventArgs e)
    {
        if (s is not FrameworkElement { DataContext: VialItemViewModel item }) return;

        _openVial = item.Vial;
        VialOverlaySubtitle.Text = string.Format(Strings.Advanced_Vials_Overlay_Subtitle,
            item.TriggerDisplay, item.Vial.TakenUtc.ToLocalTime().ToString("g"));
        VialSummaryText.Text = string.Empty;
        ResetVialSelectAllVisual();
        VialOverlayContainer.Visibility = Visibility.Visible;
        await LoadVialDiffAsync();
    }

    private async Task LoadVialDiffAsync()
    {
        if (_openVial is null) return;

        VialDiffLoading.Visibility = Visibility.Visible;
        VialNoChanges.Visibility = Visibility.Collapsed;
        VialDiffScroll.Visibility = Visibility.Collapsed;
        VialRestoreButton.IsEnabled = false;
        _vialChanges.Clear();

        var changes = await SettingsTimeMachine.DiffAsync(_openVial);
        VialDiffLoading.Visibility = Visibility.Collapsed;

        if (changes is null)
        {
            VialSummaryText.Text = Strings.Advanced_Vials_DiffFailed;
            return;
        }

        foreach (var change in changes)
            _vialChanges.Add(new VialChangeRow(change, UpdateVialSelection));

        if (_vialChanges.Count == 0)
        {
            VialNoChanges.Visibility = Visibility.Visible;
            VialSummaryText.Text = string.Empty;
        }
        else
        {
            VialDiffScroll.Visibility = Visibility.Visible;
            int modified = changes.Count(c => c.VialValue is not null && c.CurrentValue is not null);
            int added = changes.Count(c => c.VialValue is null);
            int removed = changes.Count(c => c.CurrentValue is null);
            VialSummaryText.Text = string.Format(Strings.Advanced_Vials_Summary, modified, added, removed);
        }
        UpdateVialSelection();
    }

    private void OnVialSelectAll(object s, RoutedEventArgs e)
    {
        if (_suppressVialSelectAll) return;
        var check = VialSelectAllCheckBox.IsChecked == true;
        foreach (var row in _vialChanges.Where(r => r.CanRestore))
            row.IsSelected = check;
    }

    private void ResetVialSelectAllVisual()
    {
        _suppressVialSelectAll = true;
        VialSelectAllCheckBox.IsChecked = false;
        _suppressVialSelectAll = false;
    }

    private void UpdateVialSelection()
    {
        var count = _vialChanges.Count(r => r.IsSelected);
        VialSelectedCountText.Text = count > 0 ? string.Format(Strings.Debloat_Selected_Count, count) : string.Empty;
        VialRestoreButton.IsEnabled = count > 0;
    }

    private async void OnRestoreVialSelectedClick(object s, RoutedEventArgs e)
    {
        if (_openVial is null) return;
        var selected = _vialChanges.Where(r => r.IsSelected && r.CanRestore).Select(r => r.Change).ToList();
        if (selected.Count == 0) return;

        if (!DialogService.Instance.ConfirmDirect(
            string.Format(Strings.Advanced_Vials_RestoreQuestion, selected.Count),
            Window.GetWindow(this), Strings.Advanced_Confirm_Title)) return;

        VialRestoreButton.IsEnabled = false;
        VialSummaryText.Text = Strings.Advanced_Status_Processing;
        try
        {
            var applied = await SettingsTimeMachine.RestoreAsync(selected, _openVial.DeviceSerial);
            await LoadVialDiffAsync();
            VialSummaryText.Text = string.Format(Strings.Advanced_Vials_RestoreDone, applied);
        }
        catch (Exception ex)
        {
            VialSummaryText.Text = string.Format(Strings.Common_Status_Error, ex.Message);
        }
        ResetVialSelectAllVisual();
    }

    private void OnCloseVialOverlayClick(object s, RoutedEventArgs e) => CloseVialOverlay();

    private void OnVialOverlayBackgroundMouseDown(object s, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, VialOverlayContainer)) CloseVialOverlay();
    }

    private void CloseVialOverlay()
    {
        VialOverlayContainer.Visibility = Visibility.Collapsed;
        _openVial = null;
        _vialChanges.Clear();
    }
}
