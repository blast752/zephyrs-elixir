namespace ZephyrsElixir.Core;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DebloatMode { Uninstall, Disable }

public sealed class RecipePackage
{
    public string PackageName { get; set; } = string.Empty;
    public string? Label { get; set; }
}

public sealed class RecipeApk
{
    public string FileName { get; set; } = string.Empty;
    public string? Label { get; set; }

    // Local machine path — never exported: APKs cannot travel inside a shared recipe.
    public string? FullPath { get; set; }

    [JsonIgnore] public bool IsResolved => !string.IsNullOrEmpty(FullPath) && File.Exists(FullPath);
}

public sealed class OptimizationRecipeStep
{
    public bool Extreme { get; set; }
}

public sealed class DebloatRecipeStep
{
    public DebloatMode Mode { get; set; } = DebloatMode.Uninstall;
    public List<RecipePackage> Packages { get; set; } = new();
}

public sealed class TweaksRecipeStep
{
    public string? DnsName { get; set; }
    public string? DnsHostname { get; set; }
    public double? AnimationScale { get; set; }
    public List<string> ProPrivacy { get; set; } = new();

    [JsonIgnore] public bool IsEmpty =>
        string.IsNullOrEmpty(DnsHostname) && AnimationScale is null && ProPrivacy.Count == 0;
}

public sealed class InstallRecipeStep
{
    public List<RecipeApk> Apks { get; set; } = new();
}

public sealed class Recipe
{
    public const int CurrentSchemaVersion = 1;
    public const string FileExtension = ".zerecipe";

    /// <summary>The recipe entry of a <see cref="Microsoft.Win32.FileDialog.Filter"/>; callers append their own.</summary>
    public const string FileDialogFilter = "Zephyr's Recipe (*" + FileExtension + ")|*" + FileExtension;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Glyph { get; set; } = RecipeStyle.DefaultGlyph;
    public string Accent { get; set; } = RecipeStyle.DefaultAccent;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public OptimizationRecipeStep? Optimization { get; set; }
    public DebloatRecipeStep? Debloat { get; set; }
    public TweaksRecipeStep? Tweaks { get; set; }
    public InstallRecipeStep? Install { get; set; }

    public int TimesApplied { get; set; }
    public string? CommunityId { get; set; }

    [JsonIgnore] public bool HasOptimization => Optimization is not null;
    [JsonIgnore] public bool HasDebloat => Debloat is { Packages.Count: > 0 };
    [JsonIgnore] public bool HasTweaks => Tweaks is { IsEmpty: false };
    [JsonIgnore] public bool HasInstall => Install is { Apks.Count: > 0 };
    [JsonIgnore] public bool IsEmpty => !HasOptimization && !HasDebloat && !HasTweaks && !HasInstall;

    [JsonIgnore]
    public bool RequiresPro =>
        Optimization?.Extreme == true ||
        Tweaks?.ProPrivacy.Count > 0 ||
        Install?.Apks.Count > 1;

    [JsonIgnore]
    public int StepCount =>
        (HasOptimization ? 1 : 0) + (HasDebloat ? 1 : 0) + (HasTweaks ? 1 : 0) + (HasInstall ? 1 : 0);

    public Recipe Clone()
    {
        var json = JsonSerializer.Serialize(this, RecipeJson.Options);
        return JsonSerializer.Deserialize<Recipe>(json, RecipeJson.Options)!;
    }

    /// <summary>Copy stripped of everything tied to this machine or this install.</summary>
    public Recipe ToShareable()
    {
        var copy = Clone();
        copy.TimesApplied = 0;
        copy.CommunityId = null;
        if (copy.Install is not null)
            foreach (var apk in copy.Install.Apks) apk.FullPath = null;
        return copy;
    }
}

public static class RecipeStyle
{
    public const string DefaultGlyph = "star-outline";
    public const string DefaultAccent = "blue";

