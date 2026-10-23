# Working on this repo

Read this before changing anything. It is written for a coding agent picking the project up
cold, and it is mostly a list of things that look wrong but aren't, plus things that look
fine and will bite you.

---

## What this is

Push-to-talk dictation. Hold a key, talk, release, and cleaned-up text is typed into
whatever had focus. Two independent implementations:

| | macOS | Windows |
|---|---|---|
| Language | Swift 6 | C# / .NET 10 |
| UI | SwiftUI | Avalonia |
| Speech | Apple `SpeechAnalyzer`, or Parakeet via FluidAudio | Parakeet via sherpa-onnx |
| Location | repo root | `windows/` |

**The macOS app works and is in daily use.** Since 2026-09-12 it is Acapella throughout:
bundle `com.sidgrove.acapella`, `Acapella.app`, Swift module and executable `Acapella`, data
under `~/Library/Application Support/Acapella` (migrated from the old `MurmurYouTube` folder
on first launch by `Support/AppPaths.swift`). The dictionary target is still `MurmurDictionary`.

**The Windows app runs on real hardware** (Windows 11, since 2026-09-08): hook armed,
model loaded, front panel, tray, Settings, in-app model download, microphone selection,
per-user installer. CI builds it, runs 80 tests and self-tests the published exe. The first
hardware session found three bugs CI could not see — read `windows/README.md` §"What real
hardware found" before assuming a green build means anything about audio. What no agent
has done is speak into it and read the result; say so rather than claiming otherwise.

**Windows failures are readable.** `%LOCALAPPDATA%\Murmur\murmur.log` (Help → Open log)
records hook, model and capture outcomes; the engine's `Faulted` event puts the same
sentence on the front panel. Start there, not with the debugger.

---

## The one rule that matters

**`shared/dictionary-test-vectors.json` is the specification for correction behaviour.**

Both implementations run it in CI. If you change how corrections work, change the vectors
first, watch both sides go red, then make them green. Changing one implementation to "fix"
a failing vector without changing the other is how the two silently diverge — and only one
of them can be exercised by hand.

```bash
swift test --filter VectorTests                    # macOS side
cd windows && dotnet test Murmur.CrossPlatform.slnf # Windows side, runs anywhere
```

The Swift copy at `Tests/MurmurDictionaryTests/dictionary-test-vectors.json` is a copy, and
CI fails if it drifts from `shared/`. After editing the shared file:

```bash
cp shared/dictionary-test-vectors.json Tests/MurmurDictionaryTests/
```

---

## Things that look like bugs and are not

**`dotnet build Murmur.sln` fails on macOS** with `NETSDK1073`. Expected —
`Murmur.Platform.Windows` targets `net10.0-windows`. Use `Murmur.CrossPlatform.slnf`, which
omits it; everything else, including the whole UI suite, builds and tests on macOS in about
half a second.

**`swift build` fails with "input file was modified during the build."** The repo lives in an
iCloud-synced folder and the sync engine touches files mid-compile. **Always build with
`make`**, which uses `--scratch-path` outside the synced tree. A bare `swift build` also
writes a `.build/` directory into iCloud, which makes every subsequent build minutes slower.
If you see this error, wait a few seconds and retry.

**Compare mode doesn't type anything.** By design — `Settings.compareMode` runs every engine
on one recording and shows them side by side. If both injected, two transcripts would fight
over one text field. This is the single most confusing behaviour in the app.

**The timing column isn't comparing like with like.** Apple and Parakeet are timed on local
compute with the clock started *after* model load. Wispr Flow's number is its own
`e2eLatency`, which includes a network round trip and its cleanup pass. Don't present them
as one ranking.

**`MainActor.assumeIsolated` will crash the process.** It does not check the claim, it
asserts it. Use `await MainActor.run` from any non-main-actor context. This took the app
down once already.

**Mutating `@State` inside a `Canvas` draw closure floods the log and corrupts state.** The
VU meter keeps its needle physics in a plain reference type the view merely holds, which is
invisible to SwiftUI's state graph. Don't "clean that up" into `@State`.

---

## Design system

`Sources/Acapella/UI/DesignSystem.swift` defines every colour, size, radius, duration
and material token. **Views must not contain literal values.** If a component needs a number
that isn't a token, add the token rather than inlining it.

The direction is 1980s field recorders — Sony TC-D5, Marantz PMD, Nakamichi, Braun. Silver
face in light appearance, black face in dark. Two rules that are not negotiable:

- **Red means recording.** Nothing else in the app is red.
- **Amber and green are instrumentation only** — level meters, never UI chrome.

Explicitly ruled out: neon, vaporwave, synthwave, purple/pink gradients, glowing text, chrome
lettering, grid horizons. There are **no gradients anywhere**; depth comes from flat panels,
hairline bevels and procedurally-drawn brushed grain.

---

## macOS specifics

**Code signing is load-bearing, not cosmetic.** TCC stores a code-signing *requirement* per
entry, not just a path. An ad-hoc signature changes every build, so the rebuilt binary stops
satisfying the stored requirement — and the symptom lies: the Accessibility toggle still
shows as **on** while the app is untrusted. The `Makefile` auto-detects a Developer ID via
`security find-identity`. Don't replace that with `--sign -`.

