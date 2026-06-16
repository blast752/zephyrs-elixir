namespace ZephyrsElixir.UI.Pages;

[ObservableObject]
public sealed partial class FileManager : UserControl
{
    private const string DefaultRemotePath = "/sdcard";
    private const long PreviewSizeCeiling = 8L * 1024 * 1024;
    private const int PreviewTextByteLimit = 512 * 1024;
    private const long DragOutSizeCeiling = 10L * 1024 * 1024;
    private const long OpenWithAppLargeFileThreshold = 100L * 1024 * 1024;
    private const int DragMovementThreshold = 8;
    private const int MaxParallelTransfers = 2;
    private const int MaxStatusErrorLength = 120;
    private const int ShellAssocNotFoundCode = 1155;

    private static readonly string TempBase = Path.Combine(
        Path.GetTempPath(), "ZephyrsElixir", "FileManager");

    [GeneratedRegex(@"\[\s*(\d+)%\]")]
    private static partial Regex ProgressPattern();

    [GeneratedRegex(@"^[a-z]+:.*(Permission denied|No such file|Not a directory|I/O error)",
        RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex PermissionDeniedPattern();

    private static readonly string[] AdbDeviceErrorMarkers =
    {
        "no devices/emulators",
        "device not found",
        "device unauthorized",
        "device offline"
    };

    private readonly Action _onClose;
    private readonly EventHandler _languageListener;
    private readonly SemaphoreSlim _transferGate = new(MaxParallelTransfers, MaxParallelTransfers);
    private readonly List<RemoteEntry> _clipboardEntries = new();

    private CancellationTokenSource? _listingCts;
    private CancellationTokenSource? _previewCts;
    private CancellationTokenSource? _storageCts;
    private TaskCompletionSource<string?>? _promptCompletion;
    private ClipboardMode _clipboardMode;
    private Point _gridDragOrigin;
    private bool _gridDragArmed;
    private bool _wired;
    private string? _whatsAppMediaPath;
    private bool _tempAvailable;

    [ObservableProperty]
    private string _currentPath = DefaultRemotePath;
    [ObservableProperty] private string _pathInput = DefaultRemotePath;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(ConnectionStatusText)), NotifyPropertyChangedFor(nameof(ConnectionStatusBrush))]
    private bool _isDeviceConnected;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(ConnectionStatusText))]
    private string _deviceName = "";
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private string _emptyStateMessage = "";
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private long _selectedSize;
    [ObservableProperty] private string _clipboardSummary = "";
    [ObservableProperty] private bool _hasClipboard;
    [ObservableProperty] private bool _hasEntries;
    [ObservableProperty] private bool _isPreviewOpen;
    [ObservableProperty] private bool _isPreviewLoading;
    [ObservableProperty] private string? _previewName;
    [ObservableProperty] private string? _previewSubtitle;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsImagePreview)), NotifyPropertyChangedFor(nameof(IsTextPreview))]
    private PreviewKind _previewKind = PreviewKind.None;
    [ObservableProperty] private BitmapImage? _previewImage;
    [ObservableProperty] private string? _previewText;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasPreviewError))]
    private string? _previewError;
    [ObservableProperty] private bool _isPromptOpen;
    [ObservableProperty] private string _promptTitle = "";
    [ObservableProperty] private string _promptLabel = "";
    [ObservableProperty] private string _promptValue = "";
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasPromptError))]
    private string? _promptError;
    [ObservableProperty] private bool _isDragHovering;
    [ObservableProperty] private bool _hasActiveTransfers;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(ActiveTransferCountText))]
    private int _activeTransferCount;
    [ObservableProperty] private double _aggregateProgress;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(PermissionsHeaderText))]
    private PermissionsDisplayMode _permissionsDisplayMode = PermissionsDisplayMode.Symbolic;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(DeviceSerialDisplay))]
    private string _deviceSerial = "";
    [ObservableProperty] private long _storageTotalBytes;
    [ObservableProperty] private long _storageUsedBytes;
    [ObservableProperty] private string _storageSummaryText = "";
    [ObservableProperty] private bool _isStorageLoading;
    [ObservableProperty] private bool _isStorageAvailable;
    [ObservableProperty] private LinearGradientBrush? _storageBarBrush;

    public string DeviceSerialDisplay => string.IsNullOrEmpty(DeviceSerial)
        ? ""
        : string.Format(Strings.FileManager_Status_DeviceSerialFormat, DeviceSerial);

    public string ActiveTransferCountText => string.Format(Strings.FileManager_Transfer_Count, ActiveTransferCount);

    public bool HasPromptError => !string.IsNullOrEmpty(PromptError);
    public bool HasPreviewError => !string.IsNullOrEmpty(PreviewError);
    public bool IsImagePreview => PreviewKind == PreviewKind.Image;
    public bool IsTextPreview => PreviewKind == PreviewKind.Text;

    public string ConnectionStatusText => IsDeviceConnected
        ? (string.IsNullOrEmpty(DeviceName) ? Strings.FileManager_Connection_Generic : DeviceName)
        : Strings.FileManager_Connection_None;

    public Brush ConnectionStatusBrush => IsDeviceConnected ? AppBrushes.Success : AppBrushes.Failed;

    public string PermissionsHeaderText => string.Format(
        "{0}{1}{2}",
        Strings.FileManager_Column_Permissions,
        Strings.FileManager_Perm_Header_Separator,
        PermissionsDisplayMode switch
        {
            PermissionsDisplayMode.Numeric => Strings.FileManager_Perm_Mode_Numeric_Hint,
            PermissionsDisplayMode.Simplified => Strings.FileManager_Perm_Mode_Simplified_Hint,
            _ => Strings.FileManager_Perm_Mode_Symbolic_Hint
        });

    public ObservableCollection<RemoteEntry> Entries { get; } = new();
    public ObservableCollection<TransferItem> Transfers { get; } = new();
    public ObservableCollection<TreeNode> TreeRoots { get; } = new();
    public ObservableCollection<PathSegment> Breadcrumbs { get; } = new();
    public ObservableCollection<Bookmark> Bookmarks { get; } = new();
    public ObservableCollection<StorageSegment> StorageSegments { get; } = new();

    public FileManager(Action onClose)
    {
        _onClose = onClose;

        SeedBookmarksAndTrees();

        InitializeComponent();
        DataContext = this;

        UpdateBreadcrumbs(DefaultRemotePath);
        EmptyStateMessage = Strings.FileManager_Status_Empty;

        Entries.CollectionChanged += (_, _) => HasEntries = Entries.Count > 0;
        Transfers.CollectionChanged += OnTransfersChanged;

        _languageListener = (_, _) => Dispatcher.BeginInvoke(OnLanguageChanged);

        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    private void SeedBookmarksAndTrees()
    {
        Bookmarks.Add(new Bookmark("/sdcard/Download",    Strings.FileManager_Bookmark_Downloads, "", AppBrushes.GradientApk));
        Bookmarks.Add(new Bookmark("/sdcard/DCIM/Camera", Strings.FileManager_Bookmark_Camera,    "", AppBrushes.GradientApkm));

        TreeRoots.Add(TreeNode.CreateExpandable(Strings.FileManager_Tree_InternalStorage, "/sdcard"));
        TreeRoots.Add(TreeNode.CreateExpandable(Strings.FileManager_Tree_FilesystemRoot, "/"));
        TreeRoots.Add(TreeNode.CreateExpandable(Strings.FileManager_Tree_Temp, "/data/local/tmp"));
    }

    private void RebuildBookmarks(string? whatsAppPath, bool tempAvailable)
    {
        Bookmarks.Clear();
        Bookmarks.Add(new Bookmark("/sdcard/Download",    Strings.FileManager_Bookmark_Downloads, "", AppBrushes.GradientApk));
        Bookmarks.Add(new Bookmark("/sdcard/DCIM/Camera", Strings.FileManager_Bookmark_Camera,    "", AppBrushes.GradientApkm));

        if (!string.IsNullOrEmpty(whatsAppPath))
            Bookmarks.Add(new Bookmark(whatsAppPath, Strings.FileManager_Bookmark_WhatsApp, "", AppBrushes.GradientGreen));

        if (tempAvailable)
            Bookmarks.Add(new Bookmark("/data/local/tmp", Strings.FileManager_Bookmark_Temp, "", AppBrushes.GradientOrange));
    }

    private async Task ProbeBookmarksAsync()
    {
        if (!IsDeviceConnected) return;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            var whatsAppCandidates = new[]
            {
                "/sdcard/Android/media/com.whatsapp/WhatsApp/Media",
                "/sdcard/Android/media/com.whatsapp.w4b/WhatsApp Business/Media",
                "/sdcard/WhatsApp/Media"
            };

            _whatsAppMediaPath = await ResolveFirstExistingPathAsync(whatsAppCandidates, cts.Token);
            _tempAvailable     = await RemoteDirectoryExistsAsync("/data/local/tmp", cts.Token);
        }
        catch
        {
            _whatsAppMediaPath = null;
            _tempAvailable     = false;
        }

        await Dispatcher.InvokeAsync(() => RebuildBookmarks(_whatsAppMediaPath, _tempAvailable));
    }

    private async Task<string?> ResolveFirstExistingPathAsync(IEnumerable<string> candidates, CancellationToken ct)
    {
        foreach (var candidate in candidates)
        {
            if (await RemoteDirectoryExistsAsync(candidate, ct))
                return candidate;
        }
        return null;
    }

    private async Task<bool> RemoteDirectoryExistsAsync(string remotePath, CancellationToken ct)
    {
        try
        {
            var output = await AdbExecutor.ExecuteCommandAsync(
                $"shell [ -d {AdbArg.ForShell(remotePath)} ] && echo OK", ct);
            return output?.Contains("OK") == true;
        }
        catch { return false; }
    }

    private void OnLanguageChanged()
    {
        UpdateStatusMessage();
        OnPropertyChanged(nameof(PermissionsHeaderText));
        RelocaliseSidebar();

        if (!IsLoading &&
            (string.IsNullOrEmpty(EmptyStateMessage) ||
             EmptyStateMessage == Strings.FileManager_Status_NoDevice ||
             EmptyStateMessage == Strings.FileManager_Status_Empty))
        {
            EmptyStateMessage = IsDeviceConnected
                ? Strings.FileManager_Status_Empty
                : Strings.FileManager_Status_NoDevice;
        }
    }

    private void RelocaliseSidebar()
    {
        foreach (var root in TreeRoots)
        {
            root.Name = root.Path switch
            {
                "/sdcard"         => Strings.FileManager_Tree_InternalStorage,
                "/"               => Strings.FileManager_Tree_FilesystemRoot,
                "/data/local/tmp" => Strings.FileManager_Tree_Temp,
                _                 => root.Name
            };
        }

        RebuildBookmarks(_whatsAppMediaPath, _tempAvailable);
    }

    partial void OnCurrentPathChanged(string value)
    {
        UpdateBreadcrumbs(value);
        GoUpCommand.NotifyCanExecuteChanged();
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!_wired)
            {
                TranslationManager.Instance.LanguageChanged += _languageListener;
                this.SubscribeToDeviceUpdates(
                    onStatusChanged: HandleDeviceStatus,
                    onInfoUpdated: (name, _) => DeviceName = DeviceManager.Instance.IsConnected ? name : "");
                _wired = true;
            }

            IsDeviceConnected = DeviceManager.Instance.IsConnected;
            DeviceName = IsDeviceConnected ? DeviceManager.Instance.DeviceName : "";
            DeviceSerial = IsDeviceConnected ? DeviceManager.Instance.DeviceSerial : "";
            UpdateStatusMessage();
            await NavigateAsync(CurrentPath);
            _ = LoadStorageAsync();
            _ = ProbeBookmarksAsync();
        }
        catch (Exception ex)
        {
            LogException(ex);
        }
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        if (_wired)
        {
            TranslationManager.Instance.LanguageChanged -= _languageListener;
            _wired = false;
        }

        DisposeCts(ref _listingCts);
        DisposeCts(ref _previewCts);
        DisposeCts(ref _storageCts);

        foreach (var item in Transfers.ToArray())
            item.Cancel();

        try { if (Directory.Exists(TempBase)) Directory.Delete(TempBase, true); } catch { }
    }

    [RelayCommand]
    private void Close() => _onClose();

    [RelayCommand]
    private async Task NavigateAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var normalized = PathUtil.Normalize(path);

        DisposeCts(ref _listingCts);
        var cts = new CancellationTokenSource();
        _listingCts = cts;

        IsLoading = true;
        Entries.Clear();
        ClosePreview();
        EmptyStateMessage = Strings.FileManager_Status_Loading;

        try
        {
            CurrentPath = normalized;
            PathInput = normalized;

            if (!DeviceManager.Instance.IsConnected)
            {
                EmptyStateMessage = Strings.FileManager_Status_NoDevice;
                return;
            }

            var output = await AdbExecutor.ExecuteCommandAsync(
                $"shell ls -la {AdbArg.ForShell(normalized)}", cts.Token);

            if (cts.Token.IsCancellationRequested) return;

            if (IsAdbDeviceError(output))
            {
                EmptyStateMessage = string.Format(Strings.FileManager_Status_AdbError, ShortError(output));
                HandleDeviceStatus(false);
                return;
            }

            if (IsListingFailure(output))
            {
                EmptyStateMessage = ShortError(output);
                return;
            }

            var parsed = LsParser.Parse(output, normalized).ToList();
            parsed.Sort(EntryComparer.Default);

            foreach (var entry in parsed)
                Entries.Add(entry);

            EmptyStateMessage = parsed.Count == 0 ? Strings.FileManager_Status_Empty : "";

            UpdateStatusMessage();
            AdbLogger.Instance.LogInfo("FileManager", "Listed " + normalized, parsed.Count + " entries");

            StartDirectorySizesAsync(parsed, cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            EmptyStateMessage = string.Format(Strings.FileManager_Status_Error, ex.Message);
            LogException(ex);
        }
        finally
        {
            if (ReferenceEquals(cts, _listingCts))
                IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoUp))]
    private Task GoUpAsync() => NavigateAsync(PathUtil.Parent(CurrentPath));

    private bool CanGoUp() => !string.IsNullOrEmpty(CurrentPath) && CurrentPath != "/";

    [RelayCommand] private Task GoHomeAsync() => NavigateAsync(DefaultRemotePath);
    [RelayCommand] private Task RefreshAsync() => NavigateAsync(CurrentPath);

    [RelayCommand]
    private async Task GoToInputPathAsync()
    {
        if (string.IsNullOrWhiteSpace(PathInput))
        {
            FocusPathBar();
            return;
        }
        await NavigateAsync(PathInput);
    }

    private void FocusPathBar() =>
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            PathBar.Focus();
            PathBar.SelectAll();
        }));

    [RelayCommand]
    private async Task CreateDirectoryAsync()
    {
        if (!EnsureConnected()) return;

        var name = await PromptAsync(
            Strings.FileManager_Prompt_NewFolder_Title,
            Strings.FileManager_Prompt_NewFolder_Label,
            Strings.FileManager_Prompt_NewFolder_Default,
            FileNameValidator);
        if (string.IsNullOrEmpty(name)) return;

        var newPath = PathUtil.Combine(CurrentPath, name);
        var output = await AdbExecutor.ExecuteCommandAsync(
            $"shell mkdir -p {AdbArg.ForShell(newPath)}");

        if (HasShellError(output))
            ReportShellError(Strings.FileManager_Error_CreateFolder, output);

        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RenameAsync()
    {
        if (!EnsureConnected()) return;

        var target = CurrentSingleSelection();
        if (target == null) return;

        var newName = await PromptAsync(
            Strings.FileManager_Prompt_Rename_Title,
            Strings.FileManager_Prompt_Rename_Label,
            target.Name, FileNameValidator);
        if (string.IsNullOrEmpty(newName) || newName == target.Name) return;

        var newPath = PathUtil.Combine(CurrentPath, newName);
        var output = await AdbExecutor.ExecuteCommandAsync(
            $"shell mv -- {AdbArg.ForShell(target.FullPath)} {AdbArg.ForShell(newPath)}");

        if (HasShellError(output))
            ReportShellError(Strings.FileManager_Error_Rename, output);

        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ChangePermissionsAsync()
    {
        if (!EnsureConnected()) return;

        var targets = GetSelectedEntries();
        if (targets.Count == 0) return;

        var initial = targets.Count == 1 ? PermissionsDescriber.ToOctal(targets[0].Permissions) : "";
        var itemLabel = targets.Count == 1
            ? string.Format(Strings.FileManager_Prompt_Chmod_LabelSingle, targets[0].Name)
            : string.Format(Strings.FileManager_Prompt_Chmod_LabelMulti, targets.Count);
        var label = $"{itemLabel}\n{Strings.FileManager_Prompt_Chmod_FormatsHint}";

        var input = await PromptAsync(Strings.FileManager_Prompt_Chmod_Title, label, initial, PermissionsInputValidator);
        if (string.IsNullOrEmpty(input)) return;

        PermissionsDescriber.TryParse(input.Trim(), out _, out var mode, out _);

        var errors = await RunShellBatchAsync(targets,
            entry => $"shell chmod {mode} -- {AdbArg.ForShell(entry.FullPath)}");

        TryReportBatchErrors(errors, Strings.FileManager_Error_Chmod);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (!EnsureConnected()) return;

        var targets = GetSelectedEntries();
        if (targets.Count == 0) return;

        var prompt = targets.Count == 1
            ? string.Format(Strings.FileManager_Confirm_Delete_Single, targets[0].Name)
            : string.Format(Strings.FileManager_Confirm_Delete_Multi, targets.Count);

        if (!DialogService.Instance.ConfirmDirect(prompt, Window.GetWindow(this), Strings.FileManager_Confirm_Delete_Title))
            return;

        var errors = await RunShellBatchAsync(targets, entry =>
        {
            var flag = entry.IsDirectory ? "-rf" : "-f";
            return $"shell rm {flag} -- {AdbArg.ForShell(entry.FullPath)}";
        });

        TryReportBatchErrors(errors, Strings.FileManager_Error_Delete);
        await RefreshAsync();
    }

    [RelayCommand]
    private void CutSelection() => SetClipboard(ClipboardMode.Cut);

    [RelayCommand]
    private void CopySelection() => SetClipboard(ClipboardMode.Copy);

    [RelayCommand]
    private void ClearClipboard()
    {
        _clipboardEntries.Clear();
        _clipboardMode = ClipboardMode.None;
        ClipboardSummary = "";
        HasClipboard = false;
    }

    [RelayCommand]
    private async Task PasteAsync()
    {
        if (!EnsureConnected()) return;
        if (_clipboardEntries.Count == 0 || _clipboardMode == ClipboardMode.None) return;

        var verb = _clipboardMode == ClipboardMode.Cut ? "mv" : "cp -r";
        var snapshot = _clipboardEntries.ToArray();

        var errors = await RunShellBatchAsync(snapshot, entry =>
        {
            var destPath = PathUtil.Combine(CurrentPath, entry.Name);
            return string.Equals(destPath, entry.FullPath, StringComparison.Ordinal)
                ? string.Empty
                : $"shell {verb} -- {AdbArg.ForShell(entry.FullPath)} {AdbArg.ForShell(destPath)}";
        });

        if (_clipboardMode == ClipboardMode.Cut)
            ClearClipboardCommand.Execute(null);

        TryReportBatchErrors(errors, Strings.FileManager_Error_Paste);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task UploadAsync()
    {
        if (!EnsureConnected()) return;

        var dialog = new OpenFileDialog
        {
            Title = Strings.FileManager_Dialog_SelectPush,
            Multiselect = true,
            Filter = "All Files|*.*"
        };

        if (dialog.ShowDialog() != true) return;
        await EnqueuePushesAsync(dialog.FileNames, CurrentPath);
    }

    [RelayCommand]
    private async Task PullToFolderAsync()
    {
        if (!EnsureConnected()) return;

        var targets = GetSelectedEntries();
        if (targets.Count == 0) return;

        var dialog = new OpenFolderDialog { Title = Strings.FileManager_Dialog_SelectDestination };
        if (dialog.ShowDialog() != true) return;

        await EnqueuePullsAsync(targets, dialog.FolderName);
    }

    [RelayCommand]
    private async Task OpenSelectionAsync()
    {
        var target = CurrentSingleSelection();
        if (target == null) return;
        await OpenEntryAsync(target);
    }

    [RelayCommand]
    private async Task PreviewSelectionAsync()
    {
        var target = CurrentSingleSelection();
        if (target == null || target.IsDirectory) return;
        if (target.GetPreviewKind() == PreviewKind.None) return;
        await LoadPreviewAsync(target);
    }

    [RelayCommand]
    private void CyclePermissionsDisplay()
    {
        PermissionsDisplayMode = PermissionsDisplayMode switch
        {
            PermissionsDisplayMode.Symbolic => PermissionsDisplayMode.Numeric,
            PermissionsDisplayMode.Numeric => PermissionsDisplayMode.Simplified,
            _ => PermissionsDisplayMode.Symbolic
        };
    }

    [RelayCommand]
    private async Task ShowPropertiesAsync()
    {
        var target = CurrentSingleSelection();
        if (target == null) return;

        var dash = Strings.FileManager_Common_Dash;
        var typeText = target.IsDirectory
            ? Strings.FileManager_Properties_TypeDirectory
            : target.IsSymlink
                ? Strings.FileManager_Properties_TypeSymlink
                : Strings.FileManager_Properties_TypeFile;

        long sizeBytes;
        if (target.IsDirectory)
        {
            sizeBytes = target.Size > 0
                ? target.Size
                : await DirectorySizeKbAsync(target.FullPath, CancellationToken.None).ConfigureAwait(true) * 1024L;
        }
        else
        {
            sizeBytes = target.Size;
        }

        var sizeText = target.IsDirectory && sizeBytes <= 0
            ? dash
            : $"{UIHelpers.FormatBytes(sizeBytes)} ({sizeBytes:N0} {Strings.FileManager_Properties_BytesUnit})";

        var info = new StringBuilder()
            .AppendLine($"{Strings.FileManager_Properties_Name,-15} {target.Name}")
            .AppendLine($"{Strings.FileManager_Properties_Path,-15} {target.FullPath}")
            .AppendLine($"{Strings.FileManager_Properties_Type,-15} {typeText}")
            .AppendLine($"{Strings.FileManager_Properties_Size,-15} {sizeText}")
            .AppendLine($"{Strings.FileManager_Properties_Permissions,-15} {target.Permissions}  ({PermissionsDescriber.ToOctal(target.Permissions)})")
            .AppendLine($"{string.Empty,-15} {PermissionsDescriber.Describe(target.Permissions)}")
            .AppendLine($"{Strings.FileManager_Properties_OwnerGroup,-15} {target.Owner} : {target.Group}")
            .AppendLine($"{Strings.FileManager_Properties_Modified,-15} {target.ModifiedDisplay}");

        if (target.IsSymlink && !string.IsNullOrEmpty(target.LinkTarget))
            info.AppendLine($"{Strings.FileManager_Properties_LinkTarget,-15} {target.LinkTarget}");

        DialogService.Instance.ShowInfoDirect(Strings.FileManager_Properties_Title, info.ToString(), Window.GetWindow(this));
    }

    [RelayCommand]
    private void CancelAllTransfers()
    {
        foreach (var transfer in Transfers.ToArray())
            transfer.Cancel();
    }

    [RelayCommand]
    private void ClearCompletedTransfers()
    {
        var done = Transfers.Where(t => t.IsTerminal).ToArray();
        foreach (var t in done)
            Transfers.Remove(t);
    }

    [RelayCommand]
    private void ClosePreview()
    {
        _previewCts?.Cancel();
        IsPreviewOpen = false;
        IsPreviewLoading = false;
        PreviewImage = null;
        PreviewText = null;
        PreviewError = null;
        PreviewKind = PreviewKind.None;
        PreviewName = null;
        PreviewSubtitle = null;
    }

    [RelayCommand]
    private void ConfirmPrompt() => _promptCompletion?.TrySetResult(PromptValue);

    [RelayCommand]
    private void CancelPrompt() => _promptCompletion?.TrySetResult(null);

    private void OnPathChipClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path })
            NavigateCommand.Execute(path);
    }

    private async void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        try
        {
            if (e.NewValue is TreeNode node && !node.IsPlaceholder && !string.IsNullOrEmpty(node.Path))
                await NavigateAsync(node.Path);
        }
        catch (Exception ex)
        {
            LogException(ex);
        }
    }

    private async void OnTreeItemExpanded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (e.OriginalSource is not TreeViewItem { DataContext: TreeNode node }) return;
            if (node.IsLoaded || !node.CanExpand) return;

            if (!DeviceManager.Instance.IsConnected)
            {
                node.Children.Clear();
                return;
            }

            var output = await AdbExecutor.ExecuteCommandAsync(
                $"shell ls -la {AdbArg.ForShell(node.Path)}");

            if (IsAdbDeviceError(output) || IsListingFailure(output))
            {
                node.Children.Clear();
                return;
            }

            node.Children.Clear();
            foreach (var entry in LsParser.Parse(output, node.Path)
                .Where(en => en.IsDirectory || (en.IsSymlink && !string.IsNullOrEmpty(en.LinkTarget)))
                .OrderBy(en => en.Name, StringComparer.OrdinalIgnoreCase))
            {
                node.Children.Add(TreeNode.CreateExpandable(entry.Name, entry.FullPath));
            }

            node.IsLoaded = true;
        }
        catch (Exception ex)
        {
            LogException(ex);
        }
    }

    private void OnPathBarKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                GoToInputPathCommand.Execute(null);
                break;
            case Key.Escape:
                e.Handled = true;
                PathInput = CurrentPath;
                Keyboard.ClearFocus();
                break;
        }
    }

    private void OnPromptKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                ConfirmPromptCommand.Execute(null);
                break;
            case Key.Escape:
                e.Handled = true;
                CancelPromptCommand.Execute(null);
                break;
        }
    }

    private async void OnGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (e.OriginalSource is FrameworkElement { DataContext: RemoteEntry entry })
                await OpenEntryAsync(entry);
        }
        catch (Exception ex)
        {
            LogException(ex);
        }
    }

    private void OnGridSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not DataGrid grid) return;

        SelectedCount = grid.SelectedItems.Count;
        long total = 0;
        foreach (var item in grid.SelectedItems)
            if (item is RemoteEntry entry) total += entry.Size;
        SelectedSize = total;
        UpdateStatusMessage();
    }

    private void OnGridMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;

        _gridDragOrigin = e.GetPosition(grid);
        _gridDragArmed = grid.SelectedItems.Count > 0;
    }

    private async void OnGridMouseMove(object sender, MouseEventArgs e)
    {
        try
        {
            if (!_gridDragArmed) return;
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _gridDragArmed = false;
                return;
            }

            if (sender is not DataGrid grid) return;

            var pos = e.GetPosition(grid);
            if (Math.Abs(pos.X - _gridDragOrigin.X) < DragMovementThreshold &&
                Math.Abs(pos.Y - _gridDragOrigin.Y) < DragMovementThreshold)
                return;

            _gridDragArmed = false;
            await BeginDragOutAsync(grid);
        }
        catch (Exception ex)
        {
            LogException(ex);
        }
    }

    private void OnGridDragOver(object sender, DragEventArgs e) => UpdateDragHover(e, true);

    private void OnGridDragLeave(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement fe) { IsDragHovering = false; return; }

        var pos = e.GetPosition(fe);
        if (pos.X < 0 || pos.Y < 0 || pos.X > fe.ActualWidth || pos.Y > fe.ActualHeight)
            IsDragHovering = false;
    }

    private async void OnGridDrop(object sender, DragEventArgs e)
    {
        try
        {
            IsDragHovering = false;
            if (!EnsureConnected()) return;
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            await EnqueuePushesAsync(files, CurrentPath);
            e.Handled = true;
        }
        catch (Exception ex)
        {
            LogException(ex);
        }
    }

    private void UpdateDragHover(DragEventArgs e, bool entering)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            IsDragHovering = entering;
        }
        else
        {
            e.Effects = DragDropEffects.None;
            IsDragHovering = false;
        }
        e.Handled = true;
    }

    private async Task OpenEntryAsync(RemoteEntry entry)
    {
        if (entry.IsDirectory)
        {
            await NavigateAsync(entry.FullPath);
            return;
        }

        if (entry.IsSymlink && !string.IsNullOrEmpty(entry.LinkTarget))
        {
            await NavigateAsync(PathUtil.ResolveSymlink(entry.FullPath, entry.LinkTarget!));
            return;
        }

        await OpenFileWithDefaultAppAsync(entry);
    }

    private async Task OpenFileWithDefaultAppAsync(RemoteEntry entry)
    {
        if (!EnsureConnected()) return;

        if (entry.Size > OpenWithAppLargeFileThreshold)
        {
            var prompt = string.Format(
                Strings.FileManager_Open_Confirm_LargeFile,
                entry.Name, UIHelpers.FormatBytes(entry.Size));
            if (!DialogService.Instance.ConfirmDirect(prompt, Window.GetWindow(this), Strings.FileManager_Open_Confirm_Title))
                return;
        }

        var previousStatus = StatusMessage;
        StatusMessage = string.Format(Strings.FileManager_Open_Status_Preparing, entry.Name);

        try
        {
            var localPath = await PullToCacheAsync(entry, "Open", CancellationToken.None);
            if (localPath is null)
            {
                ReportShellError(Strings.FileManager_Open_Failed_Title, Strings.FileManager_Open_Failed_Pull);
                return;
            }

            Process.Start(new ProcessStartInfo(localPath) { UseShellExecute = true });
            AdbLogger.Instance.LogInfo("FileManager", "Opened with default app", entry.FullPath);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            var message = ex.NativeErrorCode == ShellAssocNotFoundCode
                ? Strings.FileManager_Open_Failed_NoApp
                : string.Format(Strings.FileManager_Open_Failed_Generic, ex.Message);
            ReportShellError(Strings.FileManager_Open_Failed_Title, message);
            if (ex.NativeErrorCode != ShellAssocNotFoundCode) LogException(ex);
        }
        catch (Exception ex)
        {
            ReportShellError(Strings.FileManager_Open_Failed_Title, string.Format(Strings.FileManager_Open_Failed_Generic, ex.Message));
            LogException(ex);
        }
        finally
        {
            StatusMessage = previousStatus;
            UpdateStatusMessage();
        }
    }

    private async Task LoadPreviewAsync(RemoteEntry entry)
    {
        DisposeCts(ref _previewCts);
        var cts = new CancellationTokenSource();
        _previewCts = cts;

        PreviewName = entry.Name;
        PreviewSubtitle = $"{UIHelpers.FormatBytes(entry.Size)}{Strings.FileManager_Status_Separator}{entry.Permissions}";
        PreviewError = null;
        PreviewImage = null;
        PreviewText = null;
        PreviewKind = entry.GetPreviewKind();
        IsPreviewOpen = true;
        IsPreviewLoading = true;

        try
        {
            if (PreviewKind == PreviewKind.None)
            {
                PreviewError = Strings.FileManager_Preview_NotAvailable;
                return;
            }

            if (entry.Size > PreviewSizeCeiling)
            {
                PreviewError = string.Format(Strings.FileManager_Preview_TooLarge, UIHelpers.FormatBytes(entry.Size));
                return;
            }

            var localPath = await PullToCacheAsync(entry, "Preview", cts.Token);
            if (cts.Token.IsCancellationRequested) return;
            if (localPath == null || !File.Exists(localPath))
            {
                PreviewError = Strings.FileManager_Preview_FetchFailed;
                return;
            }

            if (PreviewKind == PreviewKind.Image)
                PreviewImage = LoadImage(localPath);
            else
                PreviewText = await ReadTextSampleAsync(localPath, cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            PreviewError = string.Format(Strings.FileManager_Preview_Error, ex.Message);
        }
        finally
        {
            if (ReferenceEquals(cts, _previewCts))
                IsPreviewLoading = false;
        }
    }

    private static BitmapImage LoadImage(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bmp.StreamSource = stream;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private static async Task<string> ReadTextSampleAsync(string path, CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var length = (int)Math.Min(fs.Length, PreviewTextByteLimit);
        var buffer = new byte[length];
        var read = await fs.ReadAsync(buffer.AsMemory(0, length), ct);
        var text = Encoding.UTF8.GetString(buffer, 0, read);
        return fs.Length > PreviewTextByteLimit
            ? text + Environment.NewLine + string.Format(Strings.FileManager_Preview_TextTruncated, UIHelpers.FormatBytes(PreviewTextByteLimit))
            : text;
    }

    private async Task<string?> PullToCacheAsync(RemoteEntry entry, string subfolder, CancellationToken ct)
    {
        var dir = Path.Combine(TempBase, subfolder);
        Directory.CreateDirectory(dir);
        var localPath = Path.Combine(dir, MakeSafeLocalName(entry.Name));

        try
        {
            if (Directory.Exists(localPath)) Directory.Delete(localPath, true);
            else if (File.Exists(localPath)) File.Delete(localPath);
        }
        catch { }

        var output = await AdbExecutor.ExecuteCommandAsync(
            $"pull {AdbArg.ForPullPush(entry.FullPath)} {AdbArg.ForPullPush(localPath)}", ct);

        return !File.Exists(localPath) || ContainsAdbError(output)
            ? null
            : localPath;
    }

    private async Task BeginDragOutAsync(DataGrid grid)
    {
        var selected = grid.SelectedItems.Cast<RemoteEntry>().ToList();
        if (selected.Count == 0) return;

        if (selected.Any(s => s.IsDirectory))
        {
            StatusMessage = Strings.FileManager_DragOut_NoFolders;
            return;
        }

        var totalSize = selected.Sum(s => s.Size);
        if (totalSize > DragOutSizeCeiling)
        {
            StatusMessage = string.Format(Strings.FileManager_DragOut_TooLarge, UIHelpers.FormatBytes(totalSize));
            return;
        }

        var stageDir = Path.Combine(TempBase, "Drag", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(stageDir);

        var localPaths = new List<string>();
        foreach (var entry in selected)
        {
            var local = Path.Combine(stageDir, MakeSafeLocalName(entry.Name));
            var output = await AdbExecutor.ExecuteCommandAsync(
                $"pull {AdbArg.ForPullPush(entry.FullPath)} {AdbArg.ForPullPush(local)}");
            if (File.Exists(local) && !ContainsAdbError(output))
                localPaths.Add(local);
        }

        if (localPaths.Count == 0)
        {
            StatusMessage = Strings.FileManager_DragOut_PullFailed;
            return;
        }

        try
        {
            var data = new DataObject(DataFormats.FileDrop, localPaths.ToArray());
            DragDrop.DoDragDrop(grid, data, DragDropEffects.Copy);
        }
        catch (Exception ex)
        {
            LogException(ex);
        }
    }

    private async Task EnqueuePushesAsync(IEnumerable<string> localPaths, string remoteDir)
    {
        foreach (var local in localPaths)
        {
            if (!File.Exists(local) && !Directory.Exists(local)) continue;

            long size = 0;
            var isDirectory = Directory.Exists(local);
            try { size = isDirectory ? DirectorySize(local) : new FileInfo(local).Length; }
            catch (Exception ex) { LogException(ex); }

            var name = Path.GetFileName(local.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var item = new TransferItem
            {
                LocalPath = local,
                RemotePath = PathUtil.Combine(remoteDir, name),
                Direction = TransferDirection.Push,
                TotalBytes = size,
                DisplayName = name,
                IsDirectory = isDirectory
            };
            Transfers.Add(item);
        }
        await RunPendingTransfersAsync();
    }

    private async Task EnqueuePullsAsync(IEnumerable<RemoteEntry> entries, string localDir)
    {
        foreach (var entry in entries)
        {
            var item = new TransferItem
            {
                LocalPath = Path.Combine(localDir, MakeSafeLocalName(entry.Name)),
                RemotePath = entry.FullPath,
                Direction = TransferDirection.Pull,
                TotalBytes = entry.Size,
                DisplayName = entry.Name,
                IsDirectory = entry.IsDirectory
            };
            Transfers.Add(item);
        }
        await RunPendingTransfersAsync();
    }

    private Task RunPendingTransfersAsync() =>
        Task.WhenAll(Transfers.Where(t => t.Status == TransferStatus.Pending)
                              .Select(ExecuteTransferAsync));

    private async Task ExecuteTransferAsync(TransferItem item)
    {
        if (item.Status != TransferStatus.Pending) return;
        item.Status = TransferStatus.Queued;

        var gateHeld = false;
        try
        {
            try
            {
                await _transferGate.WaitAsync(item.TokenSource.Token);
                gateHeld = true;
            }
            catch (OperationCanceledException)
            {
                item.Status = TransferStatus.Canceled;
                RecomputeTransferAggregate();
                return;
            }

            item.Status = TransferStatus.Active;
            RecomputeTransferAggregate();

            var command = item.Direction == TransferDirection.Push
                ? $"push {AdbArg.ForPullPush(item.LocalPath)} {AdbArg.ForPullPush(item.RemotePath)}"
                : $"pull -a {AdbArg.ForPullPush(item.RemotePath)} {AdbArg.ForPullPush(item.LocalPath)}";

            var output = await AdbExecutor.ExecuteCommandAsync(command, item.TokenSource.Token, line =>
            {
                var match = ProgressPattern().Match(line);
                if (match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p))
                    Dispatcher.BeginInvoke(() => { item.Progress = p; RecomputeTransferAggregate(); });
            });

            if (item.TokenSource.IsCancellationRequested)
            {
                item.Status = TransferStatus.Canceled;
                return;
            }

            if (ContainsAdbError(output))
            {
                item.Status = TransferStatus.Failed;
                item.Detail = ExtractTransferDetail(output);
            }
            else
            {
                item.Progress = 100;
                item.Status = TransferStatus.Completed;
                item.Detail = ExtractTransferDetail(output);
            }

            if (item.Status == TransferStatus.Completed &&
                item.Direction == TransferDirection.Push &&
                string.Equals(PathUtil.Parent(item.RemotePath), CurrentPath, StringComparison.Ordinal))
            {
                await Dispatcher.InvokeAsync(() => RefreshCommand.Execute(null));
            }
        }
        catch (OperationCanceledException)
        {
            item.Status = TransferStatus.Canceled;
        }
        catch (Exception ex)
        {
            item.Status = TransferStatus.Failed;
            item.Detail = ex.Message;
            LogException(ex);
        }
        finally
        {
            if (gateHeld) _transferGate.Release();
            RecomputeTransferAggregate();
        }
    }

    private void OnTransfersChanged(object? sender, NotifyCollectionChangedEventArgs e) => RecomputeTransferAggregate();

    private void RecomputeTransferAggregate()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(RecomputeTransferAggregate);
            return;
        }

        var active = Transfers.Where(t => t.Status is TransferStatus.Active or TransferStatus.Queued or TransferStatus.Pending).ToList();
        ActiveTransferCount = active.Count;
        HasActiveTransfers = active.Count > 0;
        AggregateProgress = active.Count == 0 ? 0 : active.Average(t => t.Progress);
    }

    private void HandleDeviceStatus(bool connected)
    {
        IsDeviceConnected = connected;
        DeviceName = connected ? DeviceManager.Instance.DeviceName : "";
        DeviceSerial = connected ? DeviceManager.Instance.DeviceSerial : "";
        UpdateStatusMessage();
        if (!connected)
        {
            Entries.Clear();
            EmptyStateMessage = Strings.FileManager_Status_NoDevice;
            StorageSegments.Clear();
            IsStorageAvailable = false;
            StorageBarBrush = null;
            return;
        }
        if (IsLoaded)
        {
            _ = RefreshAsync();
            _ = LoadStorageAsync();
            _ = ProbeBookmarksAsync();
        }
    }

    private void UpdateStatusMessage()
    {
        if (!IsDeviceConnected)
        {
            StatusMessage = Strings.FileManager_Status_Waiting;
            return;
        }

        var parts = new List<string> { string.Format(Strings.FileManager_Status_Items, Entries.Count) };
        if (SelectedCount > 0)
            parts.Add(string.Format(Strings.FileManager_Status_Selected, SelectedCount, UIHelpers.FormatBytes(SelectedSize)));
        StatusMessage = string.Join(Strings.FileManager_Status_Separator, parts);
    }

    private void UpdateBreadcrumbs(string path)
    {
        Breadcrumbs.Clear();
        Breadcrumbs.Add(new PathSegment("/", "/"));
        if (path == "/" || string.IsNullOrEmpty(path)) return;

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var accumulator = "";
        foreach (var part in parts)
        {
            accumulator += "/" + part;
            Breadcrumbs.Add(new PathSegment(part, accumulator));
        }
    }

    private void SetClipboard(ClipboardMode mode)
    {
        var targets = GetSelectedEntries();
        if (targets.Count == 0) return;

        _clipboardEntries.Clear();
        _clipboardEntries.AddRange(targets);
        _clipboardMode = mode;

        ClipboardSummary = string.Format(
            mode == ClipboardMode.Cut ? Strings.FileManager_Clipboard_Cut : Strings.FileManager_Clipboard_Copy,
            targets.Count);
        HasClipboard = true;
    }

    private async Task<string?> PromptAsync(string title, string label, string initial, Func<string, string?>? validator)
    {
        PromptTitle = title;
        PromptLabel = label;
        PromptValue = initial;
        PromptError = null;
        IsPromptOpen = true;

        while (true)
        {
            var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _promptCompletion = tcs;

            _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                PromptInput.Focus();
                PromptInput.SelectAll();
            }));

            var result = await tcs.Task;

            if (result == null)
            {
                IsPromptOpen = false;
                PromptError = null;
                return null;
            }

            var error = validator?.Invoke(result);
            if (error == null)
            {
                IsPromptOpen = false;
                PromptError = null;
                return result;
            }

            PromptError = error;
        }
    }

    private List<RemoteEntry> GetSelectedEntries() =>
        EntriesGrid.SelectedItems.Cast<RemoteEntry>().ToList();

    private RemoteEntry? CurrentSingleSelection() =>
        EntriesGrid.SelectedItems.Count == 1 ? EntriesGrid.SelectedItems[0] as RemoteEntry : null;

    private bool EnsureConnected()
    {
        if (DeviceManager.Instance.IsConnected) return true;
        DialogService.Instance.ShowInfoDirect(
            Strings.FileManager_Validate_NoDevice_Title,
            Strings.FileManager_Validate_NoDevice_Message,
            Window.GetWindow(this));
        return false;
    }

    private void ReportShellError(string title, string message) =>
        DialogService.Instance.ShowInfoDirect(title, ShortError(message), Window.GetWindow(this));

    private async Task<StringBuilder> RunShellBatchAsync(
        IReadOnlyList<RemoteEntry> targets,
        Func<RemoteEntry, string> commandBuilder)
    {
        var errors = new StringBuilder();
        foreach (var entry in targets)
        {
            var command = commandBuilder(entry);
            if (string.IsNullOrEmpty(command)) continue;

            var output = await AdbExecutor.ExecuteCommandAsync(command);
            if (HasShellError(output))
                errors.AppendLine($"{entry.Name}: {output.Trim()}");
        }
        return errors;
    }

    private void TryReportBatchErrors(StringBuilder errors, string title)
    {
        if (errors.Length > 0)
            ReportShellError(title, errors.ToString());
    }

    private static void DisposeCts(ref CancellationTokenSource? cts)
    {
        cts?.Cancel();
        cts?.Dispose();
        cts = null;
    }

    private static void LogException(Exception ex)
    {
        try { AdbLogger.Instance.LogException("FileManager", ex); } catch { }
    }

    private static bool IsAdbDeviceError(string output)
    {
        if (string.IsNullOrEmpty(output)) return false;
        if (output.StartsWith("error:", StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var marker in AdbDeviceErrorMarkers)
            if (output.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static bool IsListingFailure(string output) =>
        PermissionDeniedPattern().IsMatch(output) && !ContainsParseableLine(output);

    private static bool ContainsAdbError(string output) =>
        output.Contains("adb: error", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("error: ", StringComparison.OrdinalIgnoreCase);

    private static bool HasShellError(string output) =>
        !string.IsNullOrWhiteSpace(output) && PermissionDeniedPattern().IsMatch(output);

    private static bool ContainsParseableLine(string output)
    {
        foreach (var line in output.Split('\n'))
        {
            if (line.Length < 11) continue;
            if (line[0] is 'd' or '-' or 'l' or 'c' or 'b' or 's' or 'p') return true;
        }
        return false;
    }

    private static string ShortError(string output)
    {
        var trimmed = output.Trim();
        return trimmed.Length > MaxStatusErrorLength
            ? trimmed[..(MaxStatusErrorLength - 3)] + "..."
            : trimmed;
    }

    private static string MakeSafeLocalName(string name)
    {
        var safe = name;
        foreach (var c in Path.GetInvalidFileNameChars())
            safe = safe.Replace(c, '_');
        safe = safe.Replace('/', '_').Replace('\\', '_');
        if (safe is "." or "..") safe = "_";
        return string.IsNullOrWhiteSpace(safe) ? "file" : safe;
    }

    private static long DirectorySize(string path)
    {
        try
        {
            return new DirectoryInfo(path)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);
        }
        catch { return 0; }
    }

    private static string ExtractTransferDetail(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;
            if (ProgressPattern().IsMatch(line)) continue;
            return line.Length > 160 ? line[..157] + "..." : line;
        }
        return "";
    }

    private static string? FileNameValidator(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Strings.FileManager_Validate_NameEmpty;
        if (name.Contains('/')) return Strings.FileManager_Validate_NameSlash;
        if (name is "." or "..") return Strings.FileManager_Validate_NameReserved;
        if (name.Length > 255) return Strings.FileManager_Validate_NameTooLong;
        return null;
    }

    private static string? PermissionsInputValidator(string mode) =>
        PermissionsDescriber.TryParse(mode, out _, out _, out var error) ? null : error;

    private async Task LoadStorageAsync()
    {
        DisposeCts(ref _storageCts);
        var cts = new CancellationTokenSource();
        _storageCts = cts;
        IsStorageLoading = true;

        try
        {
            if (!DeviceManager.Instance.IsConnected)
            {
                ApplyStorageUnavailable();
                return;
            }

            var dfOutput = await AdbExecutor.ExecuteCommandAsync("shell df /sdcard | tail -1", cts.Token);
            if (cts.Token.IsCancellationRequested) return;

            if (!TryParseDfOutput(dfOutput, out var totalKb, out _, out var freeKb) || totalKb <= 0)
            {
                ApplyStorageUnavailable();
                return;
            }

            var downloadsTask = DirectorySizeKbAsync("/sdcard/Download",        cts.Token);
            var dcimTask      = DirectorySizeKbAsync("/sdcard/DCIM",            cts.Token);
            var picturesTask  = DirectorySizeKbAsync("/sdcard/Pictures",        cts.Token);
            var moviesTask    = DirectorySizeKbAsync("/sdcard/Movies",          cts.Token);
            var musicTask     = DirectorySizeKbAsync("/sdcard/Music",           cts.Token);
            var dataTask      = DirectorySizeKbAsync("/sdcard/Android/data",    cts.Token);
            var obbTask       = DirectorySizeKbAsync("/sdcard/Android/obb",     cts.Token);

            await Task.WhenAll(downloadsTask, dcimTask, picturesTask, moviesTask, musicTask, dataTask, obbTask);
            if (cts.Token.IsCancellationRequested) return;

            long totalBytes    = totalKb * 1024L;
            long freeBytes     = freeKb  * 1024L;
            long usedBytes     = totalBytes - freeBytes;
            long downloads     = downloadsTask.Result * 1024L;
            long media         = (dcimTask.Result + picturesTask.Result + moviesTask.Result + musicTask.Result) * 1024L;
            long apps          = (dataTask.Result + obbTask.Result) * 1024L;
            long accounted     = downloads + media + apps;
            long systemBytes   = Math.Max(0, usedBytes - accounted);

            var segments = new List<StorageSegment>
            {
                new(Strings.FileManager_Storage_Apps,      apps,        LeadingColor(AppBrushes.GradientApk)),
                new(Strings.FileManager_Storage_Media,     media,       LeadingColor(AppBrushes.GradientApkm)),
                new(Strings.FileManager_Storage_Downloads, downloads,   LeadingColor(AppBrushes.GradientCyan)),
                new(Strings.FileManager_Storage_System,    systemBytes, LeadingColor(AppBrushes.GradientNavy)),
                new(Strings.FileManager_Storage_Free,      freeBytes,   Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF))
            };

            ApplyStorage(segments, usedBytes, totalBytes);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            LogException(ex);
            ApplyStorageUnavailable();
        }
        finally
        {
            if (ReferenceEquals(cts, _storageCts))
                IsStorageLoading = false;
        }
    }

    private async Task<long> DirectorySizeKbAsync(string remotePath, CancellationToken ct)
    {
        try
        {
            var output = await AdbExecutor.ExecuteCommandAsync(
                $"shell du -sk -- {AdbArg.ForShell(remotePath)} 2>/dev/null | awk '{{print $1}}'", ct);
            var token = output?.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var kb) ? kb : 0;
        }
        catch { return 0; }
    }

    private void StartDirectorySizesAsync(IList<RemoteEntry> dirs, CancellationToken ct)
    {
        var dirList = dirs.Where(e => e.IsDirectory).ToList();
        if (dirList.Count == 0) return;
        foreach (var dir in dirList)
            _ = FetchAndApplyDirSizeAsync(dir, ct);
    }

    private async Task FetchAndApplyDirSizeAsync(RemoteEntry dir, CancellationToken ct)
    {
        var kb = await DirectorySizeKbAsync(dir.FullPath, ct).ConfigureAwait(false);
        if (ct.IsCancellationRequested || kb <= 0) return;
        var bytes = kb * 1024L;
        await Dispatcher.InvokeAsync(() =>
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].FullPath == dir.FullPath)
                {
                    Entries[i] = Entries[i] with { Size = bytes };
                    break;
                }
            }
        });
    }

    private static bool TryParseDfOutput(string output, out long totalKb, out long usedKb, out long freeKb)
    {
        totalKb = usedKb = freeKb = 0;
        if (string.IsNullOrWhiteSpace(output)) return false;

        var parts = output.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4) return false;
        if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out totalKb)) return false;
        if (!long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out usedKb))  return false;
        if (!long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out freeKb))  return false;
        return true;
    }

    private void ApplyStorageUnavailable()
    {
        StorageSegments.Clear();
        StorageBarBrush = null;
        StorageTotalBytes = 0;
        StorageUsedBytes = 0;
        StorageSummaryText = "";
        IsStorageAvailable = false;
    }

    private void ApplyStorage(IReadOnlyList<StorageSegment> segments, long used, long total)
    {
        StorageSegments.Clear();
        foreach (var s in segments)
            StorageSegments.Add(s);

        StorageTotalBytes = total;
        StorageUsedBytes = used;
        StorageSummaryText = string.Format(
            Strings.FileManager_Storage_TotalUsed,
            UIHelpers.FormatBytes(used),
            UIHelpers.FormatBytes(total));
        StorageBarBrush = BuildSegmentedBarBrush(segments, total);
        IsStorageAvailable = true;
    }

    private static Color LeadingColor(Brush brush)
    {
        if (brush is GradientBrush gb && gb.GradientStops.Count > 0)
            return gb.GradientStops[0].Color;
        if (brush is SolidColorBrush sb)
            return sb.Color;
        return Color.FromArgb(0xFF, 0x80, 0x80, 0x80);
    }

    private static LinearGradientBrush BuildSegmentedBarBrush(IReadOnlyList<StorageSegment> segments, long total)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint   = new Point(1, 0.5)
        };

        double cursor = 0;
        foreach (var seg in segments)
        {
            if (seg.Bytes <= 0) continue;
            double width = (double)seg.Bytes / total;
            if (width <= 0) continue;

            brush.GradientStops.Add(new GradientStop(seg.Color, cursor));
            cursor += width;
            if (cursor > 1) cursor = 1;
            brush.GradientStops.Add(new GradientStop(seg.Color, cursor));
        }

        brush.Freeze();
        return brush;
    }
}

