#nullable enable

using DivisiBill.Models;
using DivisiBill.ViewModels;

namespace DivisiBill.Views;

[QueryProperty(nameof(TargetPerson), "TargetPerson")]
public partial class PersonEditPage : ContentPage
{
    private PersonEditViewModel? personEditViewModel;
    private Person? targetPerson;

    public PersonEditPage()
    {
        InitializeComponent();
    }
    public Person? TargetPerson
    {
        get => targetPerson;
        set
        {
            if (targetPerson != value)
            {
                targetPerson = value;
                // If there's a new targetPerson update the view model
                BindingContext = personEditViewModel = TargetPerson is null ? null : new PersonEditViewModel(TargetPerson, async () => await Navigation.PopAsync());
            }
        }
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (TargetPerson is null)
            return;
        if (string.Equals(nicknameEntry.Text, TargetPerson.FirstName, System.StringComparison.OrdinalIgnoreCase))
            nicknameEntry.Text = null;
        await Task.Delay(200);
        firstNameEntry.Focus();
        if (firstNameEntry.Text is not null)
            firstNameEntry.CursorPosition = firstNameEntry.Text.Length;
    }
    // When a field is completed, MAUI will move to the next, this exits if the field has a return type of Done 
    private async void OnCompleted(object sender, System.EventArgs e)
    {
        Entry? entry = sender as Entry;
        if (personEditViewModel is not null && entry?.ReturnType == ReturnType.Done)
            await personEditViewModel.SaveAsync();
    }
}