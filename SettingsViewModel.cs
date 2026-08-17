namespace JellyfinWhisperCommand;

public sealed class SettingsViewModel : ObservableObject
{
    private string _baseUrl = "";
    private string _apiKey = "";
    private string _whisperJavExecutablePath = "whisperjav.exe";
    private string _whisperJavOutputDir = "D:\\Temp\\output";
    private string _whisperJavArgs = "";
    private string _translateExecutablePath = "whisperjav-translate.exe";
    private string _translateProvider = "custom";
    private string _translateEndpoint = "";
    private string _translateApiKey = "";
    private string _translateModel = "deepseek-v4-flash";
    private string _translateSourceLanguage = "japanese";
    private string _translateTargetLanguage = "chinese";
    private string _translateTone = "standard";
    private string _translateInstructionsFilePath = "";
    private bool _translateStream = true;
    private string _seconvExecutablePath = "seconv";
    private string _seconvMultipleReplaceRulesFile = "D:\\Temp\\SE_Replace_Rules.csv";
    private string _seconvInputFolder = "D:\\Temp";
    private string _testResult = "";
    private string _saveResult = "";
    private bool _isTesting;

    public string BaseUrl { get => _baseUrl; set => SetProperty(ref _baseUrl, value); }
    public string ApiKey { get => _apiKey; set => SetProperty(ref _apiKey, value); }
    public string WhisperJavExecutablePath { get => _whisperJavExecutablePath; set => SetProperty(ref _whisperJavExecutablePath, value); }
    public string WhisperJavOutputDir { get => _whisperJavOutputDir; set => SetProperty(ref _whisperJavOutputDir, value); }
    public string WhisperJavArgs { get => _whisperJavArgs; set => SetProperty(ref _whisperJavArgs, value); }
    public string TranslateExecutablePath { get => _translateExecutablePath; set => SetProperty(ref _translateExecutablePath, value); }
    public string TranslateProvider { get => _translateProvider; set => SetProperty(ref _translateProvider, value); }
    public string TranslateEndpoint { get => _translateEndpoint; set => SetProperty(ref _translateEndpoint, value); }
    public string TranslateApiKey { get => _translateApiKey; set => SetProperty(ref _translateApiKey, value); }
    public string TranslateModel { get => _translateModel; set => SetProperty(ref _translateModel, value); }
    public string TranslateSourceLanguage { get => _translateSourceLanguage; set => SetProperty(ref _translateSourceLanguage, value); }
    public string TranslateTargetLanguage { get => _translateTargetLanguage; set => SetProperty(ref _translateTargetLanguage, value); }
    public string TranslateTone { get => _translateTone; set => SetProperty(ref _translateTone, value); }
    public string TranslateInstructionsFilePath { get => _translateInstructionsFilePath; set => SetProperty(ref _translateInstructionsFilePath, value); }
    public bool TranslateStream { get => _translateStream; set => SetProperty(ref _translateStream, value); }
    public string SeconvExecutablePath { get => _seconvExecutablePath; set => SetProperty(ref _seconvExecutablePath, value); }
    public string SeconvMultipleReplaceRulesFile { get => _seconvMultipleReplaceRulesFile; set => SetProperty(ref _seconvMultipleReplaceRulesFile, value); }
    public string SeconvInputFolder { get => _seconvInputFolder; set => SetProperty(ref _seconvInputFolder, value); }
    public string TestResult { get => _testResult; private set => SetProperty(ref _testResult, value); }
    public string SaveResult { get => _saveResult; private set => SetProperty(ref _saveResult, value); }
    public bool IsTesting { get => _isTesting; private set => SetProperty(ref _isTesting, value); }

    public event Action<AppSettings>? SettingsSaved;

    public AsyncRelayCommand TestConnectionCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand ReloadCommand { get; }