public enum PermissionsDisplayMode
{
    Symbolic,
    Numeric,
    Simplified
}

public enum TransferDirection { Push, Pull }
public enum TransferStatus { Pending, Queued, Active, Completed, Failed, Canceled }
public enum PreviewKind { None, Image, Text }
public enum ClipboardMode { None, Cut, Copy }

public sealed record Bookmark(string Path, string Name, string Icon, Brush Brush);
public sealed record PathSegment(string Label, string FullPath);

public sealed record StorageSegment(string Label, long Bytes, Color Color)
{
    public Brush ColorBrush
    {
        get
        {
            var brush = new SolidColorBrush(Color);
            brush.Freeze();
            return brush;
        }
    }
    public string SizeDisplay => UIHelpers.FormatBytes(Bytes);
}

public sealed record RemoteEntry
{
    public string Name { get; init; } = "";
    public string FullPath { get; init; } = "";
    public string Permissions { get; init; } = "----------";
    public int HardLinks { get; init; }
    public string Owner { get; init; } = "";
    public string Group { get; init; } = "";
    public long Size { get; init; }
    public DateTime Modified { get; init; }
    public bool IsDirectory { get; init; }
    public bool IsSymlink { get; init; }
    public string? LinkTarget { get; init; }

    public string Extension  => IsDirectory ? "" : System.IO.Path.GetExtension(Name).ToLowerInvariant();
    public string TypeKey    => RemoteEntryClassifier.Classify(this);
    public string TypeIcon   => RemoteEntryClassifier.IconFor(TypeKey);
    public Brush  TypeBrush  => RemoteEntryClassifier.BrushFor(TypeKey);

