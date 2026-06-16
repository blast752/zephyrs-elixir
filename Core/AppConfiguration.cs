namespace ZephyrsElixir.Core;

public static class AppConfiguration
{
    #region Build & installer manifest — NOT consumed by app code at runtime
    // These values mirror what authoritatively lives in ZephyrsElixir.csproj / ZephyrsElixir.Pro.csproj,
    // ZephyrsElixirInstaller_V2.iss and Build-ZephyrsElixir.ps1. C# cannot share a const with Inno Setup
    // or the PowerShell build script, so nothing in this region is read at runtime — it is a single-place
    // reference that must be kept in sync manually when releasing.

    public static class Version
    {
        public const string Application = "0.7.5";
        public const string ApplicationFull = "0.7.5.0";
        public const string Pro = "1.0.2";
        public const string ProFull = "1.0.2.0";
        public const string InstallerProduct = "0.7.5.0";
    }

    public static class Identity
    {
        public const string AppNameShort = "Zephyrs Elixir";
        public const string AppMutex = "ZephyrsElixir";
        public const string AppPublisher = "Blast752";
        public const string AppDescription = "Android optimizer for all";
        public const string InstallerGuid = "{BAE3DDCA-B420-4266-97FA-A8FFB3545777}";
        public const string ProDllName = "ZephyrsElixir.Pro.dll";
    }

    public static class Installer
    {
        public const string FirewallRuleInbound = "ZephyrsElixir ADB Inbound";
        public const string FirewallRuleOutbound = "ZephyrsElixir ADB Outbound";
        public const string RegUninstallKeyV1 = "Zephyrs Elixir_is1";
    }

    #endregion

    public static class Paths
    {
        // --- Runtime paths (consumed by the app) ---
        public static readonly string AppDataDir = "ZephyrsElixir";

        public static readonly string LocalAppDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppDataDir);

        public static readonly string ToolsDir = "Tools";
        public static readonly string AdbDir = $@"{ToolsDir}\adb";

        public static readonly string ScrcpyDir = Path.Combine(LocalAppDataRoot, "scrcpy");
        public static readonly string ScrcpyExe = Path.Combine(ScrcpyDir, "scrcpy.exe");
        public static readonly string ScrcpyVersionFile = Path.Combine(ScrcpyDir, ".version");
        public static readonly string ScrcpyZipName = "scrcpy.zip";