    // Curated glyph+accent presets keyed to the app's gradient vocabulary; the UI maps accents
    // to AppBrushes so recipes always look native no matter who authored them.
    public static readonly IReadOnlyList<(string Glyph, string Accent)> Presets = new (string, string)[]
    {
        ("bolt", "gold"),
        ("delete", "red"),
        ("lock", "cyan"),
        ("gamepad", "purple"),
        ("checklist", "green"),
        ("star-outline", "blue")
    };

    // Recipes authored before the vector icon set stored a Segoe MDL2 code point on disk (and in the
    // community marketplace), so they are translated on read instead of being migrated in place.
    private static readonly Dictionary<string, string> LegacyGlyphs = new()
    {
        [""] = "bolt",
        [""] = "delete",
        [""] = "lock",
        [""] = "gamepad",
        [""] = "checklist",
        [""] = "star-outline"
    };

    public static string Normalize(string? glyph) =>
        string.IsNullOrWhiteSpace(glyph) ? DefaultGlyph
            : LegacyGlyphs.TryGetValue(glyph, out var key) ? key
            : glyph;
}

public static class RecipeJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}

public static class RecipeValidator
{
    public const int MaxNameLength = 60;
    public const int MaxDescriptionLength = 300;
    public const int MaxAuthorLength = 40;
    public const int MaxPackages = 400;
    public const int MaxApks = 40;

    private static readonly Regex PackageRegex = new(
        @"^[a-zA-Z][a-zA-Z0-9_]*(\.[a-zA-Z][a-zA-Z0-9_]*)+$", RegexOptions.Compiled);
    private static readonly Regex HostnameRegex = new(
        @"^[a-zA-Z0-9]([a-zA-Z0-9\-\.]{0,200})[a-zA-Z0-9]$", RegexOptions.Compiled);

    private static readonly HashSet<string> AllowedProPrivacy = new(StringComparer.Ordinal)
    {
        ProCommandIds.SafetyCore, ProCommandIds.ResetAdId, ProCommandIds.CaptivePortal,
        ProCommandIds.GoogleCoreControl, ProCommandIds.RamExpansion
    };

    public static bool IsValidPackageName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Length <= 150 && PackageRegex.IsMatch(name);

    public static bool IsValidHostname(string? hostname) =>
        !string.IsNullOrWhiteSpace(hostname) && HostnameRegex.IsMatch(hostname.Trim());

    /// <summary>Returns null when valid, otherwise a localized description of the first problem.</summary>
    public static string? Validate(Recipe recipe)
    {
        if (recipe.SchemaVersion > Recipe.CurrentSchemaVersion) return Strings.Recipes_Error_NewerSchema;
        if (string.IsNullOrWhiteSpace(recipe.Name) || recipe.Name.Length > MaxNameLength) return Strings.Recipes_Error_InvalidName;
        if (recipe.Description.Length > MaxDescriptionLength) return Strings.Recipes_Error_InvalidDescription;
        if (recipe.Author.Length > MaxAuthorLength) return Strings.Recipes_Error_InvalidAuthor;
        if (recipe.IsEmpty) return Strings.Recipes_Error_Empty;

        if (recipe.Debloat is { } debloat)
        {
            if (debloat.Packages.Count > MaxPackages) return Strings.Recipes_Error_TooManyPackages;
            if (debloat.Packages.Any(p => !IsValidPackageName(p.PackageName))) return Strings.Recipes_Error_InvalidPackage;
        }

        if (recipe.Tweaks is { } tweaks)
        {
            if (tweaks.AnimationScale is < 0 or > 2) return Strings.Recipes_Error_InvalidAnimationScale;
            if (!string.IsNullOrEmpty(tweaks.DnsHostname) && !HostnameRegex.IsMatch(tweaks.DnsHostname)) return Strings.Recipes_Error_InvalidDns;
            if (tweaks.ProPrivacy.Any(p => !AllowedProPrivacy.Contains(p))) return Strings.Recipes_Error_InvalidProOp;
        }

        if (recipe.Install is { } install)
        {
            if (install.Apks.Count > MaxApks) return Strings.Recipes_Error_TooManyApks;
            if (install.Apks.Any(a => string.IsNullOrWhiteSpace(a.FileName))) return Strings.Recipes_Error_InvalidApk;
        }

        return null;
    }
}

