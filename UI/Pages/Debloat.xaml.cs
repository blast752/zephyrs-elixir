
namespace ZephyrsElixir.UI.Pages
{
    public partial class Debloat : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private readonly ObservableCollection<AppInfoViewModel> _apps = new();
        private readonly ObservableCollection<HistoryAppViewModel> _history = new();
        private readonly ICollectionView _appsView, _historyView;
        private readonly DispatcherTimer _iconTimer;
        private readonly SemaphoreSlim _loadSem = new(1);
        private readonly ConcurrentQueue<AppInfoViewModel> _iconQueue = new();
        private readonly ConcurrentQueue<(AppInfoViewModel App, BitmapImage? Icon)> _loadedIcons = new();
        private readonly BlurEffect _blur = new() { Radius = 30, KernelType = KernelType.Gaussian };

        private CancellationTokenSource? _cts;
        private Task? _iconTask;
        private List<AppInfoViewModel> _toUninstall = new();
        private int _filter;
        private bool _historyMode;
        private int _selectedCount;

        public int SelectedAppsCount { get => _selectedCount; set { if (_selectedCount == value) return; _selectedCount = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedAppsCount))); } }

        public Debloat()
        {
            InitializeComponent();
            DataContext = this;

            _appsView = CollectionViewSource.GetDefaultView(_apps);
            _appsView.Filter = FilterApps;
            if (_appsView is ListCollectionView lv) lv.CustomSort = Comparer<AppInfoViewModel>.Create((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            AppsListView.ItemsSource = _appsView;

            _historyView = CollectionViewSource.GetDefaultView(_history);
            if (_historyView is ListCollectionView hlv) hlv.SortDescriptions.Add(new SortDescription(nameof(HistoryAppViewModel.UninstallDate), ListSortDirection.Descending));
            HistoryListView.ItemsSource = _historyView;

            _iconTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, OnIconTick, Dispatcher) { IsEnabled = false };
            Loaded += OnLoad;
            Unloaded += OnUnload;
        }

        private void OnLoad(object s, RoutedEventArgs e)
        {
            this.SubscribeToDeviceUpdates(onStatusChanged: OnDeviceChanged, controls: new UIElement[] { LoadAppsButton });
            UpdateStatus();
            _ = LoadHistoryAsync();
        }

        private void OnUnload(object s, RoutedEventArgs e)
        {
            _cts?.Cancel();
            _iconTimer.Stop();
            AppIconLoader.ClearCache();
        }

        private void OnDeviceChanged(bool _) => UpdateStatus();

        private void UpdateStatus()
        {
            StatusText.Text = DeviceManager.Instance.IsConnected
                ? Strings.Debloat_Status_Connected
                : Strings.Debloat_Status_Disconnected;

            if (!DeviceManager.Instance.IsConnected)
            {
                _cts?.Cancel();
                _iconTimer.Stop();
                _apps.Clear();
                UpdateCount();
                SetLoading(false);
                UpdateUI();
            }
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
                ClearQueues();
                UpdateCount();
                SelectAllCheckBox.IsChecked = false;
                UpdateUI(true);
                SetLoading(true, Strings.Debloat_Status_Loading);

                var vms = new List<AppInfoViewModel>();
                try
                {
                    var progress = new Progress<string>(msg => Dispatcher.BeginInvoke(() => StatusText.Text = msg, DispatcherPriority.Background));
                    if (!await ZephyrsAgent.EnsureAgentIsRunningAsync(progress, ct) || ct.IsCancellationRequested) { SetLoading(false); return; }

                    var apps = await ZephyrsAgent.GetInstalledAppsAsync(progress, ct);
                    if (!ct.IsCancellationRequested)
                        vms = apps.Select(a => new AppInfoViewModel { Name = a.Name, PackageName = a.PackageName, Version = a.Version, State = a.State }).ToList();
                }
                catch (Exception ex) { StatusText.Text = ex.Message; }
                finally
                {
                    if (!ct.IsCancellationRequested)
                    {
                        foreach (var vm in vms) { vm.IsSelectedChanged += _ => UpdateCount(); _apps.Add(vm); }
                        SetLoading(false);
                        UpdateUI();
                        if (vms.Any()) { StartIconLoading(vms, ct); StartAnalysis(vms, ct); }
                    }
                }
            }
            finally { _loadSem.Release(); }
        }

        private void StartAnalysis(IEnumerable<AppInfoViewModel> apps, CancellationToken ct)
        {
            Task.Run(async () =>
            {
                var map = apps.ToDictionary(a => a.PackageName);
                await CloudIntelligenceManager.AnalyzeBatchStreamAsync(map.Keys, data =>
                {
                    if (map.TryGetValue(data.PackageName, out var vm))
                        Dispatcher.BeginInvoke(() => vm.ApplyIntelligence(data), DispatcherPriority.Background);
                }, ct);
            }, ct);
        }

        private void StartIconLoading(IEnumerable<AppInfoViewModel> apps, CancellationToken ct)
        {
            ClearQueues();
            foreach (var a in apps) _iconQueue.Enqueue(a);
            _iconTimer.Start();

            _iconTask = Task.Run(async () =>
            {
                await Parallel.ForEachAsync(_iconQueue, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct }, async (a, c) =>
                {
                    await Dispatcher.InvokeAsync(() => a.IsLoadingIcon = true, DispatcherPriority.Background, c);
                    var icon = await AppIconLoader.LoadIconAsync(a.PackageName, c);
                    _loadedIcons.Enqueue((a, icon));
                });
            }, ct);
        }

        private void OnIconTick(object? s, EventArgs e)
        {
            if (_loadedIcons.IsEmpty) { if (_iconQueue.IsEmpty && (_iconTask?.IsCompleted ?? true)) _iconTimer.Stop(); return; }
            for (int i = 0; i < 10 && _loadedIcons.TryDequeue(out var item); i++) { item.App.Icon = item.Icon; item.App.IsLoadingIcon = false; }
        }

        private async Task LoadHistoryAsync()
        {
            var items = await UninstallHistoryManager.LoadHistoryAsync();
            _history.Clear();
            foreach (var h in items)
                _history.Add(new HistoryAppViewModel { Name = h.DisplayName, PackageName = h.PackageName, Version = h.Version, UninstallDate = h.UninstallDate, LocalApkPath = h.LocalApkPath, IsSystemApp = h.IsSystemApp, Icon = DecodeIcon(h.IconBase64) });
            UpdateUI();
        }

        private static BitmapImage? DecodeIcon(string? b64)
        {
            if (string.IsNullOrEmpty(b64)) return null;
            try { using var ms = new MemoryStream(Convert.FromBase64String(b64)); var bmp = new BitmapImage(); bmp.BeginInit(); bmp.StreamSource = ms; bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.EndInit(); bmp.Freeze(); return bmp; }
            catch { return null; }
        }

        private bool FilterApps(object item) => item switch
        {
            AppInfoViewModel a => MatchFilter(a) && MatchSearch(a.Name, a.PackageName, SearchBox.Text.Trim()),
            HistoryAppViewModel h => MatchSearch(h.Name, h.PackageName, SearchBox.Text.Trim()),
            _ => false
        };

        private bool MatchFilter(AppInfoViewModel a) => _filter switch
        {
            1 => a.State == AppState.User,
            2 => a.State == AppState.System,
            3 => a.State == AppState.Disabled,
            _ => true
        };

        private static bool MatchSearch(string name, string pkg, string search) =>
            string.IsNullOrEmpty(search) || name.Contains(search, StringComparison.OrdinalIgnoreCase) || pkg.Contains(search, StringComparison.OrdinalIgnoreCase);

        private void SearchBox_TextChanged(object s, TextChangedEventArgs e) => (_historyMode ? _historyView : _appsView).Refresh();

        private void OnFilterChanged(object s, RoutedEventArgs e)
        {
            if (FilterPanel == null || _appsView == null) return;
            if (s is RadioButton { IsChecked: true } rb)
            {
                _filter = FilterPanel.Children.OfType<RadioButton>().ToList().IndexOf(rb);
                _appsView.Refresh();
            }
        }

        private void OnViewModeChanged(object s, RoutedEventArgs e)
        {
            if (AppsScrollViewer == null || s is not RadioButton { IsChecked: true } rb) return;
            _historyMode = Grid.GetColumn(rb) == 1;
            AppsScrollViewer.Visibility = _historyMode ? Visibility.Collapsed : Visibility.Visible;
            HistoryListView.Visibility = _historyMode ? Visibility.Visible : Visibility.Collapsed;
            FilterPanel.Visibility = _historyMode ? Visibility.Collapsed : Visibility.Visible;
            ActionPanel.Visibility = _historyMode ? Visibility.Collapsed : Visibility.Visible;
            SelectAllCheckBox.IsChecked = false;
            if (_historyMode) _ = LoadHistoryAsync(); else UpdateUI();
        }

        private void UpdateUI(bool loading = false)
        {
            if (SelectionBar == null) return;
            var has = _historyMode ? _history.Any() : _apps.Any();
            SelectionBar.Visibility = !loading && has ? Visibility.Visible : Visibility.Collapsed;
            if (loading || !has) SelectAllCheckBox.IsChecked = false;
        }

        private void OnSelectAllChecked(object s, RoutedEventArgs e) => SetAll(true);
        private void OnSelectAllUnchecked(object s, RoutedEventArgs e) => SetAll(false);
        private void SetAll(bool sel) { if (_historyMode) foreach (var h in _historyView.Cast<HistoryAppViewModel>()) h.IsSelected = sel; else foreach (var a in _appsView.Cast<AppInfoViewModel>()) a.IsSelected = sel; }
        private void UpdateCount() => SelectedAppsCount = _apps.Count(a => a.IsSelected);

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

        private async void RevokeAllPermissions_Click(object s, RoutedEventArgs e)
        {
            if (AppDetailsOverlayReal.DataContext is not AppDetailsViewModel { HasGrantedPermissions: true } vm) return;
            if (MessageBox.Show(string.Format(Strings.Debloat_Overlay_RevokeQuestion, vm.GrantedPermissionsCount, vm.App.Name), 
                Strings.MessageBox_ConfirmAction_Title, 
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            try { var c = await vm.RevokeAllPermissionsAsync(); StatusText.Text = string.Format(Strings.Debloat_Status_Revoked, c, vm.App.Name); }
            catch (Exception ex) { DialogService.Instance.ShowInfoDirect(Strings.Advanced_Error, string.Format(Strings.Common_Status_Error, ex.Message), Window.GetWindow(this)); }
        }

        private async void DisableButton_Click(object s, RoutedEventArgs e) => await PerformAction("disable-user");

        private void UninstallButton_Click(object s, RoutedEventArgs e)
        {
            _toUninstall = _apps.Where(a => a.IsSelected).ToList();
            if (!_toUninstall.Any()) return;

            var crit = _toUninstall.Count(a => a.RiskLevel == SafetyRiskLevel.Critical);
            UninstallConfirmText.Text = crit > 0 
                ? string.Format(Strings.Debloat_Uninstall_WarningCritical, crit) 
                : string.Format(Strings.Debloat_Uninstall_Question, _toUninstall.Count);
            UninstallConfirmText.Foreground = new SolidColorBrush(crit > 0 ? Color.FromRgb(255, 107, 107) : (Color)ColorConverter.ConvertFromString("#B0B8C8"));
            ShowOverlay(UninstallConfirmOverlay);
        }

        private async void ConfirmUninstallBackup_Click(object s, RoutedEventArgs e) { CloseOverlay(); await Uninstall(true); }
        private async void ConfirmUninstallOnly_Click(object s, RoutedEventArgs e) { CloseOverlay(); await Uninstall(false); }
        private void CancelUninstall_Click(object s, RoutedEventArgs e) { CloseOverlay(); _toUninstall.Clear(); }

        private async Task PerformAction(string cmd)
        {
            var sel = _apps.Where(a => a.IsSelected).ToList();
            if (!sel.Any() || !DialogService.Instance.ConfirmDirect(
                string.Format(Strings.Debloat_Action_PerformConfirm, sel.Count), 
                Window.GetWindow(this))) 
                return;

            SetLoading(true);
            var ok = new ConcurrentBag<string>();
            await Task.Run(async () =>
            {
                foreach (var a in sel)
                {
                    await Dispatcher.InvokeAsync(() => StatusText.Text = string.Format(Strings.Debloat_Action_Processing, a.Name), DispatcherPriority.Background);
                    var r = await AdbExecutor.ExecuteCommandAsync($"shell pm {cmd} {a.PackageName}");
                    if (r.Contains("success", StringComparison.OrdinalIgnoreCase)) ok.Add(a.PackageName);
                }
            });

            UpdateAfterAction(ok.ToHashSet(), cmd);
            SetLoading(false);
            StatusText.Text = string.Format(Strings.Debloat_Action_Done, ok.Count);
        }

        private async Task Uninstall(bool backup)
        {
            SetLoading(true);
            var ok = new ConcurrentBag<string>();
            var fail = new ConcurrentBag<string>();

            await Task.Run(async () =>
            {
                foreach (var a in _toUninstall)
                {
                    await Dispatcher.InvokeAsync(() => StatusText.Text = string.Format(Strings.Debloat_Action_Processing, a.Name), DispatcherPriority.Background);

                    string? path = null;
                    var iconB64 = "";

                    if (backup)
                    {
                        try
                        {
                            var p = await AdbExecutor.ExecuteCommandAsync($"shell pm path {a.PackageName}");
                            var remote = p.Split(':')[1].Trim();
                            path = UninstallHistoryManager.GetBackupPath(a.PackageName, a.Version);
                            await AdbExecutor.ExecuteCommandAsync($"pull \"{remote}\" \"{path}\"");
                        }
                        catch { path = null; }
                    }

                    if (a.Icon != null) await Dispatcher.InvokeAsync(() => iconB64 = EncodeIcon(a.Icon));

                    var r = await AdbExecutor.ExecuteCommandAsync($"shell pm uninstall -k --user 0 {a.PackageName}");
                    if (r.Contains("success", StringComparison.OrdinalIgnoreCase))
                    {
                        ok.Add(a.PackageName);
                        await UninstallHistoryManager.AddEntryAsync(new HistoryItem { PackageName = a.PackageName, DisplayName = a.Name, Version = a.Version, UninstallDate = DateTime.Now, LocalApkPath = path, IconBase64 = iconB64, IsSystemApp = a.State == AppState.System });
                    }
                    else { fail.Add(a.Name); if (path != null && File.Exists(path)) File.Delete(path); }
                }
            });

            UpdateAfterAction(ok.ToHashSet(), "uninstall");
            SetLoading(false);
            StatusText.Text = string.Format(Strings.Debloat_Status_Success, ok.Count, _toUninstall.Count, Strings.Debloat_Action_Uninstall_Past);
            if (fail.Any()) 
                DialogService.Instance.ShowInfoDirect(
                    Strings.Common_Warning_Title, 
                    string.Format(Strings.Common_Status_Error, string.Join(", ", fail)), 
                    Window.GetWindow(this));
        }

        private static string EncodeIcon(BitmapImage icon)
        {
            using var ms = new MemoryStream();
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(icon));
            enc.Save(ms);
            return Convert.ToBase64String(ms.ToArray());
        }

        private async void RestoreApp_Click(object s, RoutedEventArgs e)
        {
            if (s is not FrameworkElement { DataContext: HistoryAppViewModel h }) return;

            SetLoading(true, string.Format(Strings.Debloat_Action_Processing, h.Name));
            var ok = await Task.Run(async () =>
            {
                if (h.HasBackup)
                {
                    await AdbExecutor.ExecuteCommandAsync($"push \"{h.LocalApkPath}\" /data/local/tmp/restore.apk");
                    var r = await AdbExecutor.ExecuteCommandAsync("shell pm install -r /data/local/tmp/restore.apk");
                    await AdbExecutor.ExecuteCommandAsync("shell rm /data/local/tmp/restore.apk");
                    return r.Contains("Success", StringComparison.OrdinalIgnoreCase);
                }
                return (await AdbExecutor.ExecuteCommandAsync($"shell cmd package install-existing {h.PackageName}")).Contains("installed", StringComparison.OrdinalIgnoreCase);
            });

            SetLoading(false);
            if (ok) { StatusText.Text = string.Format(Strings.Debloat_Restored_Success, h.Name); await UninstallHistoryManager.RemoveEntryAsync(new HistoryItem { PackageName = h.PackageName, UninstallDate = h.UninstallDate }); _history.Remove(h); }
            else DialogService.Instance.ShowInfoDirect(
                Strings.Advanced_Error, 
                string.Format(Strings.Debloat_Restored_Failed, h.Name), 
                Window.GetWindow(this));
        }

        private async void DeleteHistory_Click(object s, RoutedEventArgs e)
        {
            if (s is not FrameworkElement { DataContext: HistoryAppViewModel h }) return;
            if (!DialogService.Instance.ConfirmDirect(
                string.Format(Strings.Debloat_History_RemoveQuestion, h.Name), Window.GetWindow(this))) 
                return;
            await UninstallHistoryManager.RemoveEntryAsync(new HistoryItem { PackageName = h.PackageName, UninstallDate = h.UninstallDate, LocalApkPath = h.LocalApkPath });
            _history.Remove(h);
        }

        private void UpdateAfterAction(HashSet<string> ok, string cmd)
        {
            if (cmd.StartsWith("uninstall")) foreach (var a in _apps.Where(x => ok.Contains(x.PackageName)).ToList()) _apps.Remove(a);
            else if (cmd.Contains("disable")) { foreach (var a in _apps.Where(x => ok.Contains(x.PackageName))) a.State = AppState.Disabled; _appsView.Refresh(); }
            SelectAllCheckBox.IsChecked = false;
            UpdateCount();
            UpdateUI();
        }

        private void SetLoading(bool on, string? text = null)
        {
            if (text != null) StatusText.Text = text;
            LoadingIndicator.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            AppsScrollViewer.Effect = on ? _blur : null;
            AppsScrollViewer.IsEnabled = !on;
            HistoryListView.Effect = on ? _blur : null;
            HistoryListView.IsEnabled = !on;
        }

        private void ShowOverlay(UIElement el) { OverlayContainer.Visibility = Visibility.Visible; el.Visibility = Visibility.Visible; }
        private void HideOverlay(UIElement el) { el.Visibility = Visibility.Collapsed; OverlayContainer.Visibility = Visibility.Collapsed; }
        private void CloseOverlay() { UninstallConfirmOverlay.Visibility = Visibility.Collapsed; OverlayContainer.Visibility = Visibility.Collapsed; }
        private void ClearQueues() { while (_iconQueue.TryDequeue(out _)) { } while (_loadedIcons.TryDequeue(out _)) { } }
    }
}