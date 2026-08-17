namespace ZephyrsElixir;
public partial class App : Application
{
    private static App? _instance;

    public App()
    {
        if (_instance != null) throw new InvalidOperationException("App instance exists");
        _instance = this;
        ConfigureExceptionHandling();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

        InitLogging();

        var win = new MainWindow();
        MainWindow = win;
        win.Show();
        win.Activate();

        // SVG icons are only needed once the File Manager is opened, and every one of them is
        // frozen on load, so warming the cache off the startup path costs the window nothing.
        _ = Task.Run(AppIcons.Preload);

        // First-run legal acceptance (EULA). Blocks until accepted; declining exits the app.
        if (!LegalConsent.IsAccepted())
        {
            if (DialogService.Instance.ShowEula(win)) LegalConsent.Accept();
            else { Shutdown(); return; }
        }

        await LicenseService.Instance.InitializeAsync();
        ProLoader.EnsureLoaded();

        LicenseService.Instance.StateChanged += OnLicenseStateForProReload;

        await Updater.CheckForUpdatesAsync(win);
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized);
    }

    private static void OnLicenseStateForProReload(object? sender, LicenseStateChangedEventArgs e)
    {
        if (e.Reason == LicenseChangeReason.ProDllChanged &&
            e.NewState.DllState == ProDllState.Ready &&
            e.NewState.IsActive &&
            !ProLoader.IsLoaded)
        {
            Current?.Dispatcher.BeginInvoke(() =>
            {
                ProLoader.ReloadIfNeeded();
            });
        }
    }

    private static void InitLogging()
    {
        try
        {
            var log = AdbLogger.Instance;
            var ver = Assembly.GetExecutingAssembly().GetName().Version;
            var adbPath = AdbExecutor.GetAdbPath();

            log.LogInfo("System", $"{AppConfiguration.Application.Name} - Application Started");
            var now = DateTime.Now.ToString(AppConfiguration.Application.DateTimeFormat, CultureInfo.InvariantCulture);
            log.LogInfo("System", $"Version: {ver} | {now}");
            log.LogInfo("System", $"OS: {Environment.OSVersion} | .NET: {Environment.Version} | x64: {Environment.Is64BitProcess}");
            log.LogInfo("System", $"ADB: {adbPath} | Available: {File.Exists(adbPath)}");
        }
        catch (Exception ex) { Debug.WriteLine($"Logging init failed: {ex.Message}"); }
    }

    private void ConfigureExceptionHandling()
    {
        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainException;
        TaskScheduler.UnobservedTaskException += OnTaskException;
    }

    private void OnDispatcherException(object s, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception, "UI Thread");
        try
        {
            var config = new DialogConfig
            {
                Title = TranslationManager.Instance["Dialog_Title_Error"],
                Message = $"{e.Exception.Message}\n\n{TranslationManager.Instance["App_Error_Continue"]}",
                Type = DialogType.Error,
                Owner = MainWindow as Window,
                Buttons = new[]
                {
                    new DialogButton(TranslationManager.Instance["Common_Button_No"], DialogAction.No, ButtonStyle.Secondary),
                    new DialogButton(TranslationManager.Instance["Common_Button_Yes"], DialogAction.Yes, ButtonStyle.Primary)
                }
            };
            var dialog = UnifiedDialog.Create(config);
            dialog.ShowDialog();
            e.Handled = dialog.Result == DialogAction.Yes;
        }
        catch
        {
            e.Handled = false;
        }
        if (!e.Handled) Shutdown(1);
    }

    private void OnDomainException(object s, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            Log(ex, "Domain");
            if (e.IsTerminating)
            {
                try
                {
                    DialogService.Instance.ShowInfoDirect(
                        TranslationManager.Instance["Dialog_Title_Error"],
                        $"{TranslationManager.Instance["App_Error_Fatal"]}\n\n{ex.Message}");
                }
                catch { }
            }
        }
    }

    private void OnTaskException(object? s, UnobservedTaskExceptionEventArgs e) { Log(e.Exception, "Task"); e.SetObserved(); }

    private static void Log(Exception ex, string src) { try { AdbLogger.Instance.LogException(src, ex); } catch { } }

    protected override void OnExit(ExitEventArgs e)
    {
        LicenseService.Instance.StateChanged -= OnLicenseStateForProReload;
        ProLoader.Unload();
        _instance = null;
        base.OnExit(e);
    }
}
