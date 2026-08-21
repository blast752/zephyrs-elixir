namespace ZephyrsElixir.UI.Helpers;

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

    /// <summary>The live binding behind <c>{h:Translate key}</c>, so code-behind that has to touch a
    /// translated property re-establishes the binding instead of freezing the current language in.</summary>
    public static Binding Translation(string key) => new($"[{key}]")
    {
        Source = TranslationManager.Instance,
        Mode = BindingMode.OneWay
    };

    /// <summary>Decodes a stream into a frozen, cross-thread-usable bitmap. <c>OnLoad</c> is what lets
    /// the caller dispose the stream immediately.</summary>
    public static BitmapImage BitmapFromStream(Stream stream, int decodePixelWidth = 0, bool ignoreColorProfile = false)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        if (ignoreColorProfile) bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        if (decodePixelWidth > 0) bitmap.DecodePixelWidth = decodePixelWidth;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
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

    /// <summary>
    /// Resolves a palette <see cref="Color"/> out of the merged dictionaries, so code that has to
    /// hand a colour to something outside WPF — the DWM caption, an interop call — reads the same
    /// token the UI paints with instead of carrying a second copy of the literal. Null when the key
    /// is absent, which callers treat as "leave the platform default alone".
    /// </summary>
    public static Color? PaletteColor(string key) =>
        Application.Current?.TryFindResource(key) is Color color ? color : null;

    /// <summary>
    /// The representative "leading" colour of a brush — the first stop of a gradient, the colour
    /// of a solid brush, or a neutral grey fallback. Lets an accent/glow colour be derived from an
    /// icon brush so the two can never drift out of sync (single source of truth for both).
    /// </summary>
    public static Color LeadingColor(Brush brush) => brush switch
    {
        GradientBrush { GradientStops.Count: > 0 } gradient => gradient.GradientStops[0].Color,
        SolidColorBrush solid => solid.Color,
        _ => Color.FromRgb(0x80, 0x80, 0x80)
    };
}

/// <summary>
/// Small, reusable entrance/transition animations so pages don't hand-roll storyboards.
/// Keeps timing and easing consistent across the app (single source of truth for motion).
/// </summary>
public static class AnimationHelpers
{
    private static readonly IEasingFunction Smooth = new CubicEase { EasingMode = EasingMode.EaseOut };

    /// <summary>Fade the element in while sliding it up from <paramref name="fromY"/> pixels.</summary>
    public static void FadeSlideIn(FrameworkElement element, double fromY = 10, int durationMs = 340, int delayMs = 0)
    {
        if (element is null) return;

        var begin = TimeSpan.FromMilliseconds(delayMs);
        var duration = TimeSpan.FromMilliseconds(durationMs);

        var transform = EnsureTranslate(element);
        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, duration) { BeginTime = begin, EasingFunction = Smooth });
        transform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(fromY, 0, duration) { BeginTime = begin, EasingFunction = Smooth });
    }

    /// <summary>Cross-fade an element's content: fade out, invoke <paramref name="swap"/>, fade back in.</summary>
    public static void CrossFade(FrameworkElement element, Action swap, int durationMs = 260)
    {
        if (element is null) { swap?.Invoke(); return; }

        var half = TimeSpan.FromMilliseconds(durationMs / 2.0);
        var fadeOut = new DoubleAnimation(1, 0, half) { EasingFunction = Smooth };
        fadeOut.Completed += (_, _) =>
        {
            swap?.Invoke();
            FadeSlideIn(element, fromY: 6, durationMs: durationMs / 2);
        };
        element.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private static TranslateTransform EnsureTranslate(FrameworkElement element)
    {
        if (element.RenderTransform is TranslateTransform existing) return existing;
        var transform = new TranslateTransform();
        element.RenderTransform = transform;
        return transform;
    }
}

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

    /// <summary>Opens Explorer with <paramref name="path"/> selected; false when the shell refuses.</summary>
    public static bool RevealInExplorer(string? path) => StartExplorer(path, $"/select,\"{path}\"");

    /// <summary>Opens <paramref name="path"/> as a folder in Explorer; false when the shell refuses.</summary>
    public static bool OpenFolder(string? path) => StartExplorer(path, $"\"{path}\"");

    private static bool StartExplorer(string? path, string arguments)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        try
        {
            Process.Start("explorer.exe", arguments);
            return true;
        }
        catch (Exception ex)
        {
            try { AdbLogger.Instance.LogWarning("Shell", $"Failed to open Explorer: {ex.Message}"); } catch { }
            return false;
        }
    }
}

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

    public static readonly Brush GradientBlue    = UIHelpers.CreateGradientBrush("#63B5FF", "#1175E6");
    public static readonly Brush GradientAmber   = UIHelpers.CreateGradientBrush("#FFD700", "#CC9900");
    public static readonly Brush GradientPurple  = UIHelpers.CreateGradientBrush("#7D64FF", "#5A3FD9");
    public static readonly Brush GradientDefault = UIHelpers.CreateGradientBrush("#808080", "#606060");
    public static readonly Brush GradientOrange  = UIHelpers.CreateGradientBrush("#FF9F43", "#E67E22");
    public static readonly Brush GradientGreen   = UIHelpers.CreateGradientBrush("#00D68F", "#00B377");
    public static readonly Brush GradientCyan    = UIHelpers.CreateGradientBrush("#00BFFF", "#0099CC");
    public static readonly Brush GradientNavy    = UIHelpers.CreateGradientBrush("#1175E6", "#0D3A78");
    public static readonly Brush GradientRed     = UIHelpers.CreateGradientBrush("#FF6B6B", "#DC143C");
    public static readonly Brush GradientGold    = UIHelpers.CreateGradientBrush("#FFD700", "#FFA500");
    public static readonly Brush GradientSky     = UIHelpers.CreateGradientBrush("#00BFFF", "#007FFF");
    public static readonly Brush GradientCoral   = UIHelpers.CreateGradientBrush("#FF6B6B", "#FF4757");
    public static readonly Brush GradientViolet  = UIHelpers.CreateGradientBrush("#A78BFA", "#7C3AED");
}