    public ImageSource? TypeImage    => AppIcons.Get(TypeKey);
    public bool         HasTypeImage => TypeImage is not null;

    public string SizeDisplay => IsDirectory
        ? (Size > 0 ? UIHelpers.FormatBytes(Size) : Strings.FileManager_Common_Dash)
        : UIHelpers.FormatBytes(Size);
    public string ModifiedDisplay => Modified == DateTime.MinValue
        ? Strings.FileManager_Common_Dash
        : Modified.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    public string OwnerDisplay  => string.IsNullOrEmpty(Group) ? Owner : $"{Owner} : {Group}";
    public string SymlinkSuffix => IsSymlink && !string.IsNullOrEmpty(LinkTarget) ? " → " + LinkTarget : "";

    public PreviewKind GetPreviewKind() => Extension switch
    {
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" => PreviewKind.Image,
        ".txt" or ".json" or ".xml" or ".log" or ".md"
            or ".ini" or ".conf" or ".properties" or ".yml" or ".yaml" or ".csv" => PreviewKind.Text,
        _ => PreviewKind.None
    };
}

public sealed partial class TransferItem : ObservableObject
{
    public string LocalPath { get; init; } = "";
    public string RemotePath { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public TransferDirection Direction { get; init; }
    public long TotalBytes { get; init; }
    public bool IsDirectory { get; init; }
    public CancellationTokenSource TokenSource { get; } = new();

    [ObservableProperty][NotifyPropertyChangedFor(nameof(ProgressDisplay))]
    private double _progress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(StatusBrush), nameof(IsTerminal), nameof(IsActive), nameof(ProgressDisplay))]
    private TransferStatus _status = TransferStatus.Pending;

    [ObservableProperty] private string? _detail;

    public string DirectionIcon => Direction == TransferDirection.Push ? "" : "";
    public Brush DirectionBrush => Direction == TransferDirection.Push
        ? AppBrushes.GradientApk
        : AppBrushes.GradientGreen;
    public string DirectionLabel => Direction == TransferDirection.Push
        ? Strings.FileManager_Transfer_Direction_Push
        : Strings.FileManager_Transfer_Direction_Pull;
    public string SizeDisplay => UIHelpers.FormatBytes(TotalBytes);
    public string ProgressDisplay => Status switch
    {
        TransferStatus.Pending => Strings.FileManager_Transfer_Status_Pending,
        TransferStatus.Queued => Strings.FileManager_Transfer_Status_Queued,
        TransferStatus.Active => $"{Progress:F0}%",
        TransferStatus.Completed => Strings.FileManager_Transfer_Status_Done,
        TransferStatus.Failed => Strings.FileManager_Transfer_Status_Failed,
        TransferStatus.Canceled => Strings.FileManager_Transfer_Status_Canceled,
        _ => ""
    };
    public string StatusText => ProgressDisplay;
    public Brush StatusBrush => Status switch
    {
        TransferStatus.Pending or TransferStatus.Queued => AppBrushes.Pending,
        TransferStatus.Active => AppBrushes.Installing,
        TransferStatus.Completed => AppBrushes.Success,
        TransferStatus.Failed => AppBrushes.Failed,
        TransferStatus.Canceled => AppBrushes.Pending,
        _ => AppBrushes.Pending
    };
    public bool IsTerminal => Status is TransferStatus.Completed or TransferStatus.Failed or TransferStatus.Canceled;
    public bool IsActive => Status == TransferStatus.Active;

    public void Cancel()
    {
        try { TokenSource.Cancel(); } catch { }
        if (Status is TransferStatus.Pending or TransferStatus.Queued)
            Status = TransferStatus.Canceled;
    }
}

