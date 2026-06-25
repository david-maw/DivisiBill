//From https://github.com/hartez/CustomLayoutExamples#single-column-layout 

using Microsoft.Maui.Layouts;

namespace DivisiBill.Controls;

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
    class ColumnLayoutManager(ColumnLayout layout) : ILayoutManager
    {
        private Grid? _gridLayout;
        private GridLayoutManager? _manager;

        private static Grid ToColumnGrid(VerticalStackLayout stackLayout)
        {
            Grid grid = new LayoutGrid
            {
                ColumnDefinitions = [new ColumnDefinition { Width = GridLength.Star }],
                RowDefinitions = []
            };

            int row = -1;
            for (int childIndex = 0; childIndex < stackLayout.Count; childIndex++)
            {
                IView child = stackLayout[childIndex];

                bool useStar = ColumnLayout.IsFillSetForView(child) ?
                    ColumnLayout.GetFillForView(child) : // it's set, just use it
                    child.GetType() == typeof(CollectionView); // not set, pick a default

                bool sameRow = row >= 0 && ColumnLayout.IsSameRowSetForView(child) && ColumnLayout.GetSameRowForView(child);
                if (!sameRow)
                {
                    row++;
                    grid.RowDefinitions.Add(new RowDefinition { Height = useStar ? GridLength.Star : GridLength.Auto });
                }
                grid.Add(child);
                grid.SetRow(child, row);
            }

            return grid;
        }

        public Size Measure(double widthConstraint, double heightConstraint)
        {
            _gridLayout?.Clear();
            _gridLayout = ToColumnGrid(layout);
            _manager = new GridLayoutManager(_gridLayout);

            return _manager.Measure(widthConstraint, heightConstraint);
        }

        public Size ArrangeChildren(Rect bounds) => _manager?.ArrangeChildren(bounds) ?? Size.Zero;

        private class LayoutGrid : Grid
        {
            protected override void OnChildAdded(Element child)
            {
                // We don't want to actually re-parent the stuff we add to this			
            }

            protected override void OnChildRemoved(Element child, int oldLogicalIndex)
            {
                // Don't do anything here; the base methods will null out Parents, etc., and we don't want that
            }
        }
    }
}
