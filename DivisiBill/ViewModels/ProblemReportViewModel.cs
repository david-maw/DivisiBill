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
    public partial bool Reported { get; set; } = false;

    [ObservableProperty]
    public partial string DescriptionText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DeletedDescriptionText { get; set; } = string.Empty;

    partial void OnDescriptionTextChanged(string value) => Reported = false;

    [RelayCommand]
    private async Task ReportNow()
    {
        string mealFileName = Meal.CurrentMeal.FileName;
        if (string.IsNullOrWhiteSpace(mealFileName))
            mealFileName = "BadMeal.xml";
        SentrySdk.CaptureMessage(SentryEventProcessor.UserFeedbackTitle, scope =>
        {
            // Attach user information and comments
            scope.AddAttachment(Encoding.Latin1.GetBytes(Utilities.GetAppInformation() + "\n" + DescriptionText), "UserMsg.txt", AttachmentType.Default, "text/plain");
            // Attach an archive of just the current bill
            Archive archive = new([Meal.CurrentMeal], true);
            scope.AddAttachment(archive.AsXmlStream(), "archive-" + mealFileName);
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
        EmailMessage message = new()
        {
            Subject = "DivisiBill Message",
            Body = !Utilities.IsWinUI ? body // Detour an annoying bug where Windows/Outlook truncates longer messages, this text makes that obvious
                    : "*** Start of Message (verify end is also present) ***\n" + body + "\n*** End of Message***\n",
        };
        message.To!.Add("support@autopl.us");
        string? tempFilePath = null;
        try
        {
            // Attach an archive of just the current bill and its image if there is one
            tempFilePath = Meal.CurrentMeal.CreateZipArchive();
            if (tempFilePath is not null)
                message.Attachments!.Add(new EmailAttachment(tempFilePath));
        }
        catch (Exception ex)
        {
            ex.ReportCrash();
        }
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
            ex.ReportCrash();
        }
        finally
        {
            // Now delete the temporary file used for the archive attachment
            if (!string.IsNullOrWhiteSpace(tempFilePath) && File.Exists(tempFilePath))
                File.Delete(tempFilePath);
        }
        await Utilities.DisplayAlertAsync("Issue Reported", "A mail message has been created", "ok");
        await App.GoToRoot(1);
    }

    [RelayCommand]
    private void Clear()
    {
        DeletedDescriptionText = DescriptionText;
        DescriptionText = string.Empty;
        Reported = false;
    }

    [RelayCommand]
    private void RetreiveDeleted()
    {
        DescriptionText += DeletedDescriptionText;
        DeletedDescriptionText = string.Empty;
        Reported = false;
    }
}
