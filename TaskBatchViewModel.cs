namespace JellyfinWhisperCommand;

/// <summary>批次级收尾状态：任务已全部结束时的汇总视图状态。</summary>
public enum BatchState
{
    None,
    Completed,
    Partial,
    Failed,
    Stopped
}

/// <summary>
/// Represents the user's current batch of media-processing tasks.
/// The batch is a UX-level concept; the existing queue remains responsible for execution.
/// </summary>
public sealed class TaskBatchViewModel : ObservableObject
{
    public ObservableCollection<TaskEntryViewModel> Tasks { get; } = [];

    public int TotalCount => Tasks.Count;
    public int QueuedCount => Tasks.Count(task => task.Phase == TaskPhase.Queued);
    public int RunningCount => Tasks.Count(task => task.Phase is TaskPhase.Transcribing or TaskPhase.Translating or TaskPhase.PostProcessing);
    public int CompletedCount => Tasks.Count(task => task.Phase == TaskPhase.Completed);
    public int FailedCount => Tasks.Count(task => task.Phase == TaskPhase.Failed);
    public int StoppedCount => Tasks.Count(task => task.Phase == TaskPhase.Stopped);
    public bool HasTasks => Tasks.Count > 0;
    public bool HasActiveTasks => RunningCount > 0 || QueuedCount > 0;
    public bool HasFailures => FailedCount > 0;
    public double Progress => TotalCount == 0 ? 0 : (double)(CompletedCount + FailedCount + StoppedCount) / TotalCount;
    public string ProgressText => TotalCount == 0 ? "0%" : $"{Progress:P0}";
    public string SummaryText => TotalCount == 0
        ? "暂无任务"
        : $"{CompletedCount} 已完成 · {RunningCount} 处理中 · {QueuedCount} 等待 · {FailedCount} 失败 · {StoppedCount} 已停止";

    public TaskEntryViewModel? CurrentTask => Tasks.FirstOrDefault(task => task.IsActive && task.Phase != TaskPhase.Queued);

    public TaskBatchViewModel()
    {
        Tasks.CollectionChanged += OnTasksChanged;
    }

    public void Add(TaskEntryViewModel task)
    {
        Tasks.Add(task);
    }

    public void Remove(TaskEntryViewModel task)
    {
        Tasks.Remove(task);
    }

    public void ClearCompleted()
    {
        foreach (var task in Tasks.Where(task => !task.IsActive).ToList())
            Tasks.Remove(task);
    }

    private void OnTasksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (TaskEntryViewModel task in e.OldItems)
                task.PropertyChanged -= OnTaskPropertyChanged;
        }

        if (e.NewItems is not null)
        {
            foreach (TaskEntryViewModel task in e.NewItems)
                task.PropertyChanged += OnTaskPropertyChanged;
        }

        Refresh();
    }

    private void OnTaskPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TaskEntryViewModel.Phase) or nameof(TaskEntryViewModel.Detail))
            Refresh();
    }

    private void Refresh()
    {
        RaisePropertyChanged(nameof(TotalCount));
        RaisePropertyChanged(nameof(QueuedCount));
        RaisePropertyChanged(nameof(RunningCount));
        RaisePropertyChanged(nameof(CompletedCount));
        RaisePropertyChanged(nameof(FailedCount));
        RaisePropertyChanged(nameof(StoppedCount));
        RaisePropertyChanged(nameof(HasTasks));
        RaisePropertyChanged(nameof(HasActiveTasks));
        RaisePropertyChanged(nameof(HasFailures));
        RaisePropertyChanged(nameof(Progress));
        RaisePropertyChanged(nameof(ProgressText));
        RaisePropertyChanged(nameof(SummaryText));
        RaisePropertyChanged(nameof(CurrentTask));
    }
}
