namespace ZephyrsElixir.UI.Helpers;

#region Static Helpers

public static class UIHelpers
{
    public static LinearGradientBrush CreateGradientBrush(string hexColor1, string hexColor2)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops =
            {
                new GradientStop((Color)ColorConverter.ConvertFromString(hexColor1), 0),
                new GradientStop((Color)ColorConverter.ConvertFromString(hexColor2), 1)
            }
        };
        brush.Freeze();
        return brush;
    }

    public static string ToPascalCase(string snakeCase) =>
        string.Concat(snakeCase.Split('_')
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    public static string FormatSize(double kb) => kb switch
    {
        >= 1024 * 1024 => $"{kb / (1024.0 * 1024.0):F2} GB",
        >= 1024        => $"{kb / 1024.0:F1} MB",
        > 0            => $"{kb:F0} KB",
        _              => "0 KB"
    };

    public static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):F2} GB",
        >= 1024 * 1024         => $"{bytes / (1024.0 * 1024):F1} MB",
        >= 1024                => $"{bytes / 1024.0:F1} KB",
        _                      => $"{bytes} B"
    };

    public static SolidColorBrush FrozenSolid(byte r, byte g, byte b, byte a = 255)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}

#region Centralized Shell Utilities

public static class ShellUtils
{
    public static bool OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            try { AdbLogger.Instance.LogWarning("Shell", $"Failed to open URL: {ex.Message}"); } catch { }
            return false;
        }
    }

    public static bool OpenUrl(Uri? uri) => OpenUrl(uri?.AbsoluteUri);
}

#endregion

public static class AppBrushes
{
    public static readonly SolidColorBrush Pending    = UIHelpers.FrozenSolid(160, 176, 200);
    public static readonly SolidColorBrush Installing = UIHelpers.FrozenSolid(255, 208, 0);
    public static readonly SolidColorBrush Success    = UIHelpers.FrozenSolid(0, 214, 143);
    public static readonly SolidColorBrush Updated    = UIHelpers.FrozenSolid(0, 191, 255);
    public static readonly SolidColorBrush Failed     = UIHelpers.FrozenSolid(255, 107, 107);
    public static readonly SolidColorBrush Green      = UIHelpers.FrozenSolid(50, 205, 50);
    public static readonly SolidColorBrush Caution    = UIHelpers.FrozenSolid(255, 190, 0);
    public static readonly SolidColorBrush Critical   = UIHelpers.FrozenSolid(220, 20, 60);

    public static readonly Brush GradientApk     = UIHelpers.CreateGradientBrush("#63B5FF", "#1175E6");
    public static readonly Brush GradientXapk    = UIHelpers.CreateGradientBrush("#00D68F", "#00B377");
    public static readonly Brush GradientApks    = UIHelpers.CreateGradientBrush("#FFD000", "#CC9900");
    public static readonly Brush GradientApkm    = UIHelpers.CreateGradientBrush("#7D64FF", "#5A3FD9");
    public static readonly Brush GradientDefault = UIHelpers.CreateGradientBrush("#808080", "#606060");
    public static readonly Brush GradientOrange  = UIHelpers.CreateGradientBrush("#FF9F43", "#E67E22");
    public static readonly Brush GradientGreen   = UIHelpers.CreateGradientBrush("#00D68F", "#00B377");
    public static readonly Brush GradientCyan    = UIHelpers.CreateGradientBrush("#00BFFF", "#0099CC");
    public static readonly Brush GradientNavy    = UIHelpers.CreateGradientBrush("#1175E6", "#0D3A78");
    public static readonly Brush GradientRed     = UIHelpers.CreateGradientBrush("#FF6B6B", "#DC143C");
}

public static class AppIcons
{
    private static readonly ConcurrentDictionary<string, string> _pathRegistry;
    private static readonly ConcurrentDictionary<string, ImageSource?> _imageCache;

