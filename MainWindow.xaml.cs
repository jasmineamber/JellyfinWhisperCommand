namespace JellyfinWhisperCommand;

public partial class MainWindow : Window
{
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
}
