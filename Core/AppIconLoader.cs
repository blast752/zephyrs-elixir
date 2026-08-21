
namespace ZephyrsElixir.Core;
public static class AppIconLoader
{
    private const int IconDecodeWidth = 96;

    private static readonly ConcurrentDictionary<string, BitmapImage?> IconCache = new();
    private static readonly SemaphoreSlim LoadSemaphore = new(Environment.ProcessorCount);

    private static string CacheKey(string packageName) =>
        $"{DeviceManager.SharedActiveSerial}|{packageName}";

    public static async Task<BitmapImage?> LoadIconAsync(
        string packageName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packageName))
            return null;

        var key = CacheKey(packageName);
        if (IconCache.TryGetValue(key, out var cached))
            return cached;

        await LoadSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (IconCache.TryGetValue(key, out cached))
                return cached;

            var (icon, conclusive) = await ExtractIconAsync(packageName, cancellationToken);

            // "This package has no icon" is worth remembering. "The agent did not answer" is not:
            // caching that would leave the tile blank for the rest of the session over one hiccup.
            if (icon is not null || conclusive)
                IconCache.TryAdd(key, icon);

            return icon;
        }
        catch
        {
            return null;
        }
        finally
        {
            LoadSemaphore.Release();
        }
    }

    /// <summary>The icon, and whether the answer is final. Only a final answer may be cached: a 404
    /// means the package has no icon, anything else means the agent could not be asked.</summary>
    private static async Task<(BitmapImage? Icon, bool Conclusive)> ExtractIconAsync(
        string packageName,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await ZephyrsAgent.HttpClient.GetAsync(
                $"{ZephyrsAgent.BaseUri}/icon/{packageName}",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
                return (null, response.StatusCode == HttpStatusCode.NotFound);

            var imageBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (imageBytes is not { Length: > 0 })
                return (null, true);

            using var stream = new MemoryStream(imageBytes);
            return (UIHelpers.BitmapFromStream(stream, IconDecodeWidth), true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return (null, false);
        }
    }
}