    static AppIcons()
    {
        _pathRegistry = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _imageCache   = new ConcurrentDictionary<string, ImageSource?>(StringComparer.OrdinalIgnoreCase);

        _pathRegistry["folder"]  = "Assets/Icons/folder.svg";
        _pathRegistry["android"] = "Assets/Icons/android.svg";
        _pathRegistry["pdf"]     = "Assets/Icons/pdf.svg";
        _pathRegistry["config"]  = "Assets/Icons/config.svg";
        _pathRegistry["image"]   = "Assets/Icons/image.svg";
        _pathRegistry["text"]    = "Assets/Icons/text.svg";
        _pathRegistry["video"]   = "Assets/Icons/video.svg";
        _pathRegistry["archive"] = "Assets/Icons/archive.svg";
        _pathRegistry["web"]     = "Assets/Icons/web.svg";
        _pathRegistry["db"]     = "Assets/Icons/db.svg";
        _pathRegistry["audio"]   = "Assets/Icons/audio.svg";

    }

    public static ImageSource? Get(string key)
    {
        if (!_pathRegistry.TryGetValue(key, out var path))
            return null;

        return _imageCache.GetOrAdd(key, _ => TryLoad(path));
    }

    public static void Register(string key, string assetRelativePath)
    {
        _pathRegistry[key] = assetRelativePath;
        _imageCache.TryRemove(key, out _);
    }

    public static void Preload()
    {
        _ = Get("folder");
        _ = Get("android");
        _ = Get("pdf");
        _ = Get("config");
        _ = Get("image");
        _ = Get("text");
        _ = Get("video");
        _ = Get("archive");
        _ = Get("web");
        _ = Get("db");
        _ = Get("audio");


    }

    private static ImageSource? TryLoad(string relPath)
    {
        try
        {
            var uri = new Uri(relPath, UriKind.Relative);
            var streamInfo = Application.GetResourceStream(uri);
            if (streamInfo == null) return null;

            var settings = new WpfDrawingSettings
            {
                IncludeRuntime = false,
                TextAsGeometry = true,
                OptimizePath = true,
                EnsureViewboxSize = true
            };
            using var reader = new FileSvgReader(settings);
            var drawing = reader.Read(streamInfo.Stream);
            if (drawing == null) return null;

            var image = new DrawingImage(drawing);
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }
}

#endregion

#region Custom Controls

public class SettingsSection : ContentControl
{
    private static readonly Duration AnimationDuration = new(TimeSpan.FromSeconds(0.5));
    private static readonly IEasingFunction SlideEasing = new CubicEase { EasingMode = EasingMode.EaseOut };
    private static readonly PropertyPath OpacityPath    = new("Opacity");
    private static readonly PropertyPath TranslateYPath = new("(UIElement.RenderTransform).(TranslateTransform.Y)");

    public static readonly DependencyProperty AnimationDelayProperty =
        DependencyProperty.Register(nameof(AnimationDelay), typeof(int), typeof(SettingsSection), new PropertyMetadata(0));

    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(SettingsSection), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(string), typeof(SettingsSection), new PropertyMetadata(string.Empty));

    public int AnimationDelay
    {
        get => (int)GetValue(AnimationDelayProperty);
        set => SetValue(AnimationDelayProperty, value);
    }

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public SettingsSection()
    {
        Loaded += OnSettingsSectionLoaded;
    }

    private void OnSettingsSectionLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnSettingsSectionLoaded;

        if (Template?.FindName("Container", this) is not UIElement container)
            return;

        var beginTime = TimeSpan.FromMilliseconds(AnimationDelay);

        var fadeInAnimation = new DoubleAnimation
        {
            To = 1,
            Duration = AnimationDuration,
            BeginTime = beginTime
        };

        var slideInAnimation = new DoubleAnimation
        {
            To = 0,
            Duration = AnimationDuration,
            BeginTime = beginTime,
            EasingFunction = SlideEasing
        };

        Storyboard.SetTarget(fadeInAnimation, container);
        Storyboard.SetTargetProperty(fadeInAnimation, OpacityPath);

        Storyboard.SetTarget(slideInAnimation, container);
        Storyboard.SetTargetProperty(slideInAnimation, TranslateYPath);

        var storyboard = new Storyboard();
        storyboard.Children.Add(fadeInAnimation);
        storyboard.Children.Add(slideInAnimation);
        storyboard.Begin();
    }
}