/// <summary>
/// Single owner of everything recipe-persistence: the local library folder, the .zerecipe
/// serialization used by library files, debloat presets and community payloads alike, and the
/// small profile (author name, liked ids) that the marketplace features share.
/// </summary>
public static class RecipeStore
{
    public static readonly string LibraryDir = Path.Combine(AppConfiguration.Paths.LocalAppDataRoot, "Recipes");
    private static readonly string ProfilePath = Path.Combine(LibraryDir, ".profile");
    private static readonly SemaphoreSlim _lock = new(1, 1);

    private sealed class Profile
    {
        public string AuthorName { get; set; } = string.Empty;
        public HashSet<string> LikedIds { get; set; } = new();
    }

    private static Profile? _profile;

    public static event Action? LibraryChanged;

    public static string FilePathFor(Recipe recipe) => Path.Combine(LibraryDir, $"{recipe.Id}{Recipe.FileExtension}");

    public static async Task<List<Recipe>> LoadAllAsync()
    {
        if (!Directory.Exists(LibraryDir)) return new();

        var recipes = new List<Recipe>();
        foreach (var file in Directory.EnumerateFiles(LibraryDir, $"*{Recipe.FileExtension}"))
        {
            var recipe = await TryReadAsync(file);
            if (recipe is not null && RecipeValidator.Validate(recipe) is null)
                recipes.Add(recipe);
        }
        return recipes.OrderByDescending(r => r.UpdatedUtc).ToList();
    }

    public static async Task<Recipe?> TryReadAsync(string path)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<Recipe>(stream, RecipeJson.Options);
        }
        catch { return null; }
    }

    public static async Task SaveAsync(Recipe recipe, bool touch = true)
    {
        await _lock.WaitAsync();
        try
        {
            if (touch) recipe.UpdatedUtc = DateTime.UtcNow;
            Directory.CreateDirectory(LibraryDir);
            var path = FilePathFor(recipe);
            var temp = path + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(recipe, RecipeJson.Options));
            File.Move(temp, path, overwrite: true);
        }
        finally { _lock.Release(); }
        LibraryChanged?.Invoke();
    }

    public static void Delete(Recipe recipe)
    {
        try { File.Delete(FilePathFor(recipe)); } catch { }
        LibraryChanged?.Invoke();
    }

    public static async Task<Recipe> DuplicateAsync(Recipe recipe)
    {
        var copy = recipe.Clone();
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = Truncate($"{recipe.Name} {Strings.Recipes_CopySuffix}", RecipeValidator.MaxNameLength);
        copy.CreatedUtc = DateTime.UtcNow;
        copy.TimesApplied = 0;
        copy.CommunityId = null;
        await SaveAsync(copy);
        return copy;
    }

    /// <summary>Imports a .zerecipe file (or a community payload) into the library as a new entry.</summary>
    public static async Task<(Recipe? Recipe, string? Error)> ImportAsync(Recipe? recipe)
    {
        if (recipe is null) return (null, Strings.Recipes_Error_Unreadable);
        if (RecipeValidator.Validate(recipe) is { } error) return (null, error);

        // The id names the file on disk and arrives straight from an untrusted payload, so anything
        // that is not one of our own identifiers is replaced rather than trusted with a path.
        if (!Guid.TryParseExact(recipe.Id, "N", out _) ||
            File.Exists(Path.Combine(LibraryDir, $"{recipe.Id}{Recipe.FileExtension}")))
            recipe.Id = Guid.NewGuid().ToString("N");

        recipe.TimesApplied = 0;
        await SaveAsync(recipe, touch: false);
        return (recipe, null);
    }

    public static async Task<(Recipe? Recipe, string? Error)> ImportFileAsync(string path)
    {
        var recipe = await TryReadAsync(path);
        if (recipe is not null) recipe.CommunityId = null;
        return await ImportAsync(recipe);
    }

    public static Task ExportAsync(Recipe recipe, string path) =>
        File.WriteAllTextAsync(path, JsonSerializer.Serialize(recipe.ToShareable(), RecipeJson.Options));

    public static async Task MarkAppliedAsync(Recipe recipe)
    {
        recipe.TimesApplied++;
        await SaveAsync(recipe, touch: false);
        if (recipe.CommunityId is { } id) RecipeCommunity.ReportUsed(id);
    }

    public static string AuthorName
    {
        get => LoadProfile().AuthorName;
        set { var p = LoadProfile(); p.AuthorName = Truncate(value?.Trim() ?? "", RecipeValidator.MaxAuthorLength); SaveProfile(p); }
    }

    public static bool HasLiked(string communityId) => LoadProfile().LikedIds.Contains(communityId);

    public static void RememberLiked(string communityId)
    {
        var p = LoadProfile();
        if (p.LikedIds.Add(communityId)) SaveProfile(p);
    }

    public static void ForgetLiked(string communityId)
    {
        var p = LoadProfile();
        if (p.LikedIds.Remove(communityId)) SaveProfile(p);
    }

    public static void SetLiked(string communityId, bool liked)
    {
        if (liked) RememberLiked(communityId);
        else ForgetLiked(communityId);
    }

    private static Profile LoadProfile()
    {
        if (_profile is not null) return _profile;
        try
        {
            _profile = File.Exists(ProfilePath)
                ? JsonSerializer.Deserialize<Profile>(File.ReadAllText(ProfilePath), RecipeJson.Options) ?? new Profile()
                : new Profile();
        }
        catch { _profile = new Profile(); }
        return _profile;
    }

    private static void SaveProfile(Profile profile)
    {
        _profile = profile;
        try
        {
            Directory.CreateDirectory(LibraryDir);
            File.WriteAllText(ProfilePath, JsonSerializer.Serialize(profile, RecipeJson.Options));
        }
        catch { }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}

