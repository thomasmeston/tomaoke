# Tomaoke

Tomaoke is a Windows app for singing along with a song. You pick a YouTube video or a local audio file, and the app plays the instrumental while it tunes your microphone toward the original lead vocal.

The lead vocal is prepared ahead of time. Tomaoke separates it from the track, stores its pitch, and follows that line while you sing. A strength control sets how far your live voice moves toward that pitch. At the low end you hear yourself. At the high end the note you are singing is pulled onto the lead.

## What you can do

- Sing into a USB microphone and hear yourself through a Bluetooth speaker or the headphone jack.
- Load a YouTube link or an audio file. Songs you have already prepared show up again in a recent list.
- Drop several YouTube links into a playlist. The next songs download and prepare while the current one plays, then start when it ends.
- Read lyrics in time with the track when the video has captions.
- Watch the YouTube picture in sync with the music. The picture stays muted so it does not double the song. It can be resized, popped out, or shown full screen.
- See the lead line and your own pitch while the song plays, so you can tell when you are on the note, flat, or sharp.
- Add harmony, a tight double, echo, and reverb. Natural, Party, and Robot presets set those vocal controls together.
- Shift the song by a few semitones. The tempo stays the same, and the lyrics and video stay lined up.
- Tap along with a click track to line the tuner up with what you hear from the speakers.

## Open the app

Tomaoke needs Windows and the .NET 8 runtime. The first time a song is prepared, the app also sets up a Python 3.13 environment for vocal separation. The video panel uses the Microsoft Edge WebView2 runtime, which is already present on current Windows 11 installs.

From this folder, run:

```powershell
.\publish.ps1
```

That publishes the app and adds a **Tomaoke** shortcut to the desktop and the Start Menu. Open the app from that shortcut.

Prepared songs, the Python environment, and the video profile are stored under `%LOCALAPPDATA%\KaraokeVox`.
