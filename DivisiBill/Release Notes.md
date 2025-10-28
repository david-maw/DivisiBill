# 6.2.42

## Fix Minor Upload Bug

Occasionally the same image file was being uploaded twice. Because it's the same it did no harm but it did consume bandwidth unnecessarily so it has been corrected.

# 6.2.41

## Add Encryption Capability For Remote Data

You can now request that remote data be encrypted by adding a password in the cloud settings. This password is used to derive a key that is used to encrypt data before it is sent to the cloud and decrypt it after it is received from the cloud. If you forget your password you will still be able to read your remote data on the system where the password was entered, but forgotten passwords cannot be recovered.

If you change the password, subsequent bills (or images) sent to the cloud will be encrypted with the new password. Previously backed up bills will still be readable (they continue to use the old password but all the encryption keys you use are stored in order to permit recovery).

Your encryption keys are stored only on your device but can be archived and restored if you wish. If you uninstall DivisiBill, or lose access to your device you will lose access to any bills encrypted with a key derived from any password you do not remember. 

# 6.2.40

## Check Licenses Should Ignore "Only on WiFi"

The license check would not be attempted if "Only on WiFi" was set and WiFi was not available. This has been corrected, the license check is done as long as there is any Internet connection, even just a metered phone one.

## Improve Evaluation of Backup Options

If "Allow Cloud Backup" is off the rest of the cloud options are ignored and if "Only With WiFi" is turned on, then "Images only With WiFi" is turned on as well.

# 6.2.39

## Add Local Bills to Remote List

Bills that were stored both locally and remotely were not being added to the remote list so if the user chose to hide local meals and show remote ones the bills that were both local and remote did not show up, this has been corrected.

## Download Required Remote Image When Opening a Remote Bill

If a remote bill is made the current one and it has only a remotely stored image both the bill and the image will be downloaded.

# 6.2.38

## Option to Upload Images with Bills

A new option on the "Settings" page allows bill images to be uploaded to the cloud along with bills. This is off by default because it can use a lot of bandwidth and storage.
When this option is on, any bill that has an image and is uploaded to the cloud will have its image uploaded as well. If a bill is downloaded from the cloud and it has an image stored in the cloud that image will be downloaded along with the bill.

Images are typically much larger than bills (a few hundred kB vs. a few kB) so there is an option to only upload images when WiFi is available. Actual costs vary, but as an example uploading using the Google Fi phone network costs $10 per GB, so you could typically upload hundreds of bills for a penny but only a few images.

## Only Scroll to The Current Bill if it has Changed

After selecting a different current bill, a bill list will scroll to keep the current bill in view. The scrolling used to be done always.

## Improve Venue Edit Page

The app displays a message showing a count of bills using the venue only if the venue name has been changed.

## Update Bill List When Location Changes

When the app location changes by a significant amount (more than 20m currently) the distances to venues reported in the bill list are updated. This was being done in the list sorted by distance, but not otherwise, that has been corrected and now all bill lists are updated, regardless of how they are sorted.

# 6.2.37

## Show Release Notes at Startup

If this is the first use of a new version of DivisiBill the release notes page will be shown after startup instead of the items page.

# 6.2.36

## Show Full Page

DivisiBill once again covers the entire page, including the device status bar at the top of the page. You can swipe down from the top margin to see the device status bar temporarily.

## Enhanced Archive Files

All bill archives are now created within zip compressed containers which typically reduces their size by a factor of 10.

There's now an option on the "Manage Data" page to allow bill images to be archived along with bills.

Archive restore from a zip container will now restore the first XML file and search the container for JPG images of any bills restored from that XML file.

## Show a "Current" Icon in Venue List

The venue for the current bill is now indicated by an icon in a venue list.

## Make properties Shell Selection Navigable

The "Properties" selection now allows use of the hamburger menu to navigate away from it. The Properties page shown by tapping the venue name on the "Items" or "Totals" pages returns only to those pages, as before.

## Always Allow Appropriate Page Navigation

