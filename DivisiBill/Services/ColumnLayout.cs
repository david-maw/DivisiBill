//From https://github.com/hartez/CustomLayoutExamples#single-column-layout 
using Microsoft.Maui.Layouts;

namespace DivisiBill.Services;

public class ColumnLayout : VerticalStackLayout
{
    public static readonly BindableProperty FillProperty = BindableProperty.CreateAttached("Fill", typeof(bool),
        typeof(ColumnLayout), false);

    public static readonly BindableProperty SameRowProperty = BindableProperty.CreateAttached("SameRow", typeof(bool),
        typeof(ColumnLayout), false);

    public ColumnLayout()
    {
    }

    protected override ILayoutManager CreateLayoutManager() => new ColumnLayoutManager(this);

    // Support methods for the attached property
    public static bool GetFill(BindableObject bindableObject) => (bool)bindableObject.GetValue(FillProperty);

    public static void SetFill(BindableObject bindableObject, bool fill) => bindableObject.SetValue(FillProperty, fill);

    // Convenience method for use from the layout manager
    internal static bool IsFillSetForView(IView view) => view is BindableObject bindableObject && bindableObject.IsSet(FillProperty);
    internal static bool GetFillForView(IView view) => view is BindableObject bindableObject && GetFill(bindableObject);


    // Support methods for the attached SameRow property
    public static bool GetSameRow(BindableObject bindableObject) => (bool)bindableObject.GetValue(SameRowProperty);

    public static void SetSameRow(BindableObject bindableObject, bool SameRow) => bindableObject.SetValue(SameRowProperty, SameRow);

    // Convenience method for use from the layout manager
    internal static bool IsSameRowSetForView(IView view) => view is BindableObject bindableObject && bindableObject.IsSet(SameRowProperty);
    internal static bool GetSameRowForView(IView view) => view is BindableObject bindableObject && GetSameRow(bindableObject);
}
