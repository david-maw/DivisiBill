﻿//From https://github.com/hartez/CustomLayoutExamples#single-column-layout 
using Microsoft.Maui.Layouts;

namespace DivisiBill.Services;

public class ColumnLayout : VerticalStackLayout
{
    public static readonly BindableProperty FillProperty = BindableProperty.CreateAttached("Fill", typeof(bool),
        typeof(ColumnLayout), false);

    public ColumnLayout()
    {
    }

    protected override ILayoutManager CreateLayoutManager() => new ColumnLayoutManager(this);

    // Support methods for the attached property
    public static bool GetFill(BindableObject bindableObject) => (bool)bindableObject.GetValue(FillProperty);

    public static void SetFill(BindableObject bindableObject, bool fill) => bindableObject.SetValue(FillProperty, fill);

    // Convenience method for use from the layout manager
    internal static bool IsFillSetForView(IView view)
    {
        if (view is BindableObject bindableObject)
        {
            return bindableObject.IsSet(FillProperty);
        }
        return false;
    }
    internal static bool GetFillForView(IView view)
    {
        if (view is BindableObject bindableObject)
        {
            return GetFill(bindableObject);
        }

        return false;
    }
}