public enum RecipeStepKind { Optimization, Debloat, Tweaks, Install }
public enum RecipeStepStatus { Success, Partial, Skipped, Failed }

public sealed record RecipeStepResult(RecipeStepKind Kind, RecipeStepStatus Status, string Detail);

public sealed record RecipeProgressEvent(
    string Serial, string DeviceName, string Message, double Percent, bool IsError = false, bool IsDone = false);

public sealed class RecipeDeviceReport
{
    public string Serial { get; init; } = string.Empty;
    public string DeviceName { get; init; } = string.Empty;
    public List<RecipeStepResult> Steps { get; } = new();
    public bool Canceled { get; set; }

    public RecipeStepStatus Outcome =>
        Canceled || Steps.Any(s => s.Status == RecipeStepStatus.Failed) ? RecipeStepStatus.Failed
        : Steps.Any(s => s.Status is RecipeStepStatus.Partial or RecipeStepStatus.Skipped) ? RecipeStepStatus.Partial
        : RecipeStepStatus.Success;
}

public sealed class RecipeRunReport
{
    public required Recipe Recipe { get; init; }
    public List<RecipeDeviceReport> Devices { get; } = new();
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Executes a recipe on one or more devices: steps run sequentially per device, devices run in
/// parallel. Every adb call is pinned to its device through the ambient serial, so even Pro
/// module commands (which have no serial parameter) land on the right phone.
/// </summary>
public static class RecipeRunner
{
    public static async Task<RecipeRunReport> RunAsync(
        Recipe recipe,
        IReadOnlyList<AndroidDevice> targets,
        IProgress<RecipeProgressEvent> progress,
        CancellationToken ct)
    {
        var report = new RecipeRunReport { Recipe = recipe };
        var started = DateTime.UtcNow;

        var runs = targets.Select(device => Task.Run(async () =>
        {
            AdbExecutor.AmbientSerial = device.Serial;
            return await RunDeviceAsync(recipe, device, progress, ct);
        }, CancellationToken.None)).ToList();

        report.Devices.AddRange(await Task.WhenAll(runs));
        report.Duration = DateTime.UtcNow - started;
        return report;
    }

