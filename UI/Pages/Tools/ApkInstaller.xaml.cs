namespace ZephyrsElixir.UI.Pages;

public sealed partial class ApkInstaller : UserControl
{
    private static readonly string Aapt2Path = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Tools", "adb", "aapt2.exe");

    private static readonly Regex PkgNameRegex = new(@"package:\s*name='([^']+)'", RegexOptions.Compiled);
    private static readonly Regex VerNameRegex = new(@"versionName='([^']+)'", RegexOptions.Compiled);
    private static readonly Regex VerCodeRegex = new(@"versionCode='(\d+)'", RegexOptions.Compiled);
    private static readonly Regex LabelLangRegex = new(@"application-label-\w+:'([^']+)'", RegexOptions.Compiled);
    private static readonly Regex LabelRegex = new(@"application-label:'([^']+)'", RegexOptions.Compiled);
    private static readonly Regex DpiSplitRegex = new(@"(ldpi|mdpi|hdpi|xhdpi|xxhdpi|xxxhdpi|nodpi|\d+dpi)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LangSplitRegex = new(@"config\.[a-z]{2}($|[_-])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex InstalledVerRegex = new(@"versionCode=(\d+)", RegexOptions.Compiled);
    private static readonly Regex ProgressRegex = new(@"(\d+)%", RegexOptions.Compiled);
    private static readonly Regex FailureRegex = new(@"Failure \[([^\]]+)\]", RegexOptions.Compiled);

    private readonly Action _onClose;
    private readonly ObservableCollection<ApkPackage> _packages = new();
    private CancellationTokenSource? _cts;
    private bool _isInstalling;
    private bool _proWarningShown;

    public ObservableCollection<ApkPackage> Packages => _packages;

    private readonly EventHandler<LicenseStateChangedEventArgs> _onLicenseChanged;

    public ApkInstaller(Action onClose)
    {
        _onClose = onClose;
        InitializeComponent();
        DataContext = this;
        _packages.CollectionChanged += (_, _) => UpdateUI();

        _onLicenseChanged = (_, _) => Dispatcher.BeginInvoke(UpdateUI);
        LicenseService.Instance.StateChanged += _onLicenseChanged;
        Unloaded += (_, _) => LicenseService.Instance.StateChanged -= _onLicenseChanged;
    }

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
        Path.GetExtension(path).ToLowerInvariant() is ".apk" or ".xapk" or ".apks" or ".apkm";

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
                    DisplayName = Path.GetFileName(file),
                    PackageName = Strings.ApkInstaller_Parse_Failed,
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
        Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".apk" => ParseSingleApk(filePath),
            ".xapk" or ".apks" or ".apkm" => ParseBundleFile(filePath),
            _ => null
        };

