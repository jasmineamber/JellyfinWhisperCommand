namespace JellyfinWhisperCommand;

public partial class MainWindow : Window
{
    private System.Windows.Controls.ScrollViewer? _logScrollViewer;
    private bool _isLogFollowingTail = true;
    private bool _isLogScrollScheduled;
    private bool _isLogUserScrolling;
    private bool _isLogUserScrollResetScheduled;
    private bool _isCloseConfirmed;
    private bool _isClosingAfterTaskStop;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_isCloseConfirmed || DataContext is not MainViewModel viewModel || !viewModel.IsExecuting)
            return;

        e.Cancel = true;
        if (_isClosingAfterTaskStop) return;

        var result = MessageBox.Show(
            "当前有任务正在运行。关闭窗口将停止正在进行的任务，是否继续？",
            "确认关闭",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes) return;

        _isClosingAfterTaskStop = true;
        try
        {
            await viewModel.StopExecutionAndWaitAsync();
            _isCloseConfirmed = true;
            Close();
        }
        finally
        {
            _isClosingAfterTaskStop = false;
        }
    }

    private void LogTextBox_Loaded(object sender, RoutedEventArgs e)
    {
        _logScrollViewer = FindVisualChild<System.Windows.Controls.ScrollViewer>(LogTextBox);
        if (_logScrollViewer is not null)
        {
            _logScrollViewer.ScrollChanged += LogScrollViewer_ScrollChanged;
            _logScrollViewer.PreviewMouseDown += LogScrollViewer_PreviewMouseDown;
            _logScrollViewer.PreviewMouseUp += LogScrollViewer_PreviewMouseUp;
        }

        LogTextBox.PreviewMouseWheel += LogTextBox_PreviewMouseWheel;
        LogTextBox.PreviewKeyDown += LogTextBox_PreviewKeyDown;
    }

    private void LogTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!_isLogFollowingTail || _isLogScrollScheduled) return;

        _isLogScrollScheduled = true;
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                // Do not restore a saved offset here. Replacing the Text binding can
                // change the extent during layout, making an old absolute offset stale.
                if (_isLogFollowingTail)
                    _logScrollViewer?.ScrollToEnd();
            }
            finally
            {
                _isLogScrollScheduled = false;
            }
        }, DispatcherPriority.ContextIdle);
    }

    private void LogScrollViewer_ScrollChanged(object? sender, System.Windows.Controls.ScrollChangedEventArgs e)
    {
        // Text selection can also move the viewport. Only explicit scrolling input
        // should change whether newly appended log entries are followed.
        if (_isLogScrollScheduled || !_isLogUserScrolling || e.ExtentHeightChange != 0 || e.VerticalChange == 0) return;

        _isLogFollowingTail = IsLogAtBottom(e);
    }

    private void LogScrollViewer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (FindVisualAncestor<System.Windows.Controls.Primitives.ScrollBar>(e.OriginalSource as DependencyObject) is not null)
            _isLogUserScrolling = true;
    }

    private void LogScrollViewer_PreviewMouseUp(object sender, MouseButtonEventArgs e) =>
        _isLogUserScrolling = false;

    private void LogTextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _isLogUserScrolling = true;
        ResetLogUserScrollingAfterInput();
    }

    private void LogTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)
        {
            _isLogUserScrolling = true;
            ResetLogUserScrollingAfterInput();
        }
    }

    private void ResetLogUserScrollingAfterInput()
    {
        if (_isLogUserScrollResetScheduled) return;

        _isLogUserScrollResetScheduled = true;
        Dispatcher.BeginInvoke(() =>
        {
            _isLogUserScrollResetScheduled = false;
            _isLogUserScrolling = false;
        }, DispatcherPriority.Background);
    }

    private static bool IsLogAtBottom(System.Windows.Controls.ScrollChangedEventArgs e) =>
        e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 1;

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is T match) return match;
            var descendant = FindVisualChild<T>(child);
            if (descendant is not null) return descendant;
        }

        return null;
    }

    private static T? FindVisualAncestor<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T match) return match;
            child = System.Windows.Media.VisualTreeHelper.GetParent(child);
        }

        return null;
    }
}
