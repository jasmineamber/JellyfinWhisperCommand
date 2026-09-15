namespace JellyfinWhisperCommand;

public enum TaskPhase
{
    Queued,
    Transcribing,
    Translating,
    PostProcessing,
    Completed,
    Failed,
    Stopped
}

public enum TaskFilter
{
    All,
    Running,
    Queued,
    Completed,
    Failed
}

public sealed class TaskEntryViewModel : ObservableObject
{
    private TaskPhase _phase;
    private string _detail = "";
    private double _progress;
    private string _errorMessage = "";
    private int _queuePosition;
    private TaskPhase _retryFromPhase = TaskPhase.Transcribing;

    public required string ItemId { get; init; }
    public required string MediaName { get; init; }
    public required string FilePath { get; init; }
    public string TaskId { get; } = Guid.NewGuid().ToString("N");

    public TaskPhase Phase
    {
        get => _phase;
        set
        {
            if (!SetProperty(ref _phase, value)) return;
            if (value is TaskPhase.Transcribing or TaskPhase.Translating or TaskPhase.PostProcessing)
                StartedAt ??= DateTime.Now;
            if (value is TaskPhase.Completed or TaskPhase.Failed or TaskPhase.Stopped)
                EndedAt = DateTime.Now;
            RaisePropertyChanged(nameof(PhaseText));
            RaisePropertyChanged(nameof(QueuePositionText));
            RaisePropertyChanged(nameof(IsActive));
            RaisePropertyChanged(nameof(TimeText));
            RaisePropertyChanged(nameof(CanRetry));
        }
    }

    public string Detail
    {
        get => _detail;
        set
        {
            if (SetProperty(ref _detail, value))
                RaisePropertyChanged(nameof(HasDetail));
        }
    }

    public double Progress
    {
        get => _progress;
        set
        {
            var normalized = Math.Clamp(value, 0, 1);
            if (!SetProperty(ref _progress, normalized)) return;
            RaisePropertyChanged(nameof(ProgressText));
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (SetProperty(ref _errorMessage, value))
                RaisePropertyChanged(nameof(HasError));
        }
    }

    public bool HasDetail => !string.IsNullOrEmpty(_detail);
    public bool HasError => !string.IsNullOrWhiteSpace(_errorMessage);
    public bool CanRetry => Phase is TaskPhase.Failed or TaskPhase.Stopped;
    public TaskPhase RetryFromPhase => _retryFromPhase;
    public bool CanCancel => Phase == TaskPhase.Queued;
    public bool CanStop => Phase is TaskPhase.Transcribing or TaskPhase.Translating or TaskPhase.PostProcessing;
    public bool IsProcessing => Phase is TaskPhase.Transcribing or TaskPhase.Translating or TaskPhase.PostProcessing;
    public int RetryCount { get; private set; }
    public bool HasRetried => RetryCount > 0;
    public string RetryCountText => RetryCount > 0 ? $"已尝试 {RetryCount} 次" : "";

    /// <summary>
    /// Whether the backend supplies a genuine percentage. Whisper/Translation do not,
    /// so progress is shown as an indeterminate indicator instead of a fake percentage.
    /// </summary>
    public bool HasRealProgress { get; set; }

    public bool IsIndeterminate => IsProcessing && !HasRealProgress;
    public int QueuePosition
    {
        get => _queuePosition;
        set
        {
            if (SetProperty(ref _queuePosition, value))
                RaisePropertyChanged(nameof(QueuePositionText));
        }
    }

    public string QueuePositionText =>
        Phase == TaskPhase.Queued && QueuePosition > 0 ? $"队列位置 #{QueuePosition}" : "";
    public string CurrentStep => Detail;
    public string ProgressText => HasRealProgress && Progress > 0 ? $"{Progress:P0}" : "";
    public DateTime CreatedAt { get; } = DateTime.Now;
    public DateTime? StartedAt { get; private set; }
    public DateTime? EndedAt { get; private set; }
    public bool IsActive => Phase is TaskPhase.Queued or TaskPhase.Transcribing or TaskPhase.Translating or TaskPhase.PostProcessing;
    public string DurationText => TimeText;

    public string DisplayName => System.IO.Path.GetFileName(FilePath);

    public string PhaseText => Phase switch
    {
        TaskPhase.Queued => "等待中",
        TaskPhase.Transcribing => "转录中",
        TaskPhase.Translating => "翻译中",
        TaskPhase.PostProcessing => "后处理中",
        TaskPhase.Completed => "已完成",
        TaskPhase.Failed => "失败",
        TaskPhase.Stopped => "已停止",
        _ => ""
    };

    /// <summary>
    /// Reuses this task row for a retry instead of creating a second row for the same media path.
    /// </summary>
    public void ResetForRetry()
    {
        RetryCount++;
        StartedAt = null;
        EndedAt = null;
        Progress = 0;
        ErrorMessage = "";
        Detail = "重新加入队列";
        QueuePosition = 0;
        RaisePropertyChanged(nameof(TimeText));
        RaisePropertyChanged(nameof(DurationText));
    }

    /// <summary>
    /// Records the first processing stage that should be rerun after a failure.
    /// A translation failure can therefore retry translation directly instead of
    /// repeating the expensive transcription stage.
    /// </summary>
    public void SetRetryFromPhase(TaskPhase phase)
    {
        _retryFromPhase = phase switch
        {
            TaskPhase.Translating => TaskPhase.Translating,
            TaskPhase.PostProcessing => TaskPhase.PostProcessing,
            _ => TaskPhase.Transcribing
        };
    }

    public string TimeText
    {
        get
        {
            if (EndedAt is { } end)
            {
                var elapsed = end - (StartedAt ?? CreatedAt);
                var minutes = (int)elapsed.TotalMinutes;
                var seconds = elapsed.Seconds;
                return minutes > 0 ? $"{minutes} 分 {seconds} 秒" : $"{seconds} 秒";
            }
            return IsActive ? $"开始于 {StartedAt?.ToString("HH:mm:ss") ?? "—"}" : $"创建于 {CreatedAt:HH:mm:ss}";
        }
    }
}
