namespace JellyfinWhisperCommand.Views;

using System.Collections.Specialized;

public partial class TasksPageView : UserControl
{
    public TasksPageView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel viewModel)
                viewModel.TaskEntries.CollectionChanged += OnTasksChanged;
        };
    }

    private void OnTasksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
            Dispatcher.BeginInvoke(() => TaskScroller.ScrollToEnd());
    }
}