public class SettingItem : ContentControl
{
    static SettingItem()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SettingItem),
            new FrameworkPropertyMetadata(typeof(SettingItem)));
    }

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(SettingItem), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingItem), new PropertyMetadata(string.Empty));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }
}

public class ShadowCard : ContentControl
{
    static ShadowCard()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ShadowCard),
            new FrameworkPropertyMetadata(typeof(ShadowCard)));
    }

    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(nameof(CornerRadius), typeof(CornerRadius),
            typeof(ShadowCard), new PropertyMetadata(new CornerRadius(16)));

    public static readonly DependencyProperty ShadowEffectProperty =
        DependencyProperty.Register(nameof(ShadowEffect), typeof(Effect),
            typeof(ShadowCard), new PropertyMetadata(null));

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public Effect? ShadowEffect
    {
        get => (Effect?)GetValue(ShadowEffectProperty);
        set => SetValue(ShadowEffectProperty, value);
    }
}

public class GlowIcon : ContentControl
{
    static GlowIcon()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(GlowIcon),
            new FrameworkPropertyMetadata(typeof(GlowIcon)));
    }

    public static readonly DependencyProperty SizeProperty =
        DependencyProperty.Register(nameof(Size), typeof(double),
            typeof(GlowIcon), new PropertyMetadata(48.0));

    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(nameof(CornerRadius), typeof(CornerRadius),
            typeof(GlowIcon), new PropertyMetadata(new CornerRadius(24)));

    public static readonly DependencyProperty GlowEffectProperty =
        DependencyProperty.Register(nameof(GlowEffect), typeof(Effect),
            typeof(GlowIcon), new PropertyMetadata(null));

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public Effect? GlowEffect
    {
        get => (Effect?)GetValue(GlowEffectProperty);
        set => SetValue(GlowEffectProperty, value);
    }
}

public class PageHeader : ContentControl
{
    static PageHeader()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(PageHeader),
            new FrameworkPropertyMetadata(typeof(PageHeader)));
    }

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(PageHeader), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(PageHeader), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(string), typeof(PageHeader), new PropertyMetadata(string.Empty));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }
}

#endregion

#region Markup Extensions

[MarkupExtensionReturnType(typeof(string))]
public sealed class TranslateExtension : MarkupExtension
{
    private readonly string _key;

    public TranslateExtension(string key)
    {
        _key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{_key}]")
        {
            Source = TranslationManager.Instance,
            Mode = BindingMode.OneWay
        };
        return binding.ProvideValue(serviceProvider);
    }
}

#endregion

#region Value Converters

public sealed class StringEqualityToBoolConverter : IValueConverter
{
    public static StringEqualityToBoolConverter Instance { get; } = new();
    private StringEqualityToBoolConverter() { }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var a = value?.ToString() ?? string.Empty;
        var b = parameter?.ToString() ?? string.Empty;
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool isChecked && isChecked
            ? parameter?.ToString() ?? string.Empty
            : Binding.DoNothing;
}

