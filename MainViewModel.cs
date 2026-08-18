namespace JellyfinWhisperCommand;

public enum MediaStatusFilter
{
    All,
    Pending,
    Queued,
    Processing,
    Completed,
    Failed
}

public sealed class MainViewModel : ObservableObject
{
    private const int PageSize = 100;
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    private AppSettings _settings;
    private readonly UserSettings _userSettings;
    private JellyfinClient? _client;
    private readonly string _failedWhisperJavLogFilePath = Path.Combine(AppContext.BaseDirectory, "failed-whisperjav-tasks.log");
    private readonly List<TranslationRetryTask> _failedTranslationTasks;
    private readonly object _logLock = new();
    private readonly object _executionLock = new();
    private readonly object _taskQueueLock = new();
    private readonly object _retryQueueLock = new();
    private readonly HashSet<string> _selectedIds = [];
    private readonly Dictionary<string, string> _mediaNameById = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _stopRequestLock = new();
    private readonly HashSet<string> _stopRequestedForPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<MediaPathTask> _taskQueue = new();
    private readonly HashSet<string> _queuedMediaIds = new(StringComparer.OrdinalIgnoreCase);
    private Process? _activeProcess;
    private ProcessJob? _activeJob;
    private string? _selectedLibraryId;
    private string _selectedSort = "DateCreated";
    private string _searchTerm = "";
    private bool _hasSubtitles;
    private string _statusMessage = "正在加载媒体库...";
    private bool _isStatusVisible = true;
    private int _pageIndex;
    private int _totalCount;
    private bool _isExecuting;
    private bool _isStopping;
    private bool _shutdownWhenComplete;
    private bool _shutdownCancelledForCurrentBatch;
    private bool _currentBatchSupportsAutomaticShutdown = true;
    private string _shutdownRequestFailure = "";
    private bool _isConnected;
    private bool _isConnecting;
    private bool _isSearching;
    private bool _isMediaStatusError;
    private bool _isLogDrawerOpen;
    private PageKind _currentPage = PageKind.Media;
    private TaskFilter _selectedTaskFilter;

    public ObservableCollection<MediaLibrary> Libraries { get; } = [];
    public ObservableCollection<MediaItem> MediaItems { get; } = [];
    public TaskBatchViewModel CurrentBatch { get; } = new();
    public ObservableCollection<TaskEntryViewModel> TaskEntries => CurrentBatch.Tasks;
    public ObservableCollection<TaskEntryViewModel> FilteredTaskEntries { get; } = [];
    private readonly Dictionary<string, TaskEntryViewModel> _taskEntryByPath = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<Option<string>> SortOptions { get; } =
    [new("加入日期", "DateCreated"), new("发行日期", "PremiereDate")];
    public IReadOnlyList<Option<bool>> SubtitleOptions { get; } = [new("否", false), new("是", true)];
    public IReadOnlyList<Option<MediaStatusFilter>> MediaStatusFilterOptions { get; } =
    [
        new("全部", MediaStatusFilter.All),
        new("待处理", MediaStatusFilter.Pending),
        new("排队中", MediaStatusFilter.Queued),
        new("处理中", MediaStatusFilter.Processing),
        new("已完成", MediaStatusFilter.Completed),
        new("失败", MediaStatusFilter.Failed)
    ];
    public IReadOnlyList<Option<TaskFilter>> TaskFilterOptions { get; } =
    [
        new("全部", TaskFilter.All),
        new("处理中", TaskFilter.Running),
        new("等待", TaskFilter.Queued),
        new("成功", TaskFilter.Completed),
        new("失败", TaskFilter.Failed)
    ];
    public ObservableCollection<MediaItem> FilteredMediaItems { get; } = [];
    private MediaStatusFilter _selectedMediaStatusFilter = MediaStatusFilter.All;
    public MediaStatusFilter SelectedMediaStatusFilter
    {
        get => _selectedMediaStatusFilter;
        set { if (SetProperty(ref _selectedMediaStatusFilter, value)) ApplyMediaFilter(); }
    }
    public bool IsMediaEmpty => FilteredMediaItems.Count == 0;
    public bool ShowMediaEmptyState => !IsStatusVisible && IsMediaEmpty;

