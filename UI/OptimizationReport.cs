namespace ZephyrsElixir.UI;

public enum OptimizationOutcome
{
    Success,
    Partial,
    Error
}

public sealed class OptimizationReport
{
    public OptimizationOutcome Outcome { get; set; } = OptimizationOutcome.Success;
    public int CompletedSteps { get; set; }
    public int TotalSteps { get; set; } = 7;
    public string? ErrorMessage { get; set; }

    public bool CacheCleared { get; set; }
    public long MemoryFreedKb { get; set; }
    public int ProcessesKilled { get; set; }
    public List<(string Package, long MemoryKb)> AppsForceKilled { get; } = new();
    public long StorageCleanedKb { get; set; }
    public List<string> CleanedItems { get; } = new();
    public bool TrimExecuted { get; set; }
    public bool NetworkOptimized { get; set; }
    public string? CompilationMode { get; set; }
    public bool DexOptimized { get; set; }
    
    public double TotalFreedMb => (MemoryFreedKb + StorageCleanedKb) / 1024.0;

    public void Reset()
    {
        Outcome = OptimizationOutcome.Success;
        CompletedSteps = 0;
        ErrorMessage = null;
        CacheCleared = false;
        MemoryFreedKb = 0;
        ProcessesKilled = 0;
        AppsForceKilled.Clear();
        StorageCleanedKb = 0;
        CleanedItems.Clear();
        TrimExecuted = false;
        NetworkOptimized = false;
        CompilationMode = null;
        DexOptimized = false;
    }
}