public sealed class EqualityToVisibilityConverter : IValueConverter
{
    public static EqualityToVisibilityConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var a = value?.ToString() ?? string.Empty;
        var b = parameter?.ToString() ?? string.Empty;
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class IntToBoolConverter : IValueConverter
{
    public static IntToBoolConverter Instance { get; } = new();
    private IntToBoolConverter() { }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int count && count > 0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class PercentageToScaleConverter : IValueConverter
{
    public static PercentageToScaleConverter Instance { get; } = new();
    private PercentageToScaleConverter() { }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double percentage ? percentage / 100.0 : 0.0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class BatteryLevelToBrushConverter : IValueConverter
{
    public static BatteryLevelToBrushConverter Instance { get; } = new();

    private const string LowKey    = "App.Brush.Battery.Low";
    private const string MediumKey = "App.Brush.Battery.Medium";
    private const string HighKey   = "App.Brush.Battery.High";

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double level)
            return Brushes.Transparent;

        var key = level switch
        {
            <= 15 => LowKey,
            <= 40 => MediumKey,
            _     => HighKey
        };

        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class BatteryWidthConverter : IMultiValueConverter
{
    public static BatteryWidthConverter Instance { get; } = new();
    private BatteryWidthConverter() { }

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values?.Length >= 2 &&
            values[0] is double percentage &&
            values[1] is string maxWidthStr &&
            double.TryParse(maxWidthStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var maxWidth))
        {
            return Math.Clamp(percentage / 100.0 * maxWidth, 0, maxWidth);
        }
        return 0.0;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class CapitalizeConverter : IValueConverter
{
    public static CapitalizeConverter Instance { get; } = new();
    private CapitalizeConverter() { }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string s && !string.IsNullOrEmpty(s))
            return char.ToUpper(s[0], culture ?? CultureInfo.CurrentCulture) + s[1..];
        return value ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public static NullToVisibilityConverter Instance { get; } = new();
    private NullToVisibilityConverter() { }

    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        => value is not null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class StringToVisibilityConverter : IValueConverter
{
    public static StringToVisibilityConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class WidthToColumnsConverter : IValueConverter
{
    public static WidthToColumnsConverter Instance { get; } = new();
    private WidthToColumnsConverter() { }

    private const double MinColumnWidth = 580;
    private const int MaxColumns = 3;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double width || width <= 0)
            return 1;

        var columns = Math.Max(1, (int)(width / MinColumnWidth));
        return Math.Min(columns, MaxColumns);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public static InverseBoolToVisibilityConverter Instance { get; } = new();
    private InverseBoolToVisibilityConverter() { }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class LoadingTextConverter : IValueConverter
{
    public static LoadingTextConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not bool isLoading || parameter is not string texts)
            return "OK";

        var parts = texts.Split('|');
        var key = parts.Length == 2 ? (isLoading ? parts[1] : parts[0]) : texts;
        return Strings.ResourceManager.GetString(key, Strings.Culture) ?? key;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class SmartTruncateConverter : IValueConverter
{
    public static SmartTruncateConverter Instance { get; } = new();
    private SmartTruncateConverter() { }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string text || string.IsNullOrEmpty(text))
            return value ?? string.Empty;

        var maxLength = parameter is string p && int.TryParse(p, out var len) ? len : 50;
        if (text.Length <= maxLength)
            return text;

        var truncateAt = maxLength - 3;
        var minBreak = maxLength / 2;

        var breakPoint = -1;
        for (var i = truncateAt; i > minBreak; i--)
        {
            if (IsSeparator(text[i]))
            {
                breakPoint = i;
                break;
            }
        }

        if (breakPoint == -1)
            breakPoint = truncateAt;

        return text[..breakPoint] + "…";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static bool IsSeparator(char c) =>
        c is '_' or '-' or '.' or ' ' or '/' or '\\';
}

#endregion

#region Attached Behaviors

public static class HyperlinkExtensions
{
    public static readonly DependencyProperty OpenInBrowserProperty =
        DependencyProperty.RegisterAttached("OpenInBrowser", typeof(bool), typeof(HyperlinkExtensions),
            new PropertyMetadata(false, OnOpenInBrowserChanged));

    public static void SetOpenInBrowser(DependencyObject obj, bool value)
        => obj.SetValue(OpenInBrowserProperty, value);

    public static bool GetOpenInBrowser(DependencyObject obj)
        => (bool)obj.GetValue(OpenInBrowserProperty);

    private static void OnOpenInBrowserChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Hyperlink hyperlink)
            return;

        hyperlink.RequestNavigate -= OnHyperlinkNavigate;
        if ((bool)e.NewValue)
            hyperlink.RequestNavigate += OnHyperlinkNavigate;
    }

    private static void OnHyperlinkNavigate(object sender, RequestNavigateEventArgs e)
    {
        ShellUtils.OpenUrl(e.Uri);
        e.Handled = true;
    }
}

public static class WindowDrag
{
    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(WindowDrag),
            new PropertyMetadata(false, OnEnabledChanged));

    public static void SetEnabled(DependencyObject obj, bool value)
        => obj.SetValue(EnabledProperty, value);

    public static bool GetEnabled(DependencyObject obj)
        => (bool)obj.GetValue(EnabledProperty);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;

