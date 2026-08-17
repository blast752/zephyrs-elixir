namespace ZephyrsElixir.UI.Pages;

public sealed partial class Recipes : UserControl
{
    private static readonly (string CommandId, Func<string> Title)[] ProPrivacyOps =
    {
        (ProCommandIds.SafetyCore, () => Strings.Advanced_SafetyCore_Title),
        (ProCommandIds.ResetAdId, () => Strings.Advanced_ResetAdId_Title),
        (ProCommandIds.CaptivePortal, () => Strings.Advanced_CaptivePortal_Title),
        (ProCommandIds.GoogleCoreControl, () => Strings.Advanced_GoogleCoreControl_Title),
        (ProCommandIds.RamExpansion, () => Strings.Advanced_RamExpansion_Title)
    };

    private readonly ObservableCollection<RecipeCardViewModel> _library = new();
    private readonly ObservableCollection<CommunityRecipeViewModel> _community = new();
    private readonly ObservableCollection<RecipePackage> _editorPackages = new();
    private readonly ObservableCollection<RecipeApk> _editorApks = new();
    private readonly ObservableCollection<RecipeTargetViewModel> _targets = new();
    private readonly ObservableCollection<RecipeDeviceProgressViewModel> _deviceProgress = new();
    private readonly ObservableCollection<RunApkChoiceViewModel> _runApkChoices = new();
    private readonly ObservableCollection<AppInfoViewModel> _pickerApps = new();
    private readonly ICollectionView _pickerView;

    private readonly List<RadioButton> _glyphButtons = new();
    private readonly List<(string CommandId, CheckBox Toggle)> _proToggles = new();
    private readonly List<double?> _animValues = new() { null, 0, 0.5, 1.0 };

    private readonly GamificationViewModel _gamification = new();
    private readonly DispatcherTimer _tipTimer = new() { Interval = TimeSpan.FromSeconds(7) };
    private int _tipIndex;

