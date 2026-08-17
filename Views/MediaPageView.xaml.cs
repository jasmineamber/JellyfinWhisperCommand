namespace JellyfinWhisperCommand.Views;

public partial class MediaPageView : UserControl
{
    public MediaPageView()
    {
        InitializeComponent();
    }

    public void FocusSearch()
    {
        SearchTextBox.Focus();
        SearchTextBox.SelectAll();
    }
}
