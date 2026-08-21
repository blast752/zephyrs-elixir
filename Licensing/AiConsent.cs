namespace ZephyrsElixir.Licensing;

/// <summary>
/// Persists the user's choice on the optional cloud AI analysis (AI Act art. 50 transparency
/// and opt-out). Absent marker = not yet decided; "1" = enabled; "0" = disabled.
/// Stored in %LocalAppData%\ZephyrsElixir.
/// </summary>
public static class AiConsent
{
    private static readonly string MarkerPath = Path.Combine(
        AppConfiguration.Paths.LocalAppDataRoot, ".ai_consent");

    /// <summary>True once the user has made a choice (enabled or disabled).</summary>
    public static bool IsDecided() => File.Exists(MarkerPath);

    /// <summary>True only if cloud AI analysis is explicitly enabled.</summary>
    public static bool IsEnabled()
    {
        try { return File.Exists(MarkerPath) && File.ReadAllText(MarkerPath).Trim() == "1"; }
        catch { return false; }
    }

    /// <summary>Records the user's choice; can be changed later from Settings.</summary>
    public static void SetEnabled(bool enabled)
    {
        try
        {
            Directory.CreateDirectory(AppConfiguration.Paths.LocalAppDataRoot);
            File.WriteAllText(MarkerPath, enabled ? "1" : "0");
        }
        catch { /* non-fatal */ }
    }

    /// <summary>Clears the choice, returning the app to the undecided state of a fresh install.</summary>
    public static void Reset()
    {
        try { File.Delete(MarkerPath); } catch { /* non-fatal */ }
    }
}
