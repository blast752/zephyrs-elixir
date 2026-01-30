namespace ZephyrsElixir
{
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

            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;

            InitLogging();
            await LicenseService.Instance.InitializeAsync();

            var win = new MainWindow();
            MainWindow = win;
            win.Show();
            win.Activate();

            GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized);
        }

        private static void InitLogging()
        {
            try
            {
                var log = AdbLogger.Instance;
                var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                var adbPath = AdbExecutor.GetAdbPath();

                log.LogInfo("System", "═══════════════════════════════════════════════════════════════");
                log.LogInfo("System", "  Zephyr's Elixir - Application Started");
                log.LogInfo("System", "═══════════════════════════════════════════════════════════════");
                log.LogInfo("System", $"Version: {ver} | {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                log.LogInfo("System", $"OS: {Environment.OSVersion} | .NET: {Environment.Version} | x64: {Environment.Is64BitProcess}");
                log.LogInfo("System", $"ADB: {adbPath} | Available: {File.Exists(adbPath)}");
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Logging init failed: {ex.Message}"); }
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
            e.Handled = MessageBox.Show($"Error:\n\n{e.Exception.Message}\n\nContinue?", "Error", MessageBoxButton.YesNo, MessageBoxImage.Error, MessageBoxResult.Yes, MessageBoxOptions.DefaultDesktopOnly) == MessageBoxResult.Yes;
            if (!e.Handled) Shutdown(1);
        }

        private void OnDomainException(object s, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                Log(ex, "Domain");
                if (e.IsTerminating) MessageBox.Show($"Fatal error:\n\n{ex.Message}", "Fatal", MessageBoxButton.OK, MessageBoxImage.Stop, MessageBoxResult.OK, MessageBoxOptions.DefaultDesktopOnly);
            }
        }

        private void OnTaskException(object? s, UnobservedTaskExceptionEventArgs e) { Log(e.Exception, "Task"); e.SetObserved(); }

        private static void Log(Exception ex, string src) { try { AdbLogger.Instance.LogException(src, ex); } catch { } }

        protected override void OnExit(ExitEventArgs e) { _instance = null; base.OnExit(e); }
    }
}