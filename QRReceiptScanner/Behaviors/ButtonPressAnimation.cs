namespace QRReceiptScanner.Behaviors;

/// <summary>
/// Attached property that gives a <see cref="Button"/> a short press animation: it
/// shrinks slightly while held down and springs back to full size on release.
/// Applied to every button through the shared style in Styles.xaml.
/// </summary>
public static class ButtonPressAnimation
{
    private const double PressedScale = 0.95;
    private const uint PressMs = 80;
    private const uint ReleaseMs = 140;

    public static readonly BindableProperty IsEnabledProperty = BindableProperty.CreateAttached(
        "IsEnabled",
        typeof(bool),
        typeof(ButtonPressAnimation),
        false,
        propertyChanged: OnIsEnabledChanged);

    public static bool GetIsEnabled(BindableObject view) => (bool)view.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(BindableObject view, bool value) => view.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not Button button)
            return;

        button.Pressed -= OnPressed;
        button.Released -= OnReleased;

        if ((bool)newValue)
        {
            button.Pressed += OnPressed;
            button.Released += OnReleased;
        }
    }

    private static async void OnPressed(object? sender, EventArgs e)
    {
        if (sender is not Button button)
            return;

        button.CancelAnimations();
        await button.ScaleToAsync(PressedScale, PressMs, Easing.CubicOut);
    }

    private static async void OnReleased(object? sender, EventArgs e)
    {
        if (sender is not Button button)
            return;

        button.CancelAnimations();
        await button.ScaleToAsync(1, ReleaseMs, Easing.CubicOut);
    }
}
