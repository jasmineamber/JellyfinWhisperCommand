namespace JellyfinWhisperCommand;

public sealed class MainViewModel : ObservableObject
{
    private const int PageSize = 100;
    private readonly AppSettings _settings;
    private readonly UserSettings _userSettings;
    private readonly JellyfinClient? _client;
    private readonly Dispatcher _dispatcher;
    private readonly string _logFilePath = Path.Combine(AppContext.BaseDirectory, "execution.log");
    private readonly string _failedWhisperJavLogFilePath = Path.Combine(AppContext.BaseDirectory, "failed-whisperjav-tasks.log");
    private readonly List<TranslationRetryTask> _failedTranslationTasks;
    // Keeps the log file and the UI dispatcher queue in the same order.
    private readonly object _logLock = new();
    private readonly object _executionLock = new();
    private readonly object _retryQueueLock = new();
    private readonly HashSet<string> _selectedIds = [];
    private Process? _activeProcess;
    private ProcessJob? _activeJob;
    private string? _selectedLibraryId;
    private string _selectedSort = "DateCreated";
    private bool _hasSubtitles;
    private string _statusMessage = "正在加载媒体库...";
    private bool _isStatusVisible = true;
    private int _pageIndex;
    private int _totalCount;
    private bool _isExecuting;
    private bool _isStopping;
    private bool _shutdownWhenComplete;
    private int _selectedTabIndex;
    private string _logText = "等待执行命令。";

    public ObservableCollection<MediaLibrary> Libraries { get; } = [];
    public ObservableCollection<MediaItem> MediaItems { get; } = [];
    public IReadOnlyList<Option<string>> SortOptions { get; } =
    [new("加入日期", "DateCreated"), new("发行日期", "PremiereDate")];
    public IReadOnlyList<Option<bool>> SubtitleOptions { get; } = [new("否", false), new("是", true)];

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
    public bool HasSubtitles { get => _hasSubtitles; set => SetProperty(ref _hasSubtitles, value); }
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public bool IsStatusVisible { get => _isStatusVisible; private set => SetProperty(ref _isStatusVisible, value); }
    public bool IsExecuting { get => _isExecuting; private set => SetProperty(ref _isExecuting, value); }
    public bool IsStopping { get => _isStopping; private set => SetProperty(ref _isStopping, value); }
    public bool ShutdownWhenComplete { get => _shutdownWhenComplete; set => SetProperty(ref _shutdownWhenComplete, value); }
    public int SelectedTabIndex { get => _selectedTabIndex; set => SetProperty(ref _selectedTabIndex, value); }
    public string LogText { get => _logText; private set => SetProperty(ref _logText, value); }
    public bool CanGoPrevious => _pageIndex > 0;
    public bool CanGoNext => (_pageIndex + 1) * PageSize < _totalCount;
    public string RetryFailedTranslationButtonText
    {
        get
        {
            lock (_retryQueueLock) return $"重试失败翻译 ({_failedTranslationTasks.Count})";
        }
    }
    public string PageText => _totalCount == 0 ? "第 0 / 0 页" : $"第 {_pageIndex + 1} / {Math.Ceiling(_totalCount / (double)PageSize)} 页";
    public string SelectionSummary => $"已选择 {_selectedIds.Count} 个媒体";

    public AsyncRelayCommand SearchCommand { get; }
    public AsyncRelayCommand GenerateCommand { get; }
    public AsyncRelayCommand RetryFailedTranslationCommand { get; }
    public AsyncRelayCommand StopCommand { get; }
    public AsyncRelayCommand PreviousPageCommand { get; }
    public AsyncRelayCommand NextPageCommand { get; }

    public MainViewModel()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
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

