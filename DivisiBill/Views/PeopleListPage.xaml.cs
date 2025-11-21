using DivisiBill.Models;
using DivisiBill.Services;

namespace DivisiBill.Views;

/// <summary>
/// This page lists the available people and is used either when it's directly navigated to or 
/// when a person is added to a bill. In the latter case it is used to select the person to add.
/// </summary>
public partial class PeopleListPage : ContentPage
{
    private readonly ViewModels.PeopleListViewModel context = null;
    public PeopleListPage() : this(null)
    {
    }
    public PeopleListPage(PersonCost personCost = null)
    {
        InitializeComponent();
        context = new ViewModels.PeopleListViewModel(SelectPerson, ShowPerson, personCost);
        BindingContext = context;
        context.ScrollItemsTo = ScrollItemsTo;
    }
    ~PeopleListPage() { context.ScrollItemsTo = null; }

    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        Shell.Current.FlyoutBehavior = Shell.Current.Navigation.NavigationStack.Count > 1 // we got here by navigation
            ? FlyoutBehavior.Disabled
            : FlyoutBehavior.Flyout;
        context.ShowPeopleHint = App.Settings.ShowPeopleHint;
    }

    public delegate void SelectPersonDelegate(Person person, PersonCost personCost);
    public event SelectPersonDelegate OnPersonSelected;

    /// <summary>
    /// Add the selected Person to the current Meal.
    /// If we were called passing an OnPersonSelected function then pop the navigation stack to return to the caller. 
    /// Assumes the Person is not already a participant in the bill.
    /// </summary>
    /// <param name="p">The person to select</param>
    /// <param name="pc">The PersonCost item being replaced (if any)</param>
    private async void SelectPerson(Person p, PersonCost pc)
    {
        if (OnPersonSelected is not null)
        {
            // the caller wants to handle selection so let them do so
            OnPersonSelected(p, pc);
            await Navigation.PopAsync();
        }
        else
        {
            // We were not called from the Totals page, so just add the person at the end
            if (Meal.CurrentMeal.Costs.Count >= LineItem.maxSharers)
                await Utilities.ShowAppSnackBarAsync($"Failed: bill already has {LineItem.maxSharers} sharers");
            else
            {
                Meal.CurrentMeal.CostListAdd(p);
                await Utilities.ShowAppSnackBarAsync($"{p.Nickname} added as a sharer of the current bill");
            }
        }
    }
    private async void ShowPerson(Person p) => await Navigation.PushAsync(new PersonEditPage() { TargetPerson = p });
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        context.ForgetDeletedPeople();
    }
    #region Collection Scrolling
    private void ScrollItemsTo(int index, bool toEnd) // Passed in to viewModel
        => CurrentCollectionView.ScrollTo(index, position: toEnd ? ScrollToPosition.End : ScrollToPosition.Start);
    private void OnCollectionViewScrolled(object sender, ItemsViewScrolledEventArgs e)
    {
        context.FirstVisibleItemIndex = e.FirstVisibleItemIndex;
        context.LastVisibleItemIndex = e.LastVisibleItemIndex;
    }
    #endregion

    //TODO: Remove when https://github.com/dotnet/maui/issues/32332 is fixed
    private void OnDeleteSwipeItemInvoked(object sender, EventArgs e)
    {
        if (sender is SwipeItem si && si.BindingContext is Person p)
        {
            context.DeleteCommand.Execute(p);
        }
    }

    private void OnUseSwipeItemInvoked(object sender, EventArgs e)
    {
        if (sender is SwipeItem si && si.BindingContext is Person p)
        {
            context.UseCommand.Execute(p);
        }
    }
}