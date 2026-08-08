namespace JellyfinWhisperCommand;

public sealed class MainViewModel : ObservableObject
{
    private const int PageSize = 100;
    private readonly AppSettings _settings;
    private readonly UserSettings _userSettings;
    private readonly JellyfinClient? _client;
    private readonly HashSet<string> _selectedIds = [];
    private string? _selectedLibraryId;
    private string _selectedSort = "DateCreated";
    private bool _hasSubtitles;
    private string _statusMessage = "正在加载媒体库...";
    private bool _isStatusVisible = true;
    private int _pageIndex;
    private int _totalCount;

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
    public bool CanGoPrevious => _pageIndex > 0;
    public bool CanGoNext => (_pageIndex + 1) * PageSize < _totalCount;
    public string PageText => _totalCount == 0 ? "第 0 / 0 页" : $"第 {_pageIndex + 1} / {Math.Ceiling(_totalCount / (double)PageSize)} 页";
    public string SelectionSummary => $"已选择 {_selectedIds.Count} 个媒体";

    public AsyncRelayCommand SearchCommand { get; }
    public AsyncRelayCommand GenerateCommand { get; }
    public AsyncRelayCommand PreviousPageCommand { get; }
    public AsyncRelayCommand NextPageCommand { get; }

    public MainViewModel()
    {
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
        GenerateCommand = new AsyncRelayCommand(GenerateAsync, () => _client is not null && _selectedIds.Count > 0);
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

    private async Task GenerateAsync()
    {
        if (_client is null) return;
        try
        {
            StatusMessage = "正在获取已选媒体路径...";
            IsStatusVisible = true;
            var paths = new List<string>();
            foreach (var itemId in _selectedIds)
            {
                paths.AddRange(await _client.GetPathsAsync(itemId));
            }
            if (paths.Count == 0) throw new InvalidOperationException("已选媒体没有可用路径。");
            Clipboard.SetText(CommandBuilder.Build(paths, _settings.WhisperJav));
            StatusMessage = $"已复制命令，包含 {paths.Count} 个媒体。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"生成命令失败：{ex.Message}";
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
