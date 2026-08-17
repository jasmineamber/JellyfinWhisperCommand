namespace JellyfinWhisperCommand.Views;

using Microsoft.Win32;

public partial class SettingsPageView : UserControl
{
    public SettingsPageView()
    {
        InitializeComponent();
    }

    private SettingsViewModel? Settings => (DataContext as MainViewModel)?.Settings;

    private void BrowseFile(string propertyName, string filter)
    {
        if (Settings is null) return;
        var dialog = new OpenFileDialog { CheckFileExists = false, Filter = filter };
        dialog.ShowDialog(Window.GetWindow(this));
        if (string.IsNullOrEmpty(dialog.FileName)) return;
        switch (propertyName)
        {
            case nameof(SettingsViewModel.WhisperJavExecutablePath):
                Settings.WhisperJavExecutablePath = dialog.FileName;
                break;
            case nameof(SettingsViewModel.TranslateExecutablePath):
                Settings.TranslateExecutablePath = dialog.FileName;
                break;
            case nameof(SettingsViewModel.TranslateInstructionsFilePath):
                Settings.TranslateInstructionsFilePath = dialog.FileName;
                break;
            case nameof(SettingsViewModel.SeconvExecutablePath):
                Settings.SeconvExecutablePath = dialog.FileName;
                break;
            case nameof(SettingsViewModel.SeconvMultipleReplaceRulesFile):
                Settings.SeconvMultipleReplaceRulesFile = dialog.FileName;
                break;
        }
    }

    private void BrowseFolder(string propertyName, string title)
    {
        if (Settings is null) return;
        var dialog = new OpenFolderDialog { Title = title };
        dialog.ShowDialog(Window.GetWindow(this));
        if (string.IsNullOrEmpty(dialog.FolderName)) return;
        switch (propertyName)
        {
            case nameof(SettingsViewModel.WhisperJavOutputDir):
                Settings.WhisperJavOutputDir = dialog.FolderName;
                break;
            case nameof(SettingsViewModel.SeconvInputFolder):
                Settings.SeconvInputFolder = dialog.FolderName;
                break;
        }
    }

    private void BrowseWhisperJavExe_Click(object sender, RoutedEventArgs e)
        => BrowseFile(nameof(SettingsViewModel.WhisperJavExecutablePath), "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*");

    private void BrowseWhisperJavOutputDir_Click(object sender, RoutedEventArgs e)
        => BrowseFolder(nameof(SettingsViewModel.WhisperJavOutputDir), "选择输出目录");

    private void BrowseTranslateExe_Click(object sender, RoutedEventArgs e)
        => BrowseFile(nameof(SettingsViewModel.TranslateExecutablePath), "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*");

    private void BrowseTranslateInstructions_Click(object sender, RoutedEventArgs e)
        => BrowseFile(nameof(SettingsViewModel.TranslateInstructionsFilePath), "指令文件 (*.txt;*.md;*.json)|*.txt;*.md;*.json|所有文件 (*.*)|*.*");

    private void BrowseSeconvExe_Click(object sender, RoutedEventArgs e)
        => BrowseFile(nameof(SettingsViewModel.SeconvExecutablePath), "可执行文件 (*.exe;*.cmd;*.bat)|*.exe;*.cmd;*.bat|所有文件 (*.*)|*.*");

    private void BrowseSeconvRules_Click(object sender, RoutedEventArgs e)
        => BrowseFile(nameof(SettingsViewModel.SeconvMultipleReplaceRulesFile), "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*");

    private void BrowseSeconvInputFolder_Click(object sender, RoutedEventArgs e)
        => BrowseFolder(nameof(SettingsViewModel.SeconvInputFolder), "选择输入目录");
}