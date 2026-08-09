namespace JellyfinWhisperCommand;

public sealed class MainViewModel : ObservableObject
{
    private const int PageSize = 100;
    private readonly AppSettings _settings;
    private readonly UserSettings _userSettings;
    private readonly JellyfinClient? _client;
    private readonly Dispatcher _dispatcher;
    private readonly string _logFilePath = Path.Combine(AppContext.BaseDirectory, "execution.log");
    // Keeps the log file and the UI dispatcher queue in the same order.
    private readonly object _logLock = new();
    private readonly object _executionLock = new();
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
    public string PageText => _totalCount == 0 ? "第 0 / 0 页" : $"第 {_pageIndex + 1} / {Math.Ceiling(_totalCount / (double)PageSize)} 页";
    public string SelectionSummary => $"已选择 {_selectedIds.Count} 个媒体";

    public AsyncRelayCommand SearchCommand { get; }
    public AsyncRelayCommand GenerateCommand { get; }
    public AsyncRelayCommand StopCommand { get; }
    public AsyncRelayCommand PreviousPageCommand { get; }
    public AsyncRelayCommand NextPageCommand { get; }

    public MainViewModel()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _userSettings = SettingsStore.LoadUserSettings();
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
                    Process.Start(new ProcessStartInfo("shutdown.exe", "/s /t 0") { UseShellExecute = false, CreateNoWindow = true });
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
                File.Copy(sourceSubtitlePath, destinationSubtitlePath, overwrite: true);
                AppendLog($"已复制转换后的字幕: {destinationSubtitlePath}");
                subtitleCopied = true;
            }
            catch (Exception ex)
            {
                AppendLog($"[错误] 无法复制字幕到媒体目录: {destinationSubtitlePath}。{ex.Message} 将继续处理其他媒体。");
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

    private async Task StopAsync()
    {
        ProcessJob? job;
        Process? process;
        lock (_executionLock)
        {
            job = _activeJob;
            process = _activeProcess;
        }

        if (process is null || process.HasExited) return;
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

    private static async Task StopProcessTreeAsync(int processId)
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
