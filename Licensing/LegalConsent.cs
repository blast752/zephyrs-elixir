namespace ZephyrsElixir.Licensing;

/// <summary>
/// Persists acceptance of the EULA / legal terms (first-run gate). Stored as a tiny marker
/// file in %LocalAppData%\ZephyrsElixir so it survives updates but is removed on a full
/// uninstall — consistent with the app's local-data model.
/// </summary>
public static class LegalConsent
{
    private static readonly string MarkerPath = Path.Combine(
        AppConfiguration.Paths.LocalAppDataRoot, ".legal_accepted");

    /// <summary>True if the current EULA version has already been accepted.</summary>
    public static bool IsAccepted()
    {
        try
        {
            return File.Exists(MarkerPath) &&
                   File.ReadAllText(MarkerPath).Trim() == AppConfiguration.Legal.EulaVersion;
        }
        catch { return false; }
    }

    /// <summary>Records acceptance of the current EULA version.</summary>
    public static void Accept()
    {
        try
        {
            Directory.CreateDirectory(AppConfiguration.Paths.LocalAppDataRoot);
            File.WriteAllText(MarkerPath, AppConfiguration.Legal.EulaVersion);
        }
        catch { /* non-fatal */ }
    }
}