public sealed partial class TreeNode : ObservableObject
{
    public string Path        { get; init; } = "";
    public string TypeKey     { get; init; } = "folder";
    public bool   CanExpand   { get; init; }
    public bool   IsPlaceholder { get; init; }
    public string Icon        { get; init; } = "";

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private bool _isLoaded;
    [ObservableProperty] private bool _isExpanded;

    public ObservableCollection<TreeNode> Children { get; } = new();

    public ImageSource? NodeImage    => AppIcons.Get(TypeKey);
    public bool         HasNodeImage => NodeImage is not null;

    public static TreeNode CreateExpandable(string name, string path)
    {
        string icon, typeKey;

        if      (path == "/")              { icon = ""; typeKey = "root"; }
        else if (path.StartsWith("/data")) { icon = ""; typeKey = "data"; }
        else                               { icon = ""; typeKey = "folder"; }

        var node = new TreeNode { Name = name, Path = path, CanExpand = true, Icon = icon, TypeKey = typeKey };
        node.Children.Add(new TreeNode { Name = Strings.FileManager_Tree_Loading, IsPlaceholder = true, Icon = "", TypeKey = "loading" });
        return node;
    }
}

public sealed class PermissionsDisplayConverter : IMultiValueConverter
{
    public static PermissionsDisplayConverter Instance { get; } = new();

