namespace DivisiBill.Controls;

/// <summary>
/// Provides automatic icon opacity management for toolbar items.
/// Icons automatically adjust their opacity based on the IsEnabled andCommand.CanExecute state.
/// </summary>
public static class IconState
{
    /// <summary>
    /// Attached property that enables automatic opacity adjustment for icons.
    /// When set to true on an ImageSource, the icon will automatically update its opacity
    /// based on the parent control's IsEnabled and Command.CanExecute state.
    /// </summary>
    public static readonly BindableProperty AutoGrayOutProperty =
        BindableProperty.CreateAttached(
            "AutoGrayOut",
            typeof(bool),
            typeof(IconState),
            false,
            propertyChanged: OnAutoGrayOutChanged);

    public static bool GetAutoGrayOut(BindableObject obj) =>
        (bool)obj.GetValue(AutoGrayOutProperty);

    public static void SetAutoGrayOut(BindableObject obj, bool value) =>
        obj.SetValue(AutoGrayOutProperty, value);

    /// <summary>
    /// Called when the AutoGrayOut property changes. Sets up monitoring for parent changes.
    /// </summary>
    private static void OnAutoGrayOutChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not ImageSource icon || newValue is not true)
            return;

        // Subscribe to parent changes to detect when the icon is added to a ToolbarItem
        if (icon is Element element)
        {
            element.ParentChanged += OnParentChanged;
        }
    }

    /// <summary>
    /// Called when the icon's parent changes. Sets up command and IsEnabled monitoring on the ToolbarItem
    /// and subscribes to Command and IsEnabled property changes to handle changes after parent assignment.
    /// </summary>
    private static void OnParentChanged(object sender, EventArgs e)
    {
        if (sender is not Element element)
            return;

        if (element.Parent is ToolbarItem toolbarItem)
        {
            SetupMonitoring(element as ImageSource, toolbarItem);
            // Monitor for Command and IsEnabled property changes
            toolbarItem.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(ToolbarItem.Command))
                    SetupMonitoring(element as ImageSource, toolbarItem);
                else if (args.PropertyName == nameof(ToolbarItem.IsEnabled))
                    UpdateOpacityForToolbarItem(element as ImageSource, toolbarItem);
            };
        }
    }

    /// <summary>
    /// Sets up command and IsEnabled monitoring for a ToolbarItem. Updates the icon opacity immediately
    /// and subscribes to CanExecuteChanged events for future updates.
    /// </summary>
    private static void SetupMonitoring(ImageSource icon, ToolbarItem toolbarItem)
    {
        if (toolbarItem.Command == null)
            return;

        // Set initial opacity based on current command and IsEnabled state
        UpdateOpacityForToolbarItem(icon, toolbarItem);

        // Subscribe to command state changes
        toolbarItem.Command.CanExecuteChanged += (_, __) =>
        {
            UpdateOpacityForToolbarItem(icon, toolbarItem);
        };
    }

    /// <summary>
    /// Updates opacity for a ToolbarItem based on both Command.CanExecute and IsEnabled state.
    /// Note: ToolbarItem doesn't support opacity, so we use color changes instead.
    /// </summary>
    private static void UpdateOpacityForToolbarItem(ImageSource icon, ToolbarItem toolbarItem)
    {
        bool canExecute = toolbarItem.Command?.CanExecute(null) ?? true;
        bool isEnabled = toolbarItem.IsEnabled;
        UpdateOpacityForToolbarItemIcon(icon, canExecute && isEnabled);
    }

    /// <summary>
    /// Updates the icon color for ToolbarItem based on the enabled state.
    /// ToolbarItem doesn't support opacity on the element itself, so we simulate it by adjusting the alpha channel.
    /// Preserves the icon's original XAML-defined color and applies opacity via alpha.
    /// </summary>
    /// <param name="icon">The icon to update</param>
    /// <param name="isEnabled">True if the control is enabled, false otherwise</param>
    private static void UpdateOpacityForToolbarItemIcon(ImageSource icon, bool isEnabled)
    {
        const float enabledOpacity = 1.0f;
        const float disabledOpacity = 0.3f;

        if (icon is FontImageSource font)
        {
            // Preserve the original color defined in XAML, just modify the opacity
            var baseColor = font.Color;
            var opacity = isEnabled ? enabledOpacity : disabledOpacity;
            font.Color = baseColor.WithAlpha(opacity);
        }
    }
}
