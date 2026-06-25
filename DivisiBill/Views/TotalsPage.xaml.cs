using DivisiBill.Models;
using DivisiBill.Services;
using DivisiBill.ViewModels;

namespace DivisiBill.Views;

public partial class TotalsPage : ContentPage
{
    private MealViewModel viewModel;
    public TotalsPage()
    {
        InitializeComponent();
        viewModel = BindingContext is MealViewModel vm
            ? vm
            : throw new InvalidOperationException("TotalsPage must have a MealViewModel as its BindingContext");
    }

    protected override void OnAppearing()
    {
        Utilities.DebugMsg("In TotalsPage.OnAppearing");
        base.OnAppearing();
        viewModel = BindingContext is MealViewModel vm
            ? vm // It may have changed since the page was created, so we need to check again
            : throw new InvalidOperationException("TotalsPage must have a MealViewModel as its BindingContext");
        viewModel.DistributeCostsIfNeeded();
        viewModel.ShowTotalsHint = App.Settings.ShowTotalsHint;
    }

    protected override void OnDisappearing()
    {
        viewModel.ForgetDeletedCosts();
        Meal.RequestSnapshot();
        base.OnDisappearing();
    }

    // Because add and replace item actions need to open a new page they were historically forced to run
    // in the code behind. A more modern solution would be to put them in the view model and use Shell navigation.
    private async void OnReplaceItem(object? sender, EventArgs e)
    {
        PersonCost? pc = null;
        if (sender is BindableObject b && b.BindingContext is PersonCost boundPc)
            pc = boundPc;
        else if (sender is ToolbarItem tbi)
            pc = (PersonCost)tbi.CommandParameter;

        if (pc is not null)
        {
            PeopleListPage v = new(pc);
            v.OnPersonSelected += HandlePersonSelected;
            await Navigation.PushAsync(v);
        }
    }
    private void HandlePersonSelected(Person selectedPerson, PersonCost? pc)
    {
        if (pc is null)
        {
            pc = viewModel.CostListAdd(selectedPerson);
            if (pc is null)
                DisplayAlertAsync("Error", "This person cannot be added (probably because they are already in use)", "OK");
        }
        else
            pc.Diner = selectedPerson;
        if (pc is not null)
            CostsListView.ScrollTo(pc);
    }
    public async void OnAddItem(object? sender, EventArgs e)
    {
        if (viewModel.Costs.Count >= LineItem.maxSharers) // We need one empty slot for temporary storage
        {
            await Utilities.DisplayAlertAsync("Error", $"Sorry, you may not add more than {LineItem.maxSharers} participants");
            return;
        }
        PeopleListPage v = new();
        v.OnPersonSelected += HandlePersonSelected;
        await Navigation.PushAsync(v);
    }

    private void OnPersonDrop(object? _, DropEventArgs e)
    {
        e.Handled = true; // Inhibit default MAUI handling, see https://github.com/dotnet/maui/issues/35599
    }
}