    private PermissionsDisplayConverter() { }

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values is null || values.Length < 2) return string.Empty;
        var symbolic = values[0] as string ?? string.Empty;
        if (string.IsNullOrEmpty(symbolic)) return string.Empty;

        var mode = values[1] is PermissionsDisplayMode m ? m : PermissionsDisplayMode.Symbolic;

        return mode switch
        {
            PermissionsDisplayMode.Numeric => PermissionsDescriber.ToOctal(symbolic),
            PermissionsDisplayMode.Simplified => PermissionsDescriber.Describe(symbolic),
            _ => PermissionsDescriber.ToSymbolic(symbolic)
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

internal static partial class PathUtil
{
    [GeneratedRegex(@"/{2,}")]
    private static partial Regex MultiSlashPattern();

    public static string Combine(string parent, string name)
    {
        if (string.IsNullOrEmpty(parent)) return "/" + name;
        return parent == "/" ? "/" + name : parent + "/" + name;
    }

    public static string Parent(string path)
    {
        if (string.IsNullOrEmpty(path) || path == "/") return "/";
        var lastSlash = path.LastIndexOf('/');
        return lastSlash <= 0 ? "/" : path.Substring(0, lastSlash);
    }

    public static string Normalize(string p)
    {
        if (string.IsNullOrWhiteSpace(p)) return "/";
        var s = p.Trim().Replace('\\', '/');
        if (!s.StartsWith('/')) s = "/" + s;
        s = MultiSlashPattern().Replace(s, "/");
        return s.Length > 1 && s.EndsWith('/') ? s.TrimEnd('/') : s;
    }

    public static string ResolveSymlink(string symlinkPath, string target) =>
        target.StartsWith('/') ? Normalize(target) : Normalize(Combine(Parent(symlinkPath), target));
}

internal static class AdbArg
{
    public static string ForPullPush(string path) =>
        "\"" + path.Replace("\"", "\\\"") + "\"";

    public static string ForShell(string path)
    {
        var noQuotes = path.Replace("\"", string.Empty);
        return "\"'" + noQuotes.Replace("'", "'\\''") + "'\"";
    }
}

internal static partial class LsParser
{
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$")]
    private static partial Regex IsoDatePattern();

    [GeneratedRegex(@"^[A-Z][a-z]{2}$")]
    private static partial Regex MonthPattern();

    [GeneratedRegex(@"^\d{1,2}:\d{2}")]
    private static partial Regex TimePattern();
    private static readonly string[] LegacyFormats =
    {
        "MMM d HH:mm yyyy", "MMM d yyyy", "MMM dd HH:mm yyyy", "MMM dd yyyy"
    };

    public static IEnumerable<RemoteEntry> Parse(string output, string parent)
    {
        if (string.IsNullOrEmpty(output)) yield break;

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length < 11) continue;
            if (line.StartsWith("total ", StringComparison.Ordinal)) continue;
            if (line.StartsWith("ls:", StringComparison.Ordinal)) continue;

            var entry = TryParse(line, parent);
            if (entry != null) yield return entry;
        }
    }

    private static RemoteEntry? TryParse(string line, string parent)
    {
        var perms = line.Substring(0, 10);
        if (!IsValidPerms(perms)) return null;

        var rest = line.Substring(10).TrimStart();
        var tokens = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 7) return null;

        if (!int.TryParse(tokens[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var links)) return null;
        var owner = tokens[1];
        var group = tokens[2];
        if (!long.TryParse(tokens[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var size)) return null;

        int dateConsumed;
        DateTime modified = DateTime.MinValue;

        if (IsoDatePattern().IsMatch(tokens[4]))
        {
            if (tokens.Length < 7) return null;
            dateConsumed = 2;
            DateTime.TryParse(
                $"{tokens[4]} {tokens[5]}", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out modified);
        }
        else if (MonthPattern().IsMatch(tokens[4]))
        {
            if (tokens.Length < 8) return null;
            dateConsumed = 3;
            modified = ParseLegacyDate(tokens[4], tokens[5], tokens[6]);
        }
        else
        {
            return null;
        }

        var dateTokenEndIndex = 4 + dateConsumed;
        if (tokens.Length <= dateTokenEndIndex) return null;

        var nameAndLink = SliceAfterTokens(rest, dateTokenEndIndex);
        if (string.IsNullOrEmpty(nameAndLink)) return null;

        var isSymlink = perms[0] == 'l';
        string? linkTarget = null;
        string name = nameAndLink;

        if (isSymlink)
        {
            var arrowIdx = nameAndLink.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrowIdx >= 0)
            {
                linkTarget = nameAndLink.Substring(arrowIdx + 4);
                name = nameAndLink.Substring(0, arrowIdx);
            }
        }

        if (name is "." or "..") return null;

        return new RemoteEntry
        {
            Name = name,
            FullPath = PathUtil.Combine(parent, name),
            Permissions = perms,
            HardLinks = links,
            Owner = owner,
            Group = group,
            Size = size,
            Modified = modified,
            IsDirectory = perms[0] == 'd',
            IsSymlink = isSymlink,
            LinkTarget = linkTarget
        };
    }

    private static string SliceAfterTokens(string rest, int tokensToSkip)
    {
        int i = 0, tokenIndex = 0;
        while (i < rest.Length && tokenIndex < tokensToSkip)
        {
            while (i < rest.Length && rest[i] == ' ') i++;
            while (i < rest.Length && rest[i] != ' ') i++;
            tokenIndex++;
        }
        while (i < rest.Length && rest[i] == ' ') i++;
        return i >= rest.Length ? string.Empty : rest[i..];
    }

    private static bool IsValidPerms(string perms)
    {
        if (perms.Length != 10) return false;
        return perms[0] is 'd' or '-' or 'l' or 'c' or 'b' or 's' or 'p' or '?';
    }

    private static DateTime ParseLegacyDate(string month, string day, string yearOrTime)
    {
        var fallbackYear = DateTime.Now.Year.ToString(CultureInfo.InvariantCulture);
        var combined = TimePattern().IsMatch(yearOrTime)
            ? $"{month} {day} {yearOrTime} {fallbackYear}"
            : $"{month} {day} {yearOrTime}";

        DateTime.TryParseExact(
            combined, LegacyFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal, out var dt);
        return dt;
    }
}