        SearchCommand = new AsyncRelayCommand(SearchAsync, () => _client is not null && !string.IsNullOrWhiteSpace(SelectedLibraryId));
        GenerateCommand = new AsyncRelayCommand(ExecuteAsync, () => _client is not null && _selectedIds.Count > 0 && !IsExecuting);
        RetryFailedTranslationCommand = new AsyncRelayCommand(RetryFailedTranslationsAsync, () => _failedTranslationTasks.Count > 0 && !IsExecuting);
        StopCommand = new AsyncRelayCommand(StopAsync, () => IsExecuting && !IsStopping);
        PreviousPageCommand = new AsyncRelayCommand(async () => { _pageIndex--; await LoadPageAsync(); }, () => CanGoPrevious);
        NextPageCommand = new AsyncRelayCommand(async () => { _pageIndex++; await LoadPageAsync(); }, () => CanGoNext);
        _ = LoadLibrariesAsync();
    }

    private async Task LoadLibrariesAsync()
    {
        if (_client is null) return;
        try
        {
            var libraries = await _client.GetLibrariesAsync();
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
            StatusMessage = $"加载媒体库失败：{ex.Message}";
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
        IsStatusVisible = true;
        StatusMessage = "正在查询媒体...";
        try
        {
            var response = await _client.GetItemsAsync(SelectedLibraryId, SelectedSort, HasSubtitles, _pageIndex * PageSize, PageSize);
            foreach (var oldItem in MediaItems) oldItem.PropertyChanged -= OnMediaItemPropertyChanged;
            MediaItems.Clear();
            foreach (var item in response.Items)
            {
                var media = new MediaItem { Id = item.Id, Name = item.Name, ImageUrl = _client.GetImageUrl(item), IsSelected = _selectedIds.Contains(item.Id) };
                media.PropertyChanged += OnMediaItemPropertyChanged;
                MediaItems.Add(media);
            }
            _totalCount = response.TotalRecordCount;
            IsStatusVisible = MediaItems.Count == 0;
            StatusMessage = "没有符合筛选条件的媒体。";
            RefreshPaging();
        }
        catch (Exception ex)
        {
            MediaItems.Clear();
            _totalCount = 0;
            IsStatusVisible = true;
            StatusMessage = $"查询失败：{ex.Message}";
            RefreshPaging();
        }
    }

    private void OnMediaItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MediaItem.IsSelected) || sender is not MediaItem item) return;
        if (item.IsSelected) _selectedIds.Add(item.Id); else _selectedIds.Remove(item.Id);
        RaisePropertyChanged(nameof(SelectionSummary));
        GenerateCommand.RaiseCanExecuteChanged();
    }

    private async Task ExecuteAsync()
    {
        if (_client is null) return;
        IsExecuting = true;
        IsStopping = false;
        GenerateCommand.RaiseCanExecuteChanged();
        RetryFailedTranslationCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        try
        {
            IsStatusVisible = true;
            var selectedIds = _selectedIds.ToList();
            var tasks = new List<MediaPathTask>();
            AppendLog($"Preparing {selectedIds.Count} selected media item(s).");
            foreach (var itemId in selectedIds)
            {
                var itemPaths = await _client.GetPathsAsync(itemId);
                var mediaName = MediaItems.FirstOrDefault(item => item.Id == itemId)?.Name ?? itemId;
                tasks.AddRange(itemPaths.Select(path => new MediaPathTask(itemId, mediaName, path)));
            }

            if (tasks.Count == 0)
                throw new InvalidOperationException("The selected media items have no usable paths.");

            var validationStartInfo = CommandBuilder.BuildStartInfo([tasks[0].Path], _settings.WhisperJav);
            if (!File.Exists(validationStartInfo.FileName))
            {
                const string failure = "WhisperJav executable was not found.";
                foreach (var task in tasks) AppendWhisperJavFailure(task, failure);
                throw new FileNotFoundException("WhisperJav executable was not found.", validationStartInfo.FileName);
            }

            SelectedTabIndex = 1;
            AppendLog($"WhisperJav executable: {validationStartInfo.FileName}");
            AppendLog($"Path tasks to run: {tasks.Count}");
            var allSucceeded = true;
            var subtitleMoved = false;

            for (var taskIndex = 0; taskIndex < tasks.Count; taskIndex++)
            {
                if (IsStopping) break;

                var task = tasks[taskIndex];
                if (!await ExecuteWhisperJavAsync(task, taskIndex + 1, tasks.Count))
                {
                    allSucceeded = false;
                    continue;
                }
                if (IsStopping) break;

                if (!await ExecuteSubtitleTranslationAsync(task, taskIndex + 1, tasks.Count))
                {
                    allSucceeded = false;
                    continue;
                }
                if (IsStopping) break;

                var seconvResult = await ExecuteSeconvCommandsAsync([task.Path]);
                allSucceeded &= seconvResult.AllSucceeded;
                subtitleMoved |= seconvResult.SubtitleCopied;
                if (seconvResult.AllSucceeded) RemoveTranslationRetryTask(task.Path);
            }

            if (IsStopping)
            {
                AppendLog("Task queue stopped.");
                StatusMessage = "Task queue stopped.";
            }
            else
            {
                if (subtitleMoved && !string.IsNullOrWhiteSpace(SelectedLibraryId))
                {
                    try
                    {
                        await _client.RefreshLibraryAsync(SelectedLibraryId);
                        AppendLog("Jellyfin library refresh requested after subtitle move.");
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"[ERROR] Failed to refresh Jellyfin library: {ex.Message}");
                    }
                }

                StatusMessage = allSucceeded ? "All path tasks completed." : "Path tasks completed with failures.";
                if (ShutdownWhenComplete)
                {
                    AppendLog("System shutdown requested after task queue completion.");
                    var shutdownStartInfo = new ProcessStartInfo("shutdown.exe", "/s /t 0") { UseShellExecute = false, CreateNoWindow = true };
                    AppendLog($"Command: {CommandBuilder.FormatCommand(shutdownStartInfo)}");
                    Process.Start(shutdownStartInfo);
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to prepare command: {ex.Message}";
            SelectedTabIndex = 1;
            AppendLog($"[ERROR] {ex.Message}");
        }
        finally
        {
            lock (_executionLock)
            {
                _activeProcess = null;
                _activeJob = null;
            }
            IsExecuting = false;
            IsStopping = false;
            GenerateCommand.RaiseCanExecuteChanged();
            RetryFailedTranslationCommand.RaiseCanExecuteChanged();
            StopCommand.RaiseCanExecuteChanged();
        }
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

            SelectedTabIndex = 1;
            AppendLog($"Retrying all failed translation tasks: {tasks.Count}.");
            var allSucceeded = true;
            var subtitleMoved = false;
            for (var taskIndex = 0; taskIndex < tasks.Count; taskIndex++)
            {
                if (IsStopping) break;

                var task = tasks[taskIndex];
                if (!await ExecuteSubtitleTranslationAsync(task, taskIndex + 1, tasks.Count))
                {
                    allSucceeded = false;
                    continue;
                }
                if (IsStopping) break;

                var seconvResult = await ExecuteSeconvCommandsAsync([task.Path]);
                allSucceeded &= seconvResult.AllSucceeded;
                subtitleMoved |= seconvResult.SubtitleCopied;
                if (seconvResult.AllSucceeded) RemoveTranslationRetryTask(task.Path);
            }

            if (IsStopping)
            {
                AppendLog("Failed translation retry queue stopped.");
                StatusMessage = "Failed translation retry queue stopped.";
            }
            else
            {
                if (subtitleMoved && _client is not null && !string.IsNullOrWhiteSpace(SelectedLibraryId))
                {
                    try
                    {
                        await _client.RefreshLibraryAsync(SelectedLibraryId);
                        AppendLog("Jellyfin library refresh requested after subtitle move.");
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"[ERROR] Failed to refresh Jellyfin library: {ex.Message}");
                    }
                }

                StatusMessage = allSucceeded ? "All failed translation tasks completed." : "Failed translation retry completed with failures.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to prepare failed-translation retry: {ex.Message}";
            SelectedTabIndex = 1;
            AppendLog($"[ERROR] {ex.Message}");
        }
        finally
        {
            lock (_executionLock)
            {
                _activeProcess = null;
                _activeJob = null;
            }
            IsExecuting = false;
            IsStopping = false;
            GenerateCommand.RaiseCanExecuteChanged();
            RetryFailedTranslationCommand.RaiseCanExecuteChanged();
            StopCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task<bool> ExecuteWhisperJavAsync(MediaPathTask task, int taskIndex, int taskCount)
    {
        try
        {
            var startInfo = CommandBuilder.BuildStartInfo([task.Path], _settings.WhisperJav);
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
            StatusMessage = $"Running WhisperJav ({taskIndex}/{taskCount}): {Path.GetFileName(task.Path)}";
            await process.WaitForExitAsync();

            if (IsStopping) return false;
            if (process.ExitCode == 0)
            {
                AppendLog($"WhisperJav succeeded ({taskIndex}/{taskCount}): {task.Path}");
                return true;
            }

            var failure = $"WhisperJav exited with code {process.ExitCode}.";
            AppendLog($"[ERROR] {failure} Continuing with the next path.");
            AppendWhisperJavFailure(task, failure);
            return false;
        }
        catch (Exception ex)
        {
            if (IsStopping) return false;
            var failure = $"WhisperJav threw an exception: {ex.Message}";
            AppendLog($"[ERROR] {failure} Continuing with the next path.");
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
            AppendLog($"[ERROR] Transcription subtitle was not found: {transcriptionSubtitlePath}. Skipping Seconv for this path.");
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
            StatusMessage = $"Translating subtitles ({taskIndex}/{taskCount}): {Path.GetFileName(task.Path)}";
            await process.WaitForExitAsync();
            process.WaitForExit();

            if (IsStopping) return false;
            if (process.ExitCode == 0 && Volatile.Read(ref translationCompletionState) == 1)
            {
                AppendLog($"Subtitle translation succeeded ({taskIndex}/{taskCount}): {transcriptionSubtitlePath}");
                return true;
            }

            var failure = process.ExitCode == 0
                ? "Subtitle translation reported incomplete subtitles (All subtitles translated: NO or no completion marker)."
                : $"Subtitle translation exited with code {process.ExitCode}.";
            AppendLog($"[ERROR] {failure} Skipping Seconv for this path.");
            RecordTranslationFailure(task, failure);
            return false;
        }
        catch (Exception ex)
        {
            if (IsStopping) return false;
            var failure = $"Subtitle translation failed: {ex.Message}";
            AppendLog($"[ERROR] {failure}. Skipping Seconv for this path.");
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
            AppendLog($"[ERROR] Failed to save translation retry queue: {ex.Message}");
        }
    }

    private void RefreshTranslationRetryQueueState()
    {
        RaisePropertyChanged(nameof(RetryFailedTranslationButtonText));
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

    private async Task ExecuteLegacyAsync()
    {
        if (_client is null) return;
        IsExecuting = true;
        IsStopping = false;
        GenerateCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        try
        {
            StatusMessage = "正在获取已选媒体路径...";
            IsStatusVisible = true;
            AppendLog($"开始准备任务，已选择 {_selectedIds.Count} 个媒体。");
            var paths = new List<string>();
            var mediaPaths = new List<string>();
            foreach (var itemId in _selectedIds)
            {
                var itemPaths = await _client.GetPathsAsync(itemId);
                paths.AddRange(itemPaths);
                if (itemPaths.FirstOrDefault() is { } mediaPath) mediaPaths.Add(mediaPath);
            }
            if (paths.Count == 0) throw new InvalidOperationException("已选媒体没有可用路径。");

            var startInfo = CommandBuilder.BuildStartInfo(paths, _settings.WhisperJav);
            if (!File.Exists(startInfo.FileName))
                throw new FileNotFoundException("未找到 WhisperJav 可执行文件，请检查 appsettings.json 中 WhisperJav.ExecutablePath。", startInfo.FileName);

            SelectedTabIndex = 1;
            AppendLog($"执行文件: {startInfo.FileName}");
            AppendLog($"工作目录: {startInfo.WorkingDirectory}");
            AppendLog($"媒体数量: {paths.Count}");
            AppendLog($"Command: {CommandBuilder.FormatCommand(startInfo)}");
            using var job = new ProcessJob();
            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) AppendLog(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) AppendLog($"[stderr] {e.Data}"); };

            if (!process.Start()) throw new InvalidOperationException("无法启动 WhisperJav 进程。");
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
            StatusMessage = $"正在执行命令，包含 {paths.Count} 个媒体...";
            await process.WaitForExitAsync();

            if (IsStopping)
            {
                AppendLog("任务已终止。");
                StatusMessage = "命令已终止。";
            }
            else
            {
                AppendLog($"进程已退出，退出码: {process.ExitCode}。");
                StatusMessage = process.ExitCode == 0 ? "命令执行完成。" : $"命令执行结束，退出码: {process.ExitCode}。";
                if (process.ExitCode == 0)
                {
                    var seconvResult = await ExecuteSeconvCommandsAsync(mediaPaths);
                    if (seconvResult.SubtitleCopied && !string.IsNullOrWhiteSpace(SelectedLibraryId))
                    {
                        AppendLog("已成功复制字幕，正在请求 Jellyfin 刷新当前媒体库。");
                        try
                        {
                            await _client.RefreshLibraryAsync(SelectedLibraryId);
                            AppendLog("Jellyfin 媒体库刷新请求已提交。");
                        }
                        catch (Exception ex)
                        {
                            AppendLog($"[错误] 无法刷新 Jellyfin 媒体库: {ex.Message}");
                        }
                    }
                    if (seconvResult.AllSucceeded) StatusMessage = "所有命令执行完成。";
                }
                if (ShutdownWhenComplete)
                {
                    AppendLog("已启用执行完后关机，正在请求系统关机。");
                    var shutdownStartInfo = new ProcessStartInfo("shutdown.exe", "/s /t 0") { UseShellExecute = false, CreateNoWindow = true };
                    AppendLog($"Command: {CommandBuilder.FormatCommand(shutdownStartInfo)}");
                    Process.Start(shutdownStartInfo);
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"生成命令失败：{ex.Message}";
            SelectedTabIndex = 1;
            AppendLog($"[错误] {ex.Message}");
        }
        finally
        {
            lock (_executionLock)
            {
                _activeProcess = null;
                _activeJob = null;
            }
            IsExecuting = false;
            IsStopping = false;
            GenerateCommand.RaiseCanExecuteChanged();
            StopCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task<SeconvResult> ExecuteSeconvCommandsAsync(IEnumerable<string> mediaPaths)
    {
        const int repeatCount = 5;
        var targets = mediaPaths.ToList();
        if (targets.Count == 0)
        {
            AppendLog("[错误] 没有可用于 Seconv 后处理的媒体路径。");
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
                StatusMessage = $"正在执行 Seconv：{Path.GetFileName(mediaPath)}（{attempt}/{repeatCount}）...";
                await process.WaitForExitAsync();

                if (IsStopping)
                {
                    AppendLog("Seconv 后处理已终止。");
                    StatusMessage = "命令已终止。";
                    return new SeconvResult(false, subtitleCopied);
                }
                if (process.ExitCode != 0)
                {
                    AppendLog($"[错误] Seconv 执行失败，媒体: {mediaPath}，轮次: {attempt}/{repeatCount}，退出码: {process.ExitCode}。该媒体后续循环已停止，将继续处理其他媒体。");
                    StatusMessage = $"Seconv 执行失败，退出码: {process.ExitCode}。";
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
                AppendLog($"[错误] Seconv 后未找到转换后的字幕: {sourceSubtitlePath}。将继续处理其他媒体。");
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
                AppendLog($"[错误] 无法移动字幕到媒体目录: {destinationSubtitlePath}。{ex.Message} 将继续处理其他媒体。");
                allSucceeded = false;
                continue;
            }
            }
            catch (Exception ex)
            {
                AppendLog($"[错误] 处理媒体失败: {mediaPath}。{ex.Message} 将继续处理其他媒体。");
                allSucceeded = false;
            }
        }

        StatusMessage = allSucceeded ? "Seconv 后处理完成。" : "Seconv 后处理完成，部分媒体失败。";
        return new SeconvResult(allSucceeded, subtitleCopied);
    }

    private readonly record struct SeconvResult(bool AllSucceeded, bool SubtitleCopied);

    public async Task StopExecutionAndWaitAsync()
    {
        await StopAsync();
        while (IsExecuting)
            await Task.Delay(50);
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
        StopCommand.RaiseCanExecuteChanged();

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
            StopCommand.RaiseCanExecuteChanged();
            AppendLog($"[错误] 无法终止任务：{ex.Message}");
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

    private void AppendLog(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        lock (_logLock)
        {
            try
            {
                File.AppendAllText(_logFilePath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Logging must not interrupt the command when the log file cannot be written.
            }

            // Always enqueue, including calls made on the UI thread. This prevents direct
            // UI updates from overtaking earlier background-process output.
            _dispatcher.BeginInvoke(() => LogText += Environment.NewLine + line);
        }
    }

    private void RefreshPaging()
    {
        RaisePropertyChanged(nameof(PageText));
        RaisePropertyChanged(nameof(CanGoPrevious));
        RaisePropertyChanged(nameof(CanGoNext));
        PreviousPageCommand.RaiseCanExecuteChanged();
        NextPageCommand.RaiseCanExecuteChanged();
    }
}
