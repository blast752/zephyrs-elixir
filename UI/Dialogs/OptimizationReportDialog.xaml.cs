namespace ZephyrsElixir.UI.Dialogs;

public partial class OptimizationReportDialog : Window
{
    // One source per accent colour: the same value feeds the foreground brush (below) and the
    // header glow in GetTheme, so the two can't drift apart.
    private static readonly Color SuccessColor = Color.FromRgb(0, 200, 120);
    private static readonly Color WarningColor = Color.FromRgb(255, 180, 50);
    private static readonly Color ErrorColor   = Color.FromRgb(255, 90, 90);

    private static readonly Brush SuccessAccent = Freeze(new SolidColorBrush(SuccessColor));
    private static readonly Brush WarningAccent = Freeze(new SolidColorBrush(WarningColor));
    private static readonly Brush ErrorAccent   = Freeze(new SolidColorBrush(ErrorColor));

    private static readonly Brush DetailGreen  = Freeze(new SolidColorBrush(Color.FromRgb(100, 200, 120)));
    private static readonly Brush DetailGray   = Freeze(new SolidColorBrush(Color.FromRgb(128, 140, 160)));
    private static readonly Brush DetailMuted  = Freeze(new SolidColorBrush(Color.FromRgb(176, 184, 200)));

    private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

    private readonly record struct OutcomeTheme(
        string Icon,
        string Title,
        string Subtitle,
        Color GlowColor,
        Brush IconBackground,
        string ButtonText,
        string ButtonIcon);

    private static OutcomeTheme GetTheme(OptimizationOutcome outcome, int stepsCompleted, int totalSteps) => outcome switch
    {
        OptimizationOutcome.Success => new(
            "\uE73E",
            Strings.Report_Outcome_Success_Title,
            Strings.Report_Outcome_Success_Subtitle,
            SuccessColor,
            AppBrushes.GradientGreen,
            Strings.Report_Button_Done, "\uE73E"),

        OptimizationOutcome.Partial => new(
            "\uE7BA",
            Strings.Report_Outcome_Partial_Title,
            string.Format(Strings.Report_Outcome_Partial_Subtitle, stepsCompleted, totalSteps),
            WarningColor,
            UIHelpers.CreateGradientBrush("#FFB832", "#FF9500"),
            Strings.Dialog_Button_GotIt, "\uE7BA"),

        OptimizationOutcome.Error => new(
            "\uEA39",
            Strings.Report_Outcome_Error_Title,
            Strings.Report_Outcome_Error_Subtitle,
            ErrorColor,
            AppBrushes.GradientRed,
            Strings.Window_Tooltip_Close, "\uEA39"),
        
        _ => GetTheme(OptimizationOutcome.Success, stepsCompleted, totalSteps)
    };

    public OptimizationReportDialog(OptimizationReport report)
    {
        InitializeComponent();
        DataContext = report;
        ApplyTheme(report);
        BuildSections(report);
    }

    private void ApplyTheme(OptimizationReport r)
    {
        var theme = GetTheme(r.Outcome, r.CompletedSteps, r.TotalSteps);

        HeaderIcon.Text = theme.Icon;
        IconCircle.Background = theme.IconBackground;
        IconGlow.Background = theme.IconBackground;

        if (IconGlow.Effect is DropShadowEffect glow)
            glow.Color = theme.GlowColor;

        HeaderTitle.Text = theme.Title;
        HeaderSubtitle.Text = theme.Subtitle;

        TotalFreed.Text = UIHelpers.FormatSize(r.MemoryFreedKb + r.StorageCleanedKb);
        StepsCompleted.Text = $"{r.CompletedSteps} / {r.TotalSteps}";
        StepsCompleted.Foreground = r.Outcome == OptimizationOutcome.Success
            ? SuccessAccent
            : r.Outcome == OptimizationOutcome.Partial ? WarningAccent : ErrorAccent;

        DoneButton.Content = theme.ButtonText;
        DoneButton.Tag = theme.ButtonIcon;
    }

    private void BuildSections(OptimizationReport r)
    {
        if (r.MemoryFreedKb > 0)
        {
            MemorySection.Visibility = Visibility.Visible;
            MemoryFreed.Text = $"{r.MemoryFreedKb / 1024.0:F1} MB";
            AppsKilledCount.Text = string.Format(Strings.Report_Memory_AppsCount, r.AppsForceKilled.Count);

            if (r.AppsForceKilled.Count > 0)
            {
                AppsPanel.Visibility = Visibility.Visible;
                AddAppEntries(AppsPanel, r.AppsForceKilled);
            }
        }

        if (r.StorageCleanedKb > 0 || r.TrimExecuted)
        {
            StorageSection.Visibility = Visibility.Visible;
            StorageFreed.Text = UIHelpers.FormatSize(r.StorageCleanedKb);

            if (r.CleanedItems.Count > 0)
            {
                CleanedPanel.Visibility = Visibility.Visible;
                foreach (var item in r.CleanedItems)
                    CleanedPanel.Children.Add(CreateDetailLine($"✓ {item}", DetailGreen));
            }

            ApplyStatusBadge(TrimStatus, r.TrimExecuted);
        }

        if (r.NetworkOptimized)
            NetworkSection.Visibility = Visibility.Visible;

        if (!string.IsNullOrEmpty(r.CompilationMode))
        {
            CompilationSection.Visibility = Visibility.Visible;
            CompilationMode.Text = r.CompilationMode.ToUpperInvariant();
        }

        ApplyStatusBadge(DexStatus, r.DexOptimized);
    }

    private void AddAppEntries(Panel panel, IList<(string Package, long MemoryKb)> apps)
    {
        const int maxVisible = 5;

        foreach (var (pkg, kb) in apps.Take(maxVisible))
        {
            var name = pkg.Split('.').LastOrDefault() ?? pkg;
            panel.Children.Add(CreateDetailLine($"• {name} ({kb / 1024.0:F1} MB)", DetailMuted, 12, 11));
        }

        if (apps.Count > maxVisible)
            panel.Children.Add(CreateDetailLine(
                string.Format(Strings.Report_MoreApps, apps.Count - maxVisible), DetailGray, 11, 10, FontStyles.Italic));
    }

    private static TextBlock CreateDetailLine(string text, Brush fg,
        double fontSize = 12, double marginLeft = 12, FontStyle? style = null) => new()
    {
        Text = text,
        Foreground = fg,
        FontSize = fontSize,
        FontStyle = style ?? FontStyles.Normal,
        Margin = new Thickness(marginLeft, 2, 0, 2)
    };

    private static void ApplyStatusBadge(TextBlock target, bool completed)
    {
        target.Text = completed ? $"✓ {Strings.Report_Status_Completed}" : $"— {Strings.Report_Status_Skipped}";
        target.Foreground = completed ? DetailGreen : DetailGray;
    }
}