internal static class EntryComparer
{
    public static IComparer<RemoteEntry> Default { get; } = Comparer<RemoteEntry>.Create((a, b) =>
    {
        if (a.IsDirectory != b.IsDirectory) return a.IsDirectory ? -1 : 1;
        return StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
    });
}

internal static class RemoteEntryClassifier
{
    private static Brush? _folderBrush;

    public static string Classify(RemoteEntry e)
    {
        if (e.IsDirectory) return "folder";
        if (e.IsSymlink) return "link";
        return e.Extension switch
        {
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".heic" or ".heif" => "image",
            ".mp4" or ".mkv" or ".avi" or ".webm" or ".mov" or ".3gp" or ".m4v" => "video",
            ".mp3" or ".ogg" or ".wav" or ".m4a" or ".flac" or ".aac" or ".opus" => "audio",
            ".txt" or ".log" or ".md" => "text",
            ".json" or ".xml" or ".yml" or ".yaml" or ".ini" or ".conf" or ".properties" or ".csv" => "config",
            ".zip" or ".tar" or ".gz" or ".7z" or ".rar" or ".xz" or ".bz2" => "archive",
            ".apk" or ".apks" or ".xapk" or ".dex" or ".odex" => "android",
            ".html" or ".htm" or ".css" or ".js" => "web",
            ".sh" or ".bash" => "script",
            ".db" or ".sqlite" or ".sqlite3" => "db",
            ".pdf" => "pdf",
            _ => "file"
        };
    }