    private readonly DispatcherTimer _searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(400) };

    private Recipe? _editing;
    private Recipe? _running;
    private CancellationTokenSource? _runCts;
    private CancellationTokenSource? _communityCts;
    private CancellationTokenSource? _pickerCts;
    private bool _communityLoaded;
    private bool _runInProgress;

    public Recipes()
    {
        InitializeComponent();

        LibraryList.ItemsSource = _library;
        CommunityList.ItemsSource = _community;
        PackagesList.ItemsSource = _editorPackages;
        ApksList.ItemsSource = _editorApks;
        TargetsList.ItemsSource = _targets;
        DeviceProgressList.ItemsSource = _deviceProgress;
        RunApkChoiceList.ItemsSource = _runApkChoices;

        _pickerView = CollectionViewSource.GetDefaultView(_pickerApps);
        _pickerView.Filter = o => o is AppInfoViewModel a && MatchesPickerSearch(a);
        PickerList.ItemsSource = _pickerView;

        BuildGlyphPicker();
        BuildProPrivacyToggles();
        BuildTweakCombos();

        _searchDebounce.Tick += (_, _) => { _searchDebounce.Stop(); _ = LoadCommunityAsync(); };
        _editorPackages.CollectionChanged += (_, _) =>
            PackagesEmptyHint.Visibility = _editorPackages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        BrewmasterBar.DataContext = _gamification;
        _gamification.Tip = RecipeTips.All.FirstOrDefault() ?? string.Empty;
        _tipTimer.Tick += OnTipTick;

        RecipeStore.LibraryChanged += OnLibraryChanged;
        TranslationManager.Instance.LanguageChanged += OnLanguageChanged;
        Loaded += OnLoad;
        Unloaded += OnUnload;
    }

    private void OnTipTick(object? sender, EventArgs e)
    {
        var tips = RecipeTips.All;
        if (tips.Count == 0) return;
        _tipIndex = (_tipIndex + 1) % tips.Count;
        AnimationHelpers.CrossFade(TipText, () => _gamification.Tip = tips[_tipIndex]);
    }

    #region Lifecycle

    private void OnLoad(object sender, RoutedEventArgs e)
    {
        _tipTimer.Start();
        _ = LoadLibraryAsync();
    }

    private void OnUnload(object sender, RoutedEventArgs e)
    {
        _communityCts?.Cancel();
        _pickerCts?.Cancel();
        _searchDebounce.Stop();
        _tipTimer.Stop();
    }

    private void OnLibraryChanged() => Dispatcher.BeginInvoke(() => _ = LoadLibraryAsync());

    private void OnLanguageChanged(object? sender, EventArgs e) => BuildTweakCombos();

    private async Task LoadLibraryAsync()
    {
        var recipes = await RecipeStore.LoadAllAsync();
        _library.Clear();
        foreach (var recipe in recipes) _library.Add(new RecipeCardViewModel(recipe));

        var hasRecipes = _library.Count > 0;
        LibraryEmptyState.Visibility = hasRecipes ? Visibility.Collapsed : Visibility.Visible;
        LibraryScroll.Visibility = hasRecipes ? Visibility.Visible : Visibility.Collapsed;

        _gamification.Update(
            recipes: _library.Count,
            brews: _library.Sum(v => v.Model.TimesApplied),
            shared: _library.Count(v => v.Model.CommunityId is not null));

        var barWasHidden = BrewmasterBar.Visibility != Visibility.Visible;
        BrewmasterBar.Visibility = hasRecipes ? Visibility.Visible : Visibility.Collapsed;
        if (hasRecipes && barWasHidden) AnimationHelpers.FadeSlideIn(BrewmasterBar, fromY: -12);

        PageHeaderControl.Subtitle = string.Format(Strings.Recipes_Subtitle_Count, _library.Count);
    }

    private void Flash(string message) => PageHeaderControl.Subtitle = message;

    #endregion

    #region Tabs

    private void OnTabChanged(object sender, RoutedEventArgs e)
    {
        if (LibraryPanel is null || CommunityPanel is null) return;

        var community = CommunityTab.IsChecked == true;
        LibraryPanel.Visibility = community ? Visibility.Collapsed : Visibility.Visible;
        CommunityPanel.Visibility = community ? Visibility.Visible : Visibility.Collapsed;

        if (community && !_communityLoaded) _ = LoadCommunityAsync();
    }

    #endregion

    #region Library actions

    private static RecipeCardViewModel? CardOf(object sender) =>
        (sender as FrameworkElement)?.DataContext as RecipeCardViewModel;

    private void OnNewClick(object sender, RoutedEventArgs e) => OpenEditor(null);

    private void OnEditRecipeClick(object sender, RoutedEventArgs e)
    {
        if (CardOf(sender) is { } vm) OpenEditor(vm.Model);
    }

    private async void OnDuplicateRecipeClick(object sender, RoutedEventArgs e)
    {
        if (CardOf(sender) is not { } vm) return;
        var copy = await RecipeStore.DuplicateAsync(vm.Model);
        Flash(string.Format(Strings.Recipes_Status_Duplicated, copy.Name));
    }

    private async void OnExportRecipeClick(object sender, RoutedEventArgs e)
    {
        if (CardOf(sender) is not { } vm) return;

        var dialog = new SaveFileDialog
        {
            Title = Strings.Recipes_Export_Title,
            FileName = $"{vm.Model.Name.SanitizeFileName()}{Recipe.FileExtension}",
            DefaultExt = Recipe.FileExtension,
            Filter = $"Zephyr's Recipe (*{Recipe.FileExtension})|*{Recipe.FileExtension}"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            await RecipeStore.ExportAsync(vm.Model, dialog.FileName);
            Flash(string.Format(Strings.Recipes_Status_Exported, dialog.FileName));
        }
        catch (Exception ex)
        {
            DialogService.Instance.ShowInfoDirect(Strings.Dialog_Title_Error, ex.Message, Window.GetWindow(this));
        }
    }

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Strings.Recipes_Import_Title,
            Filter = $"Zephyr's Recipe (*{Recipe.FileExtension})|*{Recipe.FileExtension}|All Files (*.*)|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog() != true) return;

        int imported = 0;
        string? lastError = null;
        foreach (var file in dialog.FileNames)
        {
            var (recipe, error) = await RecipeStore.ImportFileAsync(file);
            if (recipe is not null) imported++;
            else lastError = error;
        }

        if (imported > 0) Flash(string.Format(Strings.Recipes_Status_Imported, imported));
        if (lastError is not null)
            DialogService.Instance.ShowInfoDirect(Strings.Dialog_Title_Warning, lastError, Window.GetWindow(this));
    }

    private void OnDeleteRecipeClick(object sender, RoutedEventArgs e)
    {
        if (CardOf(sender) is not { } vm) return;
        var recipe = vm.Model;

        if (!DialogService.Instance.ConfirmDirect(
                string.Format(Strings.Recipes_Delete_Confirm, recipe.Name),
                Window.GetWindow(this), Strings.MessageBox_ConfirmAction_Title))
            return;

        // A recipe this device published also lives on the community server: take it down in the same
        // action so deleting locally can't orphan it there. Reuses the dedicated unpublish call, and is
        // fire-and-forget — the row goes at once and the server ignores it for recipes we don't own.
        if (recipe.CommunityId is { } communityId)
            RecipeCommunity.UnpublishInBackground(communityId);

        RecipeStore.Delete(recipe);
    }

    private async void OnShareRecipeClick(object sender, RoutedEventArgs e)
    {
        if (CardOf(sender) is not { } vm) return;
        var recipe = vm.Model;

        // A published recipe: the same button becomes "remove from community".
        if (recipe.CommunityId is { } communityId)
        {
            await UnpublishAsync(vm, recipe, communityId);
            return;
        }

        if (string.IsNullOrWhiteSpace(recipe.Author))
            recipe.Author = RecipeStore.AuthorName;

        if (string.IsNullOrWhiteSpace(recipe.Author))
        {
            DialogService.Instance.ShowInfo("Recipes_Share_NeedAuthor", Window.GetWindow(this));
            OpenEditor(recipe);
            return;
        }

        if (!DialogService.Instance.Confirm("Recipes_Share_Confirm", Window.GetWindow(this), "Recipes_Share_Title"))
            return;

        try
        {
            Flash(Strings.Recipes_Status_Uploading);
            var id = await RecipeCommunity.UploadAsync(recipe);
            if (id is null) throw new InvalidOperationException(Strings.Recipes_Community_Offline);

            recipe.CommunityId = id;
            await RecipeStore.SaveAsync(recipe, touch: false);
            vm.NotifyShareChanged();
            Flash(string.Format(Strings.Recipes_Status_Shared, recipe.Name));
        }
        catch (Exception ex)
        {
            Flash(string.Empty);
            DialogService.Instance.ShowInfoDirect(Strings.Dialog_Title_Error,
                $"{Strings.Recipes_Share_Failed}\n{ex.Message}", Window.GetWindow(this));
        }
    }

    private async Task UnpublishAsync(RecipeCardViewModel vm, Recipe recipe, string communityId)
    {
        if (!DialogService.Instance.Confirm("Recipes_Unpublish_Confirm", Window.GetWindow(this), "Recipes_Unpublish_Title"))
            return;

        try
        {
            Flash(Strings.Recipes_Status_Unpublishing);
            await RecipeCommunity.UnpublishAsync(communityId);

            recipe.CommunityId = null;
            await RecipeStore.SaveAsync(recipe, touch: false);
            vm.NotifyShareChanged();
            Flash(string.Format(Strings.Recipes_Status_Unpublished, recipe.Name));
        }
        catch (Exception ex)
        {
            Flash(string.Empty);
            DialogService.Instance.ShowInfoDirect(Strings.Dialog_Title_Error,
                $"{Strings.Recipes_Unpublish_Failed}\n{ex.Message}", Window.GetWindow(this));
        }
    }

    #endregion

    #region Editor

    private void BuildGlyphPicker()
    {
        foreach (var (glyph, accent) in RecipeStyle.Presets)
        {
            var button = new RadioButton
            {
                Style = (Style)FindResource("Recipes.Style.GlyphOption"),
                GroupName = "RecipeGlyph",
                Background = RecipeAccents.BrushFor(accent),
                Content = glyph,
                Tag = accent,
                Margin = new Thickness(0, 0, 10, 0)
            };
            _glyphButtons.Add(button);
            GlyphPanel.Children.Add(button);
        }
    }

    private void BuildProPrivacyToggles()
    {
        foreach (var (commandId, title) in ProPrivacyOps)
        {
            var toggle = new CheckBox
            {
                Style = (Style)FindResource("App.Style.ToggleSwitch"),
                Tag = commandId,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };

            var label = new TextBlock
            {
                Text = title(),
                FontSize = 13,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };

            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.Children.Add(label);
            row.Children.Add(toggle);

            _proToggles.Add((commandId, toggle));
            ProPrivacyPanel.Children.Add(row);
        }
    }

    private void BuildTweakCombos()
    {
        var dnsIndex = DnsCombo.SelectedIndex;
        var animIndex = AnimCombo.SelectedIndex;

        DnsCombo.ItemsSource = new[] { Strings.Recipes_Tweaks_DontChange }
            .Concat(AppConfiguration.Dns.Providers.Select(p => p.Name)).ToList();
        AnimCombo.ItemsSource = new[]
        {
            Strings.Recipes_Tweaks_DontChange,
            Strings.Advanced_Animation_Off,
            "0.5x",
            $"1.0x ({Strings.Advanced_Animation_Default})"
        };

        DnsCombo.SelectedIndex = dnsIndex >= 0 ? dnsIndex : 0;
        AnimCombo.SelectedIndex = animIndex >= 0 ? animIndex : 0;

        foreach (var (commandId, toggle) in _proToggles)
        {
            var title = ProPrivacyOps.First(op => op.CommandId == commandId).Title();
            if (((Grid)toggle.Parent).Children[0] is TextBlock label) label.Text = title;
        }
    }

    private void OpenEditor(Recipe? recipe)
    {
        _editing = recipe;

        EditorTitle.Text = recipe is null ? Strings.Recipes_Editor_TitleNew : Strings.Recipes_Editor_TitleEdit;
        EditorName.Text = recipe?.Name ?? string.Empty;
        EditorDescription.Text = recipe?.Description ?? string.Empty;
        EditorAuthor.Text = recipe?.Author is { Length: > 0 } author ? author : RecipeStore.AuthorName;
        EditorError.Visibility = Visibility.Collapsed;
        PkgError.Visibility = Visibility.Collapsed;
        PkgInput.Text = string.Empty;

        var accent = recipe?.Accent ?? RecipeStyle.DefaultAccent;
        foreach (var button in _glyphButtons)
            button.IsChecked = (string)button.Tag == accent;
        if (_glyphButtons.All(b => b.IsChecked != true)) _glyphButtons[^1].IsChecked = true;

        OptEnable.IsChecked = recipe?.HasOptimization == true;
        ExtremeCheck.IsChecked = recipe?.Optimization?.Extreme == true;
        ApplyProGate(ExtremeCheck, Features.ExtremeMode);

        DebEnable.IsChecked = recipe?.HasDebloat == true;
        DebModeUninstall.IsChecked = recipe?.Debloat?.Mode != DebloatMode.Disable;
        DebModeDisable.IsChecked = recipe?.Debloat?.Mode == DebloatMode.Disable;
        _editorPackages.Clear();
        foreach (var pkg in recipe?.Debloat?.Packages ?? Enumerable.Empty<RecipePackage>())
            _editorPackages.Add(new RecipePackage { PackageName = pkg.PackageName, Label = pkg.Label });
        PackagesEmptyHint.Visibility = _editorPackages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        TweaksEnable.IsChecked = recipe?.HasTweaks == true;
        var dnsHostname = recipe?.Tweaks?.DnsHostname;
        var providerIndex = AppConfiguration.Dns.Providers.ToList().FindIndex(p => p.Hostname == dnsHostname);
        DnsCombo.SelectedIndex = providerIndex >= 0 ? providerIndex + 1 : 0;
        AnimCombo.SelectedIndex = Math.Max(0, _animValues.FindIndex(v => v == recipe?.Tweaks?.AnimationScale));
        foreach (var (commandId, toggle) in _proToggles)
        {
            toggle.IsChecked = recipe?.Tweaks?.ProPrivacy.Contains(commandId) == true;
            ApplyProGate((FrameworkElement)toggle.Parent, commandId);
        }

        InstEnable.IsChecked = recipe?.HasInstall == true;
        _editorApks.Clear();
        foreach (var apk in recipe?.Install?.Apks ?? Enumerable.Empty<RecipeApk>())
            _editorApks.Add(new RecipeApk { FileName = apk.FileName, FullPath = apk.FullPath, Label = apk.Label });
        ApkTierHint.Visibility = Features.IsAvailable(Features.MultiApkInstall) ? Visibility.Collapsed : Visibility.Visible;

        ShowOverlay(EditorOverlay);
    }

    private void OnEditorCancelClick(object sender, RoutedEventArgs e) => HideOverlays();

    private async void OnEditorSaveClick(object sender, RoutedEventArgs e)
    {
        var recipe = _editing?.Clone() ?? new Recipe();
        recipe.Name = EditorName.Text.Trim();
        recipe.Description = EditorDescription.Text.Trim();
        recipe.Author = EditorAuthor.Text.Trim();

        var accent = (string?)_glyphButtons.FirstOrDefault(b => b.IsChecked == true)?.Tag ?? RecipeStyle.DefaultAccent;
        recipe.Accent = accent;
        recipe.Glyph = RecipeStyle.Presets.First(p => p.Accent == accent).Glyph;

        recipe.Optimization = OptEnable.IsChecked == true
            ? new OptimizationRecipeStep { Extreme = ExtremeCheck.IsChecked == true }
            : null;

        recipe.Debloat = DebEnable.IsChecked == true && _editorPackages.Count > 0
            ? new DebloatRecipeStep
            {
                Mode = DebModeDisable.IsChecked == true ? DebloatMode.Disable : DebloatMode.Uninstall,
                Packages = _editorPackages.Select(p => new RecipePackage { PackageName = p.PackageName, Label = p.Label }).ToList()
            }
            : null;

        recipe.Tweaks = TweaksEnable.IsChecked == true ? BuildTweaksStep() : null;
        if (recipe.Tweaks?.IsEmpty == true) recipe.Tweaks = null;

        recipe.Install = InstEnable.IsChecked == true && _editorApks.Count > 0
            ? new InstallRecipeStep
            {
                Apks = _editorApks.Select(a => new RecipeApk { FileName = a.FileName, FullPath = a.FullPath, Label = a.Label }).ToList()
            }
            : null;

        if (RecipeValidator.Validate(recipe) is { } error)
        {
            EditorError.Text = error;
            EditorError.Visibility = Visibility.Visible;
            return;
        }

        if (recipe.Author.Length > 0) RecipeStore.AuthorName = recipe.Author;
        await RecipeStore.SaveAsync(recipe);
        HideOverlays();
        Flash(string.Format(Strings.Recipes_Status_Saved, recipe.Name));
    }

    private TweaksRecipeStep BuildTweaksStep()
    {
        var step = new TweaksRecipeStep();

        if (DnsCombo.SelectedIndex > 0)
        {
            var (name, hostname) = AppConfiguration.Dns.Providers[DnsCombo.SelectedIndex - 1];
            step.DnsName = name;
            step.DnsHostname = hostname;
        }

        step.AnimationScale = _animValues[Math.Max(0, AnimCombo.SelectedIndex)];
        step.ProPrivacy = _proToggles.Where(t => t.Toggle.IsChecked == true).Select(t => t.CommandId).ToList();
        return step;
    }

    // Pro selectors follow the app-wide gate (same result as h:LicenseGuard, minus its device coupling
    // since a recipe is authored offline): greyed and non-interactive unless the licence AND the Pro
    // module are present. Features.IsAvailable already means exactly that, so a missing/expired licence
    // or a physically removed Pro DLL locks the control — nothing Pro can be armed here.
    private static void ApplyProGate(FrameworkElement element, string featureId)
    {
        var available = Features.IsAvailable(featureId);
        element.IsEnabled = available;
        element.Opacity = available ? 1.0 : 0.5;
    }

    private void OnPkgInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { AddPackageFromInput(); e.Handled = true; }
    }

    private void OnAddPackageClick(object sender, RoutedEventArgs e) => AddPackageFromInput();

    private void AddPackageFromInput()
    {
        var pkg = PkgInput.Text.Trim();
        if (!RecipeValidator.IsValidPackageName(pkg))
        {
            PkgError.Visibility = Visibility.Visible;
            return;
        }

        PkgError.Visibility = Visibility.Collapsed;
        if (_editorPackages.All(p => p.PackageName != pkg))
            _editorPackages.Add(new RecipePackage { PackageName = pkg });
        PkgInput.Text = string.Empty;
    }

    private void OnRemovePackageClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is RecipePackage pkg)
            _editorPackages.Remove(pkg);
    }

    private void OnAddApkClick(object sender, RoutedEventArgs e)
    {
        // Any tier can bundle several APKs into a recipe; the one-at-a-time Free limit is applied at
        // run time, where the user picks which APK to install. Batch install is the Pro upgrade.
        var dialog = new OpenFileDialog
        {
            Title = Strings.Recipes_AddApk_Title,
            Filter = "Android Packages|*.apk|All Files|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog() != true) return;

        foreach (var file in dialog.FileNames)
        {
            if (_editorApks.Any(a => a.FullPath == file)) continue;
            _editorApks.Add(new RecipeApk
            {
                FileName = Path.GetFileName(file),
                FullPath = file,
                Label = Path.GetFileNameWithoutExtension(file)
            });
        }
    }

    private void OnRemoveApkClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is RecipeApk apk)
            _editorApks.Remove(apk);
    }

    #endregion

    #region Device app picker

    private bool MatchesPickerSearch(AppInfoViewModel app)
    {
        var search = PickerSearch.Text.Trim();
        return string.IsNullOrEmpty(search) ||
               app.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               app.PackageName.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private void OnPickerSearchChanged(object sender, TextChangedEventArgs e) => _pickerView.Refresh();

    private async void OnFromDeviceClick(object sender, RoutedEventArgs e)
    {
        if (!DeviceManager.Instance.IsConnected)
        {
            DialogService.Instance.ShowInfo("Recipes_Picker_NoDevice", Window.GetWindow(this));
            return;
        }

        _pickerCts?.Cancel();
        _pickerCts = new CancellationTokenSource();
        var ct = _pickerCts.Token;

        _pickerApps.Clear();
        PickerSearch.Text = string.Empty;
        PickerLoading.Visibility = Visibility.Visible;
        PickerOverlay.Visibility = Visibility.Visible;

        try
        {
            var progress = new Progress<string>(msg => PickerStatus.Text = msg);
            if (!await ZephyrsAgent.EnsureAgentIsRunningAsync(progress, ct)) return;

            var apps = await ZephyrsAgent.GetInstalledAppsAsync(progress, ct);
            if (ct.IsCancellationRequested) return;

            var existing = _editorPackages.Select(p => p.PackageName).ToHashSet();
            foreach (var app in apps)
            {
                if (existing.Contains(app.PackageName)) continue;
                if (CloudIntelligenceManager.IsCriticalPackage(app.PackageName)) continue;
                _pickerApps.Add(new AppInfoViewModel { Name = app.Name, PackageName = app.PackageName, Version = app.Version, State = app.State });
            }
        }
        catch (Exception ex)
        {
            PickerStatus.Text = ex.Message;
        }
        finally
        {
            if (!ct.IsCancellationRequested)
                PickerLoading.Visibility = _pickerApps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OnPickerCancelClick(object sender, RoutedEventArgs e)
    {
        _pickerCts?.Cancel();
        PickerOverlay.Visibility = Visibility.Collapsed;
    }

    private void OnPickerAddClick(object sender, RoutedEventArgs e)
    {
        foreach (var app in _pickerApps.Where(a => a.IsSelected))
            if (_editorPackages.All(p => p.PackageName != app.PackageName))
                _editorPackages.Add(new RecipePackage { PackageName = app.PackageName, Label = app.Name });

        _pickerCts?.Cancel();
        PickerOverlay.Visibility = Visibility.Collapsed;
    }

    #endregion

    #region Run

    private void OnRunRecipeClick(object sender, RoutedEventArgs e)
    {
        if (CardOf(sender) is not { } vm) return;
        _running = vm.Model;

        RunTitle.Text = vm.Model.Name;
        RunSubtitle.Text = string.Join("  ·  ", RecipeChips.For(vm.Model));

        _targets.Clear();
        foreach (var device in DeviceManager.Instance.Devices)
            _targets.Add(new RecipeTargetViewModel
            {
                Device = device,
                IsSelected = device.IsAuthorized && device.Serial == DeviceManager.Instance.ActiveSerial
            });

        RunProNote.Visibility = vm.Model.RequiresPro && !LicenseService.Instance.IsPro ? Visibility.Visible : Visibility.Collapsed;
        RunDebloatNote.Visibility = vm.Model.HasDebloat ? Visibility.Visible : Visibility.Collapsed;
        PrepareApkChoice(vm.Model);

        RunSetupPanel.Visibility = Visibility.Visible;
        RunProgressPanel.Visibility = Visibility.Collapsed;
        RunSummaryText.Visibility = Visibility.Collapsed;
        RunStartButton.Visibility = Visibility.Visible;
        RunStartButton.IsEnabled = _targets.Any(t => t.IsEnabled);
        RunCancelButton.Visibility = Visibility.Visible;
        RunStopButton.Visibility = Visibility.Collapsed;
        RunDoneButton.Visibility = Visibility.Collapsed;
        RunCloseX.Visibility = Visibility.Visible;

        if (_targets.Count == 0)
            RunSubtitle.Text = Strings.Recipes_Run_NoDevices;

        ShowOverlay(RunOverlay);
    }

    // Free installs one APK per run: surface every bundled APK and let the user pick which single one
    // to install. Pro — and any recipe with a single APK — skip the chooser and install as authored.
    private void PrepareApkChoice(Recipe recipe)
    {
        _runApkChoices.Clear();
        var needsChoice = recipe.Install is { Apks.Count: > 1 } && !Features.IsAvailable(Features.MultiApkInstall);
        if (needsChoice)
        {
            foreach (var apk in recipe.Install!.Apks)
                _runApkChoices.Add(new RunApkChoiceViewModel { Apk = apk });
            (_runApkChoices.FirstOrDefault(c => c.IsResolved) ?? _runApkChoices[0]).IsSelected = true;
        }
        RunApkChoice.Visibility = needsChoice ? Visibility.Visible : Visibility.Collapsed;
    }

    // With the chooser active, run a deep clone carrying only the selected APK so the pipeline is
    // otherwise identical; Pro and single-APK runs pass the recipe through untouched.
    private Recipe EffectiveRecipeFor(Recipe recipe)
    {
        if (RunApkChoice.Visibility != Visibility.Visible || recipe.Install is null) return recipe;

        var index = _runApkChoices.ToList().FindIndex(c => c.IsSelected);
        var clone = recipe.Clone();
        index = Math.Clamp(index, 0, clone.Install!.Apks.Count - 1);
        clone.Install.Apks = new List<RecipeApk> { clone.Install.Apks[index] };
        return clone;
    }

    private async void OnRunStartClick(object sender, RoutedEventArgs e)
    {
        if (_running is not { } recipe) return;

        var targets = _targets.Where(t => t.IsSelected && t.IsEnabled).Select(t => t.Device).ToList();
        if (targets.Count == 0)
        {
            RunSubtitle.Text = Strings.Recipes_Run_SelectAtLeastOne;
            return;
        }

        _runInProgress = true;
        _runCts = new CancellationTokenSource();

        _deviceProgress.Clear();
        foreach (var device in targets)
            _deviceProgress.Add(new RecipeDeviceProgressViewModel
            {
                Serial = device.Serial,
                Name = device.Name,
                Message = Strings.Recipes_Run_Waiting
            });

        RunSetupPanel.Visibility = Visibility.Collapsed;
        RunProgressPanel.Visibility = Visibility.Visible;
        RunStartButton.Visibility = Visibility.Collapsed;
        RunCancelButton.Visibility = Visibility.Collapsed;
        RunCloseX.Visibility = Visibility.Collapsed;
        RunStopButton.Visibility = Visibility.Visible;

        var progress = new Progress<RecipeProgressEvent>(evt =>
        {
            var vm = _deviceProgress.FirstOrDefault(d => d.Serial == evt.Serial);
            if (vm is null) return;
            vm.Message = evt.Message;
            vm.Percent = evt.Percent;
            if (evt.IsDone) { vm.IsDone = true; vm.IsError = evt.IsError; vm.Percent = 100; }
        });

        RecipeRunReport report;
        try
        {
            report = await RecipeRunner.RunAsync(EffectiveRecipeFor(recipe), targets, progress, _runCts.Token);
        }
        catch (Exception ex)
        {
            RunSummaryText.Text = ex.Message;
            RunSummaryText.Visibility = Visibility.Visible;
            RunStopButton.Visibility = Visibility.Collapsed;
            RunDoneButton.Visibility = Visibility.Visible;
            RunCloseX.Visibility = Visibility.Visible;
            return;
        }
        finally
        {
            _runInProgress = false;
            _runCts.Dispose();
            _runCts = null;
        }

        // A brew only counts (locally and for the community "used" ping) when at least one device
        // actually got through it — a canceled or fully failed run is not an application.
        if (report.Devices.Any(d => d.Outcome is RecipeStepStatus.Success or RecipeStepStatus.Partial))
        {
            try { await RecipeStore.MarkAppliedAsync(recipe); }
            catch (Exception ex) { AdbLogger.Instance.LogWarning("Recipes", $"MarkApplied failed: {ex.Message}"); }
        }

        var summary = new StringBuilder();
        foreach (var device in report.Devices)
        {
            summary.AppendLine($"{device.DeviceName}:");
            foreach (var step in device.Steps)
                summary.AppendLine($"   {StepName(step.Kind)} — {step.Detail}");
        }
        RunSummaryText.Text = summary.ToString().TrimEnd();
        RunSummaryText.Visibility = Visibility.Visible;

        RunStopButton.Visibility = Visibility.Collapsed;
        RunDoneButton.Visibility = Visibility.Visible;
        RunCloseX.Visibility = Visibility.Visible;
        Flash(string.Format(Strings.Recipes_Status_RunFinished, recipe.Name));
    }

    private static string StepName(RecipeStepKind kind) => kind switch
    {
        RecipeStepKind.Optimization => Strings.Recipes_Step_Optimization,
        RecipeStepKind.Debloat => Strings.Recipes_Step_Debloat,
        RecipeStepKind.Tweaks => Strings.Recipes_Step_Tweaks,
        _ => Strings.Recipes_Step_Install
    };

    private void OnRunStopClick(object sender, RoutedEventArgs e) => _runCts?.Cancel();

    private void OnRunCloseClick(object sender, RoutedEventArgs e)
    {
        if (_runInProgress) return;
        HideOverlays();
    }

    #endregion

    #region Community

    private CommunitySort SelectedSort =>
        SortRecent.IsChecked == true ? CommunitySort.Recent
        : SortUsed.IsChecked == true ? CommunitySort.MostUsed
        : CommunitySort.Top;

    private void OnCommunitySortChanged(object sender, RoutedEventArgs e)
    {
        if (_communityLoaded) _ = LoadCommunityAsync();
    }

    private void OnCommunitySearchChanged(object sender, TextChangedEventArgs e)
    {
        if (!_communityLoaded) return;
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private void OnCommunityRefreshClick(object sender, RoutedEventArgs e) => _ = LoadCommunityAsync();

    private async Task LoadCommunityAsync()
    {
        _communityCts?.Cancel();
        _communityCts = new CancellationTokenSource();
        var ct = _communityCts.Token;

        _communityLoaded = true;
        _community.Clear();
        CommunityLoading.Visibility = Visibility.Visible;
        CommunityError.Visibility = Visibility.Collapsed;
        CommunityEmpty.Visibility = Visibility.Collapsed;

        try
        {
            var sort = SelectedSort;
            var results = await RecipeCommunity.BrowseAsync(sort, CommunitySearchBox.Text, ct);
            if (ct.IsCancellationRequested) return;

            for (int i = 0; i < results.Count; i++)
                _community.Add(new CommunityRecipeViewModel(results[i], sort == CommunitySort.Top ? i + 1 : 0));

            CommunityLoading.Visibility = Visibility.Collapsed;
            CommunityEmpty.Visibility = _community.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception)
        {
            if (ct.IsCancellationRequested) return;
            CommunityLoading.Visibility = Visibility.Collapsed;
            CommunityError.Visibility = Visibility.Visible;
        }
    }

    private static CommunityRecipeViewModel? CommunityCardOf(object sender) =>
        (sender as FrameworkElement)?.DataContext as CommunityRecipeViewModel;

    private async void OnLikeClick(object sender, RoutedEventArgs e)
    {
        if (CommunityCardOf(sender) is not { } vm) return;

        vm.IsBusy = true;
        try
        {
            var (likes, liked) = await RecipeCommunity.ToggleLikeAsync(vm.Model.Id);
            vm.Likes = likes;
            vm.HasLiked = liked;
        }
        catch (Exception ex)
        {
            Flash(ex.Message);
        }
        finally { vm.IsBusy = false; }
    }

    private async void OnGetCommunityClick(object sender, RoutedEventArgs e)
    {
        if (CommunityCardOf(sender) is not { } vm) return;

        vm.IsBusy = true;
        try
        {
            var recipe = await RecipeCommunity.DownloadAsync(vm.Model.Id);
            var (imported, error) = await RecipeStore.ImportAsync(recipe);
            if (imported is null) throw new InvalidOperationException(error ?? Strings.Recipes_Error_Unreadable);

            vm.BumpDownloads();
            Flash(string.Format(Strings.Recipes_Status_Downloaded, imported.Name));
        }
        catch (Exception ex)
        {
            DialogService.Instance.ShowInfoDirect(Strings.Dialog_Title_Error, ex.Message, Window.GetWindow(this));
        }
        finally { vm.IsBusy = false; }
    }

    #endregion

    #region Overlay helpers

    private void ShowOverlay(UIElement overlay)
    {
        OverlayContainer.Visibility = Visibility.Visible;
        EditorOverlay.Visibility = ReferenceEquals(overlay, EditorOverlay) ? Visibility.Visible : EditorOverlay.Visibility;
        RunOverlay.Visibility = ReferenceEquals(overlay, RunOverlay) ? Visibility.Visible : Visibility.Collapsed;
        if (!ReferenceEquals(overlay, PickerOverlay)) PickerOverlay.Visibility = Visibility.Collapsed;
        if (ReferenceEquals(overlay, RunOverlay)) EditorOverlay.Visibility = Visibility.Collapsed;
    }

    private void HideOverlays()
    {
        EditorOverlay.Visibility = Visibility.Collapsed;
        RunOverlay.Visibility = Visibility.Collapsed;
        PickerOverlay.Visibility = Visibility.Collapsed;
        OverlayContainer.Visibility = Visibility.Collapsed;
    }

    #endregion
}
