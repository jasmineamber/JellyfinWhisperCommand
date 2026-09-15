namespace JellyfinWhisperCommand;

using System.Windows.Media;
using JellyfinWhisperCommand.Views;

public partial class MainWindow : Window
{
    private bool _isCloseConfirmed;
    private bool _isClosingAfterTaskStop;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || !viewModel.HasFailedTranslationTasks)
            return;

        var count = viewModel.FailedTranslationTaskCount;
        var result = MessageBox.Show(
            $"检测到 {count} 个上次翻译失败的任务。\n\n是否现在重新执行这些任务？\n将从翻译阶段开始，并继续执行后处理。",
            "发现翻译失败任务",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
            return;

        await viewModel.RetryFailedTranslationsOnStartupAsync();
    }

    private void DrawerScrim_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel viewModel) viewModel.IsLogDrawerOpen = false;
    }

    private void ViewLogFile_Click(object sender, RoutedEventArgs e)
    {
        var logPath = Path.Combine(AppContext.BaseDirectory, "execution.log");
        if (!File.Exists(logPath))
        {
            MessageBox.Show($"日志文件尚未创建：{logPath}", "日志", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Process.Start(new ProcessStartInfo(logPath) { UseShellExecute = true });
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;

        if (e.Key == Key.Escape && viewModel.IsLogDrawerOpen)
        {
            viewModel.IsLogDrawerOpen = false;
            e.Handled = true;
            return;
        }

        if (!viewModel.IsMediaPage) return;

        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var editing = Keyboard.FocusedElement is TextBox or ComboBox or PasswordBox;

        if (ctrl && e.Key == Key.F)
        {
            FindVisualChild<MediaPageView>(this)?.FocusSearch();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.A && !editing)
        {
            viewModel.SelectAllCommand.Execute(null);
            e.Handled = true;
        }
        else if (!ctrl && e.Key == Key.Delete && !editing)
        {
            viewModel.ClearSelectionCommand.Execute(null);
            e.Handled = true;
        }
        else if (!ctrl && e.Key == Key.Enter && !editing)
        {
            viewModel.GenerateCommand.Execute(null);
            e.Handled = true;
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            if (FindVisualChild<T>(child) is { } found) return found;
        }
        return null;
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
}
