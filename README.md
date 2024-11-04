[![Build Status](https://github.com/david-maw/DivisiBill/actions/workflows/dotnet-build-android.yml/badge.svg)](https://github.com/david-maw/DivisiBill/actions/workflows/dotnet-build-android.yml)

# DivisiBill

DivisiBill is a free .NET MAUI project with an Android production build and a Windows build used for development. It is available in the [Play Store](https://play.google.com/store/apps/details?id=com.autoplus.divisibill). DivisiBill is currently
the only product of [AutoPlus](http://autopl.us). There's more information about the product and the company on the web site [here](https://www.autopl.us/divisibill/index.html) .

DivisiBill lets you define each item on a bill and indicate how it should be divided among guests.Then it calculates how much each guest owes, including tax and tip, or, if you enter the tax or tip amount you want to use, 
it will tell you the corresponding rate.

The app handles dark mode, orientation switching and multiple currencies correctly but has text only in English. It is primarily targeted at the experienced user who wants to minimize the number of operations to get the answer, though it contains extensive help documentation and a simplified "Tutorial Mode" to assist the new user in becoming experienced.

DivisiBill is intended to handle complex bills where you want each person to pay their share.
If all you want to do is split the bill evenly and maybe calculate a tip or tax, there are
lots of free programs available to do that. DivisiBill does not contain advertising but it does
have a couple of optional features that are chargeable, OCR scans of printed bills and cloud based data storage.

It was originally developed in the early 2010's because a group of us went out regularly and some did not drink alcohol or eat meat, so "let's just split the bill evenly" was not very fair. The initial development was for Windows Phones and when they went away towards the end of the decade the code was moved to Xamarin Forms and targeted primarily at Android phones. With the imminent demise of Xamarin in the early 2020's the code was migrated again, this time to .NET MAUI, again primarily targeted at Android phones, but with a Windows build, primarily to simplify development and debug.

The consequence of all this is that the early code is pretty unsophisticated and only later did MVVM (or at least my use of it) come along. So don't be surprised if you see some code that if you're being kind could be called naive... The code evolved along with my experience as a phone coder in C# - several decades working with other languages and server platforms helped, but phone UI programming in C# was a bit of an adventure and the learning curve shows.

If you are interested in working on this project, or just building it for fun, read "Developer Notes.md".