    public SettingsViewModel()
    {
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, () => !IsTesting);
        SaveCommand = new RelayCommand(Save);
        ReloadCommand = new RelayCommand(Reload);
    }

    public void LoadFrom(AppSettings settings)
    {
        BaseUrl = settings.Jellyfin.BaseUrl;
        ApiKey = settings.Jellyfin.ApiKey;
        WhisperJavExecutablePath = settings.WhisperJav.ExecutablePath;
        WhisperJavOutputDir = settings.WhisperJav.OutputDir;
        WhisperJavArgs = settings.WhisperJav.Args;
        TranslateExecutablePath = settings.WhisperJavTranslate.ExecutablePath;
        TranslateProvider = settings.WhisperJavTranslate.Provider;
        TranslateEndpoint = settings.WhisperJavTranslate.Endpoint;
        TranslateApiKey = settings.WhisperJavTranslate.ApiKey;
        TranslateModel = settings.WhisperJavTranslate.Model;
        TranslateSourceLanguage = settings.WhisperJavTranslate.SourceLanguage;
        TranslateTargetLanguage = settings.WhisperJavTranslate.TargetLanguage;
        TranslateTone = settings.WhisperJavTranslate.Tone;
        TranslateInstructionsFilePath = settings.WhisperJavTranslate.InstructionsFilePath;
        TranslateStream = settings.WhisperJavTranslate.Stream;
        SeconvExecutablePath = settings.Seconv.ExecutablePath;
        SeconvMultipleReplaceRulesFile = settings.Seconv.MultipleReplaceRulesFile;
        SeconvInputFolder = settings.Seconv.InputFolder;
    }

    public AppSettings BuildAppSettings() => new()
    {
        Jellyfin = new JellyfinSettings
        {
            BaseUrl = BaseUrl.Trim(),
            ApiKey = ApiKey.Trim()
        },
        WhisperJav = new WhisperJavSettings
        {
            ExecutablePath = WhisperJavExecutablePath.Trim(),
            OutputDir = WhisperJavOutputDir.Trim(),
            Args = WhisperJavArgs
        },
        WhisperJavTranslate = new WhisperJavTranslateSettings
        {
            ExecutablePath = TranslateExecutablePath.Trim(),
            Provider = TranslateProvider.Trim(),
            Endpoint = TranslateEndpoint.Trim(),
            ApiKey = TranslateApiKey.Trim(),
            Model = TranslateModel.Trim(),
            SourceLanguage = TranslateSourceLanguage.Trim(),
            TargetLanguage = TranslateTargetLanguage.Trim(),
            Tone = TranslateTone.Trim(),
            InstructionsFilePath = TranslateInstructionsFilePath.Trim(),
            Stream = TranslateStream
        },
        Seconv = new SeconvSettings
        {
            ExecutablePath = SeconvExecutablePath.Trim(),
            MultipleReplaceRulesFile = SeconvMultipleReplaceRulesFile.Trim(),
            InputFolder = SeconvInputFolder.Trim()
        }
    };

    private async Task TestConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl) || string.IsNullOrWhiteSpace(ApiKey))
        {
            TestResult = "请先填写 BaseUrl 和 ApiKey。";
            return;
        }
        IsTesting = true;
        TestResult = "正在测试连接...";
        TestConnectionCommand.RaiseCanExecuteChanged();
        try
        {
            using var client = new JellyfinClient(new JellyfinSettings { BaseUrl = BaseUrl.Trim(), ApiKey = ApiKey.Trim() });
            var libraries = await client.GetLibrariesAsync();
            TestResult = $"连接成功，找到 {libraries.Count} 个媒体库。";
        }
        catch (Exception ex)
        {
            TestResult = $"连接失败：{ex.Message}";
        }
        finally
        {
            IsTesting = false;
            TestConnectionCommand.RaiseCanExecuteChanged();
        }
    }

    private void Save()
    {
        var settings = BuildAppSettings();
        SettingsStore.SaveAppSettings(settings);
        SaveResult = "设置已保存";
        SettingsSaved?.Invoke(settings);
    }

    private void Reload()
    {
        try
        {
            LoadFrom(SettingsStore.LoadAppSettings());
            TestResult = "已从 appsettings.json 重新加载。";
        }
        catch (Exception ex)
        {
            TestResult = $"重新加载失败：{ex.Message}";
        }
    }
}
