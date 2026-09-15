namespace JellyfinWhisperCommand;

public sealed class JellyfinClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _apiKey;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public JellyfinClient(JellyfinSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.BaseUrl) || string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException("请先在 appsettings.json 中填写 Jellyfin 的 BaseUrl 和 ApiKey。");

        _baseUrl = settings.BaseUrl.TrimEnd('/');
        _apiKey = settings.ApiKey;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("X-Emby-Token", _apiKey);
    }

    public async Task<IReadOnlyList<MediaLibrary>> GetLibrariesAsync()
    {
        using var response = await _http.GetAsync($"{_baseUrl}/Library/MediaFolders");
        await EnsureSuccessAsync(response);
        var result = await response.Content.ReadFromJsonAsync<JellyfinItemsResponse>(_jsonOptions) ?? new JellyfinItemsResponse();
        return result.Items.Where(x => !string.IsNullOrWhiteSpace(x.Id))
                      .Select(x => new MediaLibrary(x.Id, x.Name))
                      .OrderBy(x => x.Name, StringComparer.CurrentCulture)
                      .ToList();
    }

    public async Task<JellyfinItemsResponse> GetItemsAsync(string libraryId, string sortBy, bool hasSubtitles, string? searchTerm, int startIndex, int limit)
    {
        if (!string.IsNullOrWhiteSpace(searchTerm))
            return await GetSearchItemsAsync(libraryId, sortBy, hasSubtitles, searchTerm.Trim(), startIndex, limit);

        var query = new Dictionary<string, string>
        {
            ["ParentId"] = libraryId,
            ["Recursive"] = "true",
            ["IncludeItemTypes"] = "Movie,Episode,Video",
            ["Fields"] = "Path,ImageTags,DateCreated,PremiereDate",
            ["SortBy"] = sortBy,
            ["SortOrder"] = "Descending",
            ["HasSubtitles"] = hasSubtitles.ToString().ToLowerInvariant(),
            ["StartIndex"] = startIndex.ToString(),
            ["Limit"] = limit.ToString()
        };
        if (!string.IsNullOrWhiteSpace(searchTerm))
            query["SearchTerm"] = searchTerm.Trim();
        var url = $"{_baseUrl}/Items?{string.Join("&", query.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"))}";
        using var response = await _http.GetAsync(url);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<JellyfinItemsResponse>(_jsonOptions) ?? new JellyfinItemsResponse();
    }

    private async Task<JellyfinItemsResponse> GetSearchItemsAsync(string libraryId, string sortBy, bool hasSubtitles, string searchTerm, int startIndex, int limit)
    {
        // Jellyfin 10.10 ignores SearchTerm on the /Items endpoint. Search hints provide
        // the matching IDs, after which /Items can apply the remaining filters and paging.
        var hintQuery = new Dictionary<string, string>
        {
            ["SearchTerm"] = searchTerm,
            ["ParentId"] = libraryId,
            ["Recursive"] = "true",
            ["IncludeItemTypes"] = "Movie,Episode,Video",
            ["Limit"] = "10000"
        };
        var hintUrl = $"{_baseUrl}/Search/Hints?{BuildQuery(hintQuery)}";
        using var hintResponse = await _http.GetAsync(hintUrl);
        await EnsureSuccessAsync(hintResponse);
        var hints = await hintResponse.Content.ReadFromJsonAsync<JellyfinSearchHintsResponse>(_jsonOptions)
                    ?? new JellyfinSearchHintsResponse();
        var itemIds = hints.SearchHints
            .Select(x => x.ItemId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (itemIds.Count == 0) return new JellyfinItemsResponse();

        var itemQuery = new Dictionary<string, string>
        {
            ["Ids"] = string.Join(",", itemIds),
            ["Fields"] = "Path,ImageTags,DateCreated,PremiereDate",
            ["SortBy"] = sortBy,
            ["SortOrder"] = "Descending",
            ["HasSubtitles"] = hasSubtitles.ToString().ToLowerInvariant(),
            ["StartIndex"] = startIndex.ToString(),
            ["Limit"] = limit.ToString()
        };
        using var itemResponse = await _http.GetAsync($"{_baseUrl}/Items?{BuildQuery(itemQuery)}");
        await EnsureSuccessAsync(itemResponse);
        return await itemResponse.Content.ReadFromJsonAsync<JellyfinItemsResponse>(_jsonOptions)
               ?? new JellyfinItemsResponse();
    }

    private static string BuildQuery(IEnumerable<KeyValuePair<string, string>> query) =>
        string.Join("&", query.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));

    public async Task<IReadOnlyList<string>> GetPathsAsync(string itemId)
    {
        // Some Jellyfin versions reject the single-item route for API-key access.
        // The collection route accepts the same ID and returns the requested fields.
        using var response = await _http.GetAsync($"{_baseUrl}/Items?Ids={Uri.EscapeDataString(itemId)}&Fields=Path");
        await EnsureSuccessAsync(response);
        var item = (await response.Content.ReadFromJsonAsync<JellyfinItemsResponse>(_jsonOptions))?.Items.FirstOrDefault();
        if (item is null) return [];

        var paths = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.Path)) paths.Add(item.Path);
        if (item.PartCount <= 1) return paths;

        using var partsResponse = await _http.GetAsync($"{_baseUrl}/Videos/{Uri.EscapeDataString(itemId)}/AdditionalParts");
        await EnsureSuccessAsync(partsResponse);
        var parts = await partsResponse.Content.ReadFromJsonAsync<JellyfinItemsResponse>(_jsonOptions);
        paths.AddRange((parts?.Items ?? []).Select(x => x.Path).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!));
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task RefreshLibraryAsync(string libraryId)
    {
        using var response = await _http.PostAsync(
            $"{_baseUrl}/Items/{Uri.EscapeDataString(libraryId)}/Refresh?Recursive=true&MetadataRefreshMode=Default&ImageRefreshMode=None&ReplaceAllMetadata=false&ReplaceAllImages=false",
            content: null);
        await EnsureSuccessAsync(response);
    }

    public string GetImageUrl(JellyfinItem item)
    {
        var tag = item.ImageTags?.GetValueOrDefault("Primary");
        var query = string.IsNullOrEmpty(tag) ? "" : $"&tag={Uri.EscapeDataString(tag)}";
        return $"{_baseUrl}/Items/{Uri.EscapeDataString(item.Id)}/Images/Primary?maxWidth=320&quality=85&api_key={Uri.EscapeDataString(_apiKey)}{query}";
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync();
        throw new HttpRequestException($"Jellyfin API 返回 {(int)response.StatusCode} {response.ReasonPhrase}。{body}");
    }

    public void Dispose() => _http.Dispose();
}
