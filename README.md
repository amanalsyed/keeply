# Keeply

A small, local-only Windows desktop app for quickly sorting the photos directly inside a folder.

## Run

Requires the .NET 8 SDK and Windows Desktop targeting pack.

```powershell
dotnet restore
dotnet run
```

The app uses Windows Imaging Component for previews. WebP previews require the Windows WebP Image Extension. Photo contents stay on the machine; they are not uploaded or added to a library.

## Shortcuts

| Key | Action |
| --- | --- |
| K | Keep and advance |
| T | Send to Recycle Bin and advance |
| A | Copy to the session album folder and advance |
| U | Undo the last triage action |
| 1–9 | Copy to the folder assigned to that Quick Folder key and advance |
| Left / Right | Previous / next photo |
| F11 | Toggle fullscreen |

Use **Quick folders** in the top bar to assign, change, or clear destinations for keys 1–9. Shortcuts are stored locally and work independently from the destination selected for **A**. If a key has no destination yet, Keeply asks you to choose one and uses it for the current photo.

Keep, Trash, and Album each play a short, distinct local sound and animate the current photo in the action direction. Use **Sound: On/Off** to mute or enable sounds, and click **Motion** to cycle through Smooth, Standard, and Reduced settings.

The footer shows queue progress and session action counts. The album destination is shown in the top bar, and the finished screen repeats the session totals.

Album and Quick Folder copies use a numbered suffix to avoid overwriting. Folder selection scans only that folder's top level. Keeply remembers sorting progress on this PC and offers to resume or start over when you reopen a folder.

## Duplicate Finder

Open **Find duplicates** to scan a selected folder for exact copies and visually similar photos. Subfolder scanning is optional. Keeply groups matches for comparison; you choose which files to send to the Recycle Bin. It never deletes matches automatically, and removals can be undone.

## Organize a photo library

Open **Organize library** to group photos by capture date, camera metadata, GPS coordinates, likely screenshots, format, dimensions, and exact or visually similar matches. Scanning and grouping run locally. **Save all groups** copies the current grouping into separate folders; **Save group as** copies one selected group into a folder with a name you choose. Originals stay in place.

Keeply stores a local scan index under `%LOCALAPPDATA%\PhotoKeepKill\LibraryIndex`. Repeat scans reuse metadata and duplicate fingerprints for files whose path, size, and modified time have not changed. Use **Recheck all files** to bypass the index and refresh it. The index stays on this PC and does not contain image data.

Duplicate Finder also caches each photo's exact-file hash and visual fingerprint locally, reusing them on repeat scans when the file's path, size, and modified time are unchanged. Bulk Compress and Convert images reuse a completed output when the source is unchanged, the operation settings and folders match, and the saved output still exists with its original size and modified time. New or changed source files and missing or edited outputs are processed again. Cache files stay under `%LOCALAPPDATA%\PhotoKeepKill\WorkCache`; files are still enumerated on each run so additions and removals are detected.

## Bulk Compress and Convert Images

Use **Bulk compress** to create separate, smaller copies while leaving originals untouched. JPEG quality is selectable; PNG/BMP/GIF/TIFF use lossless handling when possible, and files are preserved unchanged if re-encoding would make them larger. Animated images and multi-page TIFFs stay unchanged.

Use **Convert images** to create WebP, JPEG, or PNG copies in a destination folder. WebP can be lossy or lossless; JPEG quality is selectable; PNG preserves transparency. Animated GIFs and multi-page TIFFs are reported as skipped so their frames or pages are not lost. Both tools can scan subfolders, show per-file results, and cancel a run.

## Free use and lifetime license

The Free edition allows three unique folders on this Windows profile, with up to 100 supported photos directly inside each folder. Supported formats include JPG, JPEG, PNG, WebP, BMP, GIF, and TIFF. Empty folders do not count; reopening one of the same three folders is free. The allowance is stored locally under `%LOCALAPPDATA%\PhotoKeepKill` as hashes of normalized folder paths. No photos or readable folder-path list are sent anywhere.

The $6 lifetime license removes both limits and supports activation on up to three PCs. Activation and deactivation need internet; after activation this PC remains unlocked offline. The license key and server-signed offline license token are protected with Windows DPAPI. Keeply verifies the token's RSA signature before unlocking licensed features, and checks license status online at startup and about once a day when connected. If offline, the last valid signed token remains usable; a refund or revocation takes effect the next time an online check can confirm it. The matching signing private key belongs only in the website server environment. Existing unsigned activations are checked online once and migrated to a signed token without taking another activation seat. Set `apiBaseUrl` and `checkoutUrl` in `licensing.json` when the website and Creem product are ready. See [website/README.md](website/README.md) for setup.

## Windows release package

To create a Windows x64 release, run PowerShell from the project folder:

```powershell
.\scripts\Publish-Windows.ps1 -Version "1.0.4" -ApiBaseUrl "https://www.trykeeply.live/" -CheckoutUrl "https://www.creem.io/payment/prod_6z7buEm087gA7b7rLpO94B"
```

This publishes a self-contained Windows x64 app and creates a portable ZIP under `artifacts\release`. If NSIS is installed, it also creates a per-user installer with Start menu shortcuts and an uninstaller, then copies it to `website\public\downloads\Keeply-Setup.exe`. Users do not need the .NET runtime installed. The installer is unsigned; Windows may show security warnings, and a managed application-control policy may still block it.
#
