Developer Notes for DivisiBill
==============================

These are comments on topics of interest to DivisiBill 
developers with the most important ones for initial development at the start.

To build a (somewhat limited) version of the app to run on Windows, clone the repository on a 
Windows machine and:

 1. Use `dotnet restore` at a command prompt (use view -> terminal) to restore the 
    NuGet packages for the solution.
 2. Use Visual Studio 2022 to debug it, or
 3. use `dotnet run -f:net8.0-windows10.0.19041.0` at a command prompt to run it

There are several Git branches available but the only well-behaved one is "main". The "alpha" branch is used as part of the release CI/CD process and hops around somewhat randomly and others may come and go but should be considered experimental at best. 

Read on for more information.

Overall Solution Structure
==========================

The DivisiBill solution is primarily targeted at Android phones though it runs
on Windows too, mostly to simplify debugging and development but also to run tests. The only released 
version is for Android. The solution is composed of 3 parts:

## 1) DivisiBill

The is the main program source. It is a .NET MAUI application which was originally
a Windows phone app, migrated to Xamarin, then .NET MAUI. Because of all that 
migration and the time it took to do it (over a decade), the
app is reliable but filled with code of various vintages and quality.

It contains comprehensive embedded help which comes from the 'web' project
described below and uses a web service implemented on Azure Functions to provide optional 
storage and OCR services.

## 2) DivisiBill.Tests

This is a set of MsTest tests for some of the more critical functions in DivisiBill. The 
coverage is minimal, but some of the most complex and fragile code is the code to share out
costs so this tries to exercise it thoroughly.

This allows more focused unit testing than running the whole program does. It runs on Windows as part
of the CI/CD release process. There aren't many unit tests yet.

## 3) web

