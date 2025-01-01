using DivisiBill.Models;
using DivisiBill.ViewModels;

namespace DivisiBill.Views;

public partial class TotalsPage : ContentPage
{
    private MealViewModel viewModel;
    public TotalsPage()
    {
        InitializeComponent();
    }
    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel = BindingContext as MealViewModel; // It may have changed
        viewModel.DistributeCostsIfNeeded();
        viewModel.ShowTotalsHint = App.Settings.ShowTotalsHint;
    }

    protected override void OnDisappearing()
    {
        viewModel.ForgetDeletedCosts();
        Meal.RequestSnapshot();
        base.OnDisappearing();
    }
    private async void OnReplaceItem(object sender, EventArgs e)
    {
        PersonCost pc = null;
        if (sender is BindableObject b && b.BindingContext is PersonCost boundPc)
            pc = boundPc;
        else if (sender is ToolbarItem tbi)
            pc = (PersonCost)tbi.CommandParameter;

        if (pc is not null)
        {
            var v = new PeopleListPage(pc);
            v.OnPersonSelected += HandlePersonSelected;
            await Navigation.PushAsync(v);
        }
    }
    private void HandlePersonSelected(Person selectedPerson, PersonCost pc)
    {
        if (pc is null)
        {
            pc = viewModel.CostListAdd(selectedPerson);
            if (pc is null)
                DisplayAlert("Error", "This person cannot be added (probably because they are already in use)", "OK");
        }
        else
            pc.Diner = selectedPerson;
        if (pc is not null)
            CostsListView.ScrollTo(pc);
    }
    public async void OnAddItem(object sender, EventArgs e)
    {
        var v = new PeopleListPage();
        v.OnPersonSelected += HandlePersonSelected;
        await Navigation.PushAsync(v);
    }
}