Occasionally, the hamburger icon would be hidden when it should be present. This has been corrected so that the hamburger icon is always shown when it is appropriate to navigate to another page.

# 6.2.35

## Update Meal Group Venue Location When It Changes

If a venue location was updated and meals grouped by venue were being shown the location shown with the venue would not be updated, it is now.

## Correct 'Save' Action on Properties Page

Previously tapping the 'Save' icon was not correctly saving any image file associated with the bill, or entering the newly saved bill into the bill list. These errors have been corrected.

# 6.2.34

## Double Tap To Edit Venue

In a bill list that is grouped by venue, double tapping on a venue name will open the venue edit page for that venue.

# 6.2.33

## Add confirmation for renaming in-use venues

You are prompted for confirmation before renaming a venue that is currently in use.

## Reorder Affected Lists When a Venue Location Changes

When a venue location changes, the list of bills by distance was not being updated; this has been corrected.
    
## Always Allow Diner Undelete

Rarely, a stored bill would have non-contiguous DinerIDs, for example 1 and 6. In this case, the dinerID would be shown on the assignment button, which was unnecessary and confusing. If deleted, the diner could not be undeleted. These problems have been corrected.

# 6.2.32
    
## Make Current Bill Visible

When you navigate to a list of bills we scroll the current bill into view (or the current venue if bills are grouped by venue).

## Allow Bill Groups to be Deselected

Tapping a selected bill in the center already deselected it, the same action now works when bills are grouped by venue. 

## Add a "Scroll to Current" Bill Command

The bill list pages now have a command to scroll the current bill into view (or the current venue if bills are grouped by venue).

# 6.2.31
    
## Improve meal List User Interface Responsiveness

You can tap in the center of a bill list item to select or deselect it but for the past couple of releases that tap would be mistaken for a swipe initiation. This no longer happens, swipe actions may only be initiated from right or left of center.

# 6.2.30

## Indicate the Current Meal

The bill list now shows an icon to denote the current bill.

## Enhance meal selection Button logic

On Android tapping in the center of a different meal list item would just clear the current selection. This has been corrected and now also selects the new bill.

## Use Explicit Scaling in Scroll Buttons

The scroll button size change when tapped is now more obvious.

# 6.2.29

## Use Consistent Scroll Buttons

Any list that could get longer than a single page (items, bills, people, venues, people lists and venue lists) is scrollable and has indicators (an up or down arrow in a circle) showing the direction in which there are unseen elements. The indicators also act as buttons and can be tapped or long pressed to scroll up or down a page or move to the ends of the list.

These buttons used to be different on some lists, now they look and work identically across all lists and provide visual feedback when used.

# 6.2.28

## Show a Few Bills Per Group

When grouping bills by venue on a bill list page only the first few bills for each venue are shown.

To see all the bills for a venue you can tap to expand the venue heading to see the most recent bills. If there are too many to show conveniently there will be an "all bills" button at the end of the short list that you can tap to open a new page containing all the bills for that venue in creation order (newest first).

## Improved Bill List Layout

The individual entries in a bill list are laid out slightly more neatly, especially when the list is grouped by venue name so there is no venue name on individual entries.

# 6.2.27

## Improve Scrolling Bill List

When scrolling the list of bills using the scroll buttons they are hidden and no scrolling animation is used. When the scroll completes the appropriate button or buttons reappear. Scrolling to the beginning or end of the list using a long press on one of the buttons is now much quicker. Scrolling using swipe up or down is unchanged.

The scroll buttons themselves are larger and surrounded by a semi-transparent area.

# 6.2.26

## Redefine Distances that are 'Close'

When showing distances smaller ones are simply shown as 'close'. The threshold for 'close' was 100m, now it is 10m because GPS can be quite accurate and manual entry even more so.

## Allow Long press to Scroll to Beginning/End

A long press to scroll to the beginning or end of a list was already allowed on the Bill list pages, now it is allowed for the people list and venue list too.

## Ask Before Creating a Venue

If you double tap the venue name on a bill properties page it takes you to the venue definition. The application used to create a venue record of there wasn't one, now it asks first and if it does create one for the venue it has no default location.

