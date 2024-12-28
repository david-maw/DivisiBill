namespace DivisiBill.Services;

internal partial class UserStoppedTypingBehavior : CommunityToolkit.Maui.Behaviors.UserStoppedTypingBehavior
{
    bool bindingWasSet = false;
    protected override void OnAttachedTo(BindableObject bindable)
    {
        if (bindingWasSet = (BindingContext is null))
            SetBinding(BindingContextProperty,
            new Binding
            {
                Path = BindingContextProperty.PropertyName,
                Source = bindable,
            });
        base.OnAttachedTo(bindable);
    }
    protected override void OnDetachingFrom(BindableObject bindable)
    {
        if (bindingWasSet)
            BindingContext = null;
        base.OnDetachingFrom(bindable);
    }
}