    private static async Task<RecipeDeviceReport> RunDeviceAsync(
        Recipe recipe, AndroidDevice device, IProgress<RecipeProgressEvent> progress, CancellationToken ct)
    {
        var report = new RecipeDeviceReport { Serial = device.Serial, DeviceName = device.Name };

        await SettingsTimeMachine.CaptureAsync(SettingsTimeMachine.TriggerRecipe, device.Serial, recipe.Name, device.Name, ct);

        var steps = new List<(RecipeStepKind Kind, Func<StepContext, Task<RecipeStepResult>> Run)>();
        if (recipe.HasOptimization) steps.Add((RecipeStepKind.Optimization, ctx => RunOptimizationAsync(recipe, ctx)));
        if (recipe.HasDebloat) steps.Add((RecipeStepKind.Debloat, ctx => RunDebloatAsync(recipe, ctx)));
        if (recipe.HasTweaks) steps.Add((RecipeStepKind.Tweaks, ctx => RunTweaksAsync(recipe, ctx)));
        if (recipe.HasInstall) steps.Add((RecipeStepKind.Install, ctx => RunInstallAsync(recipe, ctx)));

        for (int i = 0; i < steps.Count; i++)
        {
            var ctx = new StepContext(device, progress, ct, i, steps.Count);
            try
            {
                ct.ThrowIfCancellationRequested();
                report.Steps.Add(await steps[i].Run(ctx));
            }
            catch (OperationCanceledException)
            {
                report.Canceled = true;
                report.Steps.Add(new RecipeStepResult(steps[i].Kind, RecipeStepStatus.Failed, Strings.Recipes_Run_Canceled));
                break;
            }
            catch (Exception ex)
            {
                ctx.Report(ex.Message, isError: true);
                report.Steps.Add(new RecipeStepResult(steps[i].Kind, RecipeStepStatus.Failed, ex.Message));
            }
        }

        var outcome = report.Outcome switch
        {
            RecipeStepStatus.Success => Strings.Recipes_Run_DeviceDone,
            RecipeStepStatus.Partial => Strings.Recipes_Run_DevicePartial,
            _ => Strings.Recipes_Run_DeviceFailed
        };
        progress.Report(new RecipeProgressEvent(device.Serial, device.Name, outcome, 100,
            IsError: report.Outcome == RecipeStepStatus.Failed, IsDone: true));

        return report;
    }

    private sealed class StepContext
    {
        private readonly IProgress<RecipeProgressEvent> _progress;
        private readonly int _index;
        private readonly int _count;
        private string _message = string.Empty;
        private double _fraction;

        public AndroidDevice Device { get; }
        public CancellationToken Ct { get; }

        public StepContext(AndroidDevice device, IProgress<RecipeProgressEvent> progress, CancellationToken ct, int index, int count)
        {
            Device = device;
            _progress = progress;
            Ct = ct;
            _index = index;
            _count = count;
        }

        // Label and fraction arrive from independent callbacks (StepChanged vs ProgressChanged): each is
        // remembered so a label update keeps the bar where it is and a progress tick keeps the last label.
        // Reporting them together stops the bar snapping back and the text flickering to a generic string.
        public void Report(string? message = null, double? fraction = null, bool isError = false)
        {
            if (message is { Length: > 0 }) _message = message;
            if (fraction is { } f) _fraction = Math.Clamp(f, 0, 1);
            _progress.Report(new RecipeProgressEvent(
                Device.Serial, Device.Name, _message,
                Math.Clamp((_index + _fraction) * 100.0 / _count, 0, 99.9), isError));
        }

        public Task<string> AdbAsync(string command) =>
            AdbExecutor.ExecuteCommandAsync(command, Ct, serial: Device.Serial);
    }