## Show Meal List Download Icons

When cloud access was turned on as a result of a request to show remote bills the remote/local status icons on each bill were not shown. This has been corrected.

# 6.2.25

## Change Scroll to Start/End of Bill List

Because a double tap did not work wall, scrolling to the start or end of the list is now triggered by a long press of the scroll buttons.

## Scroll to Moved Meal Group

When a meal group is moved (because it is in a list sorted by date and the meal with that date is deleted or a newer one added) the list will scroll to the new location so as to keep the group in view.

## Collapse Bill Group When Another is Selected

When you are viewing a list of bills grouped together by venue and you click to expand a group the currently expanded group will be collapsed automatically

## Change Sharing now includes even Sharing

On the items page you may change the current sharing of an item by tapping in the left margin or by selecting the item, using the menu for the page and selecting 'Change'. It used to alternate between no sharing and sharing proportionally, now it includes sharing equally as well.


## Go to Page after restore completion

The notification for restore completion is replaced with automatic navigation to the Bill List by Age Page.

## Count Deleted Bills Correctly

If multiple bills are selected for deletion but one of them is the current bill it will not be deleted. It was incorrectly being counted as having been deleted.

# 6.2.24

## Improvements to Handling Lists of Bills

When 'Show Venues' is selected on a list of bills, the list changes to a list of Venues. Tap on a venue to see a list of bills for that venue.

## Allow Scroll to Beginning or End of Bill List

The Scroll Up/Down buttons on the list of bills now accept a double tap to scroll to the beginning or end of the list respectively.

## Handle Empty Venue Name in Stored Bills

Some older versions of DivisiBill could create bills with no stored venue name, These now show up as "Unknown Venue".

# 6.2.23

## Warn The user if No Bills are to Be Archived

An attempt to archive with no bills selected now produces an error message rather than just creating an archive with no bills in it.

# 6.2.22

## Check for Image File Presence

When restoring a bill from an archive or downloading it from the cloud the application now checks for the presence of an image or a deleted image.

## Always Send user Feedback

User initiated feedback is sent regardless of telemetry settings since the intent is clear.

##  Handle Retrying Web Service Call

After a web service call failed but a retry succeeded the app would behave as if the call had failed. This has been corrected.

## Archive Selected Bills

It is now possible to select a set of bills on a "Bill List" page and then go to the "Manage Data" page and request that only the selected bills be archived.

# 6.2.21

## Restoring an Archive Checks Remote

Restoring an archive now checks whether restored bills are backed up to the cloud and either marks them as being available remotely or initiates a backup of those which are not.

## Correct Remote Bill Access

Bills stored in the cloud were inaccessible in 6.2.20, this has been corrected.

# 6.2.20

## Slow Web Service Calls

A progress bar has been added to the popup used to report slow web service calls. The progress bar shows how much time remains before abandoning the current request and trying again. 

An intermittent problem where the timeout on a call was ignored has been corrected.

## Improve People List Help

The image truncation was confusing - it has been made more obvious that the image is truncated.

# 6.2.19

## Improved UI on Settings and Properties

Most occurrences of a switch have been replaced by a checkbox and some minor reformatting has been done on the Settings and Properties pages.

# 6.2.18

## Change Cloud Timeout to 30s

If the web service is not responsive the app waits 30s before giving up instead of waiting 100s as it did before.

## Counting Improvements

The dialog that pops up when waiting for a result or to retry a call to a a web service has a countdown to retry and a count of seconds since the last try. With this change the visible count is not updated when the app is in the background (although the timer remains active and will resume at the correct time). Timing accuracy has also been improved. 

# 6.2.17

## Enhance Archiving with Explicit Related Item Filtering

A new "Only Related People and Venues" checkbox is added to the data management page. It allows you to limit archived or restored people and venues to just those relating to the bills to be archived or restored.

## Enhance app lifecycle handling and web service retry logic

If you switch away from the app while it is trying to connect to the web service it will now pause the connection attempt and resume it when you return to the app. If the connection attempt fails it will retry it after a short delay.