This is a set of files making up a static web site for [AutoPlus](https://www.autopl.us), 
the bulk of which are DivisiBill help files.

Along with these is a small MAUI app which displays just the DivisiBill help files
so you can easily get a sense of how they'll look in an Android app, for example. 

Most of these files are generated from MarkDown sources with the HTML being auto-generated
by a Visual Studio MarkDown extension (see below).

Some of the files are svg files used to create overlays of page images with clickable hot spots.
You can hand edit these (svg files are XML files) but it's easier to use an editor like Inkscape.

There's also a small amount of javaScript.

Required Visual Studio Extensions
---------------------------------

The Release Notes.html file is generated automatically from Release Notes.md
by using the Markdown Editor NuGet (v1) package and enabling "Generate HTML File".
You don't have to do this, you can just hand edit the HTML, but it's a pain.

Likewise, the AutoPlus web site files in the web project (which include DivisiBill help files) 
are also generated from MarkDown files which use the same MarkDown extension to automatically
generate HTML when saved. 

Not required, but very helpful is the spell checking extension. It can occasionally be
annoying but saves a lot of typos.

Handy Additional Tools
----------------------

Some useful tools in this environment are:

- Inkscape - a graphical SVG editor, for help project interactive screen images.
- GitHub CLI - especially useful for managing secrets.

Building the Solution
---------------------

You should be able to clone the repository and simply restore, build, and run the solution. Most functionality will
work but you'll be missing licensing, cloud storage and OCR functionality because that is provided by the
DivisiBill Web Service. You can run the web service in debug locally using an emulated Azure environment,
but to deploy it in production you'll need to define an Azure Function service and lots of Azure items.

To get a fully functional version of DivisiBill you'll need to define a number of secrets in environment variables.
In Azure they come from the secrets store and YAML maps the secrets into environment variables.
Locally they are simply defined as persistent environment
variables, ether per-user or per-machine (see the DOS SETX command). Here's a summary of them:

| Environment variable         | Usage |
|                              
| DIVISIBILL_WS_URI            | The path to the DivisiBill web service
| DIVISIBILL_WS_URI_RELEASE    | The path to be used in a release build
| DIVISIBILL_WS_KEY            | The authentication token for the DivisiBill web service
| DIVISIBILL_WS_KEY_RELEASE    | The token to be used in a release build
| DIVISIBILL_SENTRY_DSN        | The path to the Sentry application health service
| SENTRY_AUTH_TOKEN            | The authentication token for the Sentry application health service
| DIVISIBILL_BING_MAPS_SECRET  | The authentication token for Bing maps used in Windows Builds
| DIVISIBILL_TEST_PRO_JSON_B64 | A test pro license encoded in Base-64 
| DIVISIBILL_TEST_OCR_JSON_B64 | A test OCR license encoded in Base-64

The easiest way to set these is probably using the SETX program. You can also set them using the Windows UI. 
Just search for "environment" and select "Edit the system environment variables". This will allow you to set 
values either for the current user or the local machine. Don't forget these will not take effect until you start 
a new program, so you may need to restart Visual Studio.

Read through the section on "Build Information and Secrets" and "Automated Build and Deployment" 
below for more information, especially on how and why we use Base-64 encoding and the mechanism 
by which the contents of environment variables is made available at runtime.

Branching strategy
------------------

There are two branches in GitHub and occasionally there are local 
feature branches for in-process stuff.

Here are the branches with well defined functions:

- main - This is the main branch and where new development eventually goes
- alpha - Pushing to this branch causes a build to be generated and sent 
  to the Play Store Closed Testing 'alpha' track

New development is typically done in a feature branch off main and merged into main when it's all done using a 
normal pull request flow. This gives the main branch a detailed record of every change in a very 
granular way. Once the feature branch is merged you can safely delete it
or you can rebase it on main and keep it around for future features.

Releases are created by the repository owner simply by resetting the alpha branch
to point at whatever set of changes are to be released and pushing the 
update to GitHub. Assuming all the secrets are set correctly the result will
be an AAB file released to the Play Store closed testing "Alpha" stream. 

To point another branch to the current one without switching, just
> git branch -f otherbranch currentbranch

And to push the changes to remote 
> git push origin otherbranch

So to trigger an android release from the main branch regardless of what branch you are currently on
the owner can enter:
> git branch -f alpha main
> git push origin alpha   

Building A Single Platform
--------------------------

Sometimes it is handy to build just one platform to check that something compiles.
You can do this by firing up the terminal (view -> terminal) and entering (for example):

>   dotnet build .\DivisiBill\DivisiBill.csproj -c:Debug -f: net8.0-Windowsl0.0.19041.0

or

>   dotnet build .\DivisiBill\DivisiBill.csproj -c:Debug -f: net8.0-Android -t:run


Release Notes and Help
----------------------

Once the set of features for a release is complete, add a description of
the changes to the "Release Notes.md" file (usually in a separate stream with a
pull request). Also, do not forget to update the help files with any new page images and 
explanatory text. 

The VS MarkDown Editor mentioned above will recreate the Release Notes.html file.
Check in these changes (typically calling it "Release Notes for N.N.N").
Do not forget to push these changes to the remote development stream.

Since this is mechanical stuff and unlikely to affect the 
functioning of DivisiBill you can do it before or after testing the development
branch build.

Release Instructions
--------------------

The version should already be correct, but check that it is as you expect
then push changes to the development branch on GitHub.

Pushing the development branch to GitHub does not trigger a build, update the Alpha branch for that.
Assuming it builds cleanly the resulting apk and aab files are pushed to the Google Play Store and will
automatically get pushed to testers. If it looks good it can be promoted to the beta 
(open testing) track or even the production track.

If you're keeping a long running feature branch ("current" for example) then 
it should be rebased against "development" so everything flows forward from
release through development to current and future changes will merge smoothly
(meaning a fast forward merge).

If you made these changes locally, make sure they all get pushed to the 
server, so nothing is lost if the local copy is removed. This means at least
pushing the development and release branches, that should bring 
everything up to date. You can push all branches with

> git push origin --all

Don't forget to push the tag, you can do this using 
    
> git push origin [tag name]

Now is probably a good time to bump the version in the development branch
and perhaps rebase any ongoing feature branch so you don't forget later.

Automated Build and Deployment
------------------------------

Generally a push to GitHub on the Alpha branch initiates a build and deploy to Play store closed testing
using GitHub actions. The chief complexity in the build is dealing
with secrets for the app (see below) and secrets for the build itself.

The deploy secrets are:

| Secret                       | Usage 
|                              
| KEYSTORE_B64                 | The base-64 encoded keystore containing the Android app signing certificate
| KEYSTORE_PASSWORD            | The key store password
| KEYSTORE_PASSWORD_ALIAS      | The signing key password
| SERVICE_ACCOUNT_JSON         | The Android Play Store service account used to upload the signed AAB/APK file

The KEYSTORE... secrets are used to build signed Android AAB and APK files, SERVICE_ACCOUNT_JSON is used to upload
the file that was built to th Play Store. 

The build secrets are a subset of the environment variable names described above, they intentionally have the same 
names as the environment variables they map to, they are:

| Secret                       | Usage 
|                              
| DIVISIBILL_WS_URI            | The path to the DivisiBill web service
| DIVISIBILL_WS_KEY            | The authentication token for the DivisiBill web service
| DIVISIBILL_SENTRY_DSN        | The path to the Sentry application health service
| SENTRY_AUTH_TOKEN            | The authentication token for the Sentry application health service

The GitHub Action build is based on a video by Gerald Versluis [Build Your .NET 
MAUI Android App with GitHub Actions](https://www.youtube.com/watch?v=GQuQPm40kys)
and a significant challenge in the build phase is signing the apk and aab files generated by 
the .NET Android build. Signing requires a keystore file and for security Gerald stores
it in the secrets store by turning it into a pgp message (with password), then converting
the message back to a key store at build time. Not strictly necessary, since the keystore
is encrypted as is the key within it, but if (like me) you've used weak passwords for
convenience then storing it as a secret is a good idea. Still gpg seems like overkill and, 
alas, as of December 2023 at least, gpg created what I suspect of being Unicode output, so 
it can't trivially be used for this. Luckily just converting the keystore to a base-64 
string and storing it as a secret seems sufficient as well as using one fewer password.

You can convert a file to a base-64 string in PowerShell like this:

```
    $data = Get-Content '.\Some.keystore' -Encoding Byte -Raw 

    $b64data = [Convert]::ToBase64String($data)
```
If you want to write the base64 data itself to a file use:

> [IO.File]::WriteAllText("$PWD\Some.keystore.b64",$b64data) 

To convert the base-64 string back to a file:

```
    $b64Decoded = [Convert]::FromBase64String($b64data)

    [IO.File]::WriteAllBytes("$PWD\new.keystore",$b64Decoded)
```

The `$PWD` is to ensure the file ends up in the correct folder, which can be a problem with PowerShell.

At this point the file `new.keystore` ought to contain a valid keystore which you can examine using keytool:

> keytool -list -v -keystore new.keystore -storepass xxxx

By default Visual Studio stores its keystores in
```
C:\Users\YourUserName\AppData\Local\Xamarin\Mono for Android\Keystore\
```
In production the base64 encoded keystore is a string stored in the secret store copied to an 
environment variable by the GitHub action used for the build. That environment variable is rehydrated 
into a keystore by a PowerShell step in the action.

The deployment part of the action is from the GitHub action marketplace. There are several choices but
the one I liked was [Upload Android Release to Play Store](
https://github.com/marketplace/actions/upload-android-release-to-play-store) on GitHub at [r0adkill/upload-google-play](
https://github.com/r0adkll/upload-google-play). It had a nice brief explanation of how to configure a Google service account
for use deploying a build. 

Another alternative is  [Upload Android application to Play Store with 
changesNotSentForReview](https://github.com/marketplace/actions/upload-android-application-to-play-store-with-changesnotsentforreview)
in the GitHub actions marketplace. The source is on github as [lozdan/upload-google-play](https://github.com/lozdan/upload-google-play)
It also looked good though I didn't try it.

Secrets Using GitHub CLI
------------------------

You can set up many of the secrets using the environment variables you already set, a git command prompt, and the gitHub 
CLI, for example:
```
gh secret set DIVISIBILL_WS_URI     -b "%DIVISIBILL_WS_URI_RELEASE%%"
gh secret set DIVISIBILL_WS_KEY     -b "%DIVISIBILL_WS_KEY_RELEASE%"
gh secret set DIVISIBILL_SENTRY_DSN -b "%DIVISIBILL_SENTRY_DSN%"
gh secret set SENTRY_AUTH_TOKEN     -b "%SENTRY_AUTH_TOKEN%"
```
or, if you prefer PowerShell:
```
gh secret set DIVISIBILL_WS_URI     -b "$env:DIVISIBILL_WS_URI_RELEASE";
gh secret set DIVISIBILL_WS_KEY     -b "$env:DIVISIBILL_WS_KEY_RELEASE";
gh secret set DIVISIBILL_SENTRY_DSN -b "$env:DIVISIBILL_SENTRY_DSN";
gh secret set SENTRY_AUTH_TOKEN     -b "$env:SENTRY_AUTH_TOKEN";
```
Secrets used for the build process are not in local environment variables so you'll need to 
set these explicitly:
```
gh secret set KEYSTORE_PASSWORD -b "keystore password";
gh secret set KEYSTORE_PASSWORD_ALIAS -b "key password (usually the same)";
```
The final secrets are typically set from a file because they are long and/or contain characters the
command line parser handles poorly. That's easiest at a command prompt rather than in PowerShell:
```
gh secret set SERVICE_ACCOUNT_JSON < service_account_file.json;
gh secret set KEYSTORE_B64 < keystorele.b64;
```
or, in PowerShell:
```
gh Get-Content service_account_file.json | secret set SERVICE_ACCOUNT_JSON
gh Get-Content keystore.b64 | secret set KEYSTORE_B64
```

Google Service Account
----------------------

Part of setting up release automation is coming up with a Google service account that has permission to push the compiled app
(in the form of an AAB file) to the Play Store and start the release process (automated testing followed by making it
available to testers via the store). This is not the same problem that the license checking web service has and can (and 
probably should) use different Google credentials.

After a successful automated build, the Google service account is retrieved from an environment variable and used to deploy
the app to the play store "alpha" stream. This starts with putting the service account into a secret so the 'Deploy' build
action can use it. The secret used is

> SERVICE_ACCOUNT_JSON

Because the secret data is JSON and need not be manipulated by DOS commands that might be confused by the multiple lines or special characters (especially the double quote character used to delimit strings) it can be stored directly in an Azure Secret and moved from there to SERVICE_ACCOUNTJSON by the YAML file (dotnet-build-android.yaml) describing the build.

Run Time Monitoring
-------------------

For MAUI the solution is [Sentry](https://sentry.io). The release build pushes PDB files to Sentry so that problems
can be analyzed symbolically. At present Sentry is only used for crash reporting and user problem reporting, not
performance monitoring. The bulk of problems turn up in Play Store testing which causes faults never 
seen in production (perhaps it sends input fast enough to fall into a few timing holes).

Build Information and Secrets
-----------------------------

There's information that belongs in the build process, not in the source. There's also some 
secret information (like the web service key) that doesn't belong in source control. 

The solution to both these problems is project file tasks that generate .cs files containing the build time information 
we need. Such files are created in the obj folders for whatever architecture the build is for so they are never checked in.

For example, the BuildInfo.cs file will look something like this

```csharp
namespace DivisiBill.Generated
{
    internal class BuildInfo
    {
        internal const string DivisiBillWsUri = "https://1234-99-999-999-999.ngrok-free.app/api/";
        internal const string DivisiBillWsKey = "some string likely ending in ==";
        ...
    }
}
```

Annoyingly when you change VS build from Android to Windows (and maybe other times) these files sometimes get generated with `CDATA`
tags actually in the file, which is irritating and a [bug](https://developercommunity.visualstudio.com/t/Project-Generated-Files-Sometimes-Contai/10604117?port=1025&fsid=289c13cb-3f26-49b3-ad9f-3ce964430f13&q=CDATA&ref=native&refTime=1729901351487&refUserId=87be68b7-e95b-4f21-a5ac-92dc4c3f90a9) Microsoft have declined to fix . To work around this just repeat the build. 

On the developer machine, the secrets are stored in environment variables that mostly begin with DIVISIBILL_.
CI/CD creates environment variables on the fly using secrets stored in the CI/CD system. Some example secrets and their corresponding environment variable (the full list is above):

- DivisiBillWsUri / DIVISIBILL_WS_URI - The URI used to reach the DivisiBill web service. Get this from
    the web service definition in Azure.
- DivisiBillWsKey / DIVISIBILL_WS_KEY - The key required to use the DivisiBill web service. Get this from
    the web service definition in Azure. If this and the URI were stolen they could be used to reach the web service, which would reject requests without a valid product key also embedded in them. The thief would need the product key as well to get access to stored data.  
- DivisiBillSentryDsn / DIVISIBILL_SENTRY_DSNDIVISIBILL_SENTRY_DSN - The URI required to use Sentry for DivisiBill in order to log events or crashes. In theory anyone could steal this and create fake bug reports for DivisiBill but it would not allow them to read anything so that does not seem like a significant threat.
- DivisiBillBingMapsSecret / DIVISIBILL_BING_MAPS_SECRET - The secret required to use Bing Maps, which the Windows build does using the Community Toolkit because for now MAUI doesn't have a standard map control on Windows. Since that build is only ever used for development it is only included in GitHub secrets for simplicity and completeness. Get this from the bing maps web page.

In GitHub a build is controlled by an 'action' which is defined as a YAML build-and-deploy script in a specific folder
in this case `.github\workflows\dotnet-build-android.yml`. That script needs various secrets too, specifically 4 for the keystore 
(encrypted store data, encryption key to that data, keystore key and certificate key) as well as the ones above.

CI/CD Play Store deployment from the GitHub action YAML file uses a service account whose key is stored in SERVICE_ACCOUNT_JSON.

The project file also deploys the PDB file for a release build to Sentry using a build step. This requires a secret in SENTRY_AUTH_TOKEN so the `SentryUploadSymbols` action can upload the generated PDB file.

Build Time
----------

The build time displayed by the app is generated by a similar mechanism but it generates a class called 
BuildEnvironment (it could use the same class but this seemed cleaner).

When you load the project into Visual Studio it will generate both files so you can take a look at them - just remember not to change them since they'll be regenerated on every build and your changes will be lost. If you do need them to be different edit the templates in the project file.

In  theory the files ought to be recognized as generated files by Visual Studio as their names end with `.g.cs` but as of 2024 at least, they do not seem to be.

Play Store
----------

As well as GitHub actions to release updates to the Play Store you can also do it manually.

Switch to a Release build and an Android target then right click the project and select "Archive...".
That will start the Archive Manager which will build an archive with an AAB in it. The Archive Manager
"distribute" action lets you choose "Google Play" as a destination and that leads to the 
"Distribute - Signing Identity" screen where you'll get to choose a certificate or add one.
At present uploads use a certificate called "DivisiBill upload" and an upload account called
"Desktop Client" - both of these will be requested when an archive is selected and you press
the "Distribute..." button and select "Google Play" (the other is "ad-hoc" and just builds a 
package file).

If you create a new upload certificate you'll need to notify Google support about it via email
so they allow it for uploads. To do that you'll need a PEM file containing the public key. All
this means manipulating Java keystores which the Visual Studio archive manager can then use to 
store a certificate to upload files to the play store. Keystores are stored in

    C:\Users\%USERNAME%\AppData\Local\Xamarin\Mono for Android\Keystore

Typically each keystore is in its own folder. You can create a keystore containing a single 
upload certificate using keytool from the Java SDK. Typical input might be

    keytool -genkey -keyalg RSA -alias uploadplay -keystore uploadplay.jks -storepass ChangeMe -validity 7300 -keysize 2048
    
Beware - the's a recommendation to specify "storetype pkcs12" - don't, it seems like the generated keystore 
cannot be reliably read by keytool.

Beware - create the keystore in another folder and import it using the Archive Manager "distribute"
action which leads to the "Signing Identity" screen. There's more info at:
    
    https://learn.microsoft.com/en-us/xamarin/android/deploy-test/signing

If you need a copy of the public key (to change your Google Play upload certificate because you 
forgot the password on the old one perhaps) you can extract it to a PEM file using keytool, for example:

    keytool -export -rfc -keystore uploadplay -alias uploadplay -storepass ChangeMe -file uploadplay.pem

This will generate a PEM file with the public key in it. Raise a support request and attach the PEM file. The URL 
for key support requests is:

    https://support.google.com/googleplay/android-developer/contact/key

if you want to see a summary of what's in a keystore:
    keytool -list -keystore uploadplay.keystore" -storepass ChangeMe
for more detail add "-v".

Images
------

For historical reasons Android stored images in various different sizes so 
as to deal with screens with different resolutions. With the advent of scalable
vector graphics (svg files) which can be interpreted by any browser, things 
changed, but Android apps cannot interpret an SVG file at run time. However
MAUI can generate drawable objects at compile time from an svg. You can do this
manually at http://inloop.github.io/svg2android/ but if you do so for the svg
you may have to manually fiddle with the resulting XML to get the image you want.
All in all it is easier to just let MAUI do it for you.

If you want to edit SVG files you can do it by hand (they are just XML files) but 
for larger changes try "inkscape", it's free, flexible and comprehensive; a
bit of a steep learning curve, but well worth it.

Icon Images
-----------

Tool bar icons used to be stored as per-platform images in multiple sizes, then the use
of icon fonts made it easier by allowing the use of font based icons, which are scalable.

The icon font currently used is "Material Design Icons" stored in a TrueType font file
   ...\DivisiBill\Resources\Fonts\materialdesignicons-webfont.ttf
The most current version of this file is at https://github.com/Templarian/MaterialDesign-Webfont. 
There's more information on this font at https://materialdesignicons.com/.

For easier reuse individual icons are called out in the application resource directory in App.xaml.

If you want to examine all the Icons on a font file https://andreinitescu.github.io/IconFont2Code/ will let you see the glyphs and their encodings, so you can just drop the current font file into it and see what you need to in order to add new icons (hint: ctrl-F in the browser is your friend). You may get one or two "page not responding" errors when loading the font above, these are normal, just respond "wait" and it should load in under a minute.

Another good source of free icons is FontAwsome at https://fontawesome.com/download.

Help Text
---------

The help text is based on the DivisiBill folder from the Web project - the contents of the folder all except for the 
*.md source files which are not used at run time are copied as MAUI assets by these lines in the project file:

      <!-- Raw Web site Assets for DivisiBill with a "help" prefix to prevent name collisions with any other raw resources -->
      <MauiAsset Include="..\..\web\divisibill\**" LogicalName="help\%(RecursiveDir)%(Filename)%(Extension)" />
      <!-- Don't need the MD files -->
      <MauiAsset Remove="..\..\web\divisibill\**\*.md" />

The same web project goes to create a static web site so users can go take a look at 
DivisiBill help without installing the app (see "AutoPlus Web Site" below).

Licensing
---------

The web service calls into a license provider (the Google Play store) to get either a 
"Professional" subscription or an "OCR" license. These exist because using web capabilities (storage and bill 
OCR) costs money (a few cents per month for storage, and a few cents per scan) so I needed a way to charge 
for storage and scans. The web service is used to minimize license hacking (by replaying an existing license transaction, for example) and to keep
a running count of how many scans each license has left.

As well as the Pro subscription there's a pro license used only by license testers because test annual 
subscriptions expire in half an hour, presumably to make expiration testing easier.

In order for Google to support in App Licenses it has to have a certain amount of configuration and, 
based on testing, an app in the store (just in the Alpha or Beta the test streams does not seem to work).
See the web service project documentation for more details.

Android Emulator
----------------

The android emulator is pretty fragile, if it shuts down badly it seems to run very slowly hang on restart and 
once this starts
happening the only fix seems to be reboot the system. Even after that it sometimes does not recover, although
creating a new emulator and starting it sometimes seems to fix the others. Weird, fragile code.

Windows Debug Environment
-------------------------

It's easier to debug on Windows than in Android because:

1) Data files (meals and lists of people and venues) are visible in `%USERPROFILE%\Documents\DivisiBillDebug`
2) Program configuration is visible in `%USERPROFILE%\AppData\Local\Microsoft\D9049CD2-5037-432D-BC7E-2E2FB39EBA1C\Settings\preferences.dat`. The magic number (`D904....A1C`) comes from the Windows Platform `Package.AppxManifest` file.
3) Debugging is slightly faster and easier

The files and configuration can easily be removed or renamed, or even edited directly, to simulate various conditions.

In Android, per-application encryption makes this data hard to manipulate but it is still important to test UI there because it may behave quite differently to the Windows UI.