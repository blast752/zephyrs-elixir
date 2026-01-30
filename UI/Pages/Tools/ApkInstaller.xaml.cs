namespace ZephyrsElixir.UI.Pages;

public sealed partial class ApkInstaller : UserControl
{
    private static readonly string Aapt2Path = System.IO.Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Tools", "adb", "aapt2.exe");

    private readonly Action _onClose;
    private readonly ObservableCollection<ApkPackage> _packages = new();
    private CancellationTokenSource? _cts;
    private bool _isInstalling;
    private bool _proWarningShown;

    public ObservableCollection<ApkPackage> Packages => _packages;

    public ApkInstaller(Action onClose)
    {
        _onClose = onClose;
        InitializeComponent();
        DataContext = this;
        _packages.CollectionChanged += (_, _) => UpdateUI();
        
        LicenseService.Instance.StateChanged += (_, _) => Dispatcher.BeginInvoke(UpdateUI);
    }

    #region Event Handlers

    private void OnBackClick(object sender, RoutedEventArgs e) => _onClose();

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Strings.ApkInstaller_Browse_Title,
            Filter = "Android Packages|*.apk;*.xapk;*.apks;*.apkm|All Files|*.*",
            Multiselect = Features.IsAvailable(Features.MultiApkInstall)
        };

        if (dialog.ShowDialog() == true)
            ProcessFiles(dialog.FileNames);
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        if (_isInstalling) return;
        _packages.Clear();
        _proWarningShown = false;
    }

    private void OnRemovePackage(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ApkPackage pkg } && !_isInstalling)
            _packages.Remove(pkg);
    }

    private async void OnInstallClick(object sender, RoutedEventArgs e)
    {
        if (_isInstalling) { _cts?.Cancel(); return; }
        await InstallAllPackagesAsync();
    }

    #endregion

    #region Drag & Drop

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        var hasValidFiles = files.Any(IsValidApkFile);

        e.Effects = hasValidFiles ? DragDropEffects.Copy : DragDropEffects.None;
        DragOverlay.Visibility = hasValidFiles ? Visibility.Visible : Visibility.Collapsed;
        AnimateDropZone(true);
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        DragOverlay.Visibility = Visibility.Collapsed;
        AnimateDropZone(false);
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        DragOverlay.Visibility = Visibility.Collapsed;
        AnimateDropZone(false);

        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        ProcessFiles(files.Where(IsValidApkFile).ToArray());
    }

    private void AnimateDropZone(bool active)
    {
        var scale = active ? 1.02 : 1.0;
        var animation = new DoubleAnimation(scale, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        DropZoneScale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        DropZoneScale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    private static bool IsValidApkFile(string path) =>
        System.IO.Path.GetExtension(path).ToLowerInvariant() is ".apk" or ".xapk" or ".apks" or ".apkm";

    #endregion

    #region File Processing

    private async void ProcessFiles(string[] files)
    {
        var isPro = Features.IsAvailable(Features.MultiApkInstall);
        var filesToProcess = files.ToList();
        
        if (!isPro)
        {
            var totalAfterAdd = _packages.Count + filesToProcess.Count;
            
            if (totalAfterAdd > 1)
            {
                if (!_proWarningShown)
                {
                    ShowProRequiredDialog("Pro_Required_MultiApk");
                    _proWarningShown = true;
                }
                
                if (_packages.Count == 0 && filesToProcess.Any())
                {
                    filesToProcess = new List<string> { filesToProcess.First() };
                }
                else
                {
                    StatusText.Text = Strings.ApkInstaller_Free_Limit;
                    return;
                }
            }
        }

        foreach (var file in filesToProcess)
        {
            if (_packages.Any(p => p.FilePath == file)) continue;

            try
            {
                var package = await Task.Run(() => ParsePackageFile(file));
                if (package != null)
                    await Dispatcher.InvokeAsync(() => _packages.Add(package));
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() => _packages.Add(new ApkPackage
                {
                    FilePath = file,
                    DisplayName = System.IO.Path.GetFileName(file),
                    PackageName = "Error parsing file",
                    Status = InstallStatus.Failed,
                    ErrorMessage = ex.Message,
                    PackageType = GetPackageType(file)
                }));
            }
        }
    }

    private void ShowProRequiredDialog(string messageKey)
    {
        Dispatcher.BeginInvoke(() =>
        {
            DialogService.Instance.ShowProRequiredWithUpgrade(
                messageKey, 
                Window.GetWindow(this)
            );
        });
    }

    private ApkPackage? ParsePackageFile(string filePath) =>
        System.IO.Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".apk" => ParseSingleApk(filePath),
            ".xapk" or ".apks" or ".apkm" => ParseBundleFile(filePath),
            _ => null
        };

    private static ApkPackage ParseSingleApk(string filePath)
    {
        var fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);
        var info = ExtractApkInfo(filePath);

        return new ApkPackage
        {
            FilePath = filePath,
            DisplayName = !string.IsNullOrEmpty(info.Label) ? info.Label : fileName,
            PackageName = !string.IsNullOrEmpty(info.PackageName) ? info.PackageName : fileName,
            VersionName = !string.IsNullOrEmpty(info.VersionName) ? info.VersionName : "1.0",
            VersionCode = info.VersionCode > 0 ? info.VersionCode : 1,
            Size = new FileInfo(filePath).Length,
            PackageType = PackageType.Apk,
            ApkFiles = new[] { filePath }
        };
    }

    private ApkPackage ParseBundleFile(string filePath)
    {
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ZephyrsElixir_APK", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            ZipFile.ExtractToDirectory(filePath, tempDir);

            var package = new ApkPackage
            {
                FilePath = filePath,
                TempExtractPath = tempDir,
                PackageType = GetPackageType(filePath),
                Size = new FileInfo(filePath).Length
            };

            var manifestPath = System.IO.Path.Combine(tempDir, "manifest.json");
            if (File.Exists(manifestPath))
                ParseJsonManifest(manifestPath, package, isXapk: true);

            var infoPath = System.IO.Path.Combine(tempDir, "info.json");
            if (File.Exists(infoPath) && string.IsNullOrEmpty(package.PackageName))
                ParseJsonManifest(infoPath, package, isXapk: false);

            var allApks = Directory.GetFiles(tempDir, "*.apk", SearchOption.AllDirectories);

            if (string.IsNullOrEmpty(package.PackageName) && allApks.Length > 0)
            {
                var baseApk = FindBaseApk(allApks);
                if (baseApk != null)
                {
                    var info = ExtractApkInfo(baseApk);
                    package.PackageName = !string.IsNullOrEmpty(info.PackageName) ? info.PackageName : "unknown";
                    package.DisplayName = !string.IsNullOrEmpty(info.Label) ? info.Label : System.IO.Path.GetFileNameWithoutExtension(filePath);
                    package.VersionName = !string.IsNullOrEmpty(info.VersionName) ? info.VersionName : "1.0";
                    package.VersionCode = info.VersionCode;
                }
            }

            package.ApkFiles = SelectOptimalSplitApks(allApks, package);
            package.DisplayName ??= System.IO.Path.GetFileNameWithoutExtension(filePath);

            return package;
        }
        catch
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
            throw;
        }
    }

    private static string? FindBaseApk(string[] apks)
    {
        foreach (var name in new[] { "base.apk", "app.apk", "original.apk" })
        {
            var match = apks.FirstOrDefault(a => System.IO.Path.GetFileName(a).Equals(name, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }

        return apks.FirstOrDefault(a =>
        {
            var n = System.IO.Path.GetFileNameWithoutExtension(a).ToLowerInvariant();
            return !n.Contains("split") && !n.Contains("config") && !n.Contains("dpi") &&
                   !n.Contains("arm") && !n.Contains("x86") && !n.Contains("hdpi");
        }) ?? apks.FirstOrDefault();
    }

    private static void ParseJsonManifest(string path, ApkPackage package, bool isXapk)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;

            if (isXapk)
            {
                package.PackageName = GetJsonString(root, "package_name");
                package.DisplayName = GetJsonString(root, "name");
                package.VersionName = GetJsonString(root, "version_name");
                if (root.TryGetProperty("version_code", out var vc))
                    package.VersionCode = vc.ValueKind == JsonValueKind.Number ? vc.GetInt32() :
                        int.TryParse(vc.GetString(), out var v) ? v : 0;
            }
            else
            {
                package.PackageName = GetJsonString(root, "pname") ?? GetJsonString(root, "package");
                package.DisplayName = GetJsonString(root, "appname") ?? GetJsonString(root, "label");
                package.VersionName = GetJsonString(root, "versionname") ?? GetJsonString(root, "version");
                if (root.TryGetProperty("versioncode", out var vc))
                    package.VersionCode = vc.ValueKind == JsonValueKind.Number ? vc.GetInt32() : 0;
            }
        }
        catch { }
    }

    private static string? GetJsonString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static ApkInfo ExtractApkInfo(string apkPath)
    {
        var info = new ApkInfo { PackageName = System.IO.Path.GetFileNameWithoutExtension(apkPath) };
        if (!File.Exists(Aapt2Path)) return info;

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(Aapt2Path, $"dump badging \"{apkPath}\"")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            var pkgMatch = Regex.Match(output, @"package:\s*name='([^']+)'");
            if (pkgMatch.Success) info = info with { PackageName = pkgMatch.Groups[1].Value };

            var verNameMatch = Regex.Match(output, @"versionName='([^']+)'");
            if (verNameMatch.Success) info = info with { VersionName = verNameMatch.Groups[1].Value };

            var verCodeMatch = Regex.Match(output, @"versionCode='(\d+)'");
            if (verCodeMatch.Success && int.TryParse(verCodeMatch.Groups[1].Value, out var vc))
                info = info with { VersionCode = vc };

            var labelMatch = Regex.Match(output, @"application-label-\w+:'([^']+)'");
            if (!labelMatch.Success)
                labelMatch = Regex.Match(output, @"application-label:'([^']+)'");
            if (labelMatch.Success) info = info with { Label = labelMatch.Groups[1].Value };
        }
        catch { }

        return info;
    }

    private static PackageType GetPackageType(string path) =>
        System.IO.Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".apk" => PackageType.Apk,
            ".xapk" => PackageType.Xapk,
            ".apks" => PackageType.Apks,
            ".apkm" => PackageType.Apkm,
            _ => PackageType.Apk
        };

    #endregion

    #region Split APK Selection

    private string[] SelectOptimalSplitApks(string[] allApks, ApkPackage package)
    {
        var selected = new List<string>();
        var deviceInfo = GetDeviceInfo();

        foreach (var apk in allApks)
        {
            var fileName = System.IO.Path.GetFileNameWithoutExtension(apk).ToLowerInvariant();

            if (IsBaseApk(fileName)) { selected.Add(apk); continue; }
            if (IsArchSplit(fileName)) { if (MatchesArch(fileName, deviceInfo.Abi)) selected.Add(apk); continue; }
            if (IsDpiSplit(fileName)) { if (MatchesDpi(fileName, deviceInfo.Dpi)) selected.Add(apk); continue; }
            if (IsLangSplit(fileName)) { if (MatchesLang(fileName, deviceInfo.Lang)) selected.Add(apk); continue; }
            if (fileName.Contains("config.")) continue;

            selected.Add(apk);
        }

        if (!selected.Any(s => IsArchSplit(System.IO.Path.GetFileNameWithoutExtension(s))))
            selected.AddRange(allApks.Where(a => IsArchSplit(System.IO.Path.GetFileNameWithoutExtension(a))));

        if (!selected.Any(s => IsDpiSplit(System.IO.Path.GetFileNameWithoutExtension(s))))
        {
            var dpiApk = FindClosestDpiApk(allApks, deviceInfo.Dpi);
            if (dpiApk != null) selected.Add(dpiApk);
        }

        if (!selected.Any(s => IsLangSplit(System.IO.Path.GetFileNameWithoutExtension(s))))
        {
            var langApk = allApks.FirstOrDefault(a => System.IO.Path.GetFileNameWithoutExtension(a).Contains(".en", StringComparison.OrdinalIgnoreCase))
                ?? allApks.FirstOrDefault(a => IsLangSplit(System.IO.Path.GetFileNameWithoutExtension(a)));
            if (langApk != null) selected.Add(langApk);
        }

        package.SplitInfo = $"Selected {selected.Count}/{allApks.Length} APKs ({deviceInfo.Abi}, {deviceInfo.Dpi}dpi, {deviceInfo.Lang})";
        return selected.Distinct().ToArray();
    }

    private static bool IsBaseApk(string n) => n.Contains("base") || n == "app" || (!n.Contains("split") && !n.Contains("config"));
    private static bool IsArchSplit(string n) => n.Contains("arm64") || n.Contains("armeabi") || n.Contains("x86") || n.Contains("mips");
    private static bool IsDpiSplit(string n) => Regex.IsMatch(n, @"(ldpi|mdpi|hdpi|xhdpi|xxhdpi|xxxhdpi|nodpi|\d+dpi)", RegexOptions.IgnoreCase);
    private static bool IsLangSplit(string n) => Regex.IsMatch(n, @"config\.[a-z]{2}($|[_-])", RegexOptions.IgnoreCase);

    private static bool MatchesArch(string name, string abi)
    {
        if (string.IsNullOrEmpty(abi)) return true;
        var map = new Dictionary<string, string[]>
        {
            ["arm64-v8a"] = new[] { "arm64", "arm64_v8a", "arm64-v8a" },
            ["armeabi-v7a"] = new[] { "armeabi", "armeabi_v7a", "armeabi-v7a", "arm" },
            ["x86_64"] = new[] { "x86_64", "x64" },
            ["x86"] = new[] { "x86" }
        };
        return map.TryGetValue(abi, out var patterns)
            ? patterns.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase))
            : name.Contains(abi, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesDpi(string name, int dpi)
    {
        var map = new Dictionary<string, int> { ["ldpi"] = 120, ["mdpi"] = 160, ["hdpi"] = 240, ["xhdpi"] = 320, ["xxhdpi"] = 480, ["xxxhdpi"] = 640 };
        var target = map.FirstOrDefault(d => dpi <= d.Value + 80).Key ?? "xxxhdpi";
        return name.Contains(target, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesLang(string name, string lang)
    {
        if (string.IsNullOrEmpty(lang)) return false;
        var primary = lang.Split('-', '_')[0].ToLowerInvariant();
        return name.Contains($".{primary}", StringComparison.OrdinalIgnoreCase) ||
               name.Contains($"_{primary}", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindClosestDpiApk(string[] apks, int deviceDpi)
    {
        var dpiMap = new (string name, int dpi)[] { ("ldpi", 120), ("mdpi", 160), ("hdpi", 240), ("xhdpi", 320), ("xxhdpi", 480), ("xxxhdpi", 640) };

        return dpiMap
            .OrderBy(d => Math.Abs(d.dpi - deviceDpi))
            .Select(d => apks.FirstOrDefault(a => System.IO.Path.GetFileNameWithoutExtension(a).Contains(d.name, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(a => a != null);
    }

    private static DeviceConfig GetDeviceInfo()
    {
        try
        {
            var abi = AdbExecutor.ExecuteCommand("shell getprop ro.product.cpu.abi").Trim();
            var dpiStr = AdbExecutor.ExecuteCommand("shell wm density").Replace("Physical density:", "").Trim();
            var dpi = int.TryParse(dpiStr, out var d) ? d : 480;
            var lang = AdbExecutor.ExecuteCommand("shell getprop persist.sys.locale").Split('-', '_').FirstOrDefault()?.Trim() ?? "en";
            return new DeviceConfig(abi, dpi, lang);
        }
        catch { return new DeviceConfig("arm64-v8a", 480, "en"); }
    }

    #endregion

    #region Installation

    private async Task InstallAllPackagesAsync()
    {
        if (!_packages.Any(p => p.Status == InstallStatus.Pending)) return;

        _isInstalling = true;
        _cts = new CancellationTokenSource();
        SetInstallButtonState(true);

        try
        {
            foreach (var package in _packages.Where(p => p.Status == InstallStatus.Pending).ToList())
            {
                if (_cts.Token.IsCancellationRequested) break;
                await InstallPackageAsync(package, _cts.Token);
            }
        }
        finally
        {
            _isInstalling = false;
            _cts?.Dispose();
            _cts = null;
            SetInstallButtonState(false);
            UpdateUI();
        }
    }

    private async Task InstallPackageAsync(ApkPackage package, CancellationToken token)
    {
        package.Status = InstallStatus.Installing;
        package.Progress = 0;

        try
        {
            var existingVersion = await GetInstalledVersionAsync(package.PackageName);
            var args = BuildInstallCommand(package, existingVersion);
            var result = await ExecuteAdbInstallAsync(args, package, token);

            if (result.Success)
            {
                package.Status = existingVersion > 0 ? InstallStatus.Updated : InstallStatus.Success;
                package.ErrorMessage = null;
            }
            else
            {
                package.Status = InstallStatus.Failed;
                package.ErrorMessage = ParseInstallError(result.Output);
            }
        }
        catch (OperationCanceledException) { package.Status = InstallStatus.Pending; }
        catch (Exception ex) { package.Status = InstallStatus.Failed; package.ErrorMessage = ex.Message; }
        finally { package.Progress = 100; CleanupTempFiles(package); }
    }

    private string BuildInstallCommand(ApkPackage package, int existingVersion)
    {
        var flags = new List<string> { package.ApkFiles.Length > 1 ? "install-multiple" : "install", "-r" };

        if (BypassSecurityCheck.IsChecked == true)
        {
            flags.Add("-t");
            flags.Add("--bypass-low-target-sdk-block");
        }

        if (AllowDowngradeCheck.IsChecked == true || (existingVersion > 0 && package.VersionCode < existingVersion))
            flags.Add("-d");

        flags.Add("-g");
        flags.AddRange(package.ApkFiles.Select(apk => $"\"{apk}\""));

        return string.Join(" ", flags);
    }

    private static async Task<int> GetInstalledVersionAsync(string? packageName)
    {
        if (string.IsNullOrEmpty(packageName)) return 0;
        try
        {
            var output = await AdbExecutor.ExecuteCommandAsync($"shell dumpsys package {packageName} | grep versionCode");
            var match = Regex.Match(output, @"versionCode=(\d+)");
            return match.Success && int.TryParse(match.Groups[1].Value, out var v) ? v : 0;
        }
        catch { return 0; }
    }

    private async Task<(bool Success, string Output)> ExecuteAdbInstallAsync(string args, ApkPackage package, CancellationToken token)
    {
        var output = new StringBuilder();
        var tcs = new TaskCompletionSource<bool>();

        var adbPath = AdbExecutor.GetAdbPath();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(adbPath, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            },
            EnableRaisingEvents = true
        };

        await using var _ = token.Register(() => { try { process.Kill(true); } catch { } tcs.TrySetCanceled(); });

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            output.AppendLine(e.Data);
            if (e.Data.Contains("%"))
            {
                var match = Regex.Match(e.Data, @"(\d+)%");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var p))
                    Dispatcher.BeginInvoke(() => package.Progress = p);
            }
        };

        process.ErrorDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
        process.Exited += (_, _) => tcs.TrySetResult(process.ExitCode == 0);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var success = await tcs.Task;
        var result = output.ToString();
        return (success && !result.Contains("Failure"), result);
    }

    private static string ParseInstallError(string output)
    {
        var errors = new Dictionary<string, string>
        {
            ["INSTALL_FAILED_ALREADY_EXISTS"] = "App already installed",
            ["INSTALL_FAILED_INVALID_APK"] = "Invalid APK file",
            ["INSTALL_FAILED_INSUFFICIENT_STORAGE"] = "Not enough storage",
            ["INSTALL_FAILED_DUPLICATE_PACKAGE"] = "Package already exists",
            ["INSTALL_FAILED_UPDATE_INCOMPATIBLE"] = "Incompatible update",
            ["INSTALL_FAILED_VERSION_DOWNGRADE"] = "Cannot downgrade",
            ["INSTALL_PARSE_FAILED_NO_CERTIFICATES"] = "APK not signed",
            ["INSTALL_FAILED_TEST_ONLY"] = "Test-only APK",
            ["INSTALL_FAILED_OLDER_SDK"] = "Requires newer Android",
            ["INSTALL_FAILED_MISSING_SPLIT"] = "Missing split APK"
        };

        foreach (var (pattern, message) in errors)
            if (output.Contains(pattern)) return message;

        var match = Regex.Match(output, @"Failure \[([^\]]+)\]");
        return match.Success ? match.Groups[1].Value : "Installation failed";
    }

    private static void CleanupTempFiles(ApkPackage package)
    {
        if (string.IsNullOrEmpty(package.TempExtractPath)) return;
        try { if (Directory.Exists(package.TempExtractPath)) Directory.Delete(package.TempExtractPath, true); } catch { }
    }

    #endregion

    #region UI Helpers

    private void UpdateUI()
    {
        var hasPackages = _packages.Count > 0;
        var pending = _packages.Count(p => p.Status == InstallStatus.Pending);
        var success = _packages.Count(p => p.Status is InstallStatus.Success or InstallStatus.Updated);
        var failed = _packages.Count(p => p.Status == InstallStatus.Failed);
        var isPro = Features.IsAvailable(Features.MultiApkInstall);

        Dispatcher.BeginInvoke(() =>
        {
            DropZone.Visibility = hasPackages ? Visibility.Collapsed : Visibility.Visible;
            FileListPanel.Visibility = hasPackages ? Visibility.Visible : Visibility.Collapsed;
            ClearButton.Visibility = hasPackages && !_isInstalling ? Visibility.Visible : Visibility.Collapsed;
            InstallButton.IsEnabled = pending > 0 && DeviceManager.Instance.IsConnected;

            var quotaInfo = isPro ? "" : $" • {Strings.ApkInstaller_Free_SingleOnly}";
            
            SummaryText.Text = hasPackages
                ? $"{_packages.Count} package(s) • {pending} pending • {success} success • {failed} failed{quotaInfo}"
                : Strings.ApkInstaller_NoPackages;

            StatusText.Text = _isInstalling ? Strings.ApkInstaller_Installing
                : hasPackages ? string.Format(Strings.ApkInstaller_Ready, pending)
                : Strings.ApkInstaller_DragDrop;
        });
    }

    private void SetInstallButtonState(bool isStop)
    {
        Dispatcher.BeginInvoke(() =>
        {
            InstallButton.Style = (Style)FindResource(isStop ? "App.Style.Button.Destructive" : "App.Style.Button");
            InstallButton.Content = isStop ? Strings.Common_Button_Cancel : Strings.ApkInstaller_InstallAll;
            InstallButton.Tag = isStop ? "\uE711" : "\uE896";
        });
    }

    #endregion
}

#region Data Models

public enum PackageType { Apk, Xapk, Apks, Apkm }
public enum InstallStatus { Pending, Installing, Success, Updated, Failed }

public record struct DeviceConfig(string Abi, int Dpi, string Lang);

public record ApkInfo
{
    public string? PackageName { get; init; }
    public string? VersionName { get; init; }
    public int VersionCode { get; init; }
    public string? Label { get; init; }
}

public sealed class ApkPackage : INotifyPropertyChanged
{
    private InstallStatus _status = InstallStatus.Pending;
    private int _progress;
    private string? _errorMessage;

    public string FilePath { get; init; } = "";
    public string? TempExtractPath { get; set; }
    public string? DisplayName { get; set; }
    public string? PackageName { get; set; }
    public string? VersionName { get; set; }
    public int VersionCode { get; set; }
    public long Size { get; set; }
    public PackageType PackageType { get; set; }
    public string[] ApkFiles { get; set; } = Array.Empty<string>();
    public string? SplitInfo { get; set; }

    public InstallStatus Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText));
              OnPropertyChanged(nameof(StatusBrush)); OnPropertyChanged(nameof(IsInstalling));
              OnPropertyChanged(nameof(CanRemove)); OnPropertyChanged(nameof(HasError)); }
    }

    public int Progress
    {
        get => _progress;
        set { _progress = value; OnPropertyChanged(); }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set { _errorMessage = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(HasError)); }
    }

    public bool HasVersionCode => VersionCode > 0;
    public bool IsInstalling => Status == InstallStatus.Installing;
    public bool CanRemove => Status != InstallStatus.Installing;
    public bool HasError => Status == InstallStatus.Failed && !string.IsNullOrEmpty(ErrorMessage);

    public string SizeDisplay => Size switch
    {
        < 1024 => $"{Size} B",
        < 1024 * 1024 => $"{Size / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{Size / (1024.0 * 1024):F1} MB",
        _ => $"{Size / (1024.0 * 1024 * 1024):F2} GB"
    };

    public string StatusText => Status switch
    {
        InstallStatus.Pending => "Pending",
        InstallStatus.Installing => $"{Progress}%",
        InstallStatus.Success => "Installed",
        InstallStatus.Updated => "Updated",
        InstallStatus.Failed => ErrorMessage?.Length > 12 ? ErrorMessage[..12] + "…" : ErrorMessage ?? "Failed",
        _ => "Unknown"
    };

    public Brush StatusBrush => Status switch
    {
        InstallStatus.Pending => new SolidColorBrush(Color.FromRgb(160, 176, 200)),
        InstallStatus.Installing => new SolidColorBrush(Color.FromRgb(255, 208, 0)),
        InstallStatus.Success => new SolidColorBrush(Color.FromRgb(0, 214, 143)),
        InstallStatus.Updated => new SolidColorBrush(Color.FromRgb(0, 191, 255)),
        InstallStatus.Failed => new SolidColorBrush(Color.FromRgb(255, 107, 107)),
        _ => Brushes.White
    };

    public string TypeIcon => PackageType switch
    {
        PackageType.Apk => "\uE8F1",
        _ => "\uE8F4"
    };
    
    public string TypeLetter => PackageType switch
    {
        PackageType.Apk => "A",
        PackageType.Xapk => "X",
        PackageType.Apks => "S",
        PackageType.Apkm => "M",
        _ => "?"
    };

    public Brush TypeBrush => PackageType switch
    {
        PackageType.Apk => UIHelpers.CreateGradientBrush("#63B5FF", "#1175E6"),
        PackageType.Xapk => UIHelpers.CreateGradientBrush("#00D68F", "#00B377"),
        PackageType.Apks => UIHelpers.CreateGradientBrush("#FFD000", "#CC9900"),
        PackageType.Apkm => UIHelpers.CreateGradientBrush("#7D64FF", "#5A3FD9"),
        _ => UIHelpers.CreateGradientBrush("#808080", "#606060")
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

#endregion
