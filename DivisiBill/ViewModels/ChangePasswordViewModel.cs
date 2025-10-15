#nullable enable

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DivisiBill.Services;

namespace DivisiBill.ViewModels;

public partial class ChangePasswordViewModel(Action<bool> RequestClose, string? initialCurrentPassword = null) : ObservableObject
{
    [ObservableProperty]
    public partial string? CurrentPassword { get; set; } = initialCurrentPassword;
    [ObservableProperty]
    public partial string? NewPassword { get; set; }
    [ObservableProperty]
    public partial string? ConfirmPassword { get; set; }
    [ObservableProperty]
    public partial bool ShowPasswords { get; set; }
    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }
    [ObservableProperty]
    public partial bool HasStoredPassword { get; set; } = CryptManager.HasStoredPassword;
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [RelayCommand]
    public void Cancel() => RequestClose(false);

    [RelayCommand]
    private async Task ChangeAsync()
    {
        ErrorMessage = string.Empty;
        string current = CurrentPassword ?? string.Empty;
        string next = NewPassword ?? string.Empty;
        string confirm = ConfirmPassword ?? string.Empty;

        bool hasExistingPassword = CryptManager.HasStoredPassword;

        try
        {
            if (!string.Equals(next, confirm, StringComparison.Ordinal))
            {
                ShowError("New passwords do not match.");
                return;
            }

            if (hasExistingPassword)
            {
                if (string.IsNullOrEmpty(current))
                {
                    ShowError("Enter your current password.");
                    return;
                }

                IsBusy = true;
                if (!await CryptManager.VerifyPasswordAgainstStoredAsync(current))
                {
                    ShowError("Current password is incorrect.");
                    return;
                }
            }

            if (string.IsNullOrEmpty(next))
            {
                // Allow no password: clear stored fingerprint, but retain stored RSA keys
                CryptManager.ClearPassword();

                ResetFields();
                RequestClose(true);
                return;
            }

            IsBusy = true;
            await CryptManager.SetPasswordAsync(next);

            ResetFields();
            RequestClose(true);
        }
        catch (Exception ex)
        {
            ShowError($"Failed to update password: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }
    private void ResetFields()
    {
        CurrentPassword = string.Empty;
        NewPassword = string.Empty;
        ConfirmPassword = string.Empty;
    }
    private void ShowError(string message) => ErrorMessage = message;
}
