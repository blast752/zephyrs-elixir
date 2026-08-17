namespace ZephyrsElixir.Core;

public enum CommunitySort { Top, Recent, MostUsed }

public sealed class CommunityRecipe
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Glyph { get; set; } = RecipeStyle.DefaultGlyph;
    public string Accent { get; set; } = RecipeStyle.DefaultAccent;
    public bool RequiresPro { get; set; }
    public int Likes { get; set; }
    public int Downloads { get; set; }
    public int Applied { get; set; }
    public string? CreatedUtc { get; set; }
    public int Packages { get; set; }
    public bool HasOptimization { get; set; }
    public bool HasTweaks { get; set; }
    public bool HasInstall { get; set; }
}

/// <summary>
/// Thin client for the community recipe marketplace. Identity is the anonymous device
/// fingerprint already used for licensing — enough for one-like-per-install and rate limiting
/// without accounts. All failures surface as exceptions so the UI can show its offline state.
/// </summary>
public static class RecipeCommunity
{
    private static readonly HttpClient Http;

    static RecipeCommunity()
    {
        Http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        Http.DefaultRequestHeaders.Add("User-Agent", AppConfiguration.Urls.HttpUserAgent);
    }

    private static string ClientId => LicenseService.Instance.DeviceFingerprint;

    public static async Task<IReadOnlyList<CommunityRecipe>> BrowseAsync(
        CommunitySort sort, string? query = null, CancellationToken ct = default)
    {
        var sortKey = sort switch
        {
            CommunitySort.Recent => "recent",
            CommunitySort.MostUsed => "used",
            _ => "top"
        };

        var url = $"{AppConfiguration.Urls.RecipesApi}?sort={sortKey}";
        if (!string.IsNullOrWhiteSpace(query))
            url += $"&q={Uri.EscapeDataString(query.Trim())}";

        var list = await Http.GetFromJsonAsync<List<CommunityRecipe>>(url, RecipeJson.Options, ct);
        return list ?? new List<CommunityRecipe>();
    }

    public static async Task<Recipe?> DownloadAsync(string id, CancellationToken ct = default)
    {
        var response = await PostAsync(new { action = "download", id, clientId = ClientId }, ct);
        var payload = response.GetPropertyOrNull("recipe");
        if (payload is null) return null;

        var recipe = payload.Value.Deserialize<Recipe>(RecipeJson.Options);
        if (recipe is null) return null;

        recipe.CommunityId = id;
        return recipe;
    }

    /// <summary>
    /// Toggles this device's like on a recipe (one per device) and returns the new like count and
    /// whether it is now liked. The local liked-set is kept in sync so the state survives a restart.
    /// </summary>
    public static async Task<(int Likes, bool Liked)> ToggleLikeAsync(string id, CancellationToken ct = default)
    {
        var response = await PostAsync(new { action = "like", id, clientId = ClientId }, ct);
        var likes = response.GetPropertyOrNull("likes")?.GetInt32() ?? 0;
        var liked = response.GetPropertyOrNull("liked")?.GetBoolean() ?? false;
        RecipeStore.SetLiked(id, liked);
        return (likes, liked);
    }

    public static async Task<string?> UploadAsync(Recipe recipe, CancellationToken ct = default)
    {
        var response = await PostAsync(new
        {
            action = "upload",
            clientId = ClientId,
            recipe = recipe.ToShareable()
        }, ct);
        return response.GetPropertyOrNull("id")?.GetString();
    }

    /// <summary>Removes a recipe the current device published. The server enforces authorship.</summary>
    public static Task UnpublishAsync(string communityId, CancellationToken ct = default) =>
        PostAsync(new { action = "unpublish", id = communityId, clientId = ClientId }, ct);

    /// <summary>
    /// Best-effort takedown fired when a published recipe is deleted locally, so its server copy is
    /// never orphaned. Detached so the row disappears instantly; the server enforces authorship, so
    /// it is simply ignored for a recipe this device didn't publish (e.g. a downloaded one).
    /// </summary>
    public static void UnpublishInBackground(string communityId) => _ = Task.Run(async () =>
    {
        try { await UnpublishAsync(communityId); }
        catch { /* offline, or not our recipe to remove */ }
    });

    public static void ReportUsed(string id) => _ = Task.Run(async () =>
    {
        try { await PostAsync(new { action = "used", id, clientId = ClientId }, CancellationToken.None); }
        catch { /* best-effort telemetry ping */ }
    });

    private static async Task<JsonElement> PostAsync(object body, CancellationToken ct)
    {
        using var response = await Http.PostAsJsonAsync(AppConfiguration.Urls.RecipesApi, body, RecipeJson.Options, ct);

        // Error bodies are not guaranteed to be JSON (proxies, gateways, empty 5xx): a parse
        // failure must not mask the HTTP status the UI needs to show its offline state.
        JsonElement json = default;
        try { json = await response.Content.ReadFromJsonAsync<JsonElement>(RecipeJson.Options, ct); }
        catch (JsonException) { }

        if (!response.IsSuccessStatusCode)
        {
            var error = json.GetPropertyOrNull("error")?.GetString();
            throw new HttpRequestException(error ?? $"HTTP {(int)response.StatusCode}");
        }
        return json;
    }

    private static JsonElement? GetPropertyOrNull(this JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined
            ? value
            : null;
}
