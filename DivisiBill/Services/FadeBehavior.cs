using System.ComponentModel;

namespace DivisiBill.Services;

/// <summary>
/// Behavior that automatically fades out a control after a specified duration. Watches the IsVisible property and triggers the fade
/// animation when the element becomes visible. The animation is applied directly to the attached element's opacity.
/// </summary>
public class FadeBehavior : Behavior<VisualElement>
{
    public static readonly BindableProperty DurationMsProperty =
        BindableProperty.Create(
            nameof(DurationMs),
            typeof(int),
            typeof(FadeBehavior),
            3000);

    public int DurationMs
    {
        get => (int)GetValue(DurationMsProperty);
        set => SetValue(DurationMsProperty, value);
    }

    private CancellationTokenSource? fadeTokenSource;
    private VisualElement? element;

    protected override void OnAttachedTo(VisualElement entry)
    {
        element = entry;

        // Listen for IsVisible changes to trigger fade animation
        entry.PropertyChanged += OnElementPropertyChanged;
        base.OnAttachedTo(entry);
    }

    protected override void OnDetachingFrom(VisualElement entry)
    {
        entry.PropertyChanged -= OnElementPropertyChanged;
        fadeTokenSource?.Cancel();
        element = null;
        base.OnDetachingFrom(entry);
    }

    private void OnElementPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "IsVisible")
        {
            if (element?.IsVisible == true)
            {
                // Element became visible, trigger fade
                TriggerFadeAsync();
            }
            else
            {
                // Element became hidden, cancel any ongoing fade
                fadeTokenSource?.Cancel();
            }
        }
    }

    private async void TriggerFadeAsync()
    {
        fadeTokenSource = new CancellationTokenSource();
        var token = fadeTokenSource.Token;

        try
        {
            if (element is null)
                return; // Defensive code, should never happen
            element.Opacity = 1;
            element.IsVisible = true;
            // Wait for the display duration
            await Task.Delay(DurationMs, token);

            if (token.IsCancellationRequested || element is null)
                return;

            // Fade out using platform animation on the element directly
            const uint fadeOutDurationMs = 500;
            var fadeAnimation = new Animation(
                v => element.Opacity = v,
                start: 1.0,
                end: 0.0,
                easing: Easing.Linear);

            fadeAnimation.Commit(
                owner: element,
                name: "FadeOut",
                length: fadeOutDurationMs,
                finished: (v, cancelled) =>
                {
                    element.Opacity = 0;
                    element.IsVisible = false;
                });

            await Task.Delay((int)fadeOutDurationMs, token);
        }
        catch (OperationCanceledException)
        {
            // Fade was cancelled
            element.AbortAnimation("FadeOut"); // Just in case it was running, stop it, does nothing if it was not running
        }
    }
}