    private static async Task<RecipeStepResult> RunOptimizationAsync(Recipe recipe, StepContext ctx)
    {
        var wantsExtreme = recipe.Optimization!.Extreme;
        var extreme = wantsExtreme && Features.IsAvailable(Features.ExtremeMode);

        if (wantsExtreme && !extreme)
            ctx.Report(Strings.Recipes_Run_ExtremeSkipped);

        var engine = new OptimizationEngine(ctx.Device.Serial)
        {
            Extreme = extreme,
            StepChanged = label => ctx.Report(label),
            ProgressChanged = step => ctx.Report(fraction: (double)step / OptimizationEngine.TotalSteps)
        };

        var outcome = await engine.RunAsync(ctx.Ct);
        ctx.Ct.ThrowIfCancellationRequested();

        return outcome switch
        {
            OptimizationOutcome.Success when wantsExtreme && !extreme =>
                new(RecipeStepKind.Optimization, RecipeStepStatus.Partial, Strings.Recipes_Run_ExtremeSkipped),
            OptimizationOutcome.Success =>
                new(RecipeStepKind.Optimization, RecipeStepStatus.Success,
                    string.Format(Strings.Recipes_Run_OptimizationDetail, UIHelpers.FormatSize(engine.Report.MemoryFreedKb + engine.Report.StorageCleanedKb))),
            OptimizationOutcome.Partial =>
                new(RecipeStepKind.Optimization, RecipeStepStatus.Partial, Strings.Optimize_Console_Interrupted),
            _ => new(RecipeStepKind.Optimization, RecipeStepStatus.Failed, engine.Report.ErrorMessage ?? Strings.Recipes_Run_DeviceFailed)
        };
    }

    private static async Task<RecipeStepResult> RunDebloatAsync(Recipe recipe, StepContext ctx)
    {
        var step = recipe.Debloat!;
        int ok = 0, failed = 0, skipped = 0;
        var sdk = await DeviceApi.GetSdkAsync(ctx.Device.Serial, ctx.Ct);

        // One listing for the whole batch. Without it every entry claims to be a system app, and the
        // history then offers a restore that cannot work on a user-installed one.
        var systemPackages = step.Mode == DebloatMode.Uninstall
            ? await DeviceApi.GetSystemPackagesAsync(ctx.Device.Serial, ctx.Ct)
            : null;

        for (int i = 0; i < step.Packages.Count; i++)
        {
            ctx.Ct.ThrowIfCancellationRequested();
            var pkg = step.Packages[i];
            ctx.Report(string.Format(Strings.Debloat_Action_Processing, pkg.Label ?? pkg.PackageName), (double)i / step.Packages.Count);

            if (CloudIntelligenceManager.IsCriticalPackage(pkg.PackageName))
            {
                skipped++;
                continue;
            }

            var isSystem = systemPackages?.Contains(pkg.PackageName) == true;

            var command = step.Mode == DebloatMode.Uninstall
                ? DeviceApi.UninstallCommand(sdk, pkg.PackageName, keepForRestore: isSystem)
                : DeviceApi.DisableCommand(sdk, pkg.PackageName);

            var result = await ctx.AdbAsync(command);
            if (DeviceApi.IsSuccess(result))
            {
                ok++;
                if (step.Mode == DebloatMode.Uninstall)
                    await UninstallHistoryManager.AddEntryAsync(new HistoryItem
                    {
                        PackageName = pkg.PackageName,
                        DisplayName = pkg.Label ?? pkg.PackageName,
                        Version = string.Empty,
                        UninstallDate = DateTime.Now,
                        IsSystemApp = isSystem,
                        DeviceSerial = ctx.Device.Serial
                    });
            }
            else failed++;
        }

        var detail = string.Format(Strings.Recipes_Run_DebloatDetail, ok, failed, skipped);
        var status = failed == 0 && skipped == 0 ? RecipeStepStatus.Success
            : ok > 0 ? RecipeStepStatus.Partial
            : RecipeStepStatus.Failed;
        return new(RecipeStepKind.Debloat, status, detail);
    }