If a grant does get wedged, reset that one row — never toggle, and never omit the bundle ID:

```bash
tccutil reset Accessibility com.sidgrove.acapella
```

A bare `tccutil reset Accessibility` wipes every app on the machine. Then quit System
Settings entirely (⌘Q) before reopening; the Privacy pane caches its list.

**`log` may be shadowed in the user's shell.** Use `/usr/bin/log` explicitly.

**Don't run the `.app` from the repo folder.** It's iCloud-synced and the sync engine can
corrupt the signature. `make install` puts the running copy in `/Applications`.

---

## Windows specifics

The specifics below were expensive to establish and several were found the hard way. Treat
them as load-bearing. Full detail in `windows/README.md` and `docs/PARAKEET-WINDOWS.md`.

**Three pinned versions that break silently at "latest":**

| Package | Pin | Why |
|---|---|---|
| `NAudio` | 2.3.0 | 3.x targets .NET 9+ and will not restore |
| `Avalonia.Headless.XUnit` | 11.3.20 | 12.x requires xUnit **v3**, a different package line |
| `org.k2fsa.sherpa.onnx` | 1.13.5 | Bundles ONNX Runtime — never also reference `Microsoft.ML.OnnxRuntime` |

**Right Alt is AltGr** on German, Polish, UK, Nordic and most Latin-American layouts. Binding
push-to-talk there — and especially suppressing it — breaks typing `@`, `€`, `\`, `|` for
those users. Default is **Right Ctrl**, and the hook **observes without swallowing**: if the
key-down is swallowed and the key-up escapes, the target app believes Ctrl is held forever.

**UI Automation cannot inject text.** `TextPattern` is documented read-only and
`ValuePattern` replaces a whole field rather than inserting at the caret. `SendInput` is the
primary path, not a fallback.

**`Murmur.App` loads the platform layer by reflection, not by reference.** A direct
reference would force the UI onto `net10.0-windows` and you would lose the ability to run it
on your own machine. Two consequences that have already bitten once: the assembly is
invisible to `PublishSingleFile`, so it is published as a loose file beside the exe *and*
resolved by an explicit `AssemblyLoadContext` handler; and the published self-test checks
this, because when it breaks the app starts perfectly and then does nothing at all when the
key is pressed.

**Keep `Murmur.Platform.Windows` logic-free.** Anything living there is code CI cannot
exercise. Retries, debouncing and device-change handling belong in the platform-neutral
projects behind an interface — those target plain `net10.0`, so `CA1416` turns any accidental
Win32 call into a build error.

**CI is the only place the Windows code is compiled.** Warnings are errors and the analyzers
are strict on purpose. `--no-incremental` is mandatory: Roslyn does not re-emit analyzer
warnings on a cached build, so without it the gate proves nothing.

---

## Regex, if you touch the dictionary

The two engines are not identical. Measured across 30 cases, **9 diverged**. Two affect this
code and are handled — don't remove either:

- `RegexOptions.CultureInvariant` on the C# side, or Turkish `İ` matches `i`.
- **NFC normalization on both sides.** macOS returns decomposed strings, so without it an
  accented trigger silently never fires.

Two more are unfixable and simply avoided: ICU folds `ß` to `ss` and .NET doesn't; .NET's `.`
splits surrogate pairs. Stay inside the safe subset — `\b`, `\d`, `\w`, `\s`, character
classes, greedy/lazy quantifiers, alternation, `(?<name>…)`, fixed-length lookbehind,
lookahead, `\p{L}`, and `$1`–`$9` in replacements. Nothing else.

---

## Updating the app on one of Dave's computers

Dave runs Acapella on more than one computer and the Windows copies sync with each other, so
updating one is never only about that one. **Follow `docs/COMPUTERS.md`** whenever he asks
for the app to be updated or installed anywhere: it lists the computers and the build each
runs, and holds what is waiting for each. Every reply about an update says which other
computers now need it and why. And when a change you make will need anything on another
computer (the same build, a repair, a setting), add it there in the same commit.

---

## What isn't built

1. **Command Mode** — select text, hold a second key, "make this more formal."
2. **Onboarding** — a first-run window walking through the macOS permissions.
3. **Notarization** (macOS) and **code signing** (Windows). `make notarize` does the Mac
   side once a Developer ID certificate and a notarytool profile exist on the building
   machine (docs/MAC.md); nobody has run it yet. Windows is unsigned, so users meet
   SmartScreen.
4. **Engine biasing on Windows.** sherpa-onnx's offline recogniser exposes no contextual
   phrase list for this model, so the dictionary's correction pass does the whole job there.
5. **Live device-change notifications** on Windows. A microphone chosen in Settings takes
   effect at once, but a headset Windows promotes to the default mid-session is only picked
   up when the warm stream next reopens. An `IMMNotificationClient` forwarder would fix it.

## What no amount of CI can verify

On Windows the app has been launched, armed and model-loaded on a real machine, but no
agent can hear itself speak. Still unverified by a person:

- A spoken dictation landing in a foreground app, and how good the transcript is.
- The OS microphone-privacy block message appearing live.
- Unplugging a microphone mid-capture.

**A real-hardware check should start with `Sidders.exe --selftest`, then `sidders.log`, then
a single short dictation into Notepad.**
