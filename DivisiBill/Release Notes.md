# 6.2.7

## Improved Handling of Long Lists of Items

Lists longer than a page now show up/down buttons to notify off-screen items and allow easy scrolling to them.

Had to add a grid so as to control layout.

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