public sealed record IconShape(Geometry Geometry, bool IsFilled);

public static class AppIcons
{
    /// <summary>Side of the square grid every vector icon is authored on.</summary>
    public const double DesignSize = 24.0;

    private static readonly ConcurrentDictionary<string, string> _pathRegistry;
    private static readonly ConcurrentDictionary<string, ImageSource?> _imageCache;
    private static readonly ConcurrentDictionary<string, IconShape?> _shapeCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Vector icon set, authored on a 24×24 grid. Entries are centre-line paths stroked at 2 units
    /// with round caps and joins; the few intrinsically solid symbols opt in by opening with the
    /// "F1" (non-zero) fill-rule token, which doubles as the filled marker.
    /// </summary>
    private static readonly Dictionary<string, string> _vectors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["add"]            = "M12 5v14M5 12h14",
        ["alert"]          = "M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18M12 7.4v5.6M12 16.7v.05",
        ["apps"]           = "M4.8 4.8h5.4v5.4H4.8zM13.8 4.8h5.4v5.4h-5.4zM4.8 13.8h5.4v5.4H4.8zM13.8 13.8h5.4v5.4h-5.4z",
        ["arrow-left"]     = "M20.4 12H4.2M10.4 5.8 4.2 12l6.2 6.2",
        ["arrow-right"]    = "M3.6 12h16.2M13.6 5.8 19.8 12l-6.2 6.2",
        ["arrow-up"]       = "M12 20.4V4.2M5.8 10.4 12 4.2l6.2 6.2",
        ["archive"]        = "M3.2 4.6h17.6a1 1 0 0 1 1 1v2.8a1 1 0 0 1-1 1H3.2a1 1 0 0 1-1-1V5.6a1 1 0 0 1 1-1zM4.4 9.4v9a1.8 1.8 0 0 0 1.8 1.8h11.6a1.8 1.8 0 0 0 1.8-1.8v-9M9.8 13.4h4.4",
        ["arrow-to-end"]   = "M3.6 12h13.2M12.4 7.6 16.8 12l-4.4 4.4M20.4 5.2v13.6",
        ["audio"]          = "M9.2 17.2a2.8 2.8 0 1 1-2.8-2.8M9.2 17.2V5.4l9.4-2v11.8M18.6 15.2a2.8 2.8 0 1 1-2.8-2.8M9.2 9.2l9.4-2",
        ["back-to-start"]  = "M20.4 12H7.2M11.6 7.6 7.2 12l4.4 4.4M3.6 5.2v13.6",
        ["battery"]        = "F1 M3.4 8.2h13.4a1.9 1.9 0 0 1 1.9 1.9v3.8a1.9 1.9 0 0 1-1.9 1.9H3.4a1.9 1.9 0 0 1-1.9-1.9v-3.8a1.9 1.9 0 0 1 1.9-1.9zM20.1 10.4h.7a1 1 0 0 1 1 1v1.2a1 1 0 0 1-1 1h-.7z",
        ["bell"]           = "F1 M12 2.4a1.3 1.3 0 0 1 1.3 1.3v.72a6.6 6.6 0 0 1 5.3 6.46v3.62l1.5 2.42a1 1 0 0 1-.85 1.53H4.75a1 1 0 0 1-.85-1.53l1.5-2.42v-3.62a6.6 6.6 0 0 1 5.3-6.46v-.72A1.3 1.3 0 0 1 12 2.4zM9.4 19.9h5.2a2.6 2.6 0 0 1-5.2 0z",
        ["bolt"]           = "F1 M13.7 2 5.9 13.3h5.2L9.5 22 18.1 10.2h-5.4z",
        ["book"]           = "M5 4.8A1.8 1.8 0 0 1 6.8 3h11.4a1 1 0 0 1 1 1v14.6a1 1 0 0 1-1 1H6.8A1.8 1.8 0 0 1 5 17.8zM5 17.8A1.8 1.8 0 0 1 6.8 16h12.4M9 7h6",
        ["braces"]         = "M10 3.6H9a3.2 3.2 0 0 0-3.2 3.2v2.5A1.7 1.7 0 0 1 4.1 11h-.9v2h.9a1.7 1.7 0 0 1 1.7 1.7v2.5A3.2 3.2 0 0 0 9 20.4h1M14 3.6h1a3.2 3.2 0 0 1 3.2 3.2v2.5A1.7 1.7 0 0 0 19.9 11h.9v2h-.9a1.7 1.7 0 0 0-1.7 1.7v2.5a3.2 3.2 0 0 1-3.2 3.2h-1",
        ["calendar"]       = "M5.4 5.4h13.2a2 2 0 0 1 2 2v11.4a2 2 0 0 1-2 2H5.4a2 2 0 0 1-2-2V7.4a2 2 0 0 1 2-2zM3.4 10h17.2M8 3.2v4M16 3.2v4M7.6 13.4h.05M12 13.4h.05M16.4 13.4h.05M7.6 17h.05M12 17h.05",
        ["camera"]         = "M9.5 4.6h5l1.5 2.4h3.6a2.2 2.2 0 0 1 2.2 2.2v8a2.2 2.2 0 0 1-2.2 2.2H4.4a2.2 2.2 0 0 1-2.2-2.2v-8A2.2 2.2 0 0 1 4.4 7H8zM12 9.9a3.6 3.6 0 1 0 0 7.2a3.6 3.6 0 1 0 0-7.2",
        ["cast"]           = "M4.6 4.4h14.8a2 2 0 0 1 2 2v11.2a2 2 0 0 1-2 2H4.6a2 2 0 0 1-2-2V6.4a2 2 0 0 1 2-2zM6.6 16.4a2 2 0 0 1 2 2M6.6 12.8a5.6 5.6 0 0 1 5.6 5.6M6.6 9.2a9.2 9.2 0 0 1 9.2 9.2",
        ["chart"]          = "M4.2 4.6h15.6a1.8 1.8 0 0 1 1.8 1.8v11.2a1.8 1.8 0 0 1-1.8 1.8H4.2a1.8 1.8 0 0 1-1.8-1.8V6.4a1.8 1.8 0 0 1 1.8-1.8zM5.8 14.8 9.2 10l2.8 3.1 3.1-5.5 3.1 6.6",
        ["chat"]           = "M4.4 4.4h15.2a1.8 1.8 0 0 1 1.8 1.8v8.6a1.8 1.8 0 0 1-1.8 1.8H9.4l-4.6 3.9v-3.9h-.4a1.8 1.8 0 0 1-1.8-1.8V6.2a1.8 1.8 0 0 1 1.8-1.8z",
        ["check"]          = "M4.6 12.6 9.6 17.6 19.6 6.6",
        ["check-circle"]   = "M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18M7.9 12.3 10.9 15.3 16.4 9.2",
        ["checklist"]      = "M3.6 6.4 5.2 8 8 5.2M3.6 12 5.2 13.6 8 10.8M3.6 17.6 5.2 19.2 8 16.4M11.4 6.6h9M11.4 12.2h9M11.4 17.8h9",
        ["chevron-right"]  = "M9 5.4 15.6 12 9 18.6",
        ["chip"]           = "M4.8 4.8h14.4v14.4H4.8zM9.2 9.2h5.6v5.6H9.2zM9.4 2.6v2.2M14.6 2.6v2.2M9.4 19.2v2.2M14.6 19.2v2.2M2.6 9.4h2.2M2.6 14.6h2.2M19.2 9.4h2.2M19.2 14.6h2.2",
        ["clock"]          = "M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18M12 6.8V12l3.6 2.2",
        ["close"]          = "M6 6 18 18M18 6 6 18",
        ["cloud"]          = "M7.4 19.4a4.9 4.9 0 0 1-.5-9.77 5.9 5.9 0 0 1 11.24 1.62A4.1 4.1 0 0 1 17.4 19.4z",
        ["console"]        = "M4.4 4.4h15.2a2 2 0 0 1 2 2v11.2a2 2 0 0 1-2 2H4.4a2 2 0 0 1-2-2V6.4a2 2 0 0 1 2-2zM6.6 9.8 9.4 12.4 6.6 15M11.8 15.2h5.6",
        ["contacts"]       = "M8.2 4.4a2.9 2.9 0 1 0 0 5.8a2.9 2.9 0 1 0 0-5.8M3.2 19.6a5 5 0 0 1 10 0M15.6 8h5.2M15.6 12h5.2M15.6 16h5.2",
        ["copy"]           = "M9 8.4a2 2 0 0 1 2-2h8.2a2 2 0 0 1 2 2v9.2a2 2 0 0 1-2 2H11a2 2 0 0 1-2-2zM15.6 6.4V5.2a2 2 0 0 0-2-2H4.8a2 2 0 0 0-2 2V14a2 2 0 0 0 2 2H6",
        ["cut"]            = "M6.6 3.6 17.4 17.6M17.4 3.6 6.6 17.6M6.4 17.2a2.9 2.9 0 1 0 0 5.8a2.9 2.9 0 1 0 0-5.8M17.6 17.2a2.9 2.9 0 1 0 0 5.8a2.9 2.9 0 1 0 0-5.8",
        ["database"]       = "M12 3.2c4.4 0 8 1.3 8 2.9s-3.6 2.9-8 2.9-8-1.3-8-2.9 3.6-2.9 8-2.9zM4 6.1v11.8c0 1.6 3.6 2.9 8 2.9s8-1.3 8-2.9V6.1M4 12c0 1.6 3.6 2.9 8 2.9s8-1.3 8-2.9",
        ["delete"]         = "M4.4 6.6h15.2M9.6 6.6V4.6a1.2 1.2 0 0 1 1.2-1.2h2.4a1.2 1.2 0 0 1 1.2 1.2v2M6.4 6.6l.85 12.5a1.6 1.6 0 0 0 1.6 1.5h6.3a1.6 1.6 0 0 0 1.6-1.5l.85-12.5M10.2 10.4v6.2M13.8 10.4v6.2",
        ["devices"]        = "M2.6 6.2a1.8 1.8 0 0 1 1.8-1.8h9.4a1.8 1.8 0 0 1 1.8 1.8v1.4M2.6 6.2v9.4a1.8 1.8 0 0 0 1.8 1.8h5.2M6.8 20.4h4.4M9 17.4v3M17.6 7.6h2.8a1.8 1.8 0 0 1 1.8 1.8v9.2a1.8 1.8 0 0 1-1.8 1.8h-2.8a1.8 1.8 0 0 1-1.8-1.8V9.4a1.8 1.8 0 0 1 1.8-1.8z",
        ["display"]        = "M4.2 4.6h15.6a1.8 1.8 0 0 1 1.8 1.8v8.8a1.8 1.8 0 0 1-1.8 1.8H4.2a1.8 1.8 0 0 1-1.8-1.8V6.4a1.8 1.8 0 0 1 1.8-1.8zM8.6 20.4h6.8M12 17v3.4",
        ["document"]       = "M6.4 3.4h7.2l5 5v12.2a1.4 1.4 0 0 1-1.4 1.4H6.4A1.4 1.4 0 0 1 5 20.6V4.8a1.4 1.4 0 0 1 1.4-1.4zM13.4 3.6v5.2h5.2",
        ["document-add"]   = "M6.4 3.4h7.2l5 5v12.2a1.4 1.4 0 0 1-1.4 1.4H6.4A1.4 1.4 0 0 1 5 20.6V4.8a1.4 1.4 0 0 1 1.4-1.4zM13.4 3.6v5.2h5.2M12 12.4v5.6M9.2 15.2h5.6",
        ["download"]       = "M12 3.6v11.8M7.4 10.8 12 15.4l4.6-4.6M4.4 20.4h15.2",
        ["edit"]           = "M4.2 19.8v-4.2L15.4 4.4a2.2 2.2 0 0 1 3.1 0l1.1 1.1a2.2 2.2 0 0 1 0 3.1L8.4 19.8zM14.4 5.4 18.6 9.6",
        ["eraser"]         = "M9.2 20.6 3.6 15a1.8 1.8 0 0 1 0-2.55L12.4 3.6a1.8 1.8 0 0 1 2.55 0l5.45 5.45a1.8 1.8 0 0 1 0 2.55L11.75 20.6zM7.2 11.6l5.2 5.2M9.2 20.6h11.2",
        ["error-circle"]   = "M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18M9.1 9.1 14.9 14.9M14.9 9.1 9.1 14.9",
        ["firmware"]       = "M5.6 5.6h12.8v12.8H5.6zM12 8.4v6.6M9.5 12.5 12 15 14.5 12.5M9.4 2.6v3M14.6 2.6v3M9.4 18.4v3M14.6 18.4v3M2.6 9.4h3M2.6 14.6h3M18.4 9.4h3M18.4 14.6h3",
        ["flame"]          = "M12 2.8c-1.1 2.9-2.8 4.4-4.4 6.1-1.7 1.9-2.6 3.8-2.6 5.7a7 7 0 0 0 14 0c0-3.5-2.3-7.1-7-11.8zM12 21.6a3.3 3.3 0 0 1-3.3-3.3c0-1.5 1.1-3.1 3.3-4.9 2.2 1.8 3.3 3.4 3.3 4.9a3.3 3.3 0 0 1-3.3 3.3z",
        ["folder"]         = "M3 7.4a1.8 1.8 0 0 1 1.8-1.8h4.3l2.2 2.6h7.9a1.8 1.8 0 0 1 1.8 1.8v8.6a1.8 1.8 0 0 1-1.8 1.8H4.8A1.8 1.8 0 0 1 3 18.6z",
        ["folder-open"]    = "M3 19.4V6.4a1.8 1.8 0 0 1 1.8-1.8h4.3l2.2 2.6h6.9a1.8 1.8 0 0 1 1.8 1.8v1.6M3 19.4h15.1l3.2-8.1a.9.9 0 0 0-.84-1.24H7.5a1.4 1.4 0 0 0-1.3.9z",
        ["gamepad"]        = "M8.4 8.6h7.2a5.6 5.6 0 0 1 5.6 5.6 3.4 3.4 0 0 1-6.05 2.12l-.9-1.12H9.75l-.9 1.12A3.4 3.4 0 0 1 2.8 14.2a5.6 5.6 0 0 1 5.6-5.6zM6.4 11.7v2.6M5.1 13h2.6M15.5 12.1h.05M17.5 14.1h.05",
        ["gauge"]          = "M3.4 17.8a9.4 9.4 0 1 1 17.2 0M12 14 16.4 8.4",
        ["globe"]          = "M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18M3.2 12h17.6M12 3a13.6 13.6 0 0 1 0 18a13.6 13.6 0 0 1 0-18",
        ["heart"]          = "F1 M12 21.1 4.5 13.9A5.1 5.1 0 0 1 12 6.9a5.1 5.1 0 0 1 7.5 7z",
        ["heart-outline"]  = "M12 20.6 5.1 13.9a4.7 4.7 0 0 1 6.9-6.4a4.7 4.7 0 0 1 6.9 6.4z",
        ["history"]        = "M3.6 12a8.4 8.4 0 1 0 2.6-6.05M3.6 3.8v5.4H9M12 7.6V12l3.2 1.9",
        ["home"]           = "M3.4 10.4 12 3.2l8.6 7.2v9.2a1.4 1.4 0 0 1-1.4 1.4H4.8a1.4 1.4 0 0 1-1.4-1.4zM9.4 20.8v-6.6h5.2v6.6",
        ["image"]          = "M4.4 4.6h15.2a1.8 1.8 0 0 1 1.8 1.8v11.2a1.8 1.8 0 0 1-1.8 1.8H4.4a1.8 1.8 0 0 1-1.8-1.8V6.4a1.8 1.8 0 0 1 1.8-1.8zM8.6 9.6h.05M2.8 16.6 8.6 11.2l4.4 4 2.6-2.2 5.4 4.6",
        ["import"]         = "M12 15.4V3.8M7.4 10.8 12 15.4l4.6-4.6M3.6 15.6v3.2a1.8 1.8 0 0 0 1.8 1.8h13.2a1.8 1.8 0 0 0 1.8-1.8v-3.2",
        ["import-file"]    = "M6.4 3.4h7.2l5 5v12.2a1.4 1.4 0 0 1-1.4 1.4H6.4A1.4 1.4 0 0 1 5 20.6V4.8a1.4 1.4 0 0 1 1.4-1.4zM13.4 3.6v5.2h5.2M12 18.4v-6M9.4 14.6 12 12l2.6 2.6",
        ["info"]           = "M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18M12 16.6V11M12 7.3v.05",
        ["key"]            = "M15.4 3.6a4.4 4.4 0 1 0 0 8.8a4.4 4.4 0 1 0 0-8.8M15.4 7.4h.05M12.3 11.7 3.6 20.4M6.2 17.8l2.2 2.2M8.8 15.2l2.2 2.2",
        ["keyboard"]       = "M4.2 6.6h15.6a2 2 0 0 1 2 2v6.8a2 2 0 0 1-2 2H4.2a2 2 0 0 1-2-2V8.6a2 2 0 0 1 2-2zM6.2 10.4h.05M9.4 10.4h.05M12.6 10.4h.05M15.8 10.4h.05M18.4 10.4h.05M6.2 13.8h.05M8.8 13.8h6.4M17.8 13.8h.05",
        ["layers"]         = "M12 2.8 21.4 8 12 13.2 2.6 8zM2.6 16 12 21.2 21.4 16M2.6 12 12 17.2 21.4 12",
        ["library"]        = "M4.4 4.2h3.2v15.6H4.4zM10 4.2h3.2v15.6H10zM16.1 4.6l3.1.8-3.9 14.6-3.1-.8z",
        ["link"]           = "M10 13.6a3.6 3.6 0 0 0 5.4.4l2.8-2.8a3.6 3.6 0 0 0-5.1-5.1l-1.6 1.6M14 10.4a3.6 3.6 0 0 0-5.4-.4l-2.8 2.8a3.6 3.6 0 0 0 5.1 5.1l1.6-1.6",
        ["lightbulb"]      = "M12 2.8a6.4 6.4 0 0 1 3.9 11.5 2 2 0 0 0-.8 1.6v.5H8.9v-.5a2 2 0 0 0-.8-1.6A6.4 6.4 0 0 1 12 2.8zM9.6 19h4.8M10.4 21.4h3.2",
        ["location"]       = "M12 2.8a7 7 0 0 1 7 7c0 4.9-7 11.4-7 11.4S5 14.7 5 9.8a7 7 0 0 1 7-7zM12 7.4a2.6 2.6 0 1 0 0 5.2a2.6 2.6 0 1 0 0-5.2",
        ["lock"]           = "M6.4 10.2h11.2a1.8 1.8 0 0 1 1.8 1.8v7a1.8 1.8 0 0 1-1.8 1.8H6.4a1.8 1.8 0 0 1-1.8-1.8v-7a1.8 1.8 0 0 1 1.8-1.8zM7.8 10.2V7.6a4.2 4.2 0 0 1 8.4 0v2.6M12 14.4v2.6",
        ["microphone"]     = "M12 2.8a3.2 3.2 0 0 1 3.2 3.2v5.6a3.2 3.2 0 0 1-6.4 0V6A3.2 3.2 0 0 1 12 2.8zM5.6 11a6.4 6.4 0 0 0 12.8 0M12 17.4v3.8M8.6 21.2h6.8",
        ["open-external"]  = "M13.6 3.6h6.8v6.8M20.4 3.6 11.6 12.4M18.4 14v5.2a1.8 1.8 0 0 1-1.8 1.8H4.8A1.8 1.8 0 0 1 3 19.2V7.4a1.8 1.8 0 0 1 1.8-1.8H10",
        ["open-file"]      = "M6.4 3.4h7.2l5 5v12.2a1.4 1.4 0 0 1-1.4 1.4H6.4A1.4 1.4 0 0 1 5 20.6V4.8a1.4 1.4 0 0 1 1.4-1.4zM13.4 3.6v5.2h5.2M12 12v6.4M9.4 14.6 12 12l2.6 2.6",
        ["page"]           = "M6.4 3.4h7.2l5 5v12.2a1.4 1.4 0 0 1-1.4 1.4H6.4A1.4 1.4 0 0 1 5 20.6V4.8a1.4 1.4 0 0 1 1.4-1.4zM13.4 3.6v5.2h5.2M8.6 13h6.8M8.6 16.6h4.6",
        ["paste"]          = "M9 4.6H6.4A1.6 1.6 0 0 0 4.8 6.2v13.6a1.6 1.6 0 0 0 1.6 1.6h11.2a1.6 1.6 0 0 0 1.6-1.6V6.2a1.6 1.6 0 0 0-1.6-1.6H15M9.4 2.8h5.2a.8.8 0 0 1 .8.8v2.2a.8.8 0 0 1-.8.8H9.4a.8.8 0 0 1-.8-.8V3.6a.8.8 0 0 1 .8-.8zM8.6 12h6.8M8.6 16h4.6",
        ["phone"]          = "M7.4 2.8h9.2a2 2 0 0 1 2 2v14.4a2 2 0 0 1-2 2H7.4a2 2 0 0 1-2-2V4.8a2 2 0 0 1 2-2zM10.4 18h3.2",
        ["play"]           = "F1 M8.2 4.6 19.4 11.35a.76.76 0 0 1 0 1.3L8.2 19.4a.76.76 0 0 1-1.15-.65V5.25A.76.76 0 0 1 8.2 4.6z",
        ["power"]          = "M12 3.2v8.6M7.05 6.15a8.4 8.4 0 1 0 9.9 0",
        ["question"]       = "M8.4 8.6a3.7 3.7 0 1 1 5.2 3.4c-1.05.5-1.6 1.35-1.6 2.5v.7M12 18.6v.05",
        ["ram"]            = "M2.6 7.4h18.8v8.4H2.6zM6.2 15.8v2.4M10.1 15.8v2.4M13.9 15.8v2.4M17.8 15.8v2.4M6.6 10.4h10.8v2.4H6.6z",
        ["record"]         = "F1 M12 5.6a6.4 6.4 0 1 1 0 12.8a6.4 6.4 0 1 1 0-12.8z",
        ["refresh"]        = "M20.4 12a8.4 8.4 0 1 1-2.6-6.05M20.4 3.8v5.4H15",
        ["rename"]         = "M4.2 8.4V6.6a1.6 1.6 0 0 1 1.6-1.6h12.4a1.6 1.6 0 0 1 1.6 1.6v1.8M12 5.2v13.6M9.4 18.8h5.2",
        ["report"]         = "M4.4 4.4h15.2a1.8 1.8 0 0 1 1.8 1.8v11.6a1.8 1.8 0 0 1-1.8 1.8H4.4a1.8 1.8 0 0 1-1.8-1.8V6.2a1.8 1.8 0 0 1 1.8-1.8zM2.6 8.6h18.8M6.2 12h5.4M6.2 15.4h5.4M15 12h3.2v3.4H15z",
        ["restore"]        = "M3.6 12a8.4 8.4 0 1 0 2.6-6.05M3.6 3.8v5.4H9",
        ["rotate"]         = "M12 5.4a6.6 6.6 0 1 1-6.35 8.4M12 2.4 15.2 5.4 12 8.4",
        ["save"]           = "M5.6 3.6h9.8L20 8.2v11.4a1.8 1.8 0 0 1-1.8 1.8H5.6a1.8 1.8 0 0 1-1.8-1.8V5.4a1.8 1.8 0 0 1 1.8-1.8zM7.8 3.6v4.8h6.6V3.6M7.4 21.4V15h9.2v6.4",
        ["schedule"]       = "M20.4 11V7.4a2 2 0 0 0-2-2H5.6a2 2 0 0 0-2 2v11.4a2 2 0 0 0 2 2h5.6M3.6 10h16.8M8 3.2v4M16 3.2v4M16.8 12.4a4.8 4.8 0 1 0 0 9.6a4.8 4.8 0 1 0 0-9.6M16.8 14.8v2.4l1.6 1",
        ["search"]         = "M10.6 3.4a7.2 7.2 0 1 0 0 14.4a7.2 7.2 0 1 0 0-14.4M15.8 15.8 20.8 20.8",
        ["server"]         = "M4.6 3.8h14.8a1.4 1.4 0 0 1 1.4 1.4v4a1.4 1.4 0 0 1-1.4 1.4H4.6a1.4 1.4 0 0 1-1.4-1.4v-4a1.4 1.4 0 0 1 1.4-1.4zM4.6 13.4h14.8a1.4 1.4 0 0 1 1.4 1.4v4a1.4 1.4 0 0 1-1.4 1.4H4.6a1.4 1.4 0 0 1-1.4-1.4v-4a1.4 1.4 0 0 1 1.4-1.4zM7.2 7.2h.05M7.2 16.8h.05",
        ["settings"]       = "M20.89 9.62 20.89 14.38 17.8 14.7 17.24 15.67 18.51 18.51 14.38 20.89 12.56 18.38 11.44 18.38 9.62 20.89 5.5 18.51 6.76 15.67 6.2 14.7 3.11 14.38 3.11 9.62 6.2 9.3 6.76 8.33 5.5 5.5 9.62 3.11 11.44 5.62 12.56 5.62 14.38 3.11 18.51 5.5 17.24 8.33 17.8 9.3ZM12 8.8a3.2 3.2 0 1 0 0 6.4a3.2 3.2 0 1 0 0-6.4",
        ["shield"]         = "M12 3 19.8 5.9v6.05c0 4.35-3.1 7.45-7.8 8.35-4.7-.9-7.8-4-7.8-8.35V5.9zM8.8 12.1 11 14.3 15.4 9.8",
        ["star"]           = "F1 M12 2.6 15 9.05l7.05.85-5.2 4.85 1.4 6.95L12 18.2l-6.25 3.5 1.4-6.95L1.95 9.9 9 9.05z",
        ["star-outline"]   = "M12 3.2 14.85 9.3l6.7.95-4.85 4.6 1.15 6.55L12 18.3l-5.85 3.1 1.15-6.55L2.45 10.25l6.7-.95z",
        ["stop"]           = "M6.6 6.6h10.8v10.8H6.6z",
        ["stopwatch"]      = "M12 5.6a8.2 8.2 0 1 0 0 16.4a8.2 8.2 0 1 0 0-16.4M12 9.6v4.2h3M9.2 2.4h5.6M18.6 6.6l1.9-1.9",
        ["swap"]           = "M4 8.4h15.4M15.6 4.6 19.4 8.4l-3.8 3.8M20 15.6H4.6M8.4 11.8 4.6 15.6l3.8 3.8",
        ["sync"]           = "M20.4 12a8.4 8.4 0 0 1-13.6 6.6M3.6 12a8.4 8.4 0 0 1 13.6-6.6M17.4 1.8v3.8h-3.8M6.6 22.2v-3.8h3.8",
        ["thermometer"]    = "M12 3.4a2.4 2.4 0 0 1 2.4 2.4v7.6a4.4 4.4 0 1 1-4.8 0V5.8A2.4 2.4 0 0 1 12 3.4zM12 8.6v6.6",
        ["touch"]          = "M9.6 11.2V5.6a2.2 2.2 0 0 1 4.4 0v8.2l2.9-1.05a2.1 2.1 0 0 1 2.7 2.5l-1.05 4.2a2.6 2.6 0 0 1-2.5 1.95h-4.6a2.6 2.6 0 0 1-1.85-.77L5.4 16.2a1.7 1.7 0 0 1 2.1-2.6z",
        ["upload"]         = "M12 15.4V3.6M7.4 8.2 12 3.6l4.6 4.6M4.4 20.4h15.2",
        ["vial"]           = "M9.4 3h5.2M10.6 3v7.6L6.6 17.7a2.6 2.6 0 0 0 2.25 3.9h6.3a2.6 2.6 0 0 0 2.25-3.9L13.4 10.6V3M8.2 14.4h7.6",
        ["video"]          = "M3.6 6.4h9.6a2 2 0 0 1 2 2v7.2a2 2 0 0 1-2 2H3.6a2 2 0 0 1-2-2V8.4a2 2 0 0 1 2-2zM15.2 13.2l5.2 3.4a.7.7 0 0 0 1.1-.6V8a.7.7 0 0 0-1.1-.6l-5.2 3.4z",
        ["view"]           = "M2.6 12S6.2 5.6 12 5.6 21.4 12 21.4 12 17.8 18.4 12 18.4 2.6 12 2.6 12zM12 8.8a3.2 3.2 0 1 0 0 6.4a3.2 3.2 0 1 0 0-6.4",
        ["volume"]         = "M4 9.2h3.2L12 5.2v13.6L7.2 14.8H4a.8.8 0 0 1-.8-.8v-4a.8.8 0 0 1 .8-.8zM15.4 9.4a3.7 3.7 0 0 1 0 5.2M18.2 6.6a7.6 7.6 0 0 1 0 10.8",
        ["volume-low"]     = "M4 9.2h3.2L12 5.2v13.6L7.2 14.8H4a.8.8 0 0 1-.8-.8v-4a.8.8 0 0 1 .8-.8zM15.4 9.4a3.7 3.7 0 0 1 0 5.2",
        ["warning"]        = "M12 3.4 21.6 20.4H2.4zM12 9.6v4.6M12 17.4v.05",
        ["wifi"]           = "M2.4 8.8a14 14 0 0 1 19.2 0M5.8 12.6a9.2 9.2 0 0 1 12.4 0M9.2 16.4a4.4 4.4 0 0 1 5.6 0M12 20.2h.05",
        ["wireless"]       = "M6.4 4.6h11.2a2 2 0 0 1 2 2v10.8a2 2 0 0 1-2 2H6.4a2 2 0 0 1-2-2V6.6a2 2 0 0 1 2-2zM8.6 12.4a4.8 4.8 0 0 1 6.8 0M10.6 15a2 2 0 0 1 2.8 0M12 8.4h.05",
        ["wrench"]         = "M15.4 2.8a6.2 6.2 0 0 0-5.5 8.95L3.4 18.25a2 2 0 0 0 2.85 2.85l6.5-6.5A6.2 6.2 0 0 0 20.4 6.4l-3.3 3.3-2.8-2.8 3.3-3.3a6.2 6.2 0 0 0-2.2-.8z"
    };

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
        _pathRegistry["db"]      = "Assets/Icons/db.svg";
        _pathRegistry["audio"]   = "Assets/Icons/audio.svg";
        _pathRegistry["file"]    = "Assets/Icons/generic.svg";
        _pathRegistry["link"]    = "Assets/Icons/link.svg";
        _pathRegistry["script"]  = "Assets/Icons/script.svg";
        _pathRegistry["root"]    = "Assets/Icons/root.svg";
        _pathRegistry["data"]    = "Assets/Icons/data.svg";
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
        foreach (var key in _pathRegistry.Keys)
            _ = Get(key);
    }

    public static IconShape? Vector(string? key) =>
        string.IsNullOrEmpty(key) ? null : _shapeCache.GetOrAdd(key, Build);

    private static IconShape? Build(string key)
    {
        if (!_vectors.TryGetValue(key, out var data)) return null;

        var filled = data[0] == 'F';
        // Parse hands back a frozen geometry, so clone before giving it the centring transform.
        var geometry = System.Windows.Media.Geometry.Parse(filled ? data : "F1 " + data).Clone();

        // The stroke is symmetric about the path, so the fill bounds and the ink share a centre:
        // recentring on those bounds leaves equal margins on all four sides, which is what makes an
        // icon sit true inside a circular or square container whatever its natural silhouette.
        var bounds = geometry.Bounds;
        var centring = new TranslateTransform(
            (DesignSize - bounds.Width) / 2 - bounds.X,
            (DesignSize - bounds.Height) / 2 - bounds.Y);
        centring.Freeze();
        geometry.Transform = centring;

        geometry.Freeze();
        return new IconShape(geometry, filled);
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

/// <summary>
/// Draws a registered vector icon at <see cref="Size"/>, tinted with the inherited foreground.
/// A bare <see cref="FrameworkElement"/> rather than a templated control: one draw call per icon,
/// no visual-tree overhead, and resolution independence at every DPI and size.
/// </summary>
public sealed class Icon : FrameworkElement
{
    private const double DesignStroke = 2.0;

    static Icon()
    {
        HorizontalAlignmentProperty.OverrideMetadata(typeof(Icon),
            new FrameworkPropertyMetadata(HorizontalAlignment.Center));
        VerticalAlignmentProperty.OverrideMetadata(typeof(Icon),
            new FrameworkPropertyMetadata(VerticalAlignment.Center));
    }

    public static readonly DependencyProperty KindProperty =
        DependencyProperty.Register(nameof(Kind), typeof(string), typeof(Icon),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SizeProperty =
        DependencyProperty.Register(nameof(Size), typeof(double), typeof(Icon),
            new FrameworkPropertyMetadata(18.0,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner(typeof(Icon),
            new FrameworkPropertyMetadata(Brushes.White,
                FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender,
                (d, _) => ((Icon)d)._pen = null));

    public string Kind
    {
        get => (string)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    private Pen? _pen;

    protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize) => new(Size, Size);

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (AppIcons.Vector(Kind) is not { } shape || Foreground is not { } brush) return;

        var transform = new ScaleTransform(Size / AppIcons.DesignSize, Size / AppIcons.DesignSize);
        transform.Freeze();

        drawingContext.PushTransform(transform);
        if (shape.IsFilled) drawingContext.DrawGeometry(brush, null, shape.Geometry);
        else drawingContext.DrawGeometry(null, _pen ??= CreatePen(brush), shape.Geometry);
        drawingContext.Pop();
    }

    private static Pen CreatePen(Brush brush)
    {
        var pen = new Pen(brush, DesignStroke)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        if (pen.CanFreeze) pen.Freeze();
        return pen;
    }
}

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

/// <summary>
/// Marks a navigation control as the "hero" feature so the shared template can dress it with a
/// signature icon glow and sparkle — keeping every nav button on one template while letting a
/// single flagship (Zephyr's Recipes) stand apart.
/// </summary>
public static class NavButton
{
    public static readonly DependencyProperty IsFeaturedProperty =
        DependencyProperty.RegisterAttached(
            "IsFeatured", typeof(bool), typeof(NavButton), new PropertyMetadata(false));

    public static bool GetIsFeatured(DependencyObject element) => (bool)element.GetValue(IsFeaturedProperty);
    public static void SetIsFeatured(DependencyObject element, bool value) => element.SetValue(IsFeaturedProperty, value);
}

[MarkupExtensionReturnType(typeof(string))]
public sealed class TranslateExtension : MarkupExtension
{
    private readonly string _key;

    public TranslateExtension(string key)
    {
        _key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        UIHelpers.Translation(_key).ProvideValue(serviceProvider);
}

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

/// <summary>
/// Grows a centered block with the available space (bind whichever dimension actually constrains it —
/// here the empty state is tall and narrow, so height governs) so it stops looking lost on a large
/// display. ConverterParameter is "reference[,max[,min]]" — scale is value/reference clamped to
/// [min, max] (defaults: max 1.6, min 1.0). A min below 1 lets the block breathe back down just enough
/// to survive a very short window instead of clipping.
/// </summary>
public sealed class SizeToScaleConverter : IValueConverter
{
    public static SizeToScaleConverter Instance { get; } = new();
    private SizeToScaleConverter() { }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double size || double.IsNaN(size) || size <= 0) return 1.0;

        double reference = 1000, max = 1.6, min = 1.0;
        if (parameter is string p)
        {
            var parts = p.Split(',');
            if (parts.Length > 0 && double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var r) && r > 0) reference = r;
            if (parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var m) && m >= 1) max = m;
            if (parts.Length > 2 && double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var n) && n > 0) min = Math.Min(n, 1.0);
        }
        return Math.Clamp(size / reference, min, max);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class BatteryLevelToBrushConverter : IValueConverter
{
    public static BatteryLevelToBrushConverter Instance { get; } = new();
    private BatteryLevelToBrushConverter() { }

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
    private StringToVisibilityConverter() { }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>How many card columns a given viewport width carries. Read from code rather than a
/// binding since the Debloat list groups its own rows, but the rule stays in one place.</summary>
public static class ResponsiveColumns
{
    private const double MinColumnWidth = 580;
    private const int MaxColumns = 3;

    public static int For(double width) =>
        width <= 0 ? 1 : Math.Min(Math.Max(1, (int)(width / MinColumnWidth)), MaxColumns);
}

public sealed class InverseBoolConverter : IValueConverter
{
    public static InverseBoolConverter Instance { get; } = new();
    private InverseBoolConverter() { }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not (bool and true);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not (bool and true);
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
    private LoadingTextConverter() { }

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

        element.Loaded += handlers.OnLoaded;
        element.Unloaded += handlers.OnUnloaded;

        // Pages arm the guard from their own Loaded handler, so the guarded control has already
        // loaded by the time we get here and OnLoaded would not fire again until the next
        // navigation: attach now, or the control stays frozen at its start-up state for a whole
        // visit (a phone plugged in afterwards would never enable it).
        if (element.IsLoaded) handlers.Attach();
        else handlers.UpdateState();
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

        public void Attach()
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

