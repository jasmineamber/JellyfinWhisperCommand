namespace JellyfinWhisperCommand;

public partial class MainWindow : Window
{
    private System.Windows.Controls.ScrollViewer? _logScrollViewer;
    private bool _isLogAutoScroll = true;
    private bool _isLogScrollScheduled;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    private void LogTextBox_Loaded(object sender, RoutedEventArgs e)
    {
        _logScrollViewer = FindVisualChild<System.Windows.Controls.ScrollViewer>(LogTextBox);
        if (_logScrollViewer is not null)
            _logScrollViewer.ScrollChanged += LogScrollViewer_ScrollChanged;
    }

    private void LogTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!_isLogAutoScroll || _isLogScrollScheduled) return;

        _isLogScrollScheduled = true;
        Dispatcher.BeginInvoke(() =>
        {
            _isLogScrollScheduled = false;
            if (_isLogAutoScroll)
                LogTextBox.ScrollToEnd();
        }, DispatcherPriority.Render);
    }

    private void LogScrollViewer_ScrollChanged(object? sender, System.Windows.Controls.ScrollChangedEventArgs e)
    {
        // Text updates also change the scrollable extent; retain the user's prior mode in that case.
        if (e.ExtentHeightChange != 0 || e.VerticalChange == 0) return;

        _isLogAutoScroll = IsLogAtBottom(e);
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
}
