namespace JellyfinWhisperCommand;

public partial class App : Application
{
    public App()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }
}