    public static string IconFor(string key) => key switch
    {
        "folder"  => "",
        "link"    => "",
        "image"   => "",
        "video"   => "",
        "audio"   => "",
        "text"    => "",
        "config"  => "",
        "archive" => "",
        "android" => "",
        "web"     => "",
        "script"  => "",
        "db"      => "",
        "pdf"     => "",
        _         => ""
    };

    public static Brush BrushFor(string key) => key switch
    {
        "folder"  => FolderBrush,
        "link"    => AppBrushes.GradientApkm,
        "image"   => AppBrushes.GradientApkm,
        "video"   => AppBrushes.GradientRed,
        "audio"   => AppBrushes.GradientApks,
        "text"    => AppBrushes.GradientApk,
        "config"  => AppBrushes.GradientCyan,
        "archive" => AppBrushes.GradientOrange,
        "android" => AppBrushes.GradientGreen,
        "web"     => AppBrushes.GradientNavy,
        "script"  => AppBrushes.GradientOrange,
        "db"      => AppBrushes.GradientNavy,
        "pdf"     => AppBrushes.GradientRed,
        _         => AppBrushes.GradientDefault
    };

    private static Brush FolderBrush =>
        _folderBrush ??= Application.Current?.TryFindResource("FileManager.Brush.Folder") as Brush
                         ?? AppBrushes.GradientOrange;
}

