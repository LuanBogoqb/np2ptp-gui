# Features

What np2ptp-gui actually does, tab by tab. For how it works under the hood, see the [README](../README.md).

## Downloads

Paste a `np2ptp:` link and click **+ New download** to fetch it. Every download shows up as its own row with a live status, detail text, and progress bar — you can start several at once and they run independently.

## Seeding

Point it at a file or folder you already have and click **+ Seed** to make it available to others on the network. Seeds keep running in the background even if you close the window (it minimizes to the tray instead of quitting).

## Share

Pack a file or folder into a `.nptp` link you can send to someone. That's the file they'll paste into their own Downloads tab.

## Settings

Where the np2ptp binary lives, default folders, listen address, tracker URL, and the theme picker (see [Themes](THEMES.md)). np2ptp-gui downloads and keeps `np2ptp.exe` updated on its own — there's nothing to install separately.

Closing the main window sends the app to the tray; only "Exit" from the tray icon actually quits, and it stops any running seeds cleanly first.

## Known rough edges

This is still an early build. No installer yet (just the exe), and a few planned extras — custom cursors, UI sounds, a Clippy-style assistant — are designed but not built. If the app refuses to download np2ptp with a certificate error, that's covered in [command-line flags](flags.md).