    public string? SelectedLibraryId
    {
        get => _selectedLibraryId;
        set
        {
            if (!SetProperty(ref _selectedLibraryId, value)) return;
            _userSettings.LastLibraryId = value;
            SettingsStore.SaveUserSettings(_userSettings);
            SearchCommand.RaiseCanExecuteChanged();
        }
    }
    public string SelectedSort { get => _selectedSort; set => SetProperty(ref _selectedSort, value); }
    public string SearchTerm { get => _searchTerm; set => SetProperty(ref _searchTerm, value); }
    public bool HasSubtitles { get => _hasSubtitles; set => SetProperty(ref _hasSubtitles, value); }
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public bool IsStatusVisible
    {
        get => _isStatusVisible;
        private set
        {
            if (!SetProperty(ref _isStatusVisible, value)) return;
            RaiseMediaStatusChanged();
        }
    }
    public bool IsSearching
    {
        get => _isSearching;
        private set
        {
            if (!SetProperty(ref _isSearching, value)) return;
            RaiseMediaStatusChanged();
        }
    }
    public bool IsMediaStatusError
    {
        get => _isMediaStatusError;
        private set
        {
            if (!SetProperty(ref _isMediaStatusError, value)) return;
            RaiseMediaStatusChanged();
        }
    }
    public bool IsMediaLoading => IsStatusVisible && (IsConnecting || IsSearching);
    public bool ShowMediaPrompt => IsStatusVisible && !(IsConnecting || IsSearching) && !IsMediaStatusError;
    public bool ShowMediaError => IsStatusVisible && !(IsConnecting || IsSearching) && IsMediaStatusError;
    private void RaiseMediaStatusChanged()
    {
        RaisePropertyChanged(nameof(IsMediaLoading));
        RaisePropertyChanged(nameof(ShowMediaPrompt));
        RaisePropertyChanged(nameof(ShowMediaError));
        RaisePropertyChanged(nameof(ShowMediaEmptyState));
    }
    public bool IsExecuting { get => _isExecuting; private set => SetProperty(ref _isExecuting, value); }
    public bool IsStopping { get => _isStopping; private set => SetProperty(ref _isStopping, value); }
    public bool ShutdownWhenComplete
    {
        get => _shutdownWhenComplete;
        set
        {
            if (!SetProperty(ref _shutdownWhenComplete, value)) return;
            RaisePropertyChanged(nameof(ShowShutdownWhenCompleteStatus));
            RaisePropertyChanged(nameof(BatchStateDetailText));
        }
    }
    public PageKind CurrentPage
    {
        get => _currentPage;
        set
        {
            if (!SetProperty(ref _currentPage, value)) return;
            RaisePropertyChanged(nameof(IsMediaPage));
            RaisePropertyChanged(nameof(IsTasksPage));
            RaisePropertyChanged(nameof(IsSettingsPage));
        }
    }
    public bool IsMediaPage => CurrentPage == PageKind.Media;
    public bool IsTasksPage => CurrentPage == PageKind.Tasks;
    public bool IsSettingsPage => CurrentPage == PageKind.Settings;
    public bool IsLogDrawerOpen { get => _isLogDrawerOpen; set => SetProperty(ref _isLogDrawerOpen, value); }
    public TaskFilter SelectedTaskFilter
    {
        get => _selectedTaskFilter;
        set
        {
            if (!SetProperty(ref _selectedTaskFilter, value)) return;
            RefreshTaskFilter();
        }
    }
    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (SetProperty(ref _isConnected, value)) RaisePropertyChanged(nameof(ConnectionText));
        }
    }
    public bool IsConnecting
    {
        get => _isConnecting;
        private set
        {
            if (!SetProperty(ref _isConnecting, value)) return;
            RaisePropertyChanged(nameof(ConnectionText));
            RaiseMediaStatusChanged();
        }
    }
    public SettingsViewModel Settings { get; }
    public string ConnectionText => IsConnecting ? "正在连接..." : IsConnected ? "已连接" : "未连接";
    public bool CanGoPrevious => _pageIndex > 0;
    public bool CanGoNext => (_pageIndex + 1) * PageSize < _totalCount;
    public string RetryFailedTranslationButtonText
    {
        get
        {
            lock (_retryQueueLock) return $"重试失败翻译 ({_failedTranslationTasks.Count})";
        }
    }
    public bool HasFailedTranslationTasks
    {
        get
        {
            lock (_retryQueueLock) return _failedTranslationTasks.Count > 0;
        }
    }
    public string PageText => _totalCount == 0 ? "第 0 / 0 页" : $"第 {_pageIndex + 1} / {Math.Ceiling(_totalCount / (double)PageSize)} 页";
    public string MediaCountText => _totalCount == 0 ? "暂无媒体" : $"{_totalCount} 个媒体";
    public string SelectionSummary
    {
        get
        {
            lock (_taskQueueLock)
                return _selectedIds.Count == 0
                    ? $"已选择 0 项"
                    : $"已选择 {_selectedIds.Count} 项";
        }
    }
    public string ExecuteButtonText
    {
        get
        {
            lock (_taskQueueLock)
                return _selectedIds.Count == 0 ? "加入任务" : $"加入任务 {_selectedIds.Count} 项";
        }
    }

    public AsyncRelayCommand SearchCommand { get; }
    public AsyncRelayCommand GenerateCommand { get; }
    public AsyncRelayCommand RetryFailedTranslationCommand { get; }
    public AsyncRelayCommand StopCommand { get; }
    public AsyncRelayCommand<TaskEntryViewModel> StopTaskCommand { get; }
    public AsyncRelayCommand PreviousPageCommand { get; }
    public AsyncRelayCommand NextPageCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand ClearSelectionCommand { get; }
    public RelayCommand ClearCompletedTasksCommand { get; }
    public RelayCommand<PageKind> NavigateCommand { get; }
    public RelayCommand ToggleLogDrawerCommand { get; }
    public RelayCommand<TaskFilter> SelectTaskFilterCommand { get; }
    public AsyncRelayCommand<TaskEntryViewModel> RetryTaskCommand { get; }
    public RelayCommand<TaskEntryViewModel> ViewTaskLogCommand { get; }
    public RelayCommand<TaskEntryViewModel> CancelTaskCommand { get; }

    public MainViewModel()
    {
        _userSettings = SettingsStore.LoadUserSettings();
        try
        {
            _failedTranslationTasks = SettingsStore.LoadTranslationRetryTasks();
        }
        catch
        {
            _failedTranslationTasks = [];
        }
        try
        {
            _settings = SettingsStore.LoadAppSettings();
            _client = new JellyfinClient(_settings.Jellyfin);
        }
        catch (Exception ex)
        {
            _settings = new AppSettings();
            StatusMessage = ex.Message;
        }

        CurrentBatch.PropertyChanged += (_, _) => RefreshTaskSummary();

        Settings = new SettingsViewModel();
        Settings.LoadFrom(_settings);
        Settings.SettingsSaved += OnSettingsSaved;

        SearchCommand = new AsyncRelayCommand(SearchAsync, () => _client is not null && !string.IsNullOrWhiteSpace(SelectedLibraryId));
        GenerateCommand = new AsyncRelayCommand(ExecuteAsync, () => _client is not null && _selectedIds.Count > 0);
        RetryFailedTranslationCommand = new AsyncRelayCommand(RetryFailedTranslationsAsync, () => _failedTranslationTasks.Count > 0 && !IsExecuting);
        StopCommand = new AsyncRelayCommand(StopAsync, () => IsExecuting && !IsStopping);
        StopTaskCommand = new AsyncRelayCommand<TaskEntryViewModel>(StopTaskAsync, entry => entry?.CanStop == true);
        PreviousPageCommand = new AsyncRelayCommand(async () => { _pageIndex--; await LoadPageAsync(); }, () => CanGoPrevious);
        NextPageCommand = new AsyncRelayCommand(async () => { _pageIndex++; await LoadPageAsync(); }, () => CanGoNext);
        SelectAllCommand = new RelayCommand(SelectAll, () => MediaItems.Count > 0);
        ClearSelectionCommand = new RelayCommand(ClearSelection, () => _selectedIds.Count > 0);
        ClearCompletedTasksCommand = new RelayCommand(ClearCompletedTasks, () => TaskEntries.Any(entry => !entry.IsActive));
        NavigateCommand = new RelayCommand<PageKind>(page => CurrentPage = page);
        ToggleLogDrawerCommand = new RelayCommand(() => IsLogDrawerOpen = !IsLogDrawerOpen);
        SelectTaskFilterCommand = new RelayCommand<TaskFilter>(filter => SelectedTaskFilter = filter);
        RetryTaskCommand = new AsyncRelayCommand<TaskEntryViewModel>(RetryTaskAsync, entry => entry?.CanRetry == true);
        ViewTaskLogCommand = new RelayCommand<TaskEntryViewModel>(_ => IsLogDrawerOpen = true);
        CancelTaskCommand = new RelayCommand<TaskEntryViewModel>(CancelTask, entry => entry?.CanCancel == true);
        _ = LoadLibrariesAsync();
    }

    private void OnSettingsSaved(AppSettings newSettings)
    {
        try
        {
            _client?.Dispose();
            _settings = newSettings;
            _client = new JellyfinClient(newSettings.Jellyfin);
            StatusMessage = "设置已保存，正在重新连接...";
        }
        catch (Exception ex)
        {
            _client = null;
            StatusMessage = $"设置已保存，但连接失败：{ex.Message}";
        }
        IsConnected = false;
        IsConnecting = false;
        AppendLog("设置已保存，正在重新连接 Jellyfin 服务器...");
        Libraries.Clear();
        MediaItems.Clear();
        _selectedIds.Clear();
        RaisePropertyChanged(nameof(SelectionSummary));
        RaisePropertyChanged(nameof(ExecuteButtonText));
        GenerateCommand.RaiseCanExecuteChanged();
        SelectAllCommand.RaiseCanExecuteChanged();
        ClearSelectionCommand.RaiseCanExecuteChanged();
        _ = LoadLibrariesAsync();
    }

    private async Task LoadLibrariesAsync()
    {
        if (_client is null) return;
        IsMediaStatusError = false;
        IsConnecting = true;
        IsStatusVisible = true;
        StatusMessage = "正在连接 Jellyfin 服务器...";
        AppendLog("正在连接 Jellyfin 服务器...");
        try
        {
            var libraries = await _client.GetLibrariesAsync();
            IsConnected = true;
            AppendLog($"Jellyfin 连接成功，加载到 {libraries.Count} 个媒体库。");
            foreach (var library in libraries) Libraries.Add(library);
            if (Libraries.Any(x => x.Id == _userSettings.LastLibraryId))
            {
                SelectedLibraryId = _userSettings.LastLibraryId;
                await SearchAsync();
                return;
            }
            IsStatusVisible = true;
            StatusMessage = Libraries.Count == 0 ? "未找到可访问的媒体库。" : "请选择媒体库后点击筛选。";
        }
        catch (Exception ex)
        {
            IsConnected = false;
            IsMediaStatusError = true;
            IsStatusVisible = true;
            StatusMessage = $"加载媒体库失败：{ex.Message}";
            AppendLog($"[错误] 加载媒体库失败：{ex.Message}", NLog.LogLevel.Error);
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private async Task SearchAsync()
    {
        _pageIndex = 0;
        await LoadPageAsync();
    }

    private async Task LoadPageAsync()
    {
        if (_client is null || string.IsNullOrWhiteSpace(SelectedLibraryId)) return;
        IsSearching = true;
        IsMediaStatusError = false;
        IsStatusVisible = true;
        StatusMessage = "正在查询媒体...";
        try
        {
            var response = await _client!.GetItemsAsync(SelectedLibraryId, SelectedSort, HasSubtitles, SearchTerm, _pageIndex * PageSize, PageSize);
            foreach (var oldItem in MediaItems) oldItem.PropertyChanged -= OnMediaItemPropertyChanged;
            MediaItems.Clear();
            foreach (var item in response.Items)
            {
                _mediaNameById[item.Id] = string.IsNullOrWhiteSpace(item.Name) ? item.Id : item.Name;
                var phase = CurrentBatch.Tasks
                    .Where(task => string.Equals(task.ItemId, item.Id, StringComparison.OrdinalIgnoreCase))
                    .Select(task => (TaskPhase?)task.Phase)
                    .LastOrDefault();
                var media = new MediaItem { Id = item.Id, Name = item.Name, ImageUrl = _client!.GetImageUrl(item), IsSelected = _selectedIds.Contains(item.Id), TaskPhase = phase };
                media.PropertyChanged += OnMediaItemPropertyChanged;
                MediaItems.Add(media);
            }
            _totalCount = response.TotalRecordCount;
            IsStatusVisible = false;
            StatusMessage = $"找到 {MediaItems.Count} 个媒体。";
            AppendLog($"媒体查询完成：{MediaItems.Count} 条（共 {_totalCount} 条）。");
            RefreshPaging();
            RefreshSelectionCommands();
            ApplyMediaFilter();
        }
        catch (Exception ex)
        {
            MediaItems.Clear();
            _totalCount = 0;
            IsMediaStatusError = true;
            IsStatusVisible = true;
            StatusMessage = $"查询失败：{ex.Message}";
            RefreshPaging();
            RefreshSelectionCommands();
        }
        finally
        {
            IsSearching = false;
        }
    }

    private void RefreshSelectionCommands()
    {
        SelectAllCommand.RaiseCanExecuteChanged();
        ClearSelectionCommand.RaiseCanExecuteChanged();
    }

    private void ApplyMediaFilter()
    {
        var items = _selectedMediaStatusFilter switch
        {
            MediaStatusFilter.Pending => MediaItems.Where(m => m.TaskPhase is null),
            MediaStatusFilter.Queued => MediaItems.Where(m => m.TaskPhase == TaskPhase.Queued),
            MediaStatusFilter.Processing => MediaItems.Where(m => m.TaskPhase is TaskPhase.Transcribing or TaskPhase.Translating or TaskPhase.PostProcessing),
            MediaStatusFilter.Completed => MediaItems.Where(m => m.TaskPhase == TaskPhase.Completed),
            MediaStatusFilter.Failed => MediaItems.Where(m => m.TaskPhase is TaskPhase.Failed or TaskPhase.Stopped),
            _ => (IEnumerable<MediaItem>)MediaItems
        };
        FilteredMediaItems.Clear();
        foreach (var item in items) FilteredMediaItems.Add(item);
        RaisePropertyChanged(nameof(IsMediaEmpty));
        RaisePropertyChanged(nameof(ShowMediaEmptyState));
    }

    private void OnMediaItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MediaItem item) return;
        if (e.PropertyName == nameof(MediaItem.IsSelected))
        {
            if (item.IsSelected) _selectedIds.Add(item.Id); else _selectedIds.Remove(item.Id);
            RaisePropertyChanged(nameof(SelectionSummary));
            RaisePropertyChanged(nameof(ExecuteButtonText));
            GenerateCommand.RaiseCanExecuteChanged();
            ClearSelectionCommand.RaiseCanExecuteChanged();
        }
        else if (e.PropertyName == nameof(MediaItem.TaskPhase))
        {
            ApplyMediaFilter();
        }
    }

    private void SelectAll()
    {
        foreach (var item in MediaItems)
            item.IsSelected = true;
    }

    private void ClearSelection()
    {
        foreach (var item in MediaItems)
            item.IsSelected = false;
    }

    public bool HasTasks => CurrentBatch.HasTasks;
    public string TaskSummaryText => CurrentBatch.SummaryText;
    public int TotalTaskCount => CurrentBatch.TotalCount;
    public int QueuedTaskCount => CurrentBatch.QueuedCount;
    public int RunningTaskCount => CurrentBatch.RunningCount;
    public int CompletedTaskCount => CurrentBatch.CompletedCount;
    public int FailedTaskCount => CurrentBatch.FailedCount;
    public int StoppedTaskCount => CurrentBatch.StoppedCount;
    public int ActiveTaskCount => QueuedTaskCount + RunningTaskCount;
    public bool HasActiveTasks => ActiveTaskCount > 0;
    public double TaskProgress => CurrentBatch.Progress;
    public string TaskProgressText => CurrentBatch.ProgressText;
    public bool HasFailedTasks => CurrentBatch.HasFailures;
    public TaskEntryViewModel? CurrentTask => CurrentBatch.CurrentTask;
    public string GlobalTaskStatusText => CurrentTask is { } task
        ? $"正在处理：{task.MediaName} - {task.PhaseText}"
        : FailedTaskCount > 0 ? $"{FailedTaskCount} 个任务处理失败"
        : CompletedTaskCount > 0 ? "最近任务已完成"
        : "就绪";
    public string ActiveTaskStatusText => ActiveTaskCount > 0 ? $"{ActiveTaskCount} 个任务处理中" : "";
    public string TaskEntryText => ActiveTaskCount > 0 ? $"处理中 {ActiveTaskCount}" : FailedTaskCount > 0 ? $"失败 {FailedTaskCount}" : "任务";
    public string HeaderTaskBadgeText => ActiveTaskCount > 0 ? ActiveTaskCount.ToString() : FailedTaskCount > 0 ? FailedTaskCount.ToString() : "";
    public bool HeaderTaskBadgeVisible => ActiveTaskCount > 0 || FailedTaskCount > 0;
    public bool HeaderTaskBadgeIsFailure => FailedTaskCount > 0 && ActiveTaskCount == 0;
    public string BatchProgressText => TotalTaskCount == 0 ? "0 / 0" : $"{CompletedTaskCount} / {TotalTaskCount}";
    public bool IsBatchSettled => HasTasks && !HasActiveTasks;
    public BatchState BatchState => IsBatchSettled
        ? FailedTaskCount > 0 && CompletedTaskCount == 0 && StoppedTaskCount == 0 ? BatchState.Failed
        : StoppedTaskCount > 0 && CompletedTaskCount == 0 && FailedTaskCount == 0 ? BatchState.Stopped
        : FailedTaskCount == 0 && StoppedTaskCount == 0 ? BatchState.Completed
        : BatchState.Partial
        : BatchState.None;
    public string BatchStateText => BatchState switch
    {
        BatchState.Completed => "✓ 全部完成",
        BatchState.Failed => "✕ 处理失败",
        BatchState.Stopped => "已停止",
        BatchState.Partial => "⚠ 部分完成",
        _ => ""
    };
    public string BatchStateDetailText => BatchState switch
    {
        BatchState.Completed when ShowShutdownWhenCompleteStatus && !string.IsNullOrWhiteSpace(_shutdownRequestFailure)
            => $"自动关机请求失败：{_shutdownRequestFailure}",
        BatchState.Completed when ShowShutdownWhenCompleteStatus => "所有任务已结束，正在请求自动关机",
        BatchState.Completed => $"{CompletedTaskCount} 个任务已成功处理",
        BatchState.Failed when ShowShutdownWhenCompleteStatus && !string.IsNullOrWhiteSpace(_shutdownRequestFailure)
            => $"{FailedTaskCount} 个任务处理失败 · 自动关机请求失败：{_shutdownRequestFailure}",
        BatchState.Failed => $"{FailedTaskCount} 个任务处理失败",
        BatchState.Stopped when ShowShutdownWhenCompleteStatus && _shutdownCancelledForCurrentBatch => "任务已停止，本次不会自动关机",
        BatchState.Stopped => $"{StoppedTaskCount} 个任务已停止",
        BatchState.Partial => $"已完成 {CompletedTaskCount} · 失败 {FailedTaskCount}"
                              + (StoppedTaskCount > 0 ? $" · 已停止 {StoppedTaskCount}" : "")
                              + (ShowShutdownWhenCompleteStatus && _shutdownCancelledForCurrentBatch ? " · 本次不会自动关机" : ""),
        _ => ""
    };
    public bool HasBatchCompletion => IsBatchSettled;
    public bool ShowShutdownWhenCompleteStatus => ShutdownWhenComplete && _currentBatchSupportsAutomaticShutdown;

    private TaskEntryViewModel AddTaskEntry(string itemId, string mediaName, string filePath)
    {
        if (_taskEntryByPath.TryGetValue(filePath, out var existing))
        {
            existing.ResetForRetry();
            existing.Phase = TaskPhase.Queued;
            UpdateMediaTaskPhase(itemId, TaskPhase.Queued);
            return existing;
        }

        var entry = new TaskEntryViewModel
        {
            ItemId = itemId,
            MediaName = mediaName,
            FilePath = filePath,
            Phase = TaskPhase.Queued
        };
        _taskEntryByPath[filePath] = entry;
        CurrentBatch.Add(entry);
        UpdateMediaTaskPhase(itemId, TaskPhase.Queued);
        return entry;
    }

    private void UpdateTaskPhase(string filePath, TaskPhase phase, string? detail = null)
    {
        if (!_taskEntryByPath.TryGetValue(filePath, out var entry)) return;
        entry.Phase = phase;
        if (detail is not null) entry.Detail = detail;
        if (phase == TaskPhase.Queued)
            entry.Progress = 0;
        else
            entry.QueuePosition = 0;
        if (phase == TaskPhase.Failed && detail is not null)
            entry.ErrorMessage = detail;
        UpdateMediaTaskPhase(entry.ItemId, phase);
        RefreshQueuePositions();
        RefreshTaskSummary();
    }

    private void MarkQueuedEntriesAsStopped()
    {
        foreach (var entry in _taskEntryByPath.Values.ToList())
        {
            if (entry.Phase != TaskPhase.Queued)
                continue;
            entry.Phase = TaskPhase.Stopped;
            UpdateMediaTaskPhase(entry.ItemId, TaskPhase.Stopped);
            _taskEntryByPath.Remove(entry.FilePath);
        }
        RefreshQueuePositions();
        RefreshTaskSummary();
    }

    private void ClearCompletedTasks()
    {
        foreach (var entry in CurrentBatch.Tasks.Where(entry => !entry.IsActive).ToList())
            _taskEntryByPath.Remove(entry.FilePath);
        CurrentBatch.ClearCompleted();
        RefreshTaskSummary();
    }

    private void CancelTask(TaskEntryViewModel? entry)
    {
        if (entry is null || !entry.CanCancel) return;
        lock (_taskQueueLock)
        {
            var remaining = new List<MediaPathTask>();
            while (_taskQueue.Count > 0)
            {
                var task = _taskQueue.Dequeue();
                if (!string.Equals(task.Path, entry.FilePath, StringComparison.OrdinalIgnoreCase))
                    remaining.Add(task);
            }
            _taskQueue.Clear();
            foreach (var task in remaining) _taskQueue.Enqueue(task);
            if (!_taskQueue.Any(task => string.Equals(task.ItemId, entry.ItemId, StringComparison.OrdinalIgnoreCase)))
                _queuedMediaIds.Remove(entry.ItemId);
        }
        _taskEntryByPath.Remove(entry.FilePath);
        CurrentBatch.Remove(entry);
        RefreshMediaPhase(entry.ItemId);
        RefreshQueuePositions();
        RefreshTaskSummary();
        RaisePropertyChanged(nameof(SelectionSummary));
    }

    private void RefreshMediaPhase(string itemId)
    {
        var phase = CurrentBatch.Tasks
            .Where(task => string.Equals(task.ItemId, itemId, StringComparison.OrdinalIgnoreCase))
            .Select(task => (TaskPhase?)task.Phase)
            .LastOrDefault();
        foreach (var media in MediaItems.Where(media => string.Equals(media.Id, itemId, StringComparison.OrdinalIgnoreCase)))
            media.TaskPhase = phase;
    }

    private void RefreshQueuePositions()
    {
        List<MediaPathTask> queuedTasks;
        lock (_taskQueueLock)
        {
            queuedTasks = _taskQueue.ToList();
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            RefreshQueuePositionsCore(queuedTasks);
        else
            dispatcher.InvokeAsync(() => RefreshQueuePositionsCore(queuedTasks));
    }

    private void RefreshQueuePositionsCore(List<MediaPathTask> queuedTasks)
    {
        foreach (var entry in CurrentBatch.Tasks)
            entry.QueuePosition = 0;

        var position = 1;
        foreach (var task in queuedTasks)
        {
            if (_taskEntryByPath.TryGetValue(task.Path, out var entry) &&
                entry.Phase == TaskPhase.Queued &&
                entry.QueuePosition == 0)
            {
                entry.QueuePosition = position;
            }
            position++;
        }
    }

    private void UpdateMediaTaskPhase(string itemId, TaskPhase phase)
    {
        foreach (var media in MediaItems.Where(media => string.Equals(media.Id, itemId, StringComparison.OrdinalIgnoreCase)))
            media.TaskPhase = phase;
    }

    private void RefreshTaskFilter()
    {
        var tasks = TaskEntries.Where(task => SelectedTaskFilter switch
        {
            TaskFilter.Running => task.Phase is TaskPhase.Transcribing or TaskPhase.Translating or TaskPhase.PostProcessing,
            TaskFilter.Queued => task.Phase == TaskPhase.Queued,
            TaskFilter.Completed => task.Phase == TaskPhase.Completed,
            TaskFilter.Failed => task.Phase is TaskPhase.Failed or TaskPhase.Stopped,
            _ => true
        }).ToList();
        FilteredTaskEntries.Clear();
        foreach (var task in tasks) FilteredTaskEntries.Add(task);
    }

    private async Task RetryTaskAsync(TaskEntryViewModel? entry)
    {
        if (entry is null || !entry.CanRetry) return;

        bool enqueue;
        lock (_taskQueueLock)
        {
            enqueue = !_taskQueue.Any(t => string.Equals(t.Path, entry.FilePath, StringComparison.OrdinalIgnoreCase));
            if (enqueue)
            {
                _queuedMediaIds.Add(entry.ItemId);
                _taskQueue.Enqueue(new MediaPathTask(entry.ItemId, entry.MediaName, entry.FilePath));
            }
        }
        if (!enqueue) return;

        entry.ResetForRetry();
        entry.Phase = TaskPhase.Queued;

        _shutdownCancelledForCurrentBatch = false;
        _currentBatchSupportsAutomaticShutdown = true;
        ClearShutdownRequestFailure();
        RaisePropertyChanged(nameof(ShowShutdownWhenCompleteStatus));

        _taskEntryByPath[entry.FilePath] = entry;

        RefreshQueuePositions();
        RefreshTaskSummary();
        CurrentPage = PageKind.Tasks;

        bool startWorker;
        lock (_executionLock)
        {
            // A retry cancels an in-progress stop: resume processing instead of discarding.
            IsStopping = false;
            startWorker = !IsExecuting;
            if (startWorker) IsExecuting = true;
        }

        if (startWorker)
        {
            AppendLog($"[QUEUE] Starting queue worker for retry. Pending path tasks: {GetQueueTaskCount()}.");
            _ = ProcessTaskQueueAsync();
        }
        else
        {
            AppendLog($"[QUEUE] Retry task added while queue worker is active. Pending path tasks: {GetQueueTaskCount()}.");
        }

        await Task.CompletedTask;
    }

    private void RefreshTaskSummary()
    {
        RaisePropertyChanged(nameof(HasTasks));
        RaisePropertyChanged(nameof(TaskSummaryText));
        RaisePropertyChanged(nameof(TotalTaskCount));
        RaisePropertyChanged(nameof(QueuedTaskCount));
        RaisePropertyChanged(nameof(RunningTaskCount));
        RaisePropertyChanged(nameof(CompletedTaskCount));
        RaisePropertyChanged(nameof(FailedTaskCount));
        RaisePropertyChanged(nameof(StoppedTaskCount));
        RaisePropertyChanged(nameof(ActiveTaskCount));
        RaisePropertyChanged(nameof(HasActiveTasks));
        RaisePropertyChanged(nameof(TaskProgress));
        RaisePropertyChanged(nameof(TaskProgressText));
        RaisePropertyChanged(nameof(HasFailedTasks));
        RaisePropertyChanged(nameof(CurrentTask));
        RaisePropertyChanged(nameof(GlobalTaskStatusText));
        RaisePropertyChanged(nameof(ActiveTaskStatusText));
        RaisePropertyChanged(nameof(TaskEntryText));
        RaisePropertyChanged(nameof(HeaderTaskBadgeText));
        RaisePropertyChanged(nameof(HeaderTaskBadgeVisible));
        RaisePropertyChanged(nameof(HeaderTaskBadgeIsFailure));
        RaisePropertyChanged(nameof(BatchProgressText));
        RaisePropertyChanged(nameof(IsBatchSettled));
        RaisePropertyChanged(nameof(BatchState));
        RaisePropertyChanged(nameof(BatchStateText));
        RaisePropertyChanged(nameof(BatchStateDetailText));
        RaisePropertyChanged(nameof(HasBatchCompletion));
        ClearCompletedTasksCommand.RaiseCanExecuteChanged();
        RetryTaskCommand.RaiseCanExecuteChanged();
        RefreshTaskFilter();
    }

    private async Task ExecuteAsync()
    {
        if (_client is null) return;

        var selectedIds = _selectedIds.ToList();
        if (selectedIds.Count == 0) return;

        var queuedCount = 0;
        _shutdownCancelledForCurrentBatch = false;
        _currentBatchSupportsAutomaticShutdown = true;
        ClearShutdownRequestFailure();
        RaisePropertyChanged(nameof(ShowShutdownWhenCompleteStatus));
        AppendLog($"Adding {selectedIds.Count} selected media item(s) to the task queue.");

        foreach (var itemId in selectedIds)
        {
            var mediaName = MediaItems.FirstOrDefault(item => item.Id == itemId)?.Name
                            ?? (_mediaNameById.TryGetValue(itemId, out var cachedName) ? cachedName : itemId);
            lock (_taskQueueLock)
            {
                if (!_queuedMediaIds.Add(itemId))
                {
                    AppendLog($"[QUEUE] Skip duplicate media: {mediaName} ({itemId})");
                    continue;
                }
            }
            RaisePropertyChanged(nameof(SelectionSummary));
            AppendLog($"[QUEUE] Adding media: {mediaName} ({itemId})");

            try
            {
                var itemPaths = await _client.GetPathsAsync(itemId);
                if (itemPaths.Count == 0)
                {
                    lock (_taskQueueLock)
                        _queuedMediaIds.Remove(itemId);
                    AppendLog($"[QUEUE][ERROR] Media has no usable paths: {mediaName} ({itemId})", NLog.LogLevel.Error);
                    RaisePropertyChanged(nameof(SelectionSummary));
                    continue;
                }

                lock (_taskQueueLock)
                {
                    foreach (var path in itemPaths)
                    {
                        _taskQueue.Enqueue(new MediaPathTask(itemId, mediaName, path));
                        AddTaskEntry(itemId, mediaName, path);
                        AppendLog($"[QUEUE]   Added path: {path}");
                    }
                    queuedCount++;
                }
                AppendLog($"[QUEUE] Media added: {mediaName} ({itemPaths.Count} path task(s)); queue size: {GetQueueTaskCount()} path task(s), {GetQueuedMediaCount()} media item(s)");
            }
            catch (Exception ex)
            {
                lock (_taskQueueLock)
                    _queuedMediaIds.Remove(itemId);
                RaisePropertyChanged(nameof(SelectionSummary));
                AppendLog($"[QUEUE][ERROR] Failed to add media: {mediaName} ({itemId}). {ex.Message}", NLog.LogLevel.Error);
            }
        }

        _selectedIds.Clear();
        foreach (var item in MediaItems) item.IsSelected = false;
        RaisePropertyChanged(nameof(SelectionSummary));
        RaisePropertyChanged(nameof(ExecuteButtonText));
        GenerateCommand.RaiseCanExecuteChanged();

        RefreshQueuePositions();
        RefreshTaskSummary();

        AppendLog(queuedCount > 0
            ? $"Added {queuedCount} media item(s) to the task queue."
            : "No new media item was added to the task queue.");

        bool startWorker;
        lock (_executionLock)
        {
            // Adding work also cancels an in-progress stop so newly enqueued tasks are not orphaned.
            IsStopping = false;
            startWorker = !IsExecuting;
            if (startWorker)
            {
                IsExecuting = true;
            }
        }

        AppendLog($"[QUEUE] Enqueue operation completed: {queuedCount} new media item(s), {GetQueueTaskCount()} path task(s) waiting, {GetQueuedMediaCount()} media item(s) tracked.");
        if (startWorker)
        {
            AppendLog("[QUEUE] Starting queue worker.");
            _ = ProcessTaskQueueAsync();
        }
        else
        {
            AppendLog($"[QUEUE] Existing queue worker is active; new tasks will be processed after the current task(s). Remaining: {GetQueueTaskCount()} path task(s).");
        }
    }

    private async Task ProcessTaskQueueAsync()
    {
        lock (_executionLock)
            IsStopping = false;

        GenerateCommand.RaiseCanExecuteChanged();
        RetryFailedTranslationCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();

        var allSucceeded = true;
        var subtitleMoved = false;

        try
        {
            var firstTask = GetNextQueuedTask();
            if (firstTask is null) return;

            var validationStartInfo = CommandBuilder.BuildStartInfo([firstTask.Value.Path], _settings.WhisperJav);
            if (!File.Exists(validationStartInfo.FileName))
            {
                const string failure = "WhisperJav executable was not found.";
                foreach (var queuedTask in GetQueuedTasksSnapshot())
                {
                    AppendWhisperJavFailure(queuedTask, failure);
                    UpdateTaskPhase(queuedTask.Path, TaskPhase.Failed, "找不到 WhisperJav 可执行文件");
                }
                throw new FileNotFoundException(failure, validationStartInfo.FileName);
            }

            CurrentPage = PageKind.Tasks;
            AppendLog($"WhisperJav executable: {validationStartInfo.FileName}");

            while (true)
            {
                MediaPathTask currentTask;
                lock (_executionLock)
                lock (_taskQueueLock)
                {
                    if (IsStopping || _taskQueue.Count == 0)
                    {
                        if (_taskQueue.Count == 0)
                            AppendLog($"[QUEUE] Queue worker has no pending tasks. Tracked media: {_queuedMediaIds.Count}.");
                        break;
                    }
                    currentTask = _taskQueue.Dequeue();
                }

                AppendLog($"[QUEUE] Dequeued media: {currentTask.MediaName} ({currentTask.ItemId})");
                AppendLog($"[QUEUE] Starting path task: {currentTask.Path}; remaining: {GetQueueTaskCount()} path task(s), {GetQueuedMediaCount()} media item(s) tracked.");

                if (WasStopRequested(currentTask.Path))
                {
                    ClearStopRequest(currentTask.Path);
                    UpdateTaskPhase(currentTask.Path, TaskPhase.Stopped);
                    AppendLog($"[QUEUE][STOPPED] Task stopped before start: {currentTask.Path}");
                    ReleaseQueuedMediaIdIfFinished(currentTask.ItemId);
                    continue;
                }

                var taskSucceeded = false;
                UpdateTaskPhase(currentTask.Path, TaskPhase.Transcribing, "正在运行 WhisperJav 转录");
                if (!await ExecuteWhisperJavAsync(currentTask, 1, 1))
                {
                    if (WasStopRequested(currentTask.Path))
                    {
                        ClearStopRequest(currentTask.Path);
                        UpdateTaskPhase(currentTask.Path, TaskPhase.Stopped);
                        AppendLog($"[QUEUE][STOPPED] Task stopped during WhisperJav: {currentTask.Path}");
                    }
                    else
                    {
                        allSucceeded = false;
                        UpdateTaskPhase(currentTask.Path, TaskPhase.Failed, "转录失败");
                        AppendLog($"[QUEUE][FAILED] WhisperJav failed: {currentTask.Path}", NLog.LogLevel.Error);
                    }
                }
                else if (WasStopRequested(currentTask.Path))
                {
                    ClearStopRequest(currentTask.Path);
                    UpdateTaskPhase(currentTask.Path, TaskPhase.Stopped);
                    AppendLog($"[QUEUE][STOPPED] Task stopped after WhisperJav: {currentTask.Path}");
                }
                else if (IsStopping)
                {
                    UpdateTaskPhase(currentTask.Path, TaskPhase.Stopped);
                    AppendLog($"[QUEUE][STOPPING] Current task interrupted after WhisperJav: {currentTask.Path}");
                }
                else
                {
                    UpdateTaskPhase(currentTask.Path, TaskPhase.Translating, "正在翻译字幕");
                    if (!await ExecuteSubtitleTranslationAsync(currentTask, 1, 1))
                    {
                        if (WasStopRequested(currentTask.Path))
                        {
                            ClearStopRequest(currentTask.Path);
                            UpdateTaskPhase(currentTask.Path, TaskPhase.Stopped);
                            AppendLog($"[QUEUE][STOPPED] Task stopped during translation: {currentTask.Path}");
                        }
                        else
                        {
                            allSucceeded = false;
                            UpdateTaskPhase(currentTask.Path, TaskPhase.Failed, "翻译失败");
                            AppendLog($"[QUEUE][FAILED] Translation failed: {currentTask.Path}", NLog.LogLevel.Error);
                        }
                    }
                    else if (WasStopRequested(currentTask.Path))
                    {
                        ClearStopRequest(currentTask.Path);
                        UpdateTaskPhase(currentTask.Path, TaskPhase.Stopped);
                        AppendLog($"[QUEUE][STOPPED] Task stopped after translation: {currentTask.Path}");
                    }
                    else if (IsStopping)
                    {
                        UpdateTaskPhase(currentTask.Path, TaskPhase.Stopped);
                        AppendLog($"[QUEUE][STOPPING] Current task interrupted after translation: {currentTask.Path}");
                    }
                    else
                    {
                        UpdateTaskPhase(currentTask.Path, TaskPhase.PostProcessing, "正在执行 Seconv 后处理");
                        var seconvResult = await ExecuteSeconvCommandsAsync([currentTask.Path]);
                        allSucceeded &= seconvResult.AllSucceeded;
                        subtitleMoved |= seconvResult.SubtitleCopied;
                        taskSucceeded = seconvResult.AllSucceeded;
                        if (WasStopRequested(currentTask.Path))
                        {
                            ClearStopRequest(currentTask.Path);
                            UpdateTaskPhase(currentTask.Path, TaskPhase.Stopped);
                            AppendLog($"[QUEUE][STOPPED] Task stopped during post-processing: {currentTask.Path}");
                        }
                        else if (seconvResult.AllSucceeded)
                        {
                            UpdateTaskPhase(currentTask.Path, TaskPhase.Completed, "全部完成");
                            RemoveTranslationRetryTask(currentTask.Path);
                        }
                        else
                        {
                            UpdateTaskPhase(currentTask.Path, TaskPhase.Failed, "后处理失败");
                            AppendLog($"[QUEUE][FAILED] Seconv/post-processing failed: {currentTask.Path}", NLog.LogLevel.Error);
                        }
                    }
                }

                ReleaseQueuedMediaIdIfFinished(currentTask.ItemId);
                AppendLog(taskSucceeded
                    ? $"[QUEUE][COMPLETED] Path task completed: {currentTask.Path}; remaining: {GetQueueTaskCount()} path task(s), {GetQueuedMediaCount()} media item(s) tracked."
                    : $"[QUEUE] Path task finished with failure/interruption: {currentTask.Path}; remaining: {GetQueueTaskCount()} path task(s), {GetQueuedMediaCount()} media item(s) tracked.");
            }

            if (IsStopping)
            {
                AppendLog($"[QUEUE][STOPPED] Queue worker stopped; pending tasks were marked stopped at stop request.");
            }
            else
            {
                if (subtitleMoved && !string.IsNullOrWhiteSpace(SelectedLibraryId))
                {
                    try
                    {
                        await _client!.RefreshLibraryAsync(SelectedLibraryId);
                        AppendLog("Jellyfin library refresh requested after subtitle move.");
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"[ERROR] Failed to refresh Jellyfin library: {ex.Message}", NLog.LogLevel.Error);
                    }
                }

                AppendLog(allSucceeded ? "All queued path tasks completed." : "Queued path tasks completed with failures.");
                if (ShutdownWhenComplete)
                    await RequestSystemShutdownAsync();
            }
        }
        catch (Exception ex)
        {
            ClearPendingTaskQueue();
            CurrentPage = PageKind.Tasks;
            AppendLog($"[ERROR] {ex.Message}", NLog.LogLevel.Error);
        }
        finally
        {
            bool restartWorker;
            lock (_executionLock)
            {
                _activeProcess = null;
                _activeJob = null;
                restartWorker = !IsStopping && GetQueueTaskCount() > 0;
                if (!restartWorker)
                {
                    IsExecuting = false;
                    IsStopping = false;
                }
            }

            RaisePropertyChanged(nameof(SelectionSummary));
            GenerateCommand.RaiseCanExecuteChanged();
            RetryFailedTranslationCommand.RaiseCanExecuteChanged();
            StopCommand.RaiseCanExecuteChanged();

            if (restartWorker)
            {
                AppendLog($"[QUEUE] Tasks were added while the worker was finishing; restarting worker with {GetQueueTaskCount()} pending path task(s).");
                _ = ProcessTaskQueueAsync();
            }
            else
            {
                AppendLog($"[QUEUE] Queue worker exited. Pending path tasks: {GetQueueTaskCount()}, tracked media: {GetQueuedMediaCount()}.");
            }
        }
    }

    private MediaPathTask? GetNextQueuedTask()
    {
        lock (_taskQueueLock)
            return _taskQueue.Count == 0 ? null : _taskQueue.Peek();
    }

    private MediaPathTask? DequeueNextTask()
    {
        lock (_taskQueueLock)
            return _taskQueue.Count == 0 ? null : _taskQueue.Dequeue();
    }

    private int GetQueueTaskCount()
    {
        lock (_taskQueueLock)
            return _taskQueue.Count;
    }

    private int GetQueuedMediaCount()
    {
        lock (_taskQueueLock)
            return _queuedMediaIds.Count;
    }

    private IReadOnlyList<MediaPathTask> GetQueuedTasksSnapshot()
    {
        lock (_taskQueueLock)
            return _taskQueue.ToList();
    }

    private void ReleaseQueuedMediaIdIfFinished(string itemId)
    {
        lock (_taskQueueLock)
        {
            if (_taskQueue.Any(task => string.Equals(task.ItemId, itemId, StringComparison.OrdinalIgnoreCase)))
                return;
            _queuedMediaIds.Remove(itemId);
        }
        RaisePropertyChanged(nameof(SelectionSummary));
    }

    private void ClearPendingTaskQueue()
    {
        lock (_taskQueueLock)
        {
            _taskQueue.Clear();
            _queuedMediaIds.Clear();
        }
        MarkQueuedEntriesAsStopped();
        RaisePropertyChanged(nameof(SelectionSummary));
    }

    private async Task RetryFailedTranslationsAsync()
    {
        List<MediaPathTask> tasks;
        lock (_retryQueueLock)
        {
            tasks = _failedTranslationTasks
                .Select(task => new MediaPathTask(task.ItemId, task.MediaName, task.MediaPath))
                .ToList();
        }
        if (tasks.Count == 0) return;

        _shutdownCancelledForCurrentBatch = false;
        _currentBatchSupportsAutomaticShutdown = false;
        ClearShutdownRequestFailure();
        RaisePropertyChanged(nameof(ShowShutdownWhenCompleteStatus));
RaisePropertyChanged(nameof(BatchStateDetailText));

        foreach (var task in tasks)
        {
            var entry = AddTaskEntry(task.ItemId, task.MediaName, task.Path);
            entry.Phase = TaskPhase.Translating;
        }

        RefreshQueuePositions();
        RefreshTaskSummary();

        IsExecuting = true;
        IsStopping = false;
        GenerateCommand.RaiseCanExecuteChanged();
        RetryFailedTranslationCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();

        try
        {
            var validationStartInfo = CommandBuilder.BuildTranslateStartInfo(GetTranscriptionSubtitlePath(tasks[0].Path), _settings.WhisperJavTranslate);
            if (!File.Exists(validationStartInfo.FileName))
                throw new FileNotFoundException("Subtitle translation executable was not found.", validationStartInfo.FileName);

            CurrentPage = PageKind.Tasks;
            AppendLog($"Retrying all failed translation tasks: {tasks.Count}.");
            var allSucceeded = true;
            var subtitleMoved = false;
            for (var taskIndex = 0; taskIndex < tasks.Count; taskIndex++)
            {
                if (IsStopping) break;

                var task = tasks[taskIndex];
                if (WasStopRequested(task.Path))
                {
                    ClearStopRequest(task.Path);
                    UpdateTaskPhase(task.Path, TaskPhase.Stopped);
                    continue;
                }
                if (!await ExecuteSubtitleTranslationAsync(task, taskIndex + 1, tasks.Count))
                {
                    if (WasStopRequested(task.Path))
                    {
                        ClearStopRequest(task.Path);
                        UpdateTaskPhase(task.Path, TaskPhase.Stopped);
                    }
                    else
                    {
                        allSucceeded = false;
                        UpdateTaskPhase(task.Path, TaskPhase.Failed, "翻译失败");
                    }
                    continue;
                }
                if (IsStopping) break;

                UpdateTaskPhase(task.Path, TaskPhase.PostProcessing, "正在执行 Seconv 后处理");
                var seconvResult = await ExecuteSeconvCommandsAsync([task.Path]);
                allSucceeded &= seconvResult.AllSucceeded;
                subtitleMoved |= seconvResult.SubtitleCopied;
                if (WasStopRequested(task.Path))
                {
                    ClearStopRequest(task.Path);
                    UpdateTaskPhase(task.Path, TaskPhase.Stopped);
                }
                else if (seconvResult.AllSucceeded)
                {
                    UpdateTaskPhase(task.Path, TaskPhase.Completed, "全部完成");
                    RemoveTranslationRetryTask(task.Path);
                }
                else
                {
                    UpdateTaskPhase(task.Path, TaskPhase.Failed, "后处理失败");
                }
            }

            if (IsStopping)
            {
                AppendLog("Failed translation retry queue stopped.");
            }
            else
            {
                if (subtitleMoved && _client is not null && !string.IsNullOrWhiteSpace(SelectedLibraryId))
                {
                    try
                    {
                        await _client!.RefreshLibraryAsync(SelectedLibraryId);
                        AppendLog("Jellyfin library refresh requested after subtitle move.");
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"[ERROR] Failed to refresh Jellyfin library: {ex.Message}", NLog.LogLevel.Error);
                    }
                }

                AppendLog(allSucceeded ? "All failed translation tasks completed." : "Failed translation retry completed with failures.");
            }
        }
        catch (Exception ex)
        {
            foreach (var task in tasks)
                UpdateTaskPhase(task.Path, TaskPhase.Failed, "重试准备失败");
            CurrentPage = PageKind.Tasks;
            AppendLog($"[ERROR] {ex.Message}", NLog.LogLevel.Error);
        }
        finally
        {
            bool startMainWorker;
            lock (_executionLock)
            {
                _activeProcess = null;
                _activeJob = null;
                IsExecuting = false;
                IsStopping = false;
                startMainWorker = GetQueueTaskCount() > 0;
                if (startMainWorker) IsExecuting = true;
            }
            GenerateCommand.RaiseCanExecuteChanged();
            RetryFailedTranslationCommand.RaiseCanExecuteChanged();
            StopCommand.RaiseCanExecuteChanged();

            if (startMainWorker)
            {
                AppendLog($"[QUEUE] Draining {GetQueueTaskCount()} pending path task(s) after translation retry.");
                _ = ProcessTaskQueueAsync();
            }
        }
    }

    private async Task<bool> ExecuteWhisperJavAsync(MediaPathTask task, int taskIndex, int taskCount)
    {
        try
        {
            var startInfo = CommandBuilder.BuildStartInfo([task.Path], _settings.WhisperJav);
            if (WasStopRequested(task.Path)) return false;
            AppendLog($"Running WhisperJav ({taskIndex}/{taskCount}): {task.Path}");
            AppendLog($"Command: {CommandBuilder.FormatCommand(startInfo)}");
            using var job = new ProcessJob();
            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) AppendLog(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) AppendLog($"[stderr] {e.Data}"); };

            if (!process.Start()) throw new InvalidOperationException("Unable to start WhisperJav process.");
            try
            {
                job.Add(process);
            }
            catch
            {
                await StopProcessTreeAsync(process.Id);
                throw;
            }

            lock (_executionLock)
            {
                _activeProcess = process;
                _activeJob = job;
            }
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            if (IsStopping || WasStopRequested(task.Path)) return false;
            if (process.ExitCode == 0)
            {
                AppendLog($"WhisperJav succeeded ({taskIndex}/{taskCount}): {task.Path}");
                return true;
            }

            var failure = $"WhisperJav exited with code {process.ExitCode}.";
            AppendLog($"[ERROR] {failure} Continuing with the next path.", NLog.LogLevel.Error);
            AppendWhisperJavFailure(task, failure);
            return false;
        }
        catch (Exception ex)
        {
            if (IsStopping || WasStopRequested(task.Path)) return false;
            var failure = $"WhisperJav threw an exception: {ex.Message}";
            AppendLog($"[ERROR] {failure} Continuing with the next path.", NLog.LogLevel.Error);
            AppendWhisperJavFailure(task, failure);
            return false;
        }
        finally
        {
            lock (_executionLock)
            {
                _activeProcess = null;
                _activeJob = null;
            }
        }
    }

    private async Task<bool> ExecuteSubtitleTranslationAsync(MediaPathTask task, int taskIndex, int taskCount)
    {
        var transcriptionSubtitlePath = GetTranscriptionSubtitlePath(task.Path);
        if (!File.Exists(transcriptionSubtitlePath))
        {
            AppendLog($"[ERROR] Transcription subtitle was not found: {transcriptionSubtitlePath}. Skipping Seconv for this path.", NLog.LogLevel.Error);
            return false;
        }

        try
        {
            var startInfo = CommandBuilder.BuildTranslateStartInfo(transcriptionSubtitlePath, _settings.WhisperJavTranslate);
            AppendLog($"Running subtitle translation ({taskIndex}/{taskCount}): {transcriptionSubtitlePath}");
            AppendLog($"Command: {CommandBuilder.FormatCommand(startInfo)}");
            using var job = new ProcessJob();
            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            var translationCompletionState = 0;
            void CaptureTranslationCompletion(string line)
            {
                if (line.Contains("All subtitles translated: YES", StringComparison.OrdinalIgnoreCase))
                    Interlocked.Exchange(ref translationCompletionState, 1);
                else if (line.Contains("All subtitles translated: NO", StringComparison.OrdinalIgnoreCase))
                    Interlocked.Exchange(ref translationCompletionState, -1);
            }
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                AppendLog(e.Data);
                CaptureTranslationCompletion(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                AppendLog($"[stderr] {e.Data}");
                CaptureTranslationCompletion(e.Data);
            };

            if (!process.Start()) throw new InvalidOperationException("Unable to start subtitle translation process.");
            try
            {
                job.Add(process);
            }
            catch
            {
                await StopProcessTreeAsync(process.Id);
                throw;
            }

            lock (_executionLock)
            {
                _activeProcess = process;
                _activeJob = job;
            }
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();
            process.WaitForExit();

            if (IsStopping || WasStopRequested(task.Path)) return false;
            if (process.ExitCode == 0 && Volatile.Read(ref translationCompletionState) == 1)
            {
                AppendLog($"Subtitle translation succeeded ({taskIndex}/{taskCount}): {transcriptionSubtitlePath}");
                return true;
            }

            var failure = process.ExitCode == 0
                ? "Subtitle translation reported incomplete subtitles (All subtitles translated: NO or no completion marker)."
                : $"Subtitle translation exited with code {process.ExitCode}.";
            AppendLog($"[ERROR] {failure} Skipping Seconv for this path.", NLog.LogLevel.Error);
            RecordTranslationFailure(task, failure);
            return false;
        }
        catch (Exception ex)
        {
            if (IsStopping || WasStopRequested(task.Path)) return false;
            var failure = $"Subtitle translation failed: {ex.Message}";
            AppendLog($"[ERROR] {failure}. Skipping Seconv for this path.", NLog.LogLevel.Error);
            RecordTranslationFailure(task, failure);
            return false;
        }
        finally
        {
            lock (_executionLock)
            {
                _activeProcess = null;
                _activeJob = null;
            }
        }
    }

    private void RecordTranslationFailure(MediaPathTask task, string failure)
    {
        lock (_retryQueueLock)
        {
            _failedTranslationTasks.RemoveAll(existing => string.Equals(existing.MediaPath, task.Path, StringComparison.OrdinalIgnoreCase));
            _failedTranslationTasks.Add(new TranslationRetryTask(task.ItemId, task.MediaName, task.Path, DateTime.Now, failure));
            SaveTranslationRetryTasks();
        }
        RefreshTranslationRetryQueueState();
    }

    private void RemoveTranslationRetryTask(string mediaPath)
    {
        lock (_retryQueueLock)
        {
            if (_failedTranslationTasks.RemoveAll(task => string.Equals(task.MediaPath, mediaPath, StringComparison.OrdinalIgnoreCase)) == 0)
                return;
            SaveTranslationRetryTasks();
        }
        RefreshTranslationRetryQueueState();
    }

    private void SaveTranslationRetryTasks()
    {
        try
        {
            SettingsStore.SaveTranslationRetryTasks(_failedTranslationTasks);
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] Failed to save translation retry queue: {ex.Message}", NLog.LogLevel.Error);
        }
    }

    private void RefreshTranslationRetryQueueState()
    {
        RaisePropertyChanged(nameof(RetryFailedTranslationButtonText));
        RaisePropertyChanged(nameof(HasFailedTranslationTasks));
        RetryFailedTranslationCommand.RaiseCanExecuteChanged();
    }

    private string GetTranscriptionSubtitlePath(string mediaPath)
    {
        var mediaName = Path.GetFileNameWithoutExtension(mediaPath);
        if (string.IsNullOrWhiteSpace(mediaName))
            throw new InvalidOperationException($"Invalid media path: {mediaPath}");

        return Path.Combine(_settings.WhisperJav.OutputDir, $"{mediaName}.ja.merged.whisperjav.srt");
    }

    private void AppendWhisperJavFailure(MediaPathTask task, string reason)
    {
        var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ItemId: {task.ItemId}{Environment.NewLine}" +
                    $"MediaName: {task.MediaName}{Environment.NewLine}" +
                    $"Path: {task.Path}{Environment.NewLine}" +
                    $"Reason: {reason}{Environment.NewLine}{Environment.NewLine}";
        lock (_logLock)
        {
            try
            {
                File.AppendAllText(_failedWhisperJavLogFilePath, entry, Encoding.UTF8);
            }
            catch
            {
                // Failure-task logging must not prevent the remaining queue from running.
            }
        }
    }

    private readonly record struct MediaPathTask(string ItemId, string MediaName, string Path);

    private async Task<SeconvResult> ExecuteSeconvCommandsAsync(IEnumerable<string> mediaPaths)
    {
        const int repeatCount = 5;
        var targets = mediaPaths.ToList();
        if (targets.Count == 0)
        {
            AppendLog("[错误] 没有可用于 Seconv 后处理的媒体路径。", NLog.LogLevel.Error);
            return new SeconvResult(false, false);
        }

        AppendLog($"开始 Seconv 后处理：{targets.Count} 个媒体，每个媒体最多执行 {repeatCount} 次。");
        var allSucceeded = true;
        var subtitleCopied = false;
        foreach (var mediaPath in targets)
        {
            var mediaSucceeded = true;
            try
            {
            for (var attempt = 1; attempt <= repeatCount; attempt++)
            {
                if (IsStopping) return new SeconvResult(false, subtitleCopied);

                var startInfo = CommandBuilder.BuildSeconvStartInfo(mediaPath, _settings.Seconv);
                if (Path.IsPathFullyQualified(startInfo.FileName) && !File.Exists(startInfo.FileName))
                    throw new FileNotFoundException("未找到 Seconv 可执行文件，请检查 appsettings.json 中 Seconv.ExecutablePath。", startInfo.FileName);

                AppendLog($"执行 Seconv（{attempt}/{repeatCount}）：{mediaPath}");
                AppendLog($"Command: {CommandBuilder.FormatCommand(startInfo)}");
                using var job = new ProcessJob();
                using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                process.OutputDataReceived += (_, e) => { if (e.Data is not null) AppendLog(e.Data); };
                process.ErrorDataReceived += (_, e) => { if (e.Data is not null) AppendLog($"[stderr] {e.Data}"); };

                if (!process.Start()) throw new InvalidOperationException("无法启动 Seconv 进程。");
                try
                {
                    job.Add(process);
                }
                catch
                {
                    await StopProcessTreeAsync(process.Id);
                    throw;
                }
                lock (_executionLock)
                {
                    _activeProcess = process;
                    _activeJob = job;
                }
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync();

                if (IsStopping)
                {
                    AppendLog("Seconv 后处理已终止。");
                    return new SeconvResult(false, subtitleCopied);
                }
                if (process.ExitCode != 0)
                {
                    AppendLog($"[错误] Seconv 执行失败，媒体: {mediaPath}，轮次: {attempt}/{repeatCount}，退出码: {process.ExitCode}。该媒体后续循环已停止，将继续处理其他媒体。", NLog.LogLevel.Error);
                    mediaSucceeded = false;
                    allSucceeded = false;
                    break;
                }
                AppendLog($"Seconv 执行成功，媒体: {mediaPath}，轮次: {attempt}/{repeatCount}。");
            }

            if (!mediaSucceeded) continue;

            var mediaFolder = Path.GetDirectoryName(mediaPath);
            var subtitleName = $"{Path.GetFileNameWithoutExtension(mediaPath)}.chi.whisperjav.srt";
            if (string.IsNullOrWhiteSpace(mediaFolder) || string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(mediaPath)))
                throw new InvalidOperationException($"媒体路径无效，无法复制字幕: {mediaPath}");

            var sourceSubtitlePath = Path.Combine(_settings.Seconv.InputFolder, subtitleName);
            var destinationSubtitlePath = Path.Combine(mediaFolder, subtitleName);
            if (!File.Exists(sourceSubtitlePath))
            {
                AppendLog($"[错误] Seconv 后未找到转换后的字幕: {sourceSubtitlePath}。将继续处理其他媒体。", NLog.LogLevel.Error);
                allSucceeded = false;
                continue;
            }
            try
            {
                File.Move(sourceSubtitlePath, destinationSubtitlePath, overwrite: true);
                AppendLog($"已移动转换后的字幕: {destinationSubtitlePath}");
                subtitleCopied = true;
            }
            catch (Exception ex)
            {
                AppendLog($"[错误] 无法移动字幕到媒体目录: {destinationSubtitlePath}。{ex.Message} 将继续处理其他媒体。", NLog.LogLevel.Error);
                allSucceeded = false;
                continue;
            }
            }
            catch (Exception ex)
            {
                AppendLog($"[错误] 处理媒体失败: {mediaPath}。{ex.Message} 将继续处理其他媒体。", NLog.LogLevel.Error);
                allSucceeded = false;
            }
        }

        return new SeconvResult(allSucceeded, subtitleCopied);
    }

    private readonly record struct SeconvResult(bool AllSucceeded, bool SubtitleCopied);

    public async Task StopExecutionAndWaitAsync()
    {
        await StopAsync();
        while (IsExecuting)
            await Task.Delay(50);
    }

    private async Task StopTaskAsync(TaskEntryViewModel? entry)
    {
        if (entry is null || !entry.IsProcessing) return;
        lock (_stopRequestLock)
            _stopRequestedForPaths.Add(entry.FilePath);
        var current = CurrentTask;
        if (current is not null && string.Equals(current.FilePath, entry.FilePath, StringComparison.OrdinalIgnoreCase))
            await TerminateActiveProcessAsync();
    }

    private bool WasStopRequested(string filePath)
    {
        lock (_stopRequestLock) return _stopRequestedForPaths.Contains(filePath);
    }

    private void ClearStopRequest(string filePath)
    {
        lock (_stopRequestLock) _stopRequestedForPaths.Remove(filePath);
    }

    private async Task TerminateActiveProcessAsync()
    {
        ProcessJob? job;
        Process? process;
        lock (_executionLock)
        {
            job = _activeJob;
            process = _activeProcess;
        }
        if (process is null) return;
        try
        {
            if (process.HasExited) return;
        }
        catch (InvalidOperationException)
        {
            return;
        }
        AppendLog("正在终止任务及其子进程...");
        try
        {
            if (job is not null)
            {
                job.Terminate();
                AppendLog("已向进程作业对象发送终止请求。");
            }
            else
            {
                AppendLog("进程作业对象不可用，使用 taskkill 终止进程树。");
                await StopProcessTreeAsync(process.Id);
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[错误] 无法终止任务：{ex.Message}", NLog.LogLevel.Error);
        }
    }

    private async Task StopAsync()
    {
        ProcessJob? job;
        Process? process;
        lock (_executionLock)
        {
            job = _activeJob;
            process = _activeProcess;
        }

        if (!IsExecuting || IsStopping) return;
        IsStopping = true;
        _shutdownCancelledForCurrentBatch = true;
        StopCommand.RaiseCanExecuteChanged();

        // Snapshot-and-stop: mark currently-pending queued tasks as Stopped now, so any
        // task enqueued *after* this request (e.g. a retry) is never wiped by the worker.
        lock (_taskQueueLock)
        {
            _taskQueue.Clear();
            _queuedMediaIds.Clear();
        }
        MarkQueuedEntriesAsStopped();

        if (process is null)
        {
            AppendLog("Cancellation requested; the task will stop after the current preparation step.");
            return;
        }
        try
        {
            if (process.HasExited) return;
        }
        catch (InvalidOperationException)
        {
            return;
        }
        IsStopping = true;
        StopCommand.RaiseCanExecuteChanged();
        AppendLog("正在终止任务及其子进程...");
        try
        {
            if (job is not null)
            {
                job.Terminate();
                AppendLog("已向进程作业对象发送终止请求。");
            }
            else
            {
                AppendLog("进程作业对象不可用，使用 taskkill 终止进程树。");
                await StopProcessTreeAsync(process.Id);
            }
        }
        catch (Exception ex)
        {
            IsStopping = false;
            _shutdownCancelledForCurrentBatch = false;
            StopCommand.RaiseCanExecuteChanged();
            AppendLog($"[错误] 无法终止任务：{ex.Message}", NLog.LogLevel.Error);
        }
    }

    private async Task StopProcessTreeAsync(int processId)
    {
        using var taskKill = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "taskkill.exe",
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        taskKill.StartInfo.ArgumentList.Add("/PID");
        taskKill.StartInfo.ArgumentList.Add(processId.ToString());
        taskKill.StartInfo.ArgumentList.Add("/T");
        taskKill.StartInfo.ArgumentList.Add("/F");
        AppendLog($"Command: {CommandBuilder.FormatCommand(taskKill.StartInfo)}");
        if (!taskKill.Start()) throw new InvalidOperationException("Unable to start taskkill.");
        await taskKill.WaitForExitAsync();
        if (taskKill.ExitCode != 0) throw new InvalidOperationException($"taskkill 退出码: {taskKill.ExitCode}。");
    }

    private async Task RequestSystemShutdownAsync()
    {
        var shutdownPath = Path.Combine(Environment.SystemDirectory, "shutdown.exe");
        if (!File.Exists(shutdownPath))
        {
            SetShutdownRequestFailure($"未找到 {shutdownPath}。");
            return;
        }

        var consoleEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        var startInfo = new ProcessStartInfo
        {
            FileName = shutdownPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = consoleEncoding,
            StandardErrorEncoding = consoleEncoding
        };
        startInfo.ArgumentList.Add("/s");
        startInfo.ArgumentList.Add("/f");
        startInfo.ArgumentList.Add("/t");
        startInfo.ArgumentList.Add("0");

        AppendLog("System shutdown requested after task queue completion.");
        AppendLog($"Command: {CommandBuilder.FormatCommand(startInfo)}");
        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("无法启动 shutdown.exe。");
            var standardOutputTask = process.StandardOutput.ReadToEndAsync();
            var standardErrorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var standardOutput = await standardOutputTask;
            var standardError = await standardErrorTask;

            if (process.ExitCode == 0)
            {
                AppendLog("System shutdown command completed successfully.");
                return;
            }

            var detail = string.Join(" ", new[] { standardError, standardOutput }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim()));
            SetShutdownRequestFailure(detail.Length > 0
                ? $"shutdown.exe 退出码 {process.ExitCode}：{detail}"
                : $"shutdown.exe 退出码：{process.ExitCode}。");
        }
        catch (Exception ex)
        {
            SetShutdownRequestFailure(ex.Message);
        }
    }

    private void ClearShutdownRequestFailure()
    {
        if (string.IsNullOrEmpty(_shutdownRequestFailure)) return;
        _shutdownRequestFailure = "";
        RaisePropertyChanged(nameof(BatchStateDetailText));
    }

    private void SetShutdownRequestFailure(string message)
    {
        _shutdownRequestFailure = message;
        RaisePropertyChanged(nameof(BatchStateDetailText));
        AppendLog($"[ERROR] System shutdown request failed: {message}", NLog.LogLevel.Error);
    }

    private void AppendLog(string message, NLog.LogLevel? level = null)
    {
        _logger.Log(level ?? NLog.LogLevel.Info, message);
    }

    private void RefreshPaging()
    {
        RaisePropertyChanged(nameof(PageText));
        RaisePropertyChanged(nameof(MediaCountText));
        RaisePropertyChanged(nameof(CanGoPrevious));
        RaisePropertyChanged(nameof(CanGoNext));
        PreviousPageCommand.RaiseCanExecuteChanged();
        NextPageCommand.RaiseCanExecuteChanged();
    }
}