internal static partial class PermissionsDescriber
{
    private readonly record struct Triad(bool Read, bool Write, bool Execute)
    {
        public int Octal => (Read ? 4 : 0) | (Write ? 2 : 0) | (Execute ? 1 : 0);
    }

    [GeneratedRegex(@"^[0-7]{3,4}$")]
    private static partial Regex OctalInputPattern();

    [GeneratedRegex(@"^[-dlcbspDLCBSP]?[rwxsStT\-]{9}$")]
    private static partial Regex SymbolicInputPattern();

    public static string ToSymbolic(string permissions)
    {
        if (string.IsNullOrEmpty(permissions)) return string.Empty;
        var start = TriadStart(permissions);
        return start == 0 || permissions.Length < start + 9
            ? permissions
            : permissions.Substring(start, 9);
    }

    public static string ToOctal(string permissions)
    {
        if (!TryParseTriads(permissions, out var owner, out var group, out var other))
            return permissions ?? string.Empty;
        return $"{owner.Octal}{group.Octal}{other.Octal}";
    }

    public static string Describe(string permissions)
    {
        if (!TryParseTriads(permissions, out var owner, out var group, out var other))
            return permissions ?? string.Empty;

        if (group.Equals(owner) && other.Equals(owner))
            return string.Format(Strings.FileManager_Perm_Phrase_Everyone, Phrase(owner));

        if (group.Equals(other))
        {
            if (IsNone(other))
                return string.Format(Strings.FileManager_Perm_Phrase_OwnerOnly, Phrase(owner));
            return string.Format(Strings.FileManager_Perm_Phrase_OwnerVsRest, Phrase(owner), Phrase(other));
        }

        return string.Format(Strings.FileManager_Perm_Phrase_Full, Phrase(owner), Phrase(group), Phrase(other));
    }

    public static bool TryParse(string input, out string symbolicNineChar, out string octalThreeDigit, out string error)
    {
        symbolicNineChar = string.Empty;
        octalThreeDigit  = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = Strings.FileManager_Validate_ModeEmpty;
            return false;
        }

        var trimmed = input.Trim();

        if (TryParseOctal(trimmed, out symbolicNineChar, out octalThreeDigit) ||
            TryParseSymbolic(trimmed, out symbolicNineChar, out octalThreeDigit) ||
            TryParseDescribed(trimmed, out symbolicNineChar, out octalThreeDigit))
        {
            error = string.Empty;
            return true;
        }

        error = Strings.FileManager_Validate_ModeUnrecognised;
        return false;
    }

    private static bool TryParseOctal(string input, out string symbolic, out string octal)
    {
        symbolic = string.Empty;
        octal    = string.Empty;

        if (!OctalInputPattern().IsMatch(input)) return false;

        var rwxDigits = input.Length == 4 ? input.Substring(1) : input;
        symbolic = OctalTriad(rwxDigits[0]) + OctalTriad(rwxDigits[1]) + OctalTriad(rwxDigits[2]);
        octal    = input;
        return true;
    }

    private static string OctalTriad(char c)
    {
        var d = c - '0';
        return string.Create(3, d, static (span, v) =>
        {
            span[0] = v >= 4 ? 'r' : '-';
            span[1] = (v & 2) != 0 ? 'w' : '-';
            span[2] = (v & 1) != 0 ? 'x' : '-';
        });
    }

    private static bool TryParseSymbolic(string input, out string symbolic, out string octal)
    {
        symbolic = string.Empty;
        octal    = string.Empty;

        if (!SymbolicInputPattern().IsMatch(input)) return false;
        if (!TryParseTriads(input, out var owner, out var group, out var other)) return false;

        symbolic = TriadToChars(owner) + TriadToChars(group) + TriadToChars(other);
        octal    = $"{owner.Octal}{group.Octal}{other.Octal}";
        return true;
    }

    private static string TriadToChars(Triad t) =>
        $"{(t.Read ? 'r' : '-')}{(t.Write ? 'w' : '-')}{(t.Execute ? 'x' : '-')}";

    private static bool TryParseDescribed(string input, out string symbolic, out string octal)
    {
        symbolic = string.Empty;
        octal    = string.Empty;

        // Enumerate all 512 symbolic combinations, find the one whose Describe() output matches input.
        for (var o = 0; o < 8; o++)
        for (var g = 0; g < 8; g++)
        for (var ot = 0; ot < 8; ot++)
        {
            var sym = OctalTriad((char)('0' + o))
                    + OctalTriad((char)('0' + g))
                    + OctalTriad((char)('0' + ot));
            if (string.Equals(Describe(sym), input, StringComparison.CurrentCultureIgnoreCase))
            {
                symbolic = sym;
                octal    = $"{o}{g}{ot}";
                return true;
            }
        }
        return false;
    }

    private static int TriadStart(string permissions) =>
        permissions.Length >= 10 && permissions[0] is 'd' or '-' or 'l' or 'c' or 'b' or 's' or 'p'
            ? 1
            : 0;

    private static bool TryParseTriads(string permissions, out Triad owner, out Triad group, out Triad other)
    {
        owner = group = other = default;
        if (string.IsNullOrEmpty(permissions)) return false;

        var start = TriadStart(permissions);
        if (permissions.Length < start + 9) return false;

        owner = ParseTriad(permissions, start);
        group = ParseTriad(permissions, start + 3);
        other = ParseTriad(permissions, start + 6);
        return true;
    }

    private static Triad ParseTriad(string s, int offset) => new(
        s[offset] == 'r',
        s[offset + 1] == 'w',
        s[offset + 2] is 'x' or 's' or 't');

    private static bool IsNone(Triad t) => !t.Read && !t.Write && !t.Execute;

    private static string Phrase(Triad t) => t switch
    {
        { Read: false, Write: false, Execute: false } => Strings.FileManager_Perm_None,
        { Read: true,  Write: true,  Execute: true  } => Strings.FileManager_Perm_Rwx,
        { Read: true,  Write: true,  Execute: false } => Strings.FileManager_Perm_Rw,
        { Read: true,  Write: false, Execute: true  } => Strings.FileManager_Perm_Rx,
        { Read: true,  Write: false, Execute: false } => Strings.FileManager_Perm_R,
        { Read: false, Write: true,  Execute: true  } => Strings.FileManager_Perm_Wx,
        { Read: false, Write: true,  Execute: false } => Strings.FileManager_Perm_W,
        _                                              => Strings.FileManager_Perm_X
    };
}