    private static async Task<RecipeStepResult> RunTweaksAsync(Recipe recipe, StepContext ctx)
    {
        var step = recipe.Tweaks!;
        var applied = new List<string>();
        var skippedPro = 0;
        var failed = 0;

        // Nothing below is tracked in the ledger unless the device confirmed it: a tracked operation
        // that never happened turns "Reset all" into a revert of something that was never applied.
        if (!string.IsNullOrEmpty(step.DnsHostname))
        {
            ctx.Report(Strings.Advanced_DNS_Title, 0.1);
            var modeSet = DeviceApi.IsSilentSuccess(await ctx.AdbAsync("shell settings put global private_dns_mode hostname"));
            var hostSet = DeviceApi.IsSilentSuccess(await ctx.AdbAsync($"shell settings put global private_dns_specifier {step.DnsHostname}"));

            if (modeSet && hostSet)
            {
                OperationLedger.Track(ctx.Device.Serial, OperationLedger.Ops.Dns);
                applied.Add($"DNS: {step.DnsName ?? step.DnsHostname}");
            }
            else failed++;
        }

        if (step.AnimationScale is { } scale)
        {
            ctx.Report(Strings.Advanced_Animations_Header, 0.3);
            var value = scale.ToString("F1", CultureInfo.InvariantCulture);

            var allSet = true;
            foreach (var key in OperationLedger.AnimationKeys)
                allSet &= DeviceApi.IsSilentSuccess(await ctx.AdbAsync($"shell settings put global {key} {value}"));

            if (allSet)
            {
                OperationLedger.Track(ctx.Device.Serial, OperationLedger.Ops.Animations);
                applied.Add($"{Strings.Recipes_Chip_Animations} {value}x");
            }
            else failed++;
        }

        for (int i = 0; i < step.ProPrivacy.Count; i++)
        {
            ctx.Ct.ThrowIfCancellationRequested();
            var commandId = step.ProPrivacy[i];
            if (!Features.IsAvailable(commandId))
            {
                skippedPro++;
                continue;
            }

            ctx.Report(string.Format(Strings.Recipes_Run_ApplyingTweak, commandId), 0.5 + 0.5 * i / step.ProPrivacy.Count);
            var result = await Pro.ExecuteAsync(commandId, ct: ctx.Ct);
            if (!result.Success) { failed++; continue; }
            if (OperationLedger.OpForProCommand(commandId) is { } op)
                OperationLedger.Track(ctx.Device.Serial, op);
            applied.Add(commandId);
        }

        var detail = skippedPro > 0
            ? string.Format(Strings.Recipes_Run_TweaksDetailSkipped, applied.Count, skippedPro)
            : string.Format(Strings.Recipes_Run_TweaksDetail, applied.Count);

        var status = applied.Count == 0 && (failed > 0 || skippedPro > 0) ? RecipeStepStatus.Failed
            : failed > 0 || skippedPro > 0 ? RecipeStepStatus.Partial
            : RecipeStepStatus.Success;
        return new(RecipeStepKind.Tweaks, status, detail);
    }

    private static async Task<RecipeStepResult> RunInstallAsync(Recipe recipe, StepContext ctx)
    {
        var apks = recipe.Install!.Apks;
        var multiAllowed = Features.IsAvailable(Features.MultiApkInstall);
        int ok = 0, failed = 0, missing = 0, skippedPro = 0;

        for (int i = 0; i < apks.Count; i++)
        {
            ctx.Ct.ThrowIfCancellationRequested();
            var apk = apks[i];

            if (i > 0 && !multiAllowed) { skippedPro++; continue; }
            if (!apk.IsResolved) { missing++; continue; }

            ctx.Report(string.Format(Strings.Recipes_Run_Installing, apk.Label ?? apk.FileName), (double)i / apks.Count);
            var result = await AdbExecutor.ExecuteTransferAsync($"install -r -g \"{apk.FullPath}\"", ctx.Ct, ctx.Device.Serial);
            if (result.Contains("Success", StringComparison.OrdinalIgnoreCase)) ok++;
            else failed++;
        }

        var detail = string.Format(Strings.Recipes_Run_InstallDetail, ok, failed, missing + skippedPro);
        var status = failed == 0 && missing == 0 && skippedPro == 0 ? RecipeStepStatus.Success
            : ok > 0 ? RecipeStepStatus.Partial
            : RecipeStepStatus.Failed;
        return new(RecipeStepKind.Install, status, detail);
    }
}
