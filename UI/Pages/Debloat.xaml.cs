
namespace ZephyrsElixir.UI.Pages;
public partial class Debloat : UserControl, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly ObservableCollection<AppInfoViewModel> _apps = new();
    private readonly ObservableCollection<AppRowViewModel> _rows = new();
    private int _columns = 1;
    private readonly ObservableCollection<HistoryAppViewModel> _history = new();
    private readonly ICollectionView _appsView, _historyView;
    private readonly DispatcherTimer _iconTimer, _searchTimer;
    private readonly SemaphoreSlim _loadSem = new(1);
    private readonly ConcurrentQueue<(AppInfoViewModel App, BitmapImage? Icon)> _loadedIcons = new();

    private CancellationTokenSource? _cts;
    private Task? _iconTask;
    private List<AppInfoViewModel> _toUninstall = new();
    private int _filter;
    private int _sortMode;
    private bool _historyMode;
    private bool _suppressSelectAll;
    private bool _suppressCount;
    private string _search = string.Empty;
    private string _loadedSerial = string.Empty;
    private int _selectedCount, _selectedDisabled, _selectedActive;

    public int SelectedAppsCount { get => _selectedCount; set => SetCount(ref _selectedCount, value); }
    public int SelectedDisabledCount { get => _selectedDisabled; set => SetCount(ref _selectedDisabled, value); }
    public int SelectedActiveCount { get => _selectedActive; set => SetCount(ref _selectedActive, value); }

    private void SetCount(ref int field, int value, [CallerMemberName] string? name = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private static readonly Comparer<AppInfoViewModel> NameComparer =
        Comparer<AppInfoViewModel>.Create((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
    private static readonly Comparer<AppInfoViewModel> SafeFirstComparer =
        Comparer<AppInfoViewModel>.Create((a, b) =>
        {
            var c = b.SafetyScore.CompareTo(a.SafetyScore);
            return c != 0 ? c : NameComparer.Compare(a, b);
        });
    private static readonly Comparer<AppInfoViewModel> RiskFirstComparer =
        Comparer<AppInfoViewModel>.Create((a, b) =>
        {
            var c = RiskRank(b).CompareTo(RiskRank(a));
            if (c != 0) return c;
            c = a.SafetyScore.CompareTo(b.SafetyScore);
            return c != 0 ? c : NameComparer.Compare(a, b);
        });
    private static readonly Comparer<AppInfoViewModel>[] SortComparers = { NameComparer, SafeFirstComparer, RiskFirstComparer };

    private static int RiskRank(AppInfoViewModel a) => a.RiskLevel switch
    {
        SafetyRiskLevel.Critical => 3,
        SafetyRiskLevel.Caution => 2,
        SafetyRiskLevel.Safe => 1,
        _ => 0
    };

    public Debloat()
    {
        InitializeComponent();
        DataContext = this;

        _appsView = CollectionViewSource.GetDefaultView(_apps);
        _appsView.Filter = FilterApps;
        if (_appsView is ListCollectionView lv) lv.CustomSort = NameComparer;
        AppsListView.ItemsSource = _rows;

        _historyView = CollectionViewSource.GetDefaultView(_history);
        _historyView.Filter = FilterApps;
        if (_historyView is ListCollectionView hlv) hlv.SortDescriptions.Add(new SortDescription(nameof(HistoryAppViewModel.UninstallDate), ListSortDirection.Descending));
        HistoryListView.ItemsSource = _historyView;

        _iconTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(80), DispatcherPriority.Background, OnIconTick, Dispatcher) { IsEnabled = false };
        _searchTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(160), DispatcherPriority.Background, OnSearchTick, Dispatcher) { IsEnabled = false };
        Loaded += OnLoad;
        Unloaded += OnUnload;
    }

    private void OnLoad(object s, RoutedEventArgs e)
    {
        this.SubscribeToDeviceUpdates(onStatusChanged: OnDeviceChanged, controls: new UIElement[] { LoadAppsButton });
        this.SubscribeToActiveDevice(OnActiveDeviceSwitched);
        // The page stays alive while hidden: if the active device changed while another page was
        // open, the list shown here would belong to the previous phone — reconcile on re-entry.
        if (_apps.Any() && _loadedSerial != DeviceManager.Instance.ActiveSerial) ResetAppsView();
        UpdateStatus();
        _ = LoadHistoryAsync();
    }

    private void OnActiveDeviceSwitched(string serial)
    {
        if (_loadedSerial == serial) return;
        ResetAppsView();
        CloseActiveOverlay();
        UpdateStatus();
        _ = LoadHistoryAsync();
    }

    private void OnUnload(object s, RoutedEventArgs e)
    {
        _cts?.Cancel();
        _iconTimer.Stop();
        _searchTimer.Stop();
    }

    private void OnDeviceChanged(bool connected)
    {
        UpdateStatus();
        if (_historyMode) _ = LoadHistoryAsync();
    }

    private void UpdateStatus()
    {
        PageHeaderControl.Subtitle = DeviceManager.Instance.IsConnected
            ? Strings.Debloat_Status_Connected
            : Strings.Debloat_Status_Disconnected;

        if (!DeviceManager.Instance.IsConnected) ResetAppsView();
    }

    /// <summary>
    /// Regroups the filtered, sorted apps into rows of <see cref="_columns"/> cells. Every path that
    /// changes what the list shows — a refresh, a batch of new apps, a resize — ends here, because
    /// the rows are what the virtualizing panel actually binds to.
    /// </summary>
    private void RebuildRows()
    {
        _rows.Clear();
        var columns = Math.Max(1, _columns);
        var buffer = new List<AppInfoViewModel>(columns);

        foreach (AppInfoViewModel app in _appsView)
        {
            buffer.Add(app);
            if (buffer.Count < columns) continue;
            _rows.Add(new AppRowViewModel(buffer.ToArray(), columns));
            buffer.Clear();
        }

        if (buffer.Count > 0) _rows.Add(new AppRowViewModel(buffer.ToArray(), columns));
    }

    private void RefreshAppsView()
    {
        _appsView.Refresh();
        RebuildRows();
    }

    private void AppsList_SizeChanged(object s, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged) return;

        var columns = ResponsiveColumns.For(e.NewSize.Width);
        if (columns == _columns) return;
        _columns = columns;
        RebuildRows();
    }

    private void ResetAppsView()
    {
        _cts?.Cancel();
        _iconTimer.Stop();
        _apps.Clear();
        _rows.Clear();
        ClearQueues();
        _loadedSerial = string.Empty;
        UpdateCount();
        SetLoading(false);
        UpdateUI();
    }

    private async void LoadAppsButton_Click(object s, RoutedEventArgs e)
    {
        if (_historyMode) { _ = LoadHistoryAsync(); return; }

        await _loadSem.WaitAsync();
        try
        {
            _cts?.Cancel();
            _cts = new();
            var ct = _cts.Token;

            _iconTimer.Stop();
            _apps.Clear();
            _rows.Clear();
            ClearQueues();
            UpdateCount();
            ResetSelectAll();
            UpdateUI(true);
            SetLoading(true, Strings.Debloat_Status_Loading);

            var vms = new List<AppInfoViewModel>();
            try
            {
                var progress = new Progress<string>(msg => Dispatcher.BeginInvoke(() => PageHeaderControl.Subtitle = msg, DispatcherPriority.Background));
                if (await ZephyrsAgent.EnsureAgentIsRunningAsync(progress, ct) && !ct.IsCancellationRequested)
                {
                    var apps = await ZephyrsAgent.GetInstalledAppsAsync(progress, ct);
                    if (!ct.IsCancellationRequested)
                    {
                        vms = apps.Select(a => new AppInfoViewModel { Name = a.Name, PackageName = a.PackageName, Version = a.Version, State = a.State }).ToList();
                        _loadedSerial = DeviceManager.Instance.ActiveSerial;
                    }
                }
            }
            catch (Exception ex) { PageHeaderControl.Subtitle = ex.Message; }
            finally
            {
                if (!ct.IsCancellationRequested)
                {
                    await AddAppsBatchedAsync(vms, ct);
                    UpdateSummary();
                    UpdateUI();
                    if (vms.Any()) { StartIconLoading(vms, ct); StartAnalysis(vms, ct); }
                }

                // Outside the guard: a cancelled load still has to put the spinner away, or the page
                // stays in its loading state until something else happens to clear it.
                SetLoading(false);
            }
        }
        finally { _loadSem.Release(); }
    }

    private async Task AddAppsBatchedAsync(List<AppInfoViewModel> vms, CancellationToken ct)
    {
        const int batchSize = 80;
        for (int i = 0; i < vms.Count && !ct.IsCancellationRequested; i += batchSize)
        {
            var end = Math.Min(i + batchSize, vms.Count);
            for (int j = i; j < end; j++)
            {
                var vm = vms[j];
                vm.IsSelectedChanged += _ => UpdateCount();
                _apps.Add(vm);
            }
            RebuildRows();
            await Dispatcher.Yield(DispatcherPriority.Background);
        }
    }

    private void UpdateSummary()
    {
        if (!_apps.Any()) return;
        int sys = 0, usr = 0, dis = 0;
        foreach (var a in _apps)
            switch (a.State)
            {
                case AppState.System: sys++; break;
                case AppState.User: usr++; break;
                default: dis++; break;
            }
        PageHeaderControl.Subtitle = string.Format(Strings.Debloat_Summary, _apps.Count, sys, usr, dis);
    }

    private void StartAnalysis(IEnumerable<AppInfoViewModel> apps, CancellationToken ct)
    {
        // AI transparency (AI Act art. 50): on first use, disclose that package names are sent
        // to a third-party AI (OpenAI) and let the user opt out. The choice is persisted.
        if (!AiConsent.IsDecided())
            AiConsent.SetEnabled(DialogService.Instance.Confirm(
                "Debloat_Ai_Consent_Message", Window.GetWindow(this), "Debloat_Ai_Consent_Title"));

        Task.Run(async () =>
        {
            var map = apps.ToDictionary(a => a.PackageName);
            await CloudIntelligenceManager.AnalyzeBatchStreamAsync(map.Keys, data =>
            {
                if (map.TryGetValue(data.PackageName, out var vm))
                    Dispatcher.BeginInvoke(() => vm.ApplyIntelligence(data), DispatcherPriority.Background);
            }, ct);

            if (!ct.IsCancellationRequested)
                await Dispatcher.BeginInvoke(() => { if (_sortMode != 0) RefreshAppsView(); }, DispatcherPriority.Background);
        }, ct);
    }

    private void StartIconLoading(IEnumerable<AppInfoViewModel> apps, CancellationToken ct)
    {
        ClearQueues();
        var appList = apps.ToList();
        _iconTimer.Start();

        _iconTask = Task.Run(async () =>
        {
            await Parallel.ForEachAsync(appList, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct }, async (a, c) =>
            {
                var icon = await AppIconLoader.LoadIconAsync(a.PackageName, c);
                _loadedIcons.Enqueue((a, icon));
            });
        }, ct);
    }

    private void OnIconTick(object? s, EventArgs e)
    {
        var producerDone = _iconTask?.IsCompleted ?? true;
        for (int i = 0; i < 24 && _loadedIcons.TryDequeue(out var item); i++)
            if (item.Icon is not null) item.App.Icon = item.Icon;
        if (producerDone && _loadedIcons.IsEmpty) _iconTimer.Stop();
    }

    private async Task LoadHistoryAsync()
    {
        var deviceSerial = DeviceManager.Instance.IsConnected
            ? DeviceManager.Instance.DeviceSerial
            : null;

        var items = await UninstallHistoryManager.LoadHistoryAsync(deviceSerial);

        _history.Clear();
        foreach (var h in items)
        {
            var vm = new HistoryAppViewModel
            {
                Name = h.DisplayName,
                PackageName = h.PackageName,
                Version = h.Version,
                UninstallDate = h.UninstallDate,
                LocalApkPath = h.LocalApkPath,
                IsSystemApp = h.IsSystemApp,
                DeviceSerial = h.DeviceSerial,
                Icon = DecodeIcon(h.IconBase64)
            };
            vm.PropertyChanged += OnHistorySelectionChanged;
            _history.Add(vm);
        }

        UpdateCount();
        UpdateUI();
    }

    private void OnHistorySelectionChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HistoryAppViewModel.IsSelected)) UpdateCount();
    }

    private static BitmapImage? DecodeIcon(string? b64)
    {
        if (string.IsNullOrEmpty(b64)) return null;
        try { using var ms = new MemoryStream(Convert.FromBase64String(b64)); return UIHelpers.BitmapFromStream(ms); }
        catch { return null; }
    }

    private bool FilterApps(object item) => item switch
    {
        AppInfoViewModel a => MatchFilter(a) && MatchSearch(a.Name, a.PackageName),
        HistoryAppViewModel h => MatchSearch(h.Name, h.PackageName),
        _ => false
    };

    private bool MatchFilter(AppInfoViewModel a) => _filter switch
    {
        1 => a.State == AppState.User,
        2 => a.State == AppState.System,
        3 => a.State == AppState.Disabled,
        _ => true
    };

    private bool MatchSearch(string name, string pkg) =>
        _search.Length == 0 ||
        name.Contains(_search, StringComparison.OrdinalIgnoreCase) ||
        pkg.Contains(_search, StringComparison.OrdinalIgnoreCase);

    private void SearchBox_TextChanged(object s, TextChangedEventArgs e)
    {
        _search = SearchBox.Text.Trim();
        _searchTimer.Stop();
        if (_search.Length == 0) ApplySearch();
        else _searchTimer.Start();
    }

    private void OnSearchTick(object? s, EventArgs e)
    {
        _searchTimer.Stop();
        ApplySearch();
    }

    private void ApplySearch()
    {
        if (_historyMode) _historyView.Refresh(); else RefreshAppsView();
        UpdateEmptyState();
    }

    private void OnFilterChanged(object s, RoutedEventArgs e)
    {
        if (FilterPanel == null || _appsView == null) return;
        if (s is RadioButton { IsChecked: true } rb)
        {
            _filter = FilterPanel.Children.OfType<RadioButton>().ToList().IndexOf(rb);
            RefreshAppsView();
            UpdateEmptyState();
        }
    }

    private void OnSortChanged(object s, SelectionChangedEventArgs e)
    {
        if (SortCombo == null || _appsView is not ListCollectionView lv) return;
        _sortMode = Math.Clamp(SortCombo.SelectedIndex, 0, SortComparers.Length - 1);
        lv.CustomSort = SortComparers[_sortMode];
        RebuildRows();
    }

    private void OnViewModeChanged(object s, RoutedEventArgs e)
    {
        if (AppsListView == null || s is not RadioButton { IsChecked: true } rb) return;
        _historyMode = Grid.GetColumn(rb) == 1;
        AppsListView.Visibility = _historyMode ? Visibility.Collapsed : Visibility.Visible;
        HistoryListView.Visibility = _historyMode ? Visibility.Visible : Visibility.Collapsed;
        FilterPanel.Visibility = _historyMode ? Visibility.Collapsed : Visibility.Visible;
        PresetPanel.Visibility = _historyMode ? Visibility.Collapsed : Visibility.Visible;
        AppsActions.Visibility = _historyMode ? Visibility.Collapsed : Visibility.Visible;
        HistoryActions.Visibility = _historyMode ? Visibility.Visible : Visibility.Collapsed;
        ResetSelectAll();
        UpdateCount();
        if (_historyMode) _ = LoadHistoryAsync(); else UpdateUI();
    }

    private void UpdateUI(bool loading = false)
    {
        if (SelectionBar == null) return;
        var has = _historyMode ? _history.Any() : _apps.Any();
        SelectionBar.Visibility = !loading && has ? Visibility.Visible : Visibility.Collapsed;
        if (loading || !has) ResetSelectAll();
        UpdateEmptyState(loading);
    }

    private void UpdateEmptyState(bool loading = false)
    {
        if (EmptyState == null) return;

        string? title = null, text = null;
        var glyph = string.Empty;
        if (!loading)
        {
            if (_historyMode)
            {
                if (!_history.Any()) (title, text, glyph) = (Strings.Debloat_Empty_History_Title, Strings.Debloat_Empty_History_Text, "history");
                else if (_historyView.IsEmpty) (title, text, glyph) = (Strings.Debloat_Empty_NoResults, Strings.Debloat_Empty_NoResults_Text, "search");
            }
            else
            {
                if (!_apps.Any()) (title, text, glyph) = (Strings.Debloat_Empty_Apps_Title, Strings.Debloat_Status_ConnectAndLoad, "apps");
                else if (_appsView.IsEmpty) (title, text, glyph) = (Strings.Debloat_Empty_NoResults, Strings.Debloat_Empty_NoResults_Text, "search");
            }
        }

        if (title is null) { EmptyState.Visibility = Visibility.Collapsed; return; }
        EmptyStateIcon.Kind = glyph;
        EmptyStateTitle.Text = title;
        EmptyStateText.Text = text;
        EmptyState.Visibility = Visibility.Visible;
    }

    private void OnSelectAllChecked(object s, RoutedEventArgs e) { if (!_suppressSelectAll) SetAll(true); }
    private void OnSelectAllUnchecked(object s, RoutedEventArgs e) { if (!_suppressSelectAll) SetAll(false); }

    // Every IsSelected write raises a change that recounts the whole list, so a bulk toggle over a
    // few hundred packages would walk it once per item. Count once, at the end.
    private void SetAll(bool sel)
    {
        _suppressCount = true;
        try
        {
            if (_historyMode) foreach (var h in _historyView.Cast<HistoryAppViewModel>().ToList()) h.IsSelected = sel;
            else foreach (var a in _appsView.Cast<AppInfoViewModel>().ToList()) a.IsSelected = sel;
        }
        finally { _suppressCount = false; }

        UpdateCount();
    }

    private void ResetSelectAll()
    {
        if (SelectAllCheckBox == null) return;
        _suppressSelectAll = true;
        SelectAllCheckBox.IsChecked = false;
        _suppressSelectAll = false;
    }

    private void ClearSelection()
    {
        _suppressCount = true;
        try
        {
            if (_historyMode) foreach (var h in _history) h.IsSelected = false;
            else foreach (var a in _apps) a.IsSelected = false;
        }
        finally { _suppressCount = false; }

        UpdateCount();
    }

    private void UpdateCount()
    {
        if (_suppressCount) return;

        int sel = 0, dis = 0;
        if (_historyMode)
        {
            sel = _history.Count(h => h.IsSelected);
        }
        else
        {
            foreach (var a in _apps)
            {
                if (!a.IsSelected) continue;
                sel++;
                if (a.State == AppState.Disabled) dis++;
            }
        }

        SelectedAppsCount = sel;
        SelectedDisabledCount = _historyMode ? 0 : dis;
        SelectedActiveCount = _historyMode ? 0 : sel - dis;

        if (SelectedCountBadge == null) return;
        SelectedCountBadge.Visibility = sel > 0 ? Visibility.Visible : Visibility.Collapsed;
        SelectedCountText.Text = string.Format(Strings.Debloat_Selected_Count, sel);
    }

    private void AppItem_MouseLeftButtonDown(object s, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && s is FrameworkElement { DataContext: AppInfoViewModel a }) { OpenDetails(a); e.Handled = true; }
    }

    private void ManageAppButton_Click(object s, RoutedEventArgs e) { if (_apps.FirstOrDefault(a => a.IsSelected) is { } sel) OpenDetails(sel); }

    private async void OpenDetails(AppInfoViewModel app)
    {
        var vm = new AppDetailsViewModel(app);
        AppDetailsOverlayReal.DataContext = vm;
        ShowOverlay(AppDetailsOverlayReal);
        await vm.LoadDataAsync();
    }

    private void CloseDetails_Click(object s, RoutedEventArgs e) { HideOverlay(AppDetailsOverlayReal); AppDetailsOverlayReal.DataContext = null; }

    private AppDetailsViewModel? DetailsVm => AppDetailsOverlayReal.DataContext as AppDetailsViewModel;

    private async void RevokeAllPermissions_Click(object s, RoutedEventArgs e)
    {
        if (DetailsVm is not { HasGrantedPermissions: true } vm) return;
        if (!DialogService.Instance.ConfirmDirect(
            string.Format(Strings.Debloat_Overlay_RevokeQuestion, vm.GrantedPermissionsCount, vm.App.Name),
            Window.GetWindow(this),
            Strings.MessageBox_ConfirmAction_Title)) return;
        try { var c = await vm.RevokeAllPermissionsAsync(); PageHeaderControl.Subtitle = string.Format(Strings.Debloat_Status_Revoked, c, vm.App.Name); }
        catch (Exception ex) { DialogService.Instance.ShowInfoDirect(Strings.Advanced_Error, string.Format(Strings.Common_Status_Error, ex.Message), Window.GetWindow(this)); }
    }

    private async void LaunchApp_Click(object s, RoutedEventArgs e)
    {
        if (DetailsVm is { } vm) PageHeaderControl.Subtitle = await vm.LaunchAsync();
    }

    private async void ForceStopApp_Click(object s, RoutedEventArgs e)
    {
        if (DetailsVm is { } vm) PageHeaderControl.Subtitle = await vm.ForceStopAsync();
    }

    private async void OpenAppInfo_Click(object s, RoutedEventArgs e)
    {
        if (DetailsVm is { } vm) await vm.OpenAppInfoAsync();
    }

    private async void ExtractApk_Click(object s, RoutedEventArgs e)
    {
        if (DetailsVm is not { } vm) return;
        var dialog = new OpenFolderDialog { Title = Strings.Debloat_Extract_Title };
        if (dialog.ShowDialog() != true) return;

        PageHeaderControl.Subtitle = string.Format(Strings.Debloat_Action_Processing, vm.App.Name);
        var dest = await vm.ExtractApkAsync(dialog.FolderName);
        PageHeaderControl.Subtitle = dest is null
            ? Strings.Debloat_Status_ExtractFailed
            : string.Format(Strings.Debloat_Status_Extracted, dest);
    }

    private async void ClearAppData_Click(object s, RoutedEventArgs e)
    {
        if (DetailsVm is not { } vm) return;
        if (!DialogService.Instance.ConfirmDirect(
            string.Format(Strings.Debloat_ClearData_Question, vm.App.Name),
            Window.GetWindow(this),
            Strings.MessageBox_ConfirmAction_Title)) return;

        PageHeaderControl.Subtitle = await vm.ClearDataAsync()
            ? string.Format(Strings.Debloat_Status_DataCleared, vm.App.Name)
            : string.Format(Strings.Debloat_Status_DataClearFailed, vm.App.Name);
    }

    private async void DisableButton_Click(object s, RoutedEventArgs e) => await SetPackagesStateAsync(enable: false);
    private async void EnableButton_Click(object s, RoutedEventArgs e) => await SetPackagesStateAsync(enable: true);

    private static readonly SolidColorBrush CriticalWarningBrush = AppBrushes.Failed;
    private static readonly SolidColorBrush NormalWarningBrush = UIHelpers.FrozenSolid(0xB0, 0xB8, 0xC8);

    private void UninstallButton_Click(object s, RoutedEventArgs e)
    {
        _toUninstall = _apps.Where(a => a.IsSelected).ToList();
        if (!_toUninstall.Any()) return;

        var crit = _toUninstall.Count(a => a.RiskLevel == SafetyRiskLevel.Critical);
        UninstallConfirmText.Text = crit > 0
            ? string.Format(Strings.Debloat_Uninstall_WarningCritical, crit)
            : string.Format(Strings.Debloat_Uninstall_Question, _toUninstall.Count);
        UninstallConfirmText.Foreground = crit > 0 ? CriticalWarningBrush : NormalWarningBrush;
        UpdateBackupTierHint();
        ShowOverlay(UninstallConfirmOverlay);
    }

    private void UpdateBackupTierHint() =>
        BackupTierHint.Visibility = Features.IsAvailable(Features.ApkBackup) ? Visibility.Collapsed : Visibility.Visible;

    private async void ConfirmUninstallBackup_Click(object s, RoutedEventArgs e)
    {
        if (!Features.IsAvailable(Features.ApkBackup))
        {
            DialogService.Instance.ShowProRequiredWithUpgrade("Pro_Required_ApkBackup", Window.GetWindow(this));
            UpdateBackupTierHint();
            return;
        }
        CloseOverlay();
        await Uninstall(true);
    }
    private async void ConfirmUninstallOnly_Click(object s, RoutedEventArgs e) { CloseOverlay(); await Uninstall(false); }
    private void CancelUninstall_Click(object s, RoutedEventArgs e) { CloseOverlay(); _toUninstall.Clear(); }

    private async Task SetPackagesStateAsync(bool enable)
    {
        var sel = _apps.Where(a => a.IsSelected && (enable ? a.State == AppState.Disabled : a.State != AppState.Disabled)).ToList();
        if (!sel.Any()) return;

        var crit = enable ? 0 : sel.Count(a => a.RiskLevel == SafetyRiskLevel.Critical);
        var question = crit > 0
            ? $"{string.Format(Strings.Debloat_Uninstall_WarningCritical, crit)}\n{string.Format(Strings.Debloat_Action_PerformConfirm, sel.Count)}"
            : string.Format(Strings.Debloat_Action_PerformConfirm, sel.Count);
        if (!DialogService.Instance.ConfirmDirect(question, Window.GetWindow(this))) return;

        SetLoading(true);
        // Pin the whole batch to the device it was started on: switching the active phone from the
        // sidebar mid-run must never redirect the remaining commands at another device.
        var serial = ActiveSerialOrNull();
        var sdk = await DeviceApi.GetSdkAsync(serial);
        var ok = new HashSet<string>();

        await Task.Run(async () =>
        {
            foreach (var a in sel)
            {
                await Dispatcher.InvokeAsync(() => PageHeaderControl.Subtitle = string.Format(Strings.Debloat_Action_Processing, a.Name), DispatcherPriority.Background);
                var cmd = enable ? DeviceApi.EnableCommand(sdk, a.PackageName) : DeviceApi.DisableCommand(sdk, a.PackageName);
                var r = await AdbExecutor.ExecuteCommandAsync(cmd, serial: serial);
                if (DeviceApi.IsSuccess(r)) ok.Add(a.PackageName);
            }
        });

        var systemPkgs = enable && ok.Count > 0 ? await DeviceApi.GetSystemPackagesAsync(serial) : null;
        UpdateAfterAction(ok, enable ? "enable" : "disable", systemPkgs);
        SetLoading(false);
        PageHeaderControl.Subtitle = string.Format(Strings.Debloat_Action_Done, ok.Count);
    }

    private async Task Uninstall(bool backup)
    {
        SetLoading(true);
        var serial = ActiveSerialOrNull();
        var sdk = await DeviceApi.GetSdkAsync(serial);
        var ok = new HashSet<string>();
        var fail = new List<string>();
        string? firstFailureRaw = null;

        await Task.Run(async () =>
        {
            // The Pro module's backup command carries no serial: the ambient one pins it to this batch's device.
            AdbExecutor.AmbientSerial = serial;
            foreach (var a in _toUninstall)
            {
                await Dispatcher.InvokeAsync(() => PageHeaderControl.Subtitle = string.Format(Strings.Debloat_Action_Processing, a.Name), DispatcherPriority.Background);

                var path = backup ? await BackupApkAsync(a, serial) : null;
                var iconB64 = "";
                if (a.Icon != null) await Dispatcher.InvokeAsync(() => iconB64 = EncodeIcon(a.Icon));

                var r = await AdbExecutor.ExecuteCommandAsync(DeviceApi.UninstallCommand(sdk, a.PackageName, keepForRestore: a.State != AppState.User), serial: serial);
                if (DeviceApi.IsSuccess(r))
                {
                    ok.Add(a.PackageName);
                    await UninstallHistoryManager.AddEntryAsync(new HistoryItem
                    {
                        PackageName = a.PackageName,
                        DisplayName = a.Name,
                        Version = a.Version,
                        UninstallDate = DateTime.Now,
                        LocalApkPath = path,
                        IconBase64 = iconB64,
                        IsSystemApp = a.State == AppState.System,
                        DeviceSerial = serial ?? string.Empty
                    });
                }
                else
                {
                    fail.Add(a.Name);
                    firstFailureRaw ??= r;
                    UninstallHistoryManager.DeleteBackup(path);
                }
            }
        });

        UpdateAfterAction(ok, "uninstall");
        SetLoading(false);
        PageHeaderControl.Subtitle = string.Format(Strings.Debloat_Status_Success, ok.Count, _toUninstall.Count, Strings.Debloat_Action_Uninstall_Past);
        if (fail.Any())
            DialogService.Instance.ShowInfoDirect(
                Strings.Common_Warning_Title,
                AdbErrorCatalog.Enrich(string.Format(Strings.Common_Status_Error, string.Join(", ", fail)), firstFailureRaw),
                Window.GetWindow(this));
    }

    /// <summary>The device every command of a batch is pinned to, or null when none is active.</summary>
    private static string? ActiveSerialOrNull() =>
        DeviceManager.Instance.ActiveSerial is { Length: > 0 } serial ? serial : null;

    private static async Task<string?> BackupApkAsync(AppInfoViewModel a, string? serial)
    {
        string? path = null;
        try
        {
            var paths = await DeviceApi.GetPackagePathsAsync(a.PackageName, serial);
            if (paths.Count == 1)
            {
                path = UninstallHistoryManager.GetBackupPath(a.PackageName, a.Version);

                var pro = await Pro.ExecuteAsync(ProCommandIds.ApkBackup, new Dictionary<string, object>
                {
                    ["package"] = a.PackageName,
                    ["outputPath"] = path
                });

                // The module handles the plain single-APK case; a split package, or a module that
                // declined the command, still gets its bytes pulled here.
                if (!pro.Success || !File.Exists(path))
                    await AdbExecutor.ExecuteTransferAsync($"pull \"{paths[0]}\" \"{path}\"", serial: serial);

                if (!File.Exists(path)) path = null;
            }
            else if (paths.Count > 1)
            {
                var dir = UninstallHistoryManager.GetBackupDirectory(a.PackageName, a.Version);
                int pulled = 0;
                foreach (var remote in paths)
                {
                    var local = Path.Combine(dir, Path.GetFileName(remote));
                    await AdbExecutor.ExecuteTransferAsync($"pull \"{remote}\" \"{local}\"", serial: serial);
                    if (File.Exists(local)) pulled++;
                }
                if (pulled == paths.Count) path = dir;
                else UninstallHistoryManager.DeleteBackup(dir);
            }
        }
        catch
        {
            UninstallHistoryManager.DeleteBackup(path);
            path = null;
        }
        return path;
    }

    private static string EncodeIcon(BitmapImage icon)
    {
        using var ms = new MemoryStream();
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(icon));
        enc.Save(ms);
        return Convert.ToBase64String(ms.ToArray());
    }

    private bool EnsureDeviceForRestore()
    {
        if (DeviceManager.Instance.IsConnected) return true;
        DialogService.Instance.ShowInfoDirect(Strings.Common_Warning_Title, Strings.Debloat_History_ConnectFirst, Window.GetWindow(this));
        return false;
    }

    private static async Task<(bool Ok, string? Raw)> RestoreCoreAsync(HistoryAppViewModel h, string? serial)
    {
        var sdk = await DeviceApi.GetSdkAsync(serial);
        string? lastRaw = null;

        if (h.IsSystemApp && DeviceApi.SupportsInstallExisting(sdk))
        {
            var (ok, raw) = await TryInstallExistingAsync(h.PackageName, serial);
            if (ok) return (true, null);
            lastRaw = raw;
        }

        if (h.HasBackup)
        {
            var files = UninstallHistoryManager.GetBackupFiles(h.LocalApkPath!);
            if (files.Count > 0)
            {
                var quoted = string.Join(" ", files.Select(f => $"\"{f}\""));
                var cmd = files.Count == 1 ? $"install -r {quoted}" : $"install-multiple -r {quoted}";
                var r = await AdbExecutor.ExecuteTransferAsync(cmd, serial: serial);
                if (r.Contains("Success", StringComparison.OrdinalIgnoreCase)) return (true, null);
                lastRaw = r;
            }
        }

        if (!h.IsSystemApp && DeviceApi.SupportsInstallExisting(sdk))
        {
            var (ok, raw) = await TryInstallExistingAsync(h.PackageName, serial);
            if (ok) return (true, null);
            lastRaw ??= raw;
        }

        return (false, lastRaw);
    }

    private static async Task<(bool Ok, string Raw)> TryInstallExistingAsync(string pkg, string? serial)
    {
        var r = await AdbExecutor.ExecuteCommandAsync(DeviceApi.InstallExistingCommand(pkg), serial: serial);
        var ok = r.Contains("installed for user", StringComparison.OrdinalIgnoreCase) ||
                 r.Contains("Success", StringComparison.OrdinalIgnoreCase);
        return (ok, r);
    }

    private async Task RemoveHistoryEntryAsync(HistoryAppViewModel h)
    {
        await UninstallHistoryManager.RemoveEntryAsync(new HistoryItem { PackageName = h.PackageName, UninstallDate = h.UninstallDate, LocalApkPath = h.LocalApkPath });
        _history.Remove(h);
    }

    private async void RestoreApp_Click(object s, RoutedEventArgs e)
    {
        if (s is not FrameworkElement { DataContext: HistoryAppViewModel h }) return;
        if (!EnsureDeviceForRestore()) return;

        var serial = ActiveSerialOrNull();
        SetLoading(true, string.Format(Strings.Debloat_Action_Processing, h.Name));
        var (ok, raw) = await RestoreCoreAsync(h, serial);
        SetLoading(false);

        if (ok)
        {
            PageHeaderControl.Subtitle = string.Format(Strings.Debloat_Restored_Success, h.Name);
            await RemoveHistoryEntryAsync(h);
            UpdateCount();
            UpdateUI();
        }
        else
        {
            var sdk = await DeviceApi.GetSdkAsync(serial);
            var message = !h.HasBackup && !DeviceApi.SupportsInstallExisting(sdk)
                ? Strings.Debloat_Restore_NeedsBackup
                : AdbErrorCatalog.Humanize(raw, string.Format(Strings.Debloat_Restored_Failed, h.Name));
            DialogService.Instance.ShowInfoDirect(Strings.Advanced_Error, message, Window.GetWindow(this));
        }
    }

    private async void RestoreSelected_Click(object s, RoutedEventArgs e)
    {
        var sel = _history.Where(h => h.IsSelected).ToList();
        if (!sel.Any() || !EnsureDeviceForRestore()) return;

        SetLoading(true);
        var serial = ActiveSerialOrNull();
        int ok = 0;
        foreach (var h in sel)
        {
            PageHeaderControl.Subtitle = string.Format(Strings.Debloat_Action_Processing, h.Name);
            if (!(await RestoreCoreAsync(h, serial)).Ok) continue;
            ok++;
            await RemoveHistoryEntryAsync(h);
        }
        SetLoading(false);
        ResetSelectAll();
        UpdateCount();
        UpdateUI();
        PageHeaderControl.Subtitle = string.Format(Strings.Debloat_Status_Success, ok, sel.Count, Strings.Debloat_Action_Restore_Past);
    }

    private async void ForgetSelected_Click(object s, RoutedEventArgs e)
    {
        var sel = _history.Where(h => h.IsSelected).ToList();
        if (!sel.Any()) return;
        if (!DialogService.Instance.ConfirmDirect(
            string.Format(Strings.Debloat_History_BulkForgetQuestion, sel.Count), Window.GetWindow(this)))
            return;

        foreach (var h in sel)
            await RemoveHistoryEntryAsync(h);

        ResetSelectAll();
        UpdateCount();
        UpdateUI();
    }

    private async void DeleteHistory_Click(object s, RoutedEventArgs e)
    {
        if (s is not FrameworkElement { DataContext: HistoryAppViewModel h }) return;
        if (!DialogService.Instance.ConfirmDirect(
            string.Format(Strings.Debloat_History_RemoveQuestion, h.Name), Window.GetWindow(this)))
            return;
        await RemoveHistoryEntryAsync(h);
        UpdateCount();
        UpdateUI();
    }

    private void UpdateAfterAction(HashSet<string> ok, string cmd, HashSet<string>? systemPkgs = null)
    {
        if (cmd == "uninstall")
        {
            foreach (var a in _apps.Where(x => ok.Contains(x.PackageName)).ToList()) _apps.Remove(a);
            RebuildRows();
        }
        else if (cmd == "enable" && systemPkgs is not null)
        {
            foreach (var a in _apps.Where(x => ok.Contains(x.PackageName)))
                a.State = systemPkgs.Contains(a.PackageName) ? AppState.System : AppState.User;
            RefreshAppsView();
        }
        else if (cmd == "disable")
        {
            foreach (var a in _apps.Where(x => ok.Contains(x.PackageName))) a.State = AppState.Disabled;
            RefreshAppsView();
        }

        ClearSelection();
        ResetSelectAll();
        UpdateUI();
    }

    private void SetLoading(bool on, string? text = null)
    {
        if (text != null) PageHeaderControl.Subtitle = text;
        LoadingIndicator.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        AppsListView.Opacity = on ? 0.35 : 1.0;
        AppsListView.IsEnabled = !on;
        HistoryListView.Opacity = on ? 0.35 : 1.0;
        HistoryListView.IsEnabled = !on;
    }

    private void ShowOverlay(UIElement el) { OverlayContainer.Visibility = Visibility.Visible; el.Visibility = Visibility.Visible; }
    private void HideOverlay(UIElement el) { el.Visibility = Visibility.Collapsed; OverlayContainer.Visibility = Visibility.Collapsed; }
    private void CloseOverlay() { UninstallConfirmOverlay.Visibility = Visibility.Collapsed; OverlayContainer.Visibility = Visibility.Collapsed; }
    private void ClearQueues() { while (_loadedIcons.TryDequeue(out _)) { } }

    private void OverlayContainer_MouseDown(object s, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, OverlayContainer)) CloseActiveOverlay();
    }

    private void CloseActiveOverlay()
    {
        if (AppDetailsOverlayReal.Visibility == Visibility.Visible)
        {
            HideOverlay(AppDetailsOverlayReal);
            AppDetailsOverlayReal.DataContext = null;
        }
        else if (UninstallConfirmOverlay.Visibility == Visibility.Visible)
        {
            CloseOverlay();
            _toUninstall.Clear();
        }
    }

    private void OnRootPreviewKeyDown(object s, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (OverlayContainer.Visibility == Visibility.Visible) { CloseActiveOverlay(); e.Handled = true; }
        else if (SearchBox.Text.Length > 0) { SearchBox.Clear(); e.Handled = true; }
    }

    // A preset is a one-step debloat recipe: it imports and exports on its own, and doubles as the
    // hand-off into Zephyr's Recipes so a selection made here can be replayed as part of a full recipe.
    private DebloatRecipeStep BuildPresetFromSelection() => new()
    {
        Mode = DebloatMode.Uninstall,
        Packages = _apps.Where(a => a.IsSelected)
            .Select(a => new RecipePackage { PackageName = a.PackageName, Label = a.Name })
            .ToList()
    };

    private Recipe BuildRecipeFromStep(DebloatRecipeStep step) => new()
    {
        Name = string.Format(Strings.Debloat_Preset_DefaultName, step.Packages.Count),
        Description = Strings.Debloat_Preset_DefaultDescription,
        Author = RecipeStore.AuthorName,
        Glyph = RecipeStyle.Presets[1].Glyph,
        Accent = RecipeStyle.Presets[1].Accent,
        Debloat = step
    };

    private async void ExportPreset_Click(object s, RoutedEventArgs e)
    {
        var step = BuildPresetFromSelection();
        if (step.Packages.Count == 0) return;

        var dialog = new SaveFileDialog
        {
            Title = Strings.Debloat_Preset_Export_Title,
            FileName = "debloat_preset",
            Filter = $"{Recipe.FileDialogFilter}|{Strings.Debloat_Preset_FilterText} (*.txt)|*.txt"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            if (dialog.FilterIndex == 2)
                await File.WriteAllLinesAsync(dialog.FileName, step.Packages.Select(p => p.PackageName));
            else
                await RecipeStore.ExportAsync(BuildRecipeFromStep(step), dialog.FileName);
            PageHeaderControl.Subtitle = string.Format(Strings.Recipes_Status_Exported, dialog.FileName);
        }
        catch (Exception ex)
        {
            DialogService.Instance.ShowError(ex, Window.GetWindow(this));
        }
    }

    private async void SaveAsRecipe_Click(object s, RoutedEventArgs e)
    {
        var step = BuildPresetFromSelection();
        if (step.Packages.Count == 0) return;

        var recipe = BuildRecipeFromStep(step);
        await RecipeStore.SaveAsync(recipe);
        DialogService.Instance.ShowInfoDirect(
            Strings.Recipes_Header,
            string.Format(Strings.Debloat_Preset_SavedToLibrary, recipe.Name),
            Window.GetWindow(this));
    }

    private async void ImportPreset_Click(object s, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Strings.Debloat_Preset_Import_Title,
            Filter = $"{Strings.Debloat_Preset_FilterAll} (*{Recipe.FileExtension};*.txt)|*{Recipe.FileExtension};*.txt|All Files (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        var wanted = await ReadPresetPackagesAsync(dialog.FileName);
        if (wanted.Count == 0)
        {
            DialogService.Instance.ShowInfo("Debloat_Preset_Import_NoPackages", Window.GetWindow(this));
            return;
        }

        if (!_apps.Any())
        {
            DialogService.Instance.ShowInfo("Debloat_Preset_Import_LoadFirst", Window.GetWindow(this));
            return;
        }

        int matched = 0;
        _suppressCount = true;
        try
        {
            foreach (var app in _apps)
            {
                var select = wanted.Contains(app.PackageName);
                app.IsSelected = select;
                if (select) matched++;
            }
        }
        finally { _suppressCount = false; }

        UpdateCount();

        if (matched > 0 && _filter != 0 && FilterPanel.Children.OfType<RadioButton>().FirstOrDefault() is { } allFilter)
            allFilter.IsChecked = true;

        ResetSelectAll();
        PageHeaderControl.Subtitle = string.Format(Strings.Debloat_Preset_Import_Matched, matched, wanted.Count);
    }

    private static async Task<HashSet<string>> ReadPresetPackagesAsync(string file)
    {
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var recipe = await RecipeStore.TryReadAsync(file);
        if (recipe?.Debloat?.Packages is { Count: > 0 } packages)
        {
            foreach (var p in packages) wanted.Add(p.PackageName);
            return wanted;
        }

        try
        {
            foreach (var raw in await File.ReadAllLinesAsync(file))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("//")) continue;
                if (line.StartsWith("package:", StringComparison.OrdinalIgnoreCase)) line = line["package:".Length..].Trim();
                var token = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (RecipeValidator.IsValidPackageName(token)) wanted.Add(token!);
            }
        }
        catch { }

        return wanted;
    }
}
