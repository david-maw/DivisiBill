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

Prior to this release (November 2024) the DivisiBill sources were private, this moves them into open source. To build the released code you need to define a variety of secrets but the app runs without them. Without the defined secrets OCR and cloud storage are not available but all other features work.

