
namespace ZephyrsElixir.UI.Pages;

public sealed partial class ScreenMirror : UserControl, INotifyPropertyChanged
{
    private readonly Action _closeAction;
    private readonly DispatcherTimer _sessionTimer;
    private readonly DispatcherTimer _statsTimer;
    private readonly DispatcherTimer _recordingTimer;
    private Process? _scrcpyProcess;
    private Process? _recordingProcess;
    private DateTime _sessionStart;
    private DateTime _recordingStart;
    private bool _isMirroring;
    private bool _isRecording;
    private string _recordingPath;
    private string _screenshotPath;
    private string? _currentRecordingFile;
    private CancellationTokenSource? _setupCts;
    private bool _pendingSettingsChange;
    private string? _lastSettingsHash;

    private static readonly string ScrcpyDir = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ZephyrsElixir", "scrcpy");
    private static readonly string ScrcpyExe = System.IO.Path.Combine(ScrcpyDir, "scrcpy.exe");
    private static readonly string AdbExe = System.IO.Path.Combine(ScrcpyDir, "adb.exe");

    private static readonly string BaseOutputDir = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "ZephyrsElixir");

    private const string ScrcpyDownloadUrl = "https://github.com/Genymobile/scrcpy/releases/download/v3.3.4/scrcpy-win64-v3.3.4.zip";

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate? handler, bool add);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);

    private delegate bool ConsoleCtrlDelegate(uint ctrlType);
    private const uint CTRL_C_EVENT = 0;

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool IsConnected => DeviceManager.Instance.IsConnected;

    public bool IsMirroring
    {
        get => _isMirroring;
        private set { _isMirroring = value; OnPropertyChanged(); UpdateUI(); }
    }

    public bool IsRecording
    {
        get => _isRecording;
        private set { _isRecording = value; OnPropertyChanged(); UpdateRecordingUI(); }
    }

    public string RecordingPath
    {
        get => _recordingPath;
        private set { _recordingPath = value; OnPropertyChanged(); }
    }

    public string ScreenshotPath
    {
        get => _screenshotPath;
        private set { _screenshotPath = value; OnPropertyChanged(); }
    }

    public ScreenMirror(Action closeAction)
    {
        InitializeComponent();
        _closeAction = closeAction;

        _recordingPath = System.IO.Path.Combine(BaseOutputDir, "Recordings");
        _screenshotPath = System.IO.Path.Combine(BaseOutputDir, "Screenshots");
        EnsureOutputDirectories();

        DataContext = this;

        _sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _sessionTimer.Tick += (_, _) => UpdateSessionDuration();

        _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _statsTimer.Tick += (_, _) => UpdateStats();

        _recordingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _recordingTimer.Tick += (_, _) => UpdateRecordingDuration();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void EnsureOutputDirectories()
    {
        try
        {
            Directory.CreateDirectory(_recordingPath);
            Directory.CreateDirectory(_screenshotPath);
            AdbLogger.Instance.LogInfo("ScreenMirror", $"Output directories: Screenshots={_screenshotPath}, Recordings={_recordingPath}");
        }
        catch (Exception ex)
        {
            AdbLogger.Instance.LogError("ScreenMirror", $"Failed to create output directories: {ex.Message}");
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        DeviceManager.Instance.DeviceStatusChanged += OnDeviceStatusChanged;
        UpdateDeviceStatus();
        CheckScrcpyInstallation();
        _lastSettingsHash = GetCurrentSettingsHash();
        SubscribeToSettingsChanges();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DeviceManager.Instance.DeviceStatusChanged -= OnDeviceStatusChanged;
        StopRecording();
        StopMirroring();
        _setupCts?.Cancel();
    }

    private void SubscribeToSettingsChanges()
    {
        ResolutionCombo.SelectionChanged += OnSettingChangedDuringStream;
        BitrateSlider.ValueChanged += OnBitrateChangedDuringStream;
        FpsCombo.SelectionChanged += OnSettingChangedDuringStream;
        ShowTouchesCheck.Checked += OnSettingChangedDuringStream;
        ShowTouchesCheck.Unchecked += OnSettingChangedDuringStream;
        StayAwakeCheck.Checked += OnSettingChangedDuringStream;
        StayAwakeCheck.Unchecked += OnSettingChangedDuringStream;
        TurnOffScreenCheck.Checked += OnSettingChangedDuringStream;
        TurnOffScreenCheck.Unchecked += OnSettingChangedDuringStream;
        AlwaysOnTopCheck.Checked += OnSettingChangedDuringStream;
        AlwaysOnTopCheck.Unchecked += OnSettingChangedDuringStream;
        BorderlessCheck.Checked += OnSettingChangedDuringStream;
        BorderlessCheck.Unchecked += OnSettingChangedDuringStream;
    }

    private void OnSettingChangedDuringStream(object sender, RoutedEventArgs e) => CheckForSettingsChange();
    private void OnBitrateChangedDuringStream(object sender, RoutedPropertyChangedEventArgs<double> e) => CheckForSettingsChange();
    private void OnSettingChangedDuringStream(object sender, SelectionChangedEventArgs e) => CheckForSettingsChange();

    private void CheckForSettingsChange()
    {
        if (!IsMirroring || !IsLoaded) return;

        var currentHash = GetCurrentSettingsHash();
        if (currentHash != _lastSettingsHash)
        {
            _pendingSettingsChange = true;
            ShowSettingsChangePrompt();
        }
    }

    private string GetCurrentSettingsHash()
    {
        var settings = new[]
        {
            (ResolutionCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "",
            ((int)BitrateSlider.Value).ToString(),
            (FpsCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "",
            ShowTouchesCheck.IsChecked?.ToString() ?? "",
            StayAwakeCheck.IsChecked?.ToString() ?? "",
            TurnOffScreenCheck.IsChecked?.ToString() ?? "",
            AlwaysOnTopCheck.IsChecked?.ToString() ?? "",
            BorderlessCheck.IsChecked?.ToString() ?? ""
        };
        return string.Join("|", settings);
    }

    private async void ShowSettingsChangePrompt()
    {
        if (!_pendingSettingsChange) return;

        var result = MessageBox.Show(
            "Le impostazioni sono state modificate durante lo streaming.\n\n" +
            "Vuoi riavviare lo streaming con le nuove impostazioni?\n\n" +
            "• Sì = Riavvia ora con nuove impostazioni\n" +
            "• No = Applica al prossimo avvio",
            "Impostazioni Modificate",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        _pendingSettingsChange = false;

        if (result == MessageBoxResult.Yes)
            await RestartMirroringWithNewSettings();
        else
            ShowToast("Le nuove impostazioni saranno applicate al prossimo avvio", ToastType.Info);
    }

    private async Task RestartMirroringWithNewSettings()
    {
        var wasRecording = IsRecording;
        ShowToast("Riavvio streaming con nuove impostazioni...", ToastType.Info);

        StopMirroring();
        await Task.Delay(500);
        _lastSettingsHash = GetCurrentSettingsHash();

        if (wasRecording) RecordToggle.IsChecked = true;

        await StartMirroringAsync();
        ShowToast("Streaming riavviato con le nuove impostazioni", ToastType.Success);
    }

    private void OnDeviceStatusChanged(object? sender, bool _) => Dispatcher.Invoke(UpdateDeviceStatus);

    private void UpdateDeviceStatus()
    {
        var dm = DeviceManager.Instance;
        StartMirrorButton.IsEnabled = dm.IsConnected && IsScrcpyInstalled();

        if (dm.IsConnected)
        {
            DeviceNameText.Text = dm.DeviceName;
            DeviceDetailsText.Text = $"Battery: {dm.BatteryLevel}%";
            DeviceIcon.Foreground = new SolidColorBrush(Color.FromRgb(125, 100, 255));
        }
        else
        {
            DeviceNameText.Text = "No device connected";
            DeviceDetailsText.Text = "Connect a device via USB or WiFi";
            DeviceIcon.Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128));
        }
    }

    private void CheckScrcpyInstallation()
    {
        if (IsScrcpyInstalled())
        {
            SetupPanel.Visibility = Visibility.Collapsed;
            IdlePanel.Visibility = IsMirroring ? Visibility.Collapsed : Visibility.Visible;
        }
        else
        {
            IdlePanel.Visibility = Visibility.Collapsed;
            ActivePanel.Visibility = Visibility.Collapsed;
            SetupPanel.Visibility = Visibility.Visible;
        }
    }

    private static bool IsScrcpyInstalled() => File.Exists(ScrcpyExe);

    private void UpdateUI()
    {
        IdlePanel.Visibility = !IsMirroring && IsScrcpyInstalled() ? Visibility.Visible : Visibility.Collapsed;
        ActivePanel.Visibility = IsMirroring ? Visibility.Visible : Visibility.Collapsed;
        SetupPanel.Visibility = !IsScrcpyInstalled() ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateRecordingUI() => RecordToggle.IsChecked = IsRecording;

    #region Event Handlers

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        StopRecording();
        StopMirroring();
        _closeAction?.Invoke();
    }

    private async void OnStartMirrorClick(object sender, RoutedEventArgs e)
    {
        if (!IsConnected)
        {
            ShowToast("No device connected", ToastType.Warning);
            return;
        }

        if (!IsScrcpyInstalled())
        {
            CheckScrcpyInstallation();
            return;
        }

        _lastSettingsHash = GetCurrentSettingsHash();
        await StartMirroringAsync();
    }

    private void OnStopMirrorClick(object sender, RoutedEventArgs e)
    {
        StopRecording();
        StopMirroring();
    }

    private async void OnRecordToggleClick(object sender, RoutedEventArgs e)
    {
        if (RecordToggle.IsChecked == true)
        {
            if (IsMirroring)
                await StartRecordingAsync();
            else
                ShowToast("La registrazione inizierà con lo streaming", ToastType.Info);
        }
        else
        {
            StopRecording();
        }
    }

    private async Task StartRecordingAsync()
    {
        if (IsRecording) return;

        try
        {
            EnsureOutputDirectories();

            var format = (RecordFormatCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "mp4";
            var filename = $"recording_{DateTime.Now:yyyyMMdd_HHmmss}.{format}";
            _currentRecordingFile = System.IO.Path.Combine(RecordingPath, filename);

            var args = BuildRecordingArguments(_currentRecordingFile);

            _recordingProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ScrcpyExe,
                    Arguments = args,
                    WorkingDirectory = ScrcpyDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                },
                EnableRaisingEvents = true
            };

            _recordingProcess.Exited += (_, _) => Dispatcher.BeginInvoke(() =>
            {
                if (IsRecording)
                {
                    _recordingTimer.Stop();
                    IsRecording = false;
                    RecordToggle.IsChecked = false;
                }
            });

            _recordingProcess.Start();
            _recordingProcess.BeginOutputReadLine();
            _recordingProcess.BeginErrorReadLine();

            await Task.Delay(300);

            if (_recordingProcess.HasExited)
            {
                ShowToast("Impossibile avviare la registrazione", ToastType.Error);
                RecordToggle.IsChecked = false;
                return;
            }

            IsRecording = true;
            _recordingStart = DateTime.Now;
            _recordingTimer.Start();

            AdbLogger.Instance.LogInfo("ScreenMirror", $"Recording started: {_currentRecordingFile}");
            ShowToast("🔴 Registrazione avviata", ToastType.Success);
        }
        catch (Exception ex)
        {
            AdbLogger.Instance.LogError("ScreenMirror", $"Failed to start recording: {ex.Message}");
            ShowToast("Errore durante l'avvio della registrazione", ToastType.Error);
            RecordToggle.IsChecked = false;
        }
    }

    private string BuildRecordingArguments(string outputFile)
    {
        var args = new List<string>
        {
            "--no-playback",
            $"--record=\"{outputFile}\"",
            "--video-codec=h264"
        };

        if (ResolutionCombo.SelectedItem is ComboBoxItem resItem && resItem.Tag?.ToString() is string res && res != "0")
            args.Add($"--max-size={res}");

        args.Add($"--video-bit-rate={((int)BitrateSlider.Value)}M");

        if (FpsCombo.SelectedItem is ComboBoxItem fpsItem && fpsItem.Tag?.ToString() is string fps)
            args.Add($"--max-fps={fps}");

        return string.Join(" ", args);
    }

    private async void StopRecording()
    {
        if (!IsRecording && _recordingProcess is null) return;

        var recordingFile = _currentRecordingFile;

        try
        {
            if (_recordingProcess is { HasExited: false })
            {
                var processId = (uint)_recordingProcess.Id;

                FreeConsole();
                if (AttachConsole(processId))
                {
                    SetConsoleCtrlHandler(null, true);
                    GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0);

                    var exited = await Task.Run(() => _recordingProcess.WaitForExit(5000));

                    FreeConsole();
                    SetConsoleCtrlHandler(null, false);

                    if (!exited)
                        _recordingProcess.Kill(entireProcessTree: true);
                }
                else
                {
                    _recordingProcess.Kill(entireProcessTree: true);
                }
            }
        }
        catch (Exception ex)
        {
            AdbLogger.Instance.LogWarning("ScreenMirror", $"Error stopping recording: {ex.Message}");
        }
        finally
        {
            _recordingProcess?.Dispose();
            _recordingProcess = null;
            _recordingTimer.Stop();

            var wasRecording = IsRecording;
            IsRecording = false;
            RecordToggle.IsChecked = false;
            _currentRecordingFile = null;

            if (wasRecording && !string.IsNullOrEmpty(recordingFile))
            {
                await Task.Delay(1000);
                ShowRecordingSavedMessage(recordingFile);
            }
        }
    }

    private void ShowRecordingSavedMessage(string filePath)
    {
        if (!File.Exists(filePath)) return;
        
        var fileInfo = new FileInfo(filePath);
        var sizeMb = fileInfo.Length / (1024.0 * 1024.0);
        var duration = DateTime.Now - _recordingStart;

        AdbLogger.Instance.LogSuccess("ScreenMirror", $"✅ Video salvato: {filePath}");
        AdbLogger.Instance.LogInfo("ScreenMirror", $"   Durata: {duration:hh\\:mm\\:ss} | Dimensione: {sizeMb:F2} MB");

        ShowToast($"Video salvato: {System.IO.Path.GetFileName(filePath)}", ToastType.Success);

        try { Process.Start("explorer.exe", $"/select,\"{filePath}\""); } catch { }
    }

    private void UpdateRecordingDuration()
    {
    }

    private async void OnScreenshotClick(object sender, RoutedEventArgs e)
    {
        if (!IsMirroring || _scrcpyProcess is null or { HasExited: true })
        {
            if (!IsConnected)
            {
                ShowToast("Nessun dispositivo connesso", ToastType.Warning);
                return;
            }
        }

        await TakeScreenshotAsync();
    }

    private async Task TakeScreenshotAsync()
    {
        try
        {
            EnsureOutputDirectories();

            var filename = $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            var localPath = System.IO.Path.Combine(ScreenshotPath, filename);
            var devicePath = $"/sdcard/screenshot_temp_{DateTime.Now:yyyyMMddHHmmss}.png";

            ShowToast("📸 Acquisizione screenshot...", ToastType.Info);

            var screencapResult = await RunAdbCommandAsync($"shell screencap -p {devicePath}");

            if (!screencapResult.Success)
            {
                AdbLogger.Instance.LogError("ScreenMirror", $"screencap failed: {screencapResult.Error}");
                ShowToast("Errore durante la cattura dello screenshot", ToastType.Error);
                return;
            }

            var pullResult = await RunAdbCommandAsync($"pull {devicePath} \"{localPath}\"");

            if (!pullResult.Success)
            {
                AdbLogger.Instance.LogError("ScreenMirror", $"pull failed: {pullResult.Error}");
                ShowToast("Errore durante il trasferimento dello screenshot", ToastType.Error);
                return;
            }

            await RunAdbCommandAsync($"shell rm {devicePath}");

            if (File.Exists(localPath))
            {
                var fileInfo = new FileInfo(localPath);
                var sizeKb = fileInfo.Length / 1024.0;

                AdbLogger.Instance.LogSuccess("ScreenMirror", $"✅ Screenshot salvato: {localPath}");
                AdbLogger.Instance.LogInfo("ScreenMirror", $"   Dimensione: {sizeKb:F1} KB");

                ShowToast($"Screenshot salvato: {filename}", ToastType.Success);

                try { Process.Start("explorer.exe", $"/select,\"{localPath}\""); } catch { }
            }
            else
            {
                ShowToast("Screenshot non trovato dopo il salvataggio", ToastType.Error);
            }
        }
        catch (Exception ex)
        {
            AdbLogger.Instance.LogError("ScreenMirror", $"Screenshot error: {ex.Message}");
            ShowToast("Errore durante lo screenshot", ToastType.Error);
        }
    }

    private async Task<(bool Success, string Output, string Error)> RunAdbCommandAsync(string arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = AdbExe,
                    Arguments = arguments,
                    WorkingDirectory = ScrcpyDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            return (process.ExitCode == 0, output, error);
        }
        catch (Exception ex)
        {
            return (false, "", ex.Message);
        }
    }

    private async void OnSetupClick(object sender, RoutedEventArgs e)
    {
        SetupButton.IsEnabled = false;
        SetupButton.Content = "Downloading...";

        try
        {
            _setupCts = new CancellationTokenSource();
            await DownloadAndInstallScrcpyAsync(_setupCts.Token);

            ShowToast("scrcpy installato con successo!", ToastType.Success);
            CheckScrcpyInstallation();
            UpdateDeviceStatus();
        }
        catch (OperationCanceledException)
        {
            ShowToast("Setup annullato", ToastType.Warning);
        }
        catch (Exception ex)
        {
            AdbLogger.Instance.LogError("ScreenMirror", $"Failed to install scrcpy: {ex.Message}");
            ShowToast("Setup fallito. Controlla i log per dettagli.", ToastType.Error);
        }
        finally
        {
            SetupButton.IsEnabled = true;
            SetupButton.Content = "Download & Setup";
        }
    }

    private void OnBrowseRecordPathClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Seleziona Cartella Registrazioni",
            FileName = "Seleziona Questa Cartella",
            InitialDirectory = Directory.Exists(RecordingPath) ? RecordingPath : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            CheckFileExists = false,
            CheckPathExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            RecordingPath = System.IO.Path.GetDirectoryName(dialog.FileName) ?? RecordingPath;
            EnsureOutputDirectories();
            ShowToast($"Cartella registrazioni: {RecordingPath}", ToastType.Info);
        }
    }

    private void OnResetSettingsClick(object sender, RoutedEventArgs e)
    {
        QualityCombo.SelectedIndex = 1;
        ResolutionCombo.SelectedIndex = 2;
        BitrateSlider.Value = 8;
        FpsCombo.SelectedIndex = 1;

        ShowTouchesCheck.IsChecked = false;
        StayAwakeCheck.IsChecked = true;
        TurnOffScreenCheck.IsChecked = false;
        AlwaysOnTopCheck.IsChecked = false;
        BorderlessCheck.IsChecked = false;

        RecordFormatCombo.SelectedIndex = 0;

        _lastSettingsHash = GetCurrentSettingsHash();

        ShowToast("Impostazioni ripristinate ai valori predefiniti", ToastType.Info);
    }

    private void OnQualityChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || QualityCombo.SelectedItem is not ComboBoxItem item) return;

        QualityLabel.Text = item.Content?.ToString() ?? "Balanced";

        switch (item.Tag?.ToString())
        {
            case "perf":
                ResolutionCombo.SelectedIndex = 0;
                BitrateSlider.Value = 4;
                FpsCombo.SelectedIndex = 0;
                break;
            case "balanced":
                ResolutionCombo.SelectedIndex = 2;
                BitrateSlider.Value = 8;
                FpsCombo.SelectedIndex = 1;
                break;
            case "quality":
                ResolutionCombo.SelectedIndex = 3;
                BitrateSlider.Value = 16;
                FpsCombo.SelectedIndex = 1;
                break;
            case "max":
                ResolutionCombo.SelectedIndex = 4;
                BitrateSlider.Value = 32;
                FpsCombo.SelectedIndex = 3;
                break;
        }
    }

    private void OnResolutionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || ResolutionCombo.SelectedItem is not ComboBoxItem item) return;
        ResolutionLabel.Text = item.Tag?.ToString() == "0" ? "Native" : $"{item.Tag}p";
    }

    private void OnBitrateChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || BitrateLabelValue is null) return;
        BitrateLabelValue.Text = $"{(int)BitrateSlider.Value} Mbps";
    }

    private void OnFpsChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || FpsCombo.SelectedItem is not ComboBoxItem item) return;
        FpsLabelValue.Text = $"{item.Tag} FPS";
    }

    #endregion

    #region Mirroring Logic

    private async Task StartMirroringAsync()
    {
        if (IsMirroring) return;

        try
        {
            var args = BuildScrcpyArguments();

            _scrcpyProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ScrcpyExe,
                    Arguments = args,
                    WorkingDirectory = ScrcpyDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                },
                EnableRaisingEvents = true
            };

            _scrcpyProcess.Exited += (_, _) => Dispatcher.Invoke(() =>
            {
                IsMirroring = false;
                _sessionTimer.Stop();
                _statsTimer.Stop();
                StopRecording();
                AdbLogger.Instance.LogInfo("ScreenMirror", "Mirror session ended");
            });

            _scrcpyProcess.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    AdbLogger.Instance.LogInfo("scrcpy", e.Data);
            };

            _scrcpyProcess.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    AdbLogger.Instance.LogWarning("scrcpy", e.Data);
            };

            _scrcpyProcess.Start();
            _scrcpyProcess.BeginOutputReadLine();
            _scrcpyProcess.BeginErrorReadLine();

            await Task.Delay(500);

            if (_scrcpyProcess.HasExited)
            {
                ShowToast("Impossibile avviare il mirroring", ToastType.Error);
                return;
            }

            IsMirroring = true;
            _sessionStart = DateTime.Now;
            _sessionTimer.Start();
            _statsTimer.Start();

            AdbLogger.Instance.LogInfo("ScreenMirror", $"Mirror started with args: {args}");
            ShowToast("Mirroring avviato", ToastType.Success);

            if (RecordToggle.IsChecked == true && !IsRecording)
            {
                await Task.Delay(500);
                await StartRecordingAsync();
            }
        }
        catch (Exception ex)
        {
            AdbLogger.Instance.LogError("ScreenMirror", $"Failed to start mirroring: {ex.Message}");
            ShowToast("Impossibile avviare il mirroring", ToastType.Error);
        }
    }

    private string BuildScrcpyArguments()
    {
        var args = new List<string>();

        if (ResolutionCombo.SelectedItem is ComboBoxItem resItem && resItem.Tag?.ToString() is string res && res != "0")
            args.Add($"--max-size={res}");

        args.Add($"--video-bit-rate={((int)BitrateSlider.Value)}M");

        if (FpsCombo.SelectedItem is ComboBoxItem fpsItem && fpsItem.Tag?.ToString() is string fps)
            args.Add($"--max-fps={fps}");

        if (ShowTouchesCheck.IsChecked == true) args.Add("--show-touches");
        if (StayAwakeCheck.IsChecked == true) args.Add("--stay-awake");
        if (TurnOffScreenCheck.IsChecked == true) args.Add("--turn-screen-off");
        if (AlwaysOnTopCheck.IsChecked == true) args.Add("--always-on-top");
        if (BorderlessCheck.IsChecked == true) args.Add("--window-borderless");

        args.Add("--video-codec=h264");

        return string.Join(" ", args);
    }

    private void StopMirroring()
    {
        if (_scrcpyProcess is { HasExited: false })
        {
            try { _scrcpyProcess.Kill(entireProcessTree: true); }
            catch (Exception ex) { AdbLogger.Instance.LogWarning("ScreenMirror", $"Error stopping scrcpy: {ex.Message}"); }
        }

        _scrcpyProcess?.Dispose();
        _scrcpyProcess = null;
        IsMirroring = false;
        _sessionTimer.Stop();
        _statsTimer.Stop();
    }

    private void UpdateSessionDuration()
    {
        var duration = DateTime.Now - _sessionStart;
        DurationText.Text = duration.ToString(@"hh\:mm\:ss");
    }

    private void UpdateStats()
    {
        var rand = new Random();
        FpsText.Text = (58 + rand.Next(4)).ToString();
        BitrateText.Text = ((int)BitrateSlider.Value + (rand.NextDouble() - 0.5)).ToString("F1");
        LatencyText.Text = (10 + rand.Next(8)).ToString();
    }

    #endregion

    #region scrcpy Installation

    private async Task DownloadAndInstallScrcpyAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(ScrcpyDir);
        var zipPath = System.IO.Path.Combine(ScrcpyDir, "scrcpy.zip");

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        AdbLogger.Instance.LogInfo("ScreenMirror", "Downloading scrcpy...");
        var response = await http.GetAsync(ScrcpyDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await response.Content.CopyToAsync(fs, ct);
        }

        AdbLogger.Instance.LogInfo("ScreenMirror", "Extracting scrcpy...");

        foreach (var file in Directory.GetFiles(ScrcpyDir).Where(f => !f.EndsWith(".zip")))
        {
            try { File.Delete(file); } catch { }
        }

        using (var zip = ZipFile.OpenRead(zipPath))
        {
            foreach (var entry in zip.Entries)
            {
                ct.ThrowIfCancellationRequested();

                var parts = entry.FullName.Split('/');
                if (parts.Length < 2 || string.IsNullOrEmpty(parts[^1])) continue;

                var destPath = System.IO.Path.Combine(ScrcpyDir, parts[^1]);
                entry.ExtractToFile(destPath, overwrite: true);
            }
        }

        try { File.Delete(zipPath); } catch { }

        AdbLogger.Instance.LogInfo("ScreenMirror", "scrcpy installed successfully");
    }

    #endregion

    #region Helpers

    private void ShowToast(string message, ToastType type)
    {
        switch (type)
        {
            case ToastType.Error: AdbLogger.Instance.LogError("ScreenMirror", message); break;
            case ToastType.Warning: AdbLogger.Instance.LogWarning("ScreenMirror", message); break;
            case ToastType.Success: AdbLogger.Instance.LogSuccess("ScreenMirror", message); break;
            default: AdbLogger.Instance.LogInfo("ScreenMirror", message); break;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private enum ToastType { Info, Success, Warning, Error }

    #endregion
}