        public static readonly string BaseOutputDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            AppDataDir);
        public static readonly string RecordingsDir = Path.Combine(BaseOutputDir, "Recordings");
        public static readonly string ScreenshotsDir = Path.Combine(BaseOutputDir, "Screenshots");

        // --- Build & installer reference — NOT consumed at runtime; mirror of Build-ZephyrsElixir.ps1 / .iss ---
        public const string SolutionRoot = @"C:\Users\Administrator\Desktop\PortableGit\ZE";

        public static readonly string CsprojMain = $@"{SolutionRoot}\ZephyrsElixir\ZephyrsElixir.csproj";
        public static readonly string CsprojPro = $@"{SolutionRoot}\ZephyrsElixir.Pro\ZephyrsElixir.Pro.csproj";
        public static readonly string ProBinDir = $@"{SolutionRoot}\ZephyrsElixir.Pro\bin\Release\net8.0-windows\win-x64";
        public static readonly string ProDllDestDir = @"C:\Users\Administrator\Desktop\PortableGit\elixirsite\pro";
        public static readonly string LicenseIndexJs = @"C:\Users\Administrator\Desktop\PortableGit\elixirsite\api\license\index.js";
        public static readonly string AdbInstallDir = @"{app}\Tools\adb";

        public const string DefaultInstallDir = @"{commonpf}\Zephyrs Elixir";
        public const string InstallerOutputDir = "InstallerOutput";
        public const string OutputBaseFilenameTemplate = "ZephyrsElixir_x64_v";
        public const string NetTargetV1 = "net6.0-windows";
        public static readonly string SourcePatternV1 = $@"bin\Release\{NetTargetV1}\win-x64\publish\*";
        public const string NetTargetV2 = "net8.0-windows";
        public static readonly string SourcePatternV2 = $@"bin\Release\{NetTargetV2}\win-x64\publish\*";
    }

    public static class Urls
    {
        public const string ScrcpyGitHubApi = "https://api.github.com/repos/Genymobile/scrcpy/releases/latest";
        public const string HttpUserAgent = "ZephyrsElixir";
        public const string HttpAcceptHeader = "application/vnd.github+json";
        public const string CloudApiAnalyzeFull = "https://elixirsite.vercel.app/api/analyze";
        public const string ZephyrAgentVersion = "https://zephyrselixir.com/agent-version.txt";
        public const string ZephyrUpdateJson = "https://zephyrselixir.com/zupdate.json";
    }

    // Single source of truth for legal documents (version, date, public URLs, controller).
    public static class Legal
    {
        public const string DocumentsVersion = "1.0";
        public const string EffectiveDate    = "2026-06-15";
        public const string EulaVersion      = "1.0";   // bump to force EULA re-acceptance
        public const string PrivacyUrl       = "https://zephyrselixir.com/privacy";
        public const string TermsUrl         = "https://zephyrselixir.com/terms";
        public const string ControllerName   = "blast752";
        public const string ContactEmail     = "zephyrselixir@gmail.com";
    }

    public static class Application
    {
        public const string Name = "Zephyr's Elixir";
        public const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss";
    }

    public static class Common
    {
        public const int WindowStyleDwmCornerPreference = 33;
        public const int WindowStyleDwmDarkMode = 20;
        public const int WindowStyleDwmCaptionColor = 35;
        public const int WindowStyleDwmBorderColor = 34;

        public const int WmNcCalcSize = 0x0083;
        public const int WmNcHitTest = 0x0084;
        public const int WmDpiChanged = 0x02E0;
        public const int WmGetMinMaxInfo = 0x0024;
        public const int WmDisplayChange = 0x007E;

        public const uint MonitorDefaultToNearest = 0x00000002;

        public const int HtClient = 1;
        public const int HtCaption = 2;
        public const int HtLeft = 10;
        public const int HtRight = 11;
        public const int HtTop = 12;
        public const int HtTopLeft = 13;
        public const int HtTopRight = 14;
        public const int HtBottom = 15;
        public const int HtBottomLeft = 16;
        public const int HtBottomRight = 17;

        public const uint SwpNoZOrder = 0x0004;
        public const uint SwpNoActivate = 0x0010;
        public const uint SwpNoCopyBits = 0x0100;
        public const uint SwpReposition = SwpNoZOrder | SwpNoActivate | SwpNoCopyBits;
    }

    public static class Window
    {
        public const double DefaultWidth = 1400;
        public const double DefaultHeight = 900;
        public const double MinWidth = 1100;
        public const double MinHeight = 700;
        public const double AbsoluteMinWidth = 640;
        public const double AbsoluteMinHeight = 480;

        public const double CornerRadius = 8;
        public const double CaptionHeight = 40;
        public const double GlassFrameThickness = 1;
        public const double ResizeBorderThickness = 8;
        public const double CaptionButtonWidth = 46;
        public const double CaptionButtonCount = 3;
        public const double CaptionButtonsAreaWidth = CaptionButtonWidth * CaptionButtonCount;

        public static readonly System.Windows.CornerRadius WindowCornerRadius = new(CornerRadius);
        public static readonly System.Windows.Thickness WindowGlassFrame = new(GlassFrameThickness);
        public static readonly System.Windows.Thickness WindowResizeBorder = new(ResizeBorderThickness);

        public const string SidebarDefaultKey = "Home";
        public const string HelpScreenKey = "Help";

        public static readonly System.Windows.Media.Color TitleBarBg = System.Windows.Media.Color.FromRgb(12, 21, 40);
        public static readonly System.Windows.Media.Color BorderColor = System.Windows.Media.Color.FromRgb(26, 34, 56);

        public const string ScrcpyWindowTitle = "Zephyr's Elixir — Screen Mirror";
    }
}
