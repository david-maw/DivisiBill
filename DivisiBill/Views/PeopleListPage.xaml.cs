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
    private FlyoutBehavior savedFlyoutBehavior;
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

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Task.Delay(100); // nasty kludge to allow time for navigation to complete
        savedFlyoutBehavior = Shell.Current.FlyoutBehavior;
        if (Navigation.NavigationStack.Count > 1) // we got here by navigation
            Shell.Current.FlyoutBehavior = FlyoutBehavior.Disabled;
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
        Shell.Current.FlyoutBehavior = savedFlyoutBehavior;
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
}