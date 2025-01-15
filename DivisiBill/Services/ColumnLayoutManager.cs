//From https://github.com/hartez/CustomLayoutExamples#single-column-layout 
using Microsoft.Maui.Layouts;

namespace DivisiBill.Services;

public class ColumnLayoutManager : ILayoutManager
{
    private readonly ColumnLayout _columnLayout;
    private IGridLayout _gridLayout;
    private GridLayoutManager _manager;

    public ColumnLayoutManager(ColumnLayout layout) => _columnLayout = layout;

    private IGridLayout ToColumnGrid(VerticalStackLayout stackLayout)
    {
        Grid grid = new LayoutGrid
        {
            ColumnDefinitions = [new ColumnDefinition { Width = GridLength.Star }],
            RowDefinitions = []
        };

        int row = -1;
        for (int childIndex = 0; childIndex < stackLayout.Count; childIndex++)
        {
            var child = stackLayout[childIndex];

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
        _gridLayout = ToColumnGrid(_columnLayout);
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