    private static ApkPackage ParseSingleApk(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
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
        var tempDir = Path.Combine(Path.GetTempPath(), "ZephyrsElixir_APK", Guid.NewGuid().ToString("N"));
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

            var manifestPath = Path.Combine(tempDir, "manifest.json");
            if (File.Exists(manifestPath))
                ParseJsonManifest(manifestPath, package, isXapk: true);

            var infoPath = Path.Combine(tempDir, "info.json");
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
                    package.DisplayName = !string.IsNullOrEmpty(info.Label) ? info.Label : Path.GetFileNameWithoutExtension(filePath);
                    package.VersionName = !string.IsNullOrEmpty(info.VersionName) ? info.VersionName : "1.0";
                    package.VersionCode = info.VersionCode;
                }
            }

            package.ApkFiles = SelectOptimalSplitApks(allApks, package);
            package.DisplayName ??= Path.GetFileNameWithoutExtension(filePath);

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
            var match = apks.FirstOrDefault(a => Path.GetFileName(a).Equals(name, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }

        return apks.FirstOrDefault(a =>
        {
            var n = Path.GetFileNameWithoutExtension(a).ToLowerInvariant();
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
        var info = new ApkInfo { PackageName = Path.GetFileNameWithoutExtension(apkPath) };
        if (!File.Exists(Aapt2Path)) return info;

        try
        {
            using var process = new Process
            {
                StartInfo = AdbExecutor.CreateStartInfo(Aapt2Path, $"dump badging \"{apkPath}\"")
            };

            process.Start();
            // Drain stderr concurrently: aapt2 floods it on malformed APKs, and a full pipe with
            // no reader deadlocks the stdout ReadToEnd below.
            _ = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(5000))
            {
                try { process.Kill(true); } catch { }
            }

            var pkgMatch = PkgNameRegex.Match(output);
            if (pkgMatch.Success) info = info with { PackageName = pkgMatch.Groups[1].Value };

            var verNameMatch = VerNameRegex.Match(output);
            if (verNameMatch.Success) info = info with { VersionName = verNameMatch.Groups[1].Value };

            var verCodeMatch = VerCodeRegex.Match(output);
            if (verCodeMatch.Success && int.TryParse(verCodeMatch.Groups[1].Value, out var vc))
                info = info with { VersionCode = vc };

            var labelMatch = LabelLangRegex.Match(output);
            if (!labelMatch.Success)
                labelMatch = LabelRegex.Match(output);
            if (labelMatch.Success) info = info with { Label = labelMatch.Groups[1].Value };
        }
        catch { }

        return info;
    }

    private static PackageType GetPackageType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".apk" => PackageType.Apk,
            ".xapk" => PackageType.Xapk,
            ".apks" => PackageType.Apks,
            ".apkm" => PackageType.Apkm,
            _ => PackageType.Apk
        };

    private string[] SelectOptimalSplitApks(string[] allApks, ApkPackage package)
    {
        var selected = new List<string>();
        var deviceInfo = GetDeviceInfo();

        foreach (var apk in allApks)
        {
            var fileName = Path.GetFileNameWithoutExtension(apk).ToLowerInvariant();

            // Specific before generic: bundletool names every split "base-<qualifier>", so testing the
            // base fallback first would wave every architecture and density through unfiltered.
            if (IsArchSplit(fileName)) { if (MatchesArch(fileName, deviceInfo.Abi)) selected.Add(apk); continue; }
            if (IsDpiSplit(fileName)) { if (MatchesDpi(fileName, deviceInfo.Dpi)) selected.Add(apk); continue; }
            if (IsLangSplit(fileName)) { if (MatchesLang(fileName, deviceInfo.Lang)) selected.Add(apk); continue; }
            if (IsBaseApk(fileName)) { selected.Add(apk); continue; }
            if (fileName.Contains("config.")) continue;

            selected.Add(apk);
        }

        if (!selected.Any(s => IsArchSplit(Path.GetFileNameWithoutExtension(s))))
            selected.AddRange(allApks.Where(a => IsArchSplit(Path.GetFileNameWithoutExtension(a))));

        if (!selected.Any(s => IsDpiSplit(Path.GetFileNameWithoutExtension(s))))
        {
            var dpiApk = FindClosestDpiApk(allApks, deviceInfo.Dpi);
            if (dpiApk != null) selected.Add(dpiApk);
        }

        if (!selected.Any(s => IsLangSplit(Path.GetFileNameWithoutExtension(s))))
        {
            var langApk = allApks.FirstOrDefault(a => Path.GetFileNameWithoutExtension(a).Contains(".en", StringComparison.OrdinalIgnoreCase))
                ?? allApks.FirstOrDefault(a => IsLangSplit(Path.GetFileNameWithoutExtension(a)));
            if (langApk != null) selected.Add(langApk);
        }

        package.SplitInfo = string.Format(Strings.ApkInstaller_Split_Selected,
            selected.Count, allApks.Length, deviceInfo.Abi, deviceInfo.Dpi, deviceInfo.Lang);
        return selected.Distinct().ToArray();
    }

    private static bool IsBaseApk(string n) => n.Contains("base") || n == "app" || (!n.Contains("split") && !n.Contains("config"));
    private static bool IsArchSplit(string n) => n.Contains("arm64") || n.Contains("armeabi") || n.Contains("x86") || n.Contains("mips");
    private static bool IsDpiSplit(string n) => DpiSplitRegex.IsMatch(n);
    private static bool IsLangSplit(string n) => LangSplitRegex.IsMatch(n);

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
            .Select(d => apks.FirstOrDefault(a => Path.GetFileNameWithoutExtension(a).Contains(d.name, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(a => a != null);
    }

    private static (string Serial, DeviceConfig Config)? _deviceConfig;

    /// <summary>
    /// ABI, density and locale in one batched shell round-trip, remembered per device. This is asked
    /// once per dropped file when picking splits, and none of the three can change between files.
    /// </summary>
    private static DeviceConfig GetDeviceInfo()
    {
        var serial = DeviceManager.Instance.ActiveSerial;
        if (_deviceConfig is { } cached && cached.Serial == serial) return cached.Config;

        var config = ReadDeviceConfig();
        _deviceConfig = (serial, config);
        return config;
    }

    private static DeviceConfig ReadDeviceConfig()
    {
        const string Sep = "ZE-SEP";
        try
        {
            var sections = AdbExecutor.ExecuteCommand(
                $"shell \"getprop ro.product.cpu.abi; echo {Sep}; wm density; echo {Sep}; getprop persist.sys.locale\"")
                .Split(Sep, StringSplitOptions.None);

            if (sections.Length < 3) return DeviceConfig.Fallback;

            var abi = sections[0].Trim();
            var dpiStr = sections[1].Replace("Physical density:", "").Trim();
            var dpi = int.TryParse(dpiStr, out var d) ? d : 480;
            var lang = sections[2].Split('-', '_').FirstOrDefault()?.Trim() ?? "en";

            return string.IsNullOrEmpty(abi) ? DeviceConfig.Fallback : new DeviceConfig(abi, dpi, lang);
        }
        catch { return DeviceConfig.Fallback; }
    }

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
        finally
        {
            // Only a terminal state is done with its files. A cancel puts the package back to Pending
            // for the next run, and deleting what was extracted would leave nothing to install.
            if (package.Status is InstallStatus.Success or InstallStatus.Updated or InstallStatus.Failed)
            {
                package.Progress = 100;
                CleanupTempFiles(package);
            }
            else package.Progress = 0;
        }
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
            var output = await AdbExecutor.ExecuteCommandAsync($"shell \"dumpsys package {packageName} | grep versionCode\"");
            var match = InstalledVerRegex.Match(output);
            return match.Success && int.TryParse(match.Groups[1].Value, out var v) ? v : 0;
        }
        catch { return 0; }
    }

    private async Task<(bool Success, string Output)> ExecuteAdbInstallAsync(string args, ApkPackage package, CancellationToken token)
    {
        var output = new StringBuilder();

        // This install runs its own adb process (for live progress streaming), so the -s target
        // AdbExecutor normally injects must be added here too: with several devices connected a
        // bare "adb install" fails with "more than one device/emulator".
        var serial = DeviceManager.Instance.ActiveSerial;
        if (!string.IsNullOrEmpty(serial)) args = $"-s {serial} {args}";

        var adbPath = AdbExecutor.GetAdbPath();
        using var process = new Process
        {
            StartInfo = AdbExecutor.CreateStartInfo(adbPath, args)
        };

        await using var _ = token.Register(() => { try { process.Kill(true); } catch { } });

        // The two handlers run on separate pool threads and adb writes progress and warnings to both
        // streams at once, so the buffer they share is guarded.
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            lock (output) output.AppendLine(e.Data);
            if (e.Data.Contains("%"))
            {
                var match = ProgressRegex.Match(e.Data);
                if (match.Success && int.TryParse(match.Groups[1].Value, out var p))
                    Dispatcher.BeginInvoke(() => package.Progress = p);
            }
        };

        process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // WaitForExitAsync also waits for both redirected streams to reach EOF. The Exited event
        // does not: adb exits 0 while printing "Failure [INSTALL_FAILED_…]", so completing on
        // Exited could read an empty buffer and report a rejected install as a success.
        await process.WaitForExitAsync(token);
        token.ThrowIfCancellationRequested();

        string result;
        lock (output) result = output.ToString();
        return (process.ExitCode == 0 && !result.Contains("Failure", StringComparison.OrdinalIgnoreCase), result);
    }

    private static string ParseInstallError(string output)
    {
        if (AdbErrorCatalog.Explain(output) is { } explanation) return explanation;
        var match = FailureRegex.Match(output);
        return match.Success ? match.Groups[1].Value : "Installation failed";
    }

    private static void CleanupTempFiles(ApkPackage package)
    {
        if (string.IsNullOrEmpty(package.TempExtractPath)) return;
        try { if (Directory.Exists(package.TempExtractPath)) Directory.Delete(package.TempExtractPath, true); } catch { }
    }

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
            InstallButton.Tag = isStop ? "close" : "download";
        });
    }
}

public enum PackageType { Apk, Xapk, Apks, Apkm }
public enum InstallStatus { Pending, Installing, Success, Updated, Failed }

public record struct DeviceConfig(string Abi, int Dpi, string Lang)
{
    /// <summary>What split selection assumes when the device cannot be read.</summary>
    public static DeviceConfig Fallback => new("arm64-v8a", 480, "en");
}

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

    public string SizeDisplay => UIHelpers.FormatBytes(Size);

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
        InstallStatus.Pending => AppBrushes.Pending,
        InstallStatus.Installing => AppBrushes.Installing,
        InstallStatus.Success => AppBrushes.Success,
        InstallStatus.Updated => AppBrushes.Updated,
        InstallStatus.Failed => AppBrushes.Failed,
        _ => Brushes.White
    };

    public string TypeIcon => PackageType switch
    {
        PackageType.Apk => "library",
        _ => "document-add"
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
        PackageType.Apk => AppBrushes.GradientBlue,
        PackageType.Xapk => AppBrushes.GradientGreen,
        PackageType.Apks => AppBrushes.GradientAmber,
        PackageType.Apkm => AppBrushes.GradientPurple,
        _ => AppBrushes.GradientDefault
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
