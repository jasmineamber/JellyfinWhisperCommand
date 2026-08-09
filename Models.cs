namespace JellyfinWhisperCommand;

public sealed class AppSettings
{
    public JellyfinSettings Jellyfin { get; init; } = new();
    public WhisperJavSettings WhisperJav { get; init; } = new();
    public SeconvSettings Seconv { get; init; } = new();
}

public sealed class JellyfinSettings
{
    public string BaseUrl { get; init; } = "";
    public string ApiKey { get; init; } = "";
}

public sealed class WhisperJavSettings
{
    public string ExecutablePath { get; init; } = "whisperjav.exe";
    public string OutputDir { get; init; } = "D:\\Temp\\output";
    public string TranslateModel { get; init; } = "deepseek-v4-flash";
    public string TranslateApiKey { get; init; } = "";
    public string TranslateEndpoint { get; init; } = "";
}

public sealed class SeconvSettings
{
    public string ExecutablePath { get; init; } = "seconv";
    public string MultipleReplaceRulesFile { get; init; } = "D:\\Temp\\SE_Replace_Rules.csv";
    public string InputFolder { get; init; } = "D:\\Temp";
}

public sealed class UserSettings
{
    public string? LastLibraryId { get; set; }
}

public sealed record Option<T>(string Name, T Value);

public sealed record MediaLibrary(string Id, string Name);

public sealed class MediaItem : ObservableObject
{
    private bool _isSelected;
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string ImageUrl { get; init; }
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}

public sealed class JellyfinItemsResponse
{
    public List<JellyfinItem> Items { get; init; } = [];
    public int TotalRecordCount { get; init; }
}

public sealed class JellyfinItem
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Path { get; init; }
    public int PartCount { get; init; }
    public Dictionary<string, string>? ImageTags { get; init; }
}