        element.MouseLeftButtonDown -= OnMouseDown;
        if ((bool)e.NewValue)
            element.MouseLeftButtonDown += OnMouseDown;
    }

    private static void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            Window.GetWindow((DependencyObject)sender)?.DragMove();
    }
}

public static class WindowClose
{
    public static readonly DependencyProperty ResultProperty =
        DependencyProperty.RegisterAttached("Result", typeof(bool?), typeof(WindowClose),
            new PropertyMetadata(null, OnResultChanged));

    public static void SetResult(DependencyObject obj, bool? value)
        => obj.SetValue(ResultProperty, value);

    public static bool? GetResult(DependencyObject obj)
        => (bool?)obj.GetValue(ResultProperty);

    private static void OnResultChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ButtonBase button) return;

        button.Click -= OnButtonClick;
        if (e.NewValue is not null)
            button.Click += OnButtonClick;
    }

    private static void OnButtonClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow((DependencyObject)sender) is { } window)
        {
            window.DialogResult = GetResult((DependencyObject)sender);
            window.Close();
        }
    }
}

#endregion

#region License Guard

public static class LicenseGuard
{
    public static readonly DependencyProperty RequiredTierProperty =
        DependencyProperty.RegisterAttached("RequiredTier", typeof(LicenseTier?), typeof(LicenseGuard),
            new PropertyMetadata(null, OnRequiredTierChanged));

    public static void SetRequiredTier(DependencyObject obj, LicenseTier? value)
        => obj.SetValue(RequiredTierProperty, value);

    public static LicenseTier? GetRequiredTier(DependencyObject obj)
        => (LicenseTier?)obj.GetValue(RequiredTierProperty);

    private static readonly DependencyProperty HandlersProperty =
        DependencyProperty.RegisterAttached("Handlers", typeof(LicenseGuardHandlers), typeof(LicenseGuard),
            new PropertyMetadata(null));

    private static void OnRequiredTierChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;

        CleanupHandlers(element);

        if (e.NewValue is null) return;

        var handlers = new LicenseGuardHandlers(element);
        element.SetValue(HandlersProperty, handlers);

        handlers.UpdateState();
        element.Loaded += handlers.OnLoaded;
        element.Unloaded += handlers.OnUnloaded;
    }

    private static void CleanupHandlers(FrameworkElement element)
    {
        if (element.GetValue(HandlersProperty) is not LicenseGuardHandlers old) return;

        old.Detach();
        element.Loaded -= old.OnLoaded;
        element.Unloaded -= old.OnUnloaded;
        element.ClearValue(HandlersProperty);
    }

    private sealed class LicenseGuardHandlers(FrameworkElement element)
    {
        private bool _subscribed;

        public void UpdateState()
        {
            var required = GetRequiredTier(element);
            if (!required.HasValue) return;

            var hasLicense = LicenseService.Instance.CurrentState.EffectiveTier >= required.Value;
            var hasModule  = required.Value < LicenseTier.Pro || ProLoader.IsLoaded;
            var hasDevice  = DeviceManager.Instance.IsConnected;

            if (element is UIElement ui)
            {
                ui.IsEnabled = hasLicense && hasModule && hasDevice;
                ui.Opacity   = hasLicense && hasModule ? 1.0 : 0.5;
            }
        }

        public void OnLoaded(object sender, RoutedEventArgs e) => Attach();
        public void OnUnloaded(object sender, RoutedEventArgs e) => Detach();

        private void Attach()
        {
            if (_subscribed) return;
            LicenseService.Instance.StateChanged += OnStateChanged;
            DeviceManager.Instance.DeviceStatusChanged += OnDeviceStatusChanged;
            _subscribed = true;
            UpdateState();
        }

        public void Detach()
        {
            if (!_subscribed) return;
            LicenseService.Instance.StateChanged -= OnStateChanged;
            DeviceManager.Instance.DeviceStatusChanged -= OnDeviceStatusChanged;
            _subscribed = false;
        }

        private void OnStateChanged(object? sender, EventArgs e)
            => element.Dispatcher.BeginInvoke(UpdateState);

        private void OnDeviceStatusChanged(object? sender, bool e)
            => element.Dispatcher.BeginInvoke(UpdateState);
    }
}

#endregion
