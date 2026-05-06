namespace ZephyrsElixir.Core;

public static class AppConfiguration
{
    public static class Version
    {
        public const string Application = "0.6.5";
        public const string ApplicationFull = "0.6.5.0";
        public const string Pro = "1.0.1";
        public const string ProFull = "1.0.1.0";
        public const string InstallerProduct = "0.6.5.0";
    }

    public static class Identity
    {
        public const string AppName = "Zephyr's Elixir";
        public const string AppNameShort = "Zephyrs Elixir";
        public const string AppMutex = "ZephyrsElixir";
        public const string AppPublisher = "Blast752";
        public const string AppDescription = "Android optimizer for all";
        public const string InstallerGuid = "{BAE3DDCA-B420-4266-97FA-A8FFB3545777}";
        public const string ProDllName = "ZephyrsElixir.Pro.dll";
    }

    public static class Paths
    {
        public const string SolutionRoot = @"C:\Users\Administrator\Documents\ZephyrsElixir";

        public static readonly string CsprojMain = $@"{SolutionRoot}\ZephyrsElixir\ZephyrsElixir.csproj";
        public static readonly string CsprojPro = $@"{SolutionRoot}\ZephyrsElixir.Pro\ZephyrsElixir.Pro.csproj";
        public static readonly string ProBinDir = $@"{SolutionRoot}\ZephyrsElixir.Pro\bin\Release\net8.0-windows\win-x64";
        public static readonly string ProDllDestDir = @"C:\Users\Administrator\Desktop\PortableGit\elixirsite\pro";
        public static readonly string LicenseIndexJs = @"C:\Users\Administrator\Desktop\PortableGit\elixirsite\api\license\index.js";

        public static readonly string ToolsDir = "Tools";
        public static readonly string AdbDir = $@"{ToolsDir}\adb";
        public static readonly string AdbInstallDir = @"{app}\Tools\adb";

        public static readonly string AppDataDir = "ZephyrsElixir";
        public static readonly string ScrcpyDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppDataDir, "scrcpy");
        public static readonly string ScrcpyExe = System.IO.Path.Combine(ScrcpyDir, "scrcpy.exe");
        public static readonly string ScrcpyVersionFile = System.IO.Path.Combine(ScrcpyDir, ".version");
        public static readonly string ScrcpyZipName = "scrcpy.zip";

        public static readonly string BaseOutputDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            AppDataDir);
        public static readonly string RecordingsDir = System.IO.Path.Combine(BaseOutputDir, "Recordings");
        public static readonly string ScreenshotsDir = System.IO.Path.Combine(BaseOutputDir, "Screenshots");

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

    public static class Limits
    {
        public const int HttpClientTimeoutSeconds = 15;
        public const int HttpClientDownloadTimeoutMinutes = 5;
        public const int MirrorStartDelayMs = 500;
        public const int RecordingStartDelayMs = 300;

        public const int BitrateMin = 2;
        public const int BitrateMax = 50;
        public const int BitrateDefault = 8;

        public const int AudioBitrateMin = 32;
        public const int AudioBitrateMax = 320;
        public const int AudioBitrateDefault = 128;

        public const int DisplayBufferMin = 0;
        public const int DisplayBufferMax = 200;
        public const int DisplayBufferDefault = 50;

        public const int ScrcpyPollDelayMs = 1000;
        public const int PostKillWaitMs = 1000;

        public const int AdbDefaultTimeout = 30000;
    }

    public static class Application
    {
        public const string Name = "Zephyr's Elixir";
        public const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss";
    }

    public static class Colors
    {
        public const byte RgbTitleBarRed = 12;
        public const byte RgbTitleBarGreen = 21;
        public const byte RgbTitleBarBlue = 40;

        public const byte RgbWindowBorderRed = 26;
        public const byte RgbWindowBorderGreen = 34;
        public const byte RgbWindowBorderBlue = 56;
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

        public const uint SetWindowPosNoCopyBits = 0x0002;
    }

    public static class Window
    {
        public const double DefaultWidth = 1400;
        public const double DefaultHeight = 900;
        public const double MinWidth = 1100;
        public const double MinHeight = 700;

        public const double CornerRadiusValue = 8;
        public const double CornerRadius = 8;
        public const double TitleBarHeight = 40;
        public const double SidebarWidth = 280;
        public const double BorderThickness = 1;
        public const double ResizeBorderThickness = 8;

        public const double CaptionHeight = 40;

        public const double GlassFrameThickness = 1;
        public static readonly System.Windows.CornerRadius WindowCornerRadius = new(8);
        public static readonly System.Windows.Thickness WindowGlassFrame = new(1);
        public static readonly System.Windows.Thickness WindowResizeBorder = new(8);

        public const string MinTrackSizeKey = "MinTrackSize";
        public const string TitleBarBackgroundColor = "#FF0C1528";
        public const string WindowBackgroundColor = "#FF0E1A33";
        public const string SidebarBackgroundColor = "#FF0A1224";
        public const string WindowBorderColor = "#FF1A2238";

        public const string SidebarDefaultKey = "Home";
        public const string HelpScreenKey = "Help";

        public static readonly System.Windows.Media.Color TitleBarBg = System.Windows.Media.Color.FromRgb(12, 21, 40);
        public static readonly System.Windows.Media.Color BorderColor = System.Windows.Media.Color.FromRgb(26, 34, 56);

        public const int DwmCornerPreference = 2;
        public const int DwmDarkMode = 1;
        public const int DwmCaptionColorAttr = 35;
        public const int DwmBorderColorAttr = 34;
        public const int DwmCornerAttr = 33;
        public const int DwmDarkModeAttr = 20;

        public const string ScrcpyWindowTitle = "Zephyr's Elixir \u2014 Screen Mirror";
    }

    public static class ScreenMirror
    {
        public const string DeviceScreenshotPrefix = "/sdcard/screenshot_temp_";
        public const string ScreenshotFilenameFormat = "screenshot_{0:yyyyMMdd_HHmmss}.png";
        public const string RecordingFilenameFormat = "recording_{0:yyyyMMdd_HHmmss}.{1}";

        public static readonly string[] ResolutionOptions = { "720", "1080", "1280", "1440", "2160", "0" };
        public static readonly int[] FpsOptions = { 30, 60, 90, 120, 144 };

        public const string DefaultVideoCodec = "h264";
        public const string DefaultAudioCodec = "opus";
        public const string DefaultRecordFormat = "mp4";
        public const string DefaultQualityPreset = "balanced";
        public const string DefaultKeyboardMode = "sdk";
        public const string DefaultMouseMode = "sdk";
        public const string DefaultOrientation = "auto";
    }

    public static class Installer
    {
        public const string FirewallRuleInbound = "ZephyrsElixir ADB Inbound";
        public const string FirewallRuleOutbound = "ZephyrsElixir ADB Outbound";
        public const string RegUninstallKeyV1 = "Zephyrs Elixir_is1";
    }

    public static class ScreenMirrorSettings
    {
        public const int ResolutionDefaultIndex = 2;
        public const int QualityDefaultIndex = 1;
        public const int VideoCodecDefaultIndex = 0;
        public const int FpsDefaultIndex = 1;
        public const int AudioCodecDefaultIndex = 0;
        public const int RecordFormatDefaultIndex = 0;
        public const int KeyboardModeDefaultIndex = 0;
        public const int MouseModeDefaultIndex = 0;
        public const int OrientationDefaultIndex = 0;

        public const bool StayAwakeDefault = true;
        public const bool AudioForwardDefault = true;
        public const bool ClipboardSyncDefault = true;

        public const string ScrcpyArgsDelimiter = " ";
    }
}