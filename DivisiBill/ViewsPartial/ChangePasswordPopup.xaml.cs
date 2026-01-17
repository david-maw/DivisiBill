#nullable enable

namespace DivisiBill.ViewsPartial;

public partial class ChangePasswordPopup : CommunityToolkit.Maui.Views.Popup<bool>
{
    public ChangePasswordPopup()
    {
        InitializeComponent();
        ViewModels.ChangePasswordViewModel vm = new(async (bool b) => await CloseAsync(b), null);
        BindingContext = vm;
    }
}