## Improve Format of "Telemetry" Popup

During initialization DivisiBill may ask the user whether they wish to send telemetry in the event of a failure. The appearance of this dialog has been improved.

# 6.2.16

## Initial Popup For license Check

When DivisiBill starts it checks a web service for licensing, if this check did not quickly succeed it would display a series of questions. Now it shows a popup while it retries errors and/or waits for responses. the popup closes automatically if a good response is received or you elect to continue without waiting for licenses.

# 6.2.15

## Correct OCR License Purchase Message

When purchasing additional scans the message showed an incorrect number remaining, this has been corrected.

## Remove Extra message Buying a Subscription

Purchasing a subscription showed two messages after switching back from the "purchase a subscription" UI. Now only one is shown.

## Update OCR Scan Messages for Clarity

Replaced "OCR license" with "scan" in user alerts to better reflect the functionality for consistency and clarity.

# 6.2.14

## Change Licensing Help to a Hyperlinked Text

The Settings page now has text to tap to open up the help text licensing page and the licensing user interface has been simplified.

## Reference to Default Coupon After Tax

The Settings page was referring to the Coupon After Tax setting for the current bill rather than the default value for all new bills. this has been corrected.

# 6.2.13

## Add Licensing Help Button

The Settings page now has a button to open up the help text licensing page so you can get more information on licensing before purchasing a license or subscription.

# 6.2.12

## Camera Availability Checks

The app now explicitly handles the case where no camera is present (or where the camera permission has been denied by the user).

# 6.2.11

## Implement Scroll Up/Down Indicator Buttons for Long Lists

Wherever a list is longer than a page an indicator button shows at the top and/or bottom of the visible items to indicate that there are more items off-screen in that direction. Either scroll the list or click the button to see more items. This applies to all but the totals list (which is too short to need it). 

## Clarify 'Continue' Choice

Make it clear that if there's a problem accessing the web service the 'continue' option always means 'continue without a license'.

# 6.2.10

## Load Rounded Amount in Existing Bill

The Rounded Amount for a newly loaded bill could show up a zero instead of its actual value. This also distorted calculations of the payment amount. This has been corrected.

# 6.2.9

## Increase the Data Entry Wait Time

DivisiBill waits for the user to stop providing input during data entry before attempting to format the text (if it is a number) or update other items based on what's entered. This happens on the properties, items and settings pages.  This delay has been increased, it is possible to signal 'done' immediately by pressing the 'enter' key (which may be labeled 'done' or simply have a symbol on it) if you do not wish to wait.

# 6.2.8

## Handle Tip Delta Correctly

A tip delta is used generally because you want a specific tip amount and it's not an exact 1/4 percent multiple of the bill subtotal. If one was in use Divisibill did not correctly divide it among participants and reported an error "Oops, rounding error...". This has been corrected.

## Rounded Amount = $0 in Error

Occasionally a change to the rounded amount was not noticed and it showed as $0, this has been corrected.

## Give a Newly Created Venue a Location

A newly created venue now gets the current location by default. The location can be cleared by double tapping on it, or changed by single tapping.

## Allow OCR After Camera

If you went directly to the camera page to take a picture of a bill to create an image the OCR button on the image page did not work. This has been corrected by ensuring that the OCR button also saves the current image if necessary.

## Improve Properties Page User Interface

The font size has been increased slightly and when an entry field is selected it is enlarged further to make data entry easier.

If you press enter after typing a value on an on-screen-keyboard the keyboard will be dismissed and focus will be removed from the field.

The Tip Delta amount is the difference between the calculated tip percentage and the actual tip amount. It is now recalculated along with the tip rate whenever the tip amount is changed, and zeroed when the tip rate is changed. It is shown with a 'non default' notation if it is set.

# 6.2.7

## Improved Handling of Long Lists of Items

Lists longer than a page now show up/down buttons to notify off-screen items and allow easy scrolling to them.

## Item Count on Bill Summaries

Each bill summary now includes an item count.

