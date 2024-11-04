#nullable enable
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DivisiBill.Models;
using DivisiBill.Services;
using Sentry;
using System.Text;

namespace DivisiBill.ViewModels;

internal partial class ProblemReportViewModel : ObservableObject
{
    [ObservableProperty]
    private bool reported = false;

    [ObservableProperty]
    private string descriptionText = string.Empty;
    partial void OnDescriptionTextChanged(string value) => Reported = false;
    
    [RelayCommand]
    private async Task ReportNow()
    {
        string mealFileName = Meal.CurrentMeal.FileName;
        if (string.IsNullOrWhiteSpace(mealFileName))
            mealFileName = "BadMeal.xml";
        SentrySdk.CaptureMessage("User Feedback", scope => {
            // Attach user information and comments
            scope.AddAttachment(Encoding.Latin1.GetBytes(Utilities.GetAppInformation() + "\n" + DescriptionText), "UserMsg.txt", AttachmentType.Default, "text/plain");
            // Attach a copy of the bill if there is one
            if (File.Exists(Meal.CurrentMeal.FilePath))
                scope.AddAttachment(Meal.CurrentMeal.FilePath);
            // Attach a copy of the bill image if there is one
            if (Meal.CurrentMeal.HasImage && File.Exists(Meal.CurrentMeal.ImagePath))
                scope.AddAttachment(Meal.CurrentMeal.ImagePath);
            });
        Reported = true;
        await Utilities.DisplayAlertAsync("Problem Reported", "Your problem has been reported to DivisiBill support", "ok");
        await App.GoToRoot(1);
    }

    [RelayCommand]
    private async Task ReportMail()
    {
        string body = Utilities.GetAppInformation() + "\n" + DescriptionText;
        var message = new EmailMessage
        {
            Subject = "DivisiBill Message",
            Body = !Utilities.IsUWP ? body // Detour an annoying bug where UWP/Windows/Outlook truncates longer messages, this text makes that obvious
                    : "*** Start of Message (verify end is also present) ***\n" + body + "\n*** End of Message***\n",
        };
        message.To!.Add("support@autopl.us"); 
        // Attach a copy of the bill
        if (File.Exists(Meal.CurrentMeal.FilePath))
            message.Attachments!.Add(new EmailAttachment(Meal.CurrentMeal.FilePath));
        // Attach a copy of the bill image if there is one
        if (Meal.CurrentMeal.HasImage && File.Exists(Meal.CurrentMeal.ImagePath))
            message.Attachments!.Add(new EmailAttachment(Meal.CurrentMeal.ImagePath));
        // Send the message
        try
        {
            await Email.ComposeAsync(message);
            Reported = true;
        }
        catch (FeatureNotSupportedException)
        {
            await Utilities.DisplayAlertAsync("Failed", "This device does not support email", "ok");
        }
        catch (Exception ex)
        {
            Utilities.ReportCrash(ex);
        }
        // Now delete the temporary file used for attachment
        await Utilities.DisplayAlertAsync("Issue Reported", "Your mail has been sent", "ok");
        await App.GoToRoot(1);
    }
    
    [RelayCommand]
    private void Clear()
    {
        DescriptionText = string.Empty;
        Reported = false;
    }
}
