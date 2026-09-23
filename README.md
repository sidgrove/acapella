# Acapella

Press a key, say it, and it's typed. Acapella is a dictation app for Windows from [Sidgrove](https://sidgrove.com): hold or tap a key anywhere, speak, and the words land in whatever has focus.

- **On this machine.** Speech is transcribed locally with NVIDIA's Parakeet model through sherpa-onnx. Nothing leaves your PC unless you turn on AI clean-up.
- **Live as you speak.** The overlay shows the running transcript while you talk.
- **Spoken commands that just work.** "New line", "full stop", "comma", "question mark", "scratch that". Ums and ers are dropped. All local, all predictable.
- **Optional AI clean-up.** Gemini tidies every non-empty dictation—even one word—fixing punctuation, self-corrections, likely mishearings and spoken numbers, with a guard that refuses anything that summarises or rewrites your words. Add your own rules in Settings.
- **Your key, your way.** Record any key or chord (Ctrl, Alt, Shift, Win combinations included). Hold to talk, tap to toggle, or both. Escape cancels.
- **Dictionary.** Teach it the names and terms it gets wrong.
- **History.** Every dictation, with what was heard and what was typed.

## Install

**Windows:** open [Releases](https://github.com/sidgrove/acapella/releases), download
**Acapella-Windows-x64.zip**, choose **Extract All**, then double-click **Install.cmd**.
Preview builds are labelled **Pre-release**. Keep all the extracted files together;
the executable needs its companion DLLs. Do not download GitHub's source-code ZIP to install.

**[Step-by-step Windows installation and troubleshooting →](docs/INSTALL-WINDOWS.md)**

**Mac (Apple silicon, macOS 26):** download **Acapella-macOS.zip** from the same release,
unzip, drag **Acapella.app** to Applications, then right-click it and choose **Open** the
first time (the build is not yet notarised). Grant Microphone and Accessibility when asked.
It is a separate native Swift app with a different feature set from Windows: Apple's speech
engine by default, Parakeet optional, no Gemini tier.
**[Mac: install, build from source, permissions, feature differences and how to adapt it →](docs/MAC.md)**

Developers can build Windows from source:

```powershell
cd windows
.\publish.ps1
.\install.ps1
```

`install.ps1` puts Acapella in `%LOCALAPPDATA%\Programs\Acapella`, adds a Start menu entry and an Apps & features entry. On first run it walks you through downloading the speech model (about 600 MB, once), picking your key and trying a dictation.

## Requirements

- Windows 10 or 11, x64
- A microphone
- For AI clean-up: a Gemini API key, entered in Settings or set as `GEMINI_API_KEY`

## Building

- .NET 10 SDK
- `dotnet build windows/Murmur.sln`
- `dotnet test windows/Murmur.sln`

The engine is platform-neutral and fully tested against fakes; only the thin `Murmur.Platform.Windows` layer touches Win32. See [`windows/README.md`](windows/README.md) for the layout and [`AGENTS.md`](AGENTS.md) for the conventions.

## Data

Settings, dictionary, history, log and the speech model live under `%LOCALAPPDATA%\Acapella`.

## Licence

Proprietary. © Sidgrove.

## Release maintainers

The Windows preview-release workflow builds, tests, self-tests and packages the complete
app when a `v*` tag is pushed. It attaches the installable ZIP and SHA-256 checksum to a
GitHub pre-release. A manual workflow run produces an Actions artifact without publishing
a release. Before announcing a download, confirm the workflow succeeded and the assets
appear on the Releases page. No credentials, API keys, model weights or user history are bundled.
