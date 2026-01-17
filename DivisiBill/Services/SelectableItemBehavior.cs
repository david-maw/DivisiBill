using System.ComponentModel;

namespace DivisiBill.Services;

public static class SelectableItemBehavior
{
    // Attached property to turn this behavior on/off for a given CollectionView
    public static readonly BindableProperty EnableProperty =
        BindableProperty.CreateAttached(
            "Enable",
            typeof(bool),
            typeof(SelectableItemBehavior),
            false,
            propertyChanged: OnEnableChanged);

    public static bool GetEnable(BindableObject obj) =>
        (bool)obj.GetValue(EnableProperty);

    public static void SetEnable(BindableObject obj, bool value) =>
        obj.SetValue(EnableProperty, value);

    private static void OnEnableChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not CollectionView cv)
            return;

        if ((bool)newValue)
            Attach(cv);
        else
            Detach(cv);
    }

    private static void Attach(CollectionView cv)
    {
        // Track selection and ItemTemplate changes while the behavior is enabled
        cv.SelectionChanged += OnSelectionChanged;
        cv.PropertyChanged += OnCollectionViewPropertyChanged;

        // ItemTemplate might already be assigned at this point
        TryWrapItemTemplate(cv);
    }

    private static void Detach(CollectionView cv)
    {
        // Stop tracking when the behavior is disabled, be nice to restore the original DataTemplate but we can't
        cv.SelectionChanged -= OnSelectionChanged;
        cv.PropertyChanged -= OnCollectionViewPropertyChanged;
    }

    private static void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // VisualStateManager handles visuals, this exists just in case it is needed.
    }

    private static void OnCollectionViewPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (sender is not CollectionView cv)
            return;

        // ItemTemplate may be set after the attached property is applied,
        // so watch for it becoming non-null and then wrap it once.
        if (e.PropertyName == CollectionView.ItemTemplateProperty.PropertyName && cv.ItemTemplate is not null)
        {
            cv.PropertyChanged -= OnCollectionViewPropertyChanged; // no longer needed after wrapping
            TryWrapItemTemplate(cv);
        }
    }

    private static void TryWrapItemTemplate(CollectionView cv)
    {
        if (cv.ItemTemplate is not DataTemplate originalTemplate)
            return;

        // Replace the existing template with one that injects visual states
        // into the root element of each item.
        cv.ItemTemplate = new DataTemplate(() =>
        {
            object content = originalTemplate.CreateContent();

            if (content is VisualElement ve)
            {
                InjectVisualStates(ve);
                return ve;
            }

            return content;
        });
    }

    private static void InjectVisualStates(VisualElement root)
    {
        // Add "Normal" and "Selected" visual states to the VisualElement root so
        // CollectionView's VisualStateManager selection logic can style it.
        VisualStateGroupList groups = [];
        VisualStateGroup common = new() { Name = "CommonStates" };

        VisualState normal = new() { Name = "Normal" };
        normal.Setters.Add(new Setter { Property = VisualElement.BackgroundColorProperty, Value = Colors.Transparent });

        // Background color can be overridden via an app-level resource, otherwise a hard-coded fallback is used.
        Color highlightColor = TryGetResource<Color>("SelectedBackGroundColor", Color.FromArgb("f17b01"));

        VisualState selected = new() { Name = "Selected" };
        selected.Setters.Add(new Setter { Property = VisualElement.BackgroundColorProperty, Value = highlightColor });

        common.States.Add(normal);
        common.States.Add(selected);
        groups.Add(common);

        VisualStateManager.SetVisualStateGroups(root, groups);
    }

    private static T TryGetResource<T>(string key, T fallback) =>
        // Look up an application resource by key, falling back if missing or wrong type
        Application.Current?.Resources.TryGetValue(key, out object value) == true &&
            value is T typed
            ? typed
            : fallback;
}
