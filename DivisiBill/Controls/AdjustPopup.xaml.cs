#nullable enable
using CommunityToolkit.Maui.Core.Platform;
using DivisiBill.ViewModels;

namespace DivisiBill.Controls;

public partial class AdjustPopup : CommunityToolkit.Maui.Views.Popup<decimal> 
{
    AdjustViewModel vm;
    public AdjustPopup(AdjustViewModel vm)
	{
		InitializeComponent();
        BindingContext = this.vm = vm;
	}

    private async Task UpdateAndCloseAsync()
    {
        vm.UnloadTargetAmountString();
        vm.AdjustmentAmount = Math.Round(vm.AdjustmentAmount, 2);
        await CloseAsync(vm.AdjustmentAmount);
    }

    private async void OnPopupTapped(object? sender, TappedEventArgs e) => await UpdateAndCloseAsync();
    private async void OnInputViewUnfocused(object? sender, FocusEventArgs e)
    {
        if (sender is InputView inputView)
            await inputView.HideKeyboardAsync();
    }
    private async void OnCompleted(object? sender, EventArgs e)
    {
        (sender as VisualElement)?.Unfocus();
        await UpdateAndCloseAsync();
    }
}