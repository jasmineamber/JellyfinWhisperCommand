namespace JellyfinWhisperCommand;

public sealed class AppSettings
{
    public JellyfinSettings Jellyfin { get; init; } = new();
    public WhisperJavSettings WhisperJav { get; init; } = new();
    public WhisperJavTranslateSettings WhisperJavTranslate { get; init; } = new();
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
    public string Args { get; init; } = "--ensemble --pass1-pipeline fidelity --pass1-sensitivity balanced --pass1-params {\"model_name\":\"large-v2\",\"device\":\"cuda\",\"temperature\":[0],\"compression_ratio_threshold\":2.4,\"logprob_threshold\":-1,\"logprob_margin\":0,\"no_speech_threshold\":0.71,\"beam_size\":2,\"best_of\":2,\"patience\":1.2,\"suppress_blank\":true,\"without_timestamps\":false,\"condition_on_previous_text\":false,\"word_timestamps\":true,\"repetition_penalty\":1.3,\"no_repeat_ngram_size\":3,\"chunk_length\":30,\"max_initial_timestamp\":0,\"threshold\":0.3,\"min_speech_duration_ms\":150,\"min_silence_duration_ms\":150,\"max_speech_duration_s\":5,\"speech_pad_ms\":400,\"chunk_threshold_s\":1,\"max_group_duration_s\":10,\"force_cpu\":\"false\",\"scene_detection_method\":\"auditok\",\"min_duration\":20,\"max_duration\":420,\"snap_window\":5,\"clustering_threshold\":18,\"visualize\":false} --pass1-scene-detector auditok --pass1-speech-segmenter ten --pass1-model large-v2 --pass2-pipeline balanced --pass2-sensitivity aggressive --pass2-params {\"model_name\": \"large-v2\", \"device\": \"cuda\", \"temperature\": [0, 0.2], \"compression_ratio_threshold\": 2.6, \"logprob_threshold\": -1, \"logprob_margin\": 0, \"no_speech_threshold\": 0.72, \"beam_size\": 3, \"best_of\": 2, \"patience\": 1.3, \"suppress_blank\": true, \"without_timestamps\": false, \"condition_on_previous_text\": false, \"word_timestamps\": true, \"repetition_penalty\": 1.3, \"no_repeat_ngram_size\": 3, \"chunk_length\": 30, \"max_initial_timestamp\": 0, \"threshold\": 0.1, \"min_speech_duration_ms\": 100, \"min_silence_duration_ms\": 100, \"max_speech_duration_s\": 4, \"speech_pad_ms\": 150, \"chunk_threshold_s\": 2, \"max_group_duration_s\": 5, \"force_cpu\": \"false\", \"scene_detection_method\": \"silero\", \"max_duration_s\": 120, \"min_duration_s\": 0.2, \"pass1_max_duration_s\": 2700, \"pass1_max_silence_s\": 2.5, \"pass1_energy_threshold\": 32, \"pass2_max_duration_s\": 1800, \"pass2_max_silence_s\": 1.8, \"pass2_energy_threshold\": 38, \"brute_force_fallback\": true, \"brute_force_chunk_s\": 29} --pass2-scene-detector silero --pass2-speech-segmenter silero-v6.2 --pass2-model large-v2 --merge-strategy pass1_primary --subs-language native --language japanese";
}

public sealed class WhisperJavTranslateSettings
{
    public string ExecutablePath { get; init; } = "whisperjav-translate.exe";
    public string Provider { get; init; } = "custom";
    public string Endpoint { get; init; } = "";
    public string ApiKey { get; init; } = "";
    public string Model { get; init; } = "deepseek-v4-flash";
    public string SourceLanguage { get; init; } = "japanese";
    public string TargetLanguage { get; init; } = "chinese";
    public string Tone { get; init; } = "standard";
    public string InstructionsFilePath { get; init; } = "";
    public bool Stream { get; init; } = true;
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

public sealed record TranslationRetryTask(
    string ItemId,
    string MediaName,
    string MediaPath,
    DateTime LastFailedAt,
    string LastFailure);

public sealed record Option<T>(string Name, T Value);

public sealed record MediaLibrary(string Id, string Name);

public sealed class MediaItem : ObservableObject
{
    private bool _isSelected;
    private TaskPhase? _taskPhase;
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string ImageUrl { get; init; }
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    public TaskPhase? TaskPhase
    {
        get => _taskPhase;
        set
        {
            if (!SetProperty(ref _taskPhase, value)) return;
            RaisePropertyChanged(nameof(HasTaskState));
            RaisePropertyChanged(nameof(TaskStateText));
        }
    }

    public bool HasTaskState => TaskPhase is not null;
    public string TaskStateText => TaskPhase switch
    {
        JellyfinWhisperCommand.TaskPhase.Queued => "已加入任务",
        JellyfinWhisperCommand.TaskPhase.Transcribing => "转录中",
        JellyfinWhisperCommand.TaskPhase.Translating => "翻译中",
        JellyfinWhisperCommand.TaskPhase.PostProcessing => "后处理中",
        JellyfinWhisperCommand.TaskPhase.Completed => "已完成",
        JellyfinWhisperCommand.TaskPhase.Failed => "处理失败",
        JellyfinWhisperCommand.TaskPhase.Stopped => "已停止",
        _ => "待处理"
    };
}

public sealed class JellyfinItemsResponse
{
    public List<JellyfinItem> Items { get; init; } = [];
    public int TotalRecordCount { get; init; }
}

public sealed class JellyfinSearchHintsResponse
{
    public List<JellyfinSearchHint> SearchHints { get; init; } = [];
}

public sealed class JellyfinSearchHint
{
    public string ItemId { get; init; } = "";
}

public sealed class JellyfinItem
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Path { get; init; }
    public int PartCount { get; init; }
    public Dictionary<string, string>? ImageTags { get; init; }
}
