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
    private string _saveError = "";
    private string _reloadFeedback = "";
    private bool _isReloadFeedbackError;
    private bool _isTesting;
    private bool _isConnectionVerified;
    private bool _isDirty;
    private bool _isLoading;
    private bool _hasSavedCurrentSnapshot;
    private bool _isReloadConfirmationVisible;
    private string _savedSettingsSnapshot = "";
    private int _connectionInputVersion;
    private int _reloadFeedbackVersion;
    private DispatcherTimer? _reloadFeedbackTimer;

    public string BaseUrl { get => _baseUrl; set => SetSettingProperty(ref _baseUrl, value, resetsConnectionTest: true); }
    public string ApiKey { get => _apiKey; set => SetSettingProperty(ref _apiKey, value, resetsConnectionTest: true); }
    public string WhisperJavExecutablePath { get => _whisperJavExecutablePath; set => SetSettingProperty(ref _whisperJavExecutablePath, value); }
    public string WhisperJavOutputDir { get => _whisperJavOutputDir; set => SetSettingProperty(ref _whisperJavOutputDir, value); }
    public string WhisperJavArgs { get => _whisperJavArgs; set => SetSettingProperty(ref _whisperJavArgs, value); }
    public string TranslateExecutablePath { get => _translateExecutablePath; set => SetSettingProperty(ref _translateExecutablePath, value); }
    public string TranslateProvider { get => _translateProvider; set => SetSettingProperty(ref _translateProvider, value); }
    public string TranslateEndpoint { get => _translateEndpoint; set => SetSettingProperty(ref _translateEndpoint, value); }
    public string TranslateApiKey { get => _translateApiKey; set => SetSettingProperty(ref _translateApiKey, value); }
    public string TranslateModel { get => _translateModel; set => SetSettingProperty(ref _translateModel, value); }
    public string TranslateSourceLanguage { get => _translateSourceLanguage; set => SetSettingProperty(ref _translateSourceLanguage, value); }
    public string TranslateTargetLanguage { get => _translateTargetLanguage; set => SetSettingProperty(ref _translateTargetLanguage, value); }
    public string TranslateTone { get => _translateTone; set => SetSettingProperty(ref _translateTone, value); }
    public string TranslateInstructionsFilePath { get => _translateInstructionsFilePath; set => SetSettingProperty(ref _translateInstructionsFilePath, value); }
    public bool TranslateStream { get => _translateStream; set => SetSettingProperty(ref _translateStream, value); }
    public string SeconvExecutablePath { get => _seconvExecutablePath; set => SetSettingProperty(ref _seconvExecutablePath, value); }
    public string SeconvMultipleReplaceRulesFile { get => _seconvMultipleReplaceRulesFile; set => SetSettingProperty(ref _seconvMultipleReplaceRulesFile, value); }
    public string SeconvInputFolder { get => _seconvInputFolder; set => SetSettingProperty(ref _seconvInputFolder, value); }

    public string TestResult { get => _testResult; private set => SetProperty(ref _testResult, value); }
    public string SaveError { get => _saveError; private set => SetProperty(ref _saveError, value); }
    public string ReloadFeedback { get => _reloadFeedback; private set => SetProperty(ref _reloadFeedback, value); }
    public bool IsReloadFeedbackError { get => _isReloadFeedbackError; private set => SetProperty(ref _isReloadFeedbackError, value); }
    public bool IsTesting
    {
        get => _isTesting;
        private set
        {
            if (!SetProperty(ref _isTesting, value)) return;
            RaisePropertyChanged(nameof(TestButtonText));
            TestConnectionCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsConnectionVerified
    {
        get => _isConnectionVerified;
        private set
        {
            if (!SetProperty(ref _isConnectionVerified, value)) return;
            RaisePropertyChanged(nameof(TestButtonText));
            TestConnectionCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (!SetProperty(ref _isDirty, value)) return;
            RaisePropertyChanged(nameof(SaveButtonText));
            SaveCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsReloadConfirmationVisible
    {
        get => _isReloadConfirmationVisible;
        private set => SetProperty(ref _isReloadConfirmationVisible, value);
    }

    public string SaveButtonText => IsDirty || !_hasSavedCurrentSnapshot ? "保存" : "已保存";
    public string TestButtonText => IsTesting ? "正在连接..." : IsConnectionVerified ? "已连接" : "测试连接";

    public event Action<AppSettings>? SettingsSaved;

    public AsyncRelayCommand TestConnectionCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand ReloadCommand { get; }
    public RelayCommand ConfirmReloadCommand { get; }
    public RelayCommand CancelReloadCommand { get; }

    public SettingsViewModel()
    {
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, () => !IsTesting && !IsConnectionVerified);
        SaveCommand = new RelayCommand(Save, () => IsDirty);
        ReloadCommand = new RelayCommand(Reload);
        ConfirmReloadCommand = new RelayCommand(ConfirmReload);
        CancelReloadCommand = new RelayCommand(() => IsReloadConfirmationVisible = false);
    }

    public void LoadFrom(AppSettings settings)
    {
        _isLoading = true;
        try
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
        finally
        {
            _isLoading = false;
        }

        SetSavedSnapshot(wasSaved: false);
        ResetConnectionTest();
    }

    public AppSettings BuildAppSettings() => new()
    {
        Jellyfin = new JellyfinSettings { BaseUrl = BaseUrl.Trim(), ApiKey = ApiKey.Trim() },
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

    private void SetSettingProperty<T>(ref T field, T value, bool resetsConnectionTest = false, [CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref field, value, propertyName) || _isLoading) return;

        UpdateDirtyState();
        if (resetsConnectionTest) ResetConnectionTest();
    }

    private void UpdateDirtyState()
    {
        if (_savedSettingsSnapshot.Length == 0) return;
        IsDirty = JsonSerializer.Serialize(BuildAppSettings()) != _savedSettingsSnapshot;
    }

    private void SetSavedSnapshot(bool wasSaved)
    {
        _savedSettingsSnapshot = JsonSerializer.Serialize(BuildAppSettings());
        _hasSavedCurrentSnapshot = wasSaved;
        IsDirty = false;
        RaisePropertyChanged(nameof(SaveButtonText));
    }

    private void ResetConnectionTest()
    {
        _connectionInputVersion++;
        IsConnectionVerified = false;
        TestResult = "";
    }

    private async Task TestConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl) || string.IsNullOrWhiteSpace(ApiKey))
        {
            TestResult = "请先填写 BaseUrl 和 ApiKey。";
            return;
        }

        var connectionInputVersion = _connectionInputVersion;
        var baseUrl = BaseUrl.Trim();
        var apiKey = ApiKey.Trim();
        IsTesting = true;
        TestResult = "";
        try
        {
            using var client = new JellyfinClient(new JellyfinSettings { BaseUrl = baseUrl, ApiKey = apiKey });
            await client.GetLibrariesAsync();
            if (connectionInputVersion == _connectionInputVersion) IsConnectionVerified = true;
        }
        catch (Exception ex)
        {
            if (connectionInputVersion == _connectionInputVersion)
                TestResult = $"连接失败：{ex.Message}";
        }
        finally
        {
            IsTesting = false;
        }
    }

    private void Save()
    {
        var settings = BuildAppSettings();
        try
        {
            SettingsStore.SaveAppSettings(settings);
        }
        catch (Exception ex)
        {
            SaveError = $"保存失败：{ex.Message}";
            return;
        }

        SaveError = "";
        SetSavedSnapshot(wasSaved: true);
        SettingsSaved?.Invoke(settings);
    }

    private void Reload()
    {
        if (IsDirty)
        {
            IsReloadConfirmationVisible = true;
            return;
        }

        ReloadFromDisk();
    }

    private void ConfirmReload()
    {
        IsReloadConfirmationVisible = false;
        ReloadFromDisk();
    }

    private void ReloadFromDisk()
    {
        try
        {
            LoadFrom(SettingsStore.LoadAppSettings());
            ShowReloadFeedback("已从 appsettings.json 重新加载。", isError: false);
        }
        catch (Exception ex)
        {
            ShowReloadFeedback($"重新加载失败：{ex.Message}", isError: true);
        }
    }

    private void ShowReloadFeedback(string message, bool isError)
    {
        _reloadFeedbackVersion++;
        _reloadFeedbackTimer?.Stop();
        ReloadFeedback = message;
        IsReloadFeedbackError = isError;
        if (isError) return;

        var feedbackVersion = _reloadFeedbackVersion;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _reloadFeedbackTimer = timer;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (feedbackVersion != _reloadFeedbackVersion) return;
            ReloadFeedback = "";
            IsReloadFeedbackError = false;
        };
        timer.Start();
    }
}
