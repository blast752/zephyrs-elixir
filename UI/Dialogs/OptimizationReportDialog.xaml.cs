
namespace ZephyrsElixir;

public partial class OptimizationReportDialog : Window
{
    public OptimizationReportDialog(OptimizationReport report)
    {
        InitializeComponent();
        DataContext = report;
        BuildContent(report);
    }

    private void BuildContent(OptimizationReport r)
    {
        TotalFreed.Text = r.TotalFreedMb >= 1024 ? $"{r.TotalFreedMb / 1024:F2} GB" : $"{r.TotalFreedMb:F1} MB";

        if (r.MemoryFreedKb > 0)
            MemorySection.Visibility = Visibility.Visible;
        MemoryFreed.Text = $"{r.MemoryFreedKb / 1024.0:F1} MB";
        AppsKilledCount.Text = r.AppsForceKilled.Count.ToString();

        if (r.AppsForceKilled.Count > 0)
        {
            AppsPanel.Visibility = Visibility.Visible;
            foreach (var (pkg, kb) in r.AppsForceKilled.Take(5))
            {
                var name = pkg.Split('.').LastOrDefault() ?? pkg;
                AppsPanel.Children.Add(new TextBlock
                {
                    Text = $"• {name} ({kb / 1024.0:F1} MB)",
                    Foreground = new SolidColorBrush(Color.FromRgb(176, 184, 200)),
                    FontSize = 12,
                    Margin = new Thickness(12, 2, 0, 2)
                });
            }
            if (r.AppsForceKilled.Count > 5)
                AppsPanel.Children.Add(new TextBlock
                {
                    Text = $"  +{r.AppsForceKilled.Count - 5} more...",
                    Foreground = new SolidColorBrush(Color.FromRgb(128, 140, 160)),
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(12, 2, 0, 2)
                });
        }

        if (r.StorageCleanedKb > 0)
            StorageSection.Visibility = Visibility.Visible;
        StorageFreed.Text = r.StorageCleanedKb >= 1024 * 1024 
            ? $"{r.StorageCleanedKb / 1024.0 / 1024:F2} GB" 
            : $"{r.StorageCleanedKb / 1024.0:F1} MB";

        if (r.CleanedItems.Count > 0)
        {
            CleanedPanel.Visibility = Visibility.Visible;
            foreach (var item in r.CleanedItems)
                CleanedPanel.Children.Add(new TextBlock
                {
                    Text = $"✓ {item}",
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 200, 120)),
                    FontSize = 12,
                    Margin = new Thickness(12, 2, 0, 2)
                });
        }

        TrimStatus.Text = r.TrimExecuted ? "✓ Completed" : "— Skipped";
        TrimStatus.Foreground = new SolidColorBrush(r.TrimExecuted ? Color.FromRgb(100, 200, 120) : Color.FromRgb(128, 140, 160));

        if (r.NetworkOptimized)
            NetworkSection.Visibility = Visibility.Visible;

        if (!string.IsNullOrEmpty(r.CompilationMode))
        {
            CompilationSection.Visibility = Visibility.Visible;
            CompilationMode.Text = r.CompilationMode.ToUpperInvariant();
        }

        DexStatus.Text = r.DexOptimized ? "✓ Optimized" : "— Skipped";
        DexStatus.Foreground = new SolidColorBrush(r.DexOptimized ? Color.FromRgb(100, 200, 120) : Color.FromRgb(128, 140, 160));
    }
}
