namespace JellyfinWhisperCommand.Controls;

public static class PasswordBoxBinding
{
    public static readonly DependencyProperty BoundPasswordProperty = DependencyProperty.RegisterAttached(
        "BoundPassword", typeof(string), typeof(PasswordBoxBinding),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnBoundPasswordChanged));

    private static readonly DependencyProperty IsUpdatingProperty = DependencyProperty.RegisterAttached(
        "IsUpdating", typeof(bool), typeof(PasswordBoxBinding));

    public static string GetBoundPassword(DependencyObject target) => (string)target.GetValue(BoundPasswordProperty);
    public static void SetBoundPassword(DependencyObject target, string value) => target.SetValue(BoundPasswordProperty, value);

    private static bool GetIsUpdating(DependencyObject target) => (bool)target.GetValue(IsUpdatingProperty);
    private static void SetIsUpdating(DependencyObject target, bool value) => target.SetValue(IsUpdatingProperty, value);

    private static void OnBoundPasswordChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not PasswordBox passwordBox || GetIsUpdating(passwordBox)) return;
        passwordBox.PasswordChanged -= OnPasswordChanged;
        passwordBox.Password = (string)e.NewValue;
        passwordBox.PasswordChanged += OnPasswordChanged;
    }

    private static void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        var passwordBox = (PasswordBox)sender;
        SetIsUpdating(passwordBox, true);
        SetBoundPassword(passwordBox, passwordBox.Password);
        SetIsUpdating(passwordBox, false);
    }
}