## Delete Correct Image

When deleting a bill the image for the current bill was being deleted instead of the image corresponding to the bill being deleted. This has been corrected.

## Help Updated

The help files have been updated to be consistent with version 6.2.7.

# 6.2.6

## Allow Multiple Bill Deletion on Dummy Bills

A brand new install creates dummy bills and because they were not stored locally the multi-bill select did not delete them. This has been corrected.

## Retain Current Image During Undelete

An undelete image operation on a bill with a current image as well as a deleted one just swaps the two images, so that a second undelete puts back the original one. Note that an image you just selected or created on the image page does not count as a new one until you exit the page.

## Always Permit Item Add While Filtering

The app would fault if you attempted to add an item while the selected item was not visible because it was filtered out.

# 6.2.5

## Show a 'Has Image' Indication in Bill List

Lists of bills now include an indication whether or not each bill has a stored image associated with it. If the bill has a stored image ten at the end of each entry (by the cloud/local icons) is a new icon.

## Undelete Image Permitted

The details page shown when you double tap a bill in the list now shows whether a bill has a recoverable image. If such a bill is selected the Image page now permits its deleted image to be recovered by using the "Undelete" menu selection (or using the Undelete button shown if there is no current image).

## Deleting an Image Creates a New Bill

Deleting an image from an old bill should have created a new one but instead it was simply deleting the image from the existing bill. This has been corrected and a new bill is now created rather than modifying the existing one.

# 6.2.4

## Speed Up Initial Bill Evaluation

In parallel with initialization the app evaluates whether each local bill is stored in the cloud in order to show this status. To do this it downloads names for all the cloud bills. It used to do this 100 at a time, now it does 1000 at a time which can be considerably faster (1s vs. 6s for 3000 bills in testing, likely faster in production depending on your Internet speed).

Once the evaluation described above completes (typically in a few seconds), the status of individual bills is accurately displayed.

## Hide Cloud/Local Icons in Bill List

The local/remote icons in the bill list are not useful if cloud access is not available, so we hide them in that case. For example, when the user does not permit cloud access or cloud access is not available.

## Improved Download

When downloading bills completes the message displayed now shows the number of downloads that succeeded and/or failed.

Canceling a download is now more reliable.

Rather than showing a busy indicator for the whole page during bill download we show one for each bill until it is downloaded.

## Improved Bill Deletion

If deleting multiple bills takes significant time or if some of the deletions fail (perhaps because the process was canceled) a status message is shown.

When deleting a single bill, if it is the selected bill then we select an alternate (ideally the next bill in the list).

## Improved Undelete

When undeleting bills into a list sorted by name or distance the undeleted bill could be inserted in the wrong place in the list.

When undeleting venues into a list sorted by distance the undeleted venue could be inserted in the wrong place in the list.

These problems have been corrected.

# 6.2.3

## Notify User if Archive to Disk Fails

Previously it would fail silently, now it displays a message.

## Do Not Wait After Reporting Pro License

When a pro license is discovered where previously there had been none a message is displayed. We no longer wait solely for the user to acknowledge that message before continuing, it disappears on its own after a few seconds. 

## Upgrade to .NET 9

The build and various dependent libraries are upgraded to .NET 9. This should make no functional difference except that a few actions will be faster.

# 6.2.2

Experimental upgrade to .NET 9. No user visible changes.

# 6.2.1

## Add an Option to Show Web Service Information

The "About" page (a tab on the "Information and Problems" page) now has a "Show Web Service Information" checkbox.

If checked it shows the base URL used to reach the web service and details about the service itself if they are available.

## Add Swipe Menu Choices to Items Page

Swipe up and down through a long list of items is unreliable so this introduces command menu alternatives.

# 6.2.0

## First Open Source Release

Prior to this release (November 2024) the DivisiBill sources were private, this moves them into open source on GitHub [here](https://github.com/david-maw/DivisiBill). To build the released code you need to define a variety of secrets but the app runs without them. Without the defined secrets OCR and cloud storage are not available but all other features work.

