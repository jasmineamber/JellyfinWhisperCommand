namespace JellyfinWhisperCommand;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private static string AppDirectory => AppContext.BaseDirectory;

    public static AppSettings LoadAppSettings()
    {
        var path = Path.Combine(AppDirectory, "appsettings.json");
        if (!File.Exists(path)) throw new FileNotFoundException("未找到 appsettings.json。", path);
        return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions)
               ?? throw new InvalidOperationException("appsettings.json 格式不正确。");
    }

    public static UserSettings LoadUserSettings()
    {
        var path = Path.Combine(AppDirectory, "user-settings.json");
        return File.Exists(path)
            ? JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path), JsonOptions) ?? new UserSettings()
            : new UserSettings();
    }

    public static void SaveUserSettings(UserSettings settings) =>
        File.WriteAllText(Path.Combine(AppDirectory, "user-settings.json"), JsonSerializer.Serialize(settings, JsonOptions));
}
