using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Murmur.Abstractions;
using Murmur.App.Controls;
using Murmur.App.Design;
using Murmur.Core;
using Murmur.Speech;

namespace Murmur.App.Views;

/// <summary>Settings: the key, the microphone, the model, the writing rules, AI clean-up, learning, behaviour.</summary>
/// <remarks>
/// One card per subject, each with its own tinted tile so the page reads as a set of calm,
/// distinct things. Labels sit in a fixed column with the control beside them; what a
/// setting does in detail lives in the hint beside its name, never in a paragraph under it
/// (the Bible: "no explainer line under a label (tooltip it)").
/// </remarks>
public sealed class SettingsView : UserControl
{
    private readonly Composition _composition;
    private readonly AppSettings _settings;
    private readonly ModelPart _model;
    private readonly List<DebouncedSave> _pending = [];

    /// <summary>Raised after a model download completes, so the panel can reload it.</summary>
    public event EventHandler? ModelChanged;

    /// <summary>Re-reads the model status, after the engine has loaded or failed to load it.</summary>
    public void RefreshModel() => _model.Refresh();

    /// <summary>Writes any edit still waiting on its debounce. Call before the app quits.</summary>
    public void Flush()
    {
        foreach (var save in _pending) save.Flush();
    }

    /// <summary>
    /// Saves a text field a moment after typing stops rather than on every keystroke, and
    /// on demand at quit. The API key used to save only on focus loss, so pasting it and
    /// pressing Ctrl+Q lost it.
    /// </summary>
    private sealed class DebouncedSave
    {
        private readonly DispatcherTimer _timer = new() { Interval = Tokens.Motion.SaveDebounce };
        private Action? _pendingSave;

        public DebouncedSave()
        {
            _timer.Tick += (_, _) => Flush();
        }

        public void Schedule(Action save)
        {
            _pendingSave = save;
            _timer.Stop();
            _timer.Start();
        }

        public void Flush()
        {
            _timer.Stop();
            var save = _pendingSave;
            _pendingSave = null;
            save?.Invoke();
        }
    }

    private TextBox Debounced(TextBox box, Action<string?> save)
    {
        var debounce = new DebouncedSave();
        _pending.Add(debounce);
        box.TextChanged += (_, _) => debounce.Schedule(() => save(box.Text));
        box.LostFocus += (_, _) => debounce.Flush();
        return box;
    }

    private static TextBox Sized(TextBox box, double width)
    {
        box.Width = width;
        return box;
    }

    /// <summary>Builds the settings.</summary>
    public SettingsView(Composition composition)
    {
        _composition = composition;
        _settings = composition.Settings;

        _model = new ModelPart(composition);
        _model.ModelChanged += (_, _) => ModelChanged?.Invoke(this, EventArgs.Empty);

        var body = Panels.Column(Tokens.Space.Wide,
            Panels.SettingsCard(Icons.Keyboard, Tokens.Accent.Brand, "Push to talk", "Which key starts a dictation and whether you hold it or tap it.", new KeyPart(composition)),
            Panels.SettingsCard(Icons.Mic, Tokens.Accent.Crimson, "Microphone", "Applies from the next recording.", BuildMicrophoneSection()),
            Panels.SettingsCard(Icons.Volume, Tokens.Accent.Amber, "Sounds", "Short cues through your default speakers or headphones.", BuildSoundsSection()),
            Panels.SettingsCard(Icons.Cpu, Tokens.Accent.Info, "Hearing you", "The speech model runs on this machine. With the cloud on as well, it draws the live preview and types whenever the cloud fails.", BuildHearingSection()),
            Panels.SettingsCard(Icons.Pen, Tokens.Accent.Emerald, "Writing", "Rules applied on this machine, before anything else sees the text.", BuildWritingSection()),
            Panels.SettingsCard(Icons.Send, Tokens.Accent.Coral, "Sending", "Say a word at the end and Acapella presses Enter for you once the text is in.", BuildSendingSection()),
            Panels.SettingsCard(Icons.Sparkles, Tokens.Accent.Plum, "AI clean-up", "Gemini tidies the words before they're typed, without changing what you meant.", BuildAiSection()),
            Panels.SettingsCard(Icons.Learn, Tokens.Accent.Brand, "Learning from you", "Acapella watches what you change after it types, to measure itself and to learn your words.", BuildLearningSection()),
            Panels.SettingsCard(Icons.Zap, Tokens.Accent.Info, "Jev decisions", "TypeSafe's Jev answers small yes or no questions in about a tenth of a second: was that a send command, was that fix a mishearing, what kind of writing is this app for. Nothing you're waiting on waits for it. Leave the key blank to switch it off.", BuildJevSection()),
            Panels.SettingsCard(Icons.Toggle, Tokens.Accent.Slate, "Behaviour", null, BuildBehaviourSection()));
        body.Margin = new Thickness(Tokens.Layout.ScrollGutter * 2, 0, Tokens.Layout.ScrollGutter * 2, Tokens.Space.Section);

        Content = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    }

    private StackPanel BuildMicrophoneSection()
    {
        var list = Panels.Column(Tokens.Space.Snug);
        var devices = _composition.Devices?.ListCaptureDevices() ?? [];

        if (_composition.Devices is null)
        {
            list.Children.Add(Text.Muted("Microphone selection is not available on this platform."));
            return list;
        }

        var chosen = _settings.Data.MicrophoneDeviceId;
        var picker = new MicrophonePicker(devices, chosen, id =>
        {
            if (_settings.Data.MicrophoneDeviceId != id) Save(_settings.Data with { MicrophoneDeviceId = id });
        })
        { Width = Tokens.Layout.FieldWide, HorizontalAlignment = HorizontalAlignment.Left };
        list.Children.Add(picker);
        if (devices.Count == 0)
        {
            list.Children.Add(Text.Muted("No microphone found. Plug one in, or check Settings > Privacy & security > Microphone."));
        }

        if (chosen is not null && devices.All(d => d.Id != chosen))
        {
            list.Children.Add(Text.Muted("The microphone you chose isn't connected right now, so the Windows default is used until it is."));
        }

        return list;
    }

    private StackPanel BuildSoundsSection() => Panels.Column(Tokens.Space.Roomy,
        Panels.SwitchRow("Recording sounds", "Soft, quiet notes when listening starts and stops.", _settings.Data.RecordingSounds, v => Save(_settings.Data with { RecordingSounds = v })),
        Panels.SwitchRow("Send sound", "A subtle note when Acapella presses Enter for you.", _settings.Data.SendSound, v => Save(_settings.Data with { SendSound = v })));

    private StackPanel BuildHearingSection() => Panels.Column(Tokens.Space.Wide,
        _model,
        Panels.SwitchRow("Also hear me in the cloud",
            "Streams your voice to ElevenLabs Scribe while the key is held. Its words and the local model's both go to the clean-up, which takes each word from whichever makes more sense: about a third fewer mistakes on your own dictations. Your dictionary goes as its word list. It uses the ELEVENLABS_API_KEY on this PC and costs about 35p an hour of speech; if it fails or is late, the local model's words are used.",
            _settings.Data.CloudTranscription, v => Save(_settings.Data with { CloudTranscription = v })));

    private StackPanel BuildWritingSection()
    {
        var fullStops = new Segmented(["Drop after one sentence", "Never end with one", "Keep"], FullStopIndex(_settings.Data.FullStops));
        fullStops.Selected += (_, i) =>
        {
            var chosen = i switch { 1 => TrailingFullStop.Never, 2 => TrailingFullStop.Keep, _ => TrailingFullStop.DropAfterSingleSentence };
            if (_settings.Data.FullStops != chosen) Save(_settings.Data with { FullStops = chosen });
        };

        return Panels.Column(Tokens.Space.Roomy,
            Panels.FieldRow("Full stop at the very end", "Chat messages and fragments read better without one. Questions and exclamation marks always stay.", fullStops),
            Panels.SwitchRow("Spoken commands", "“New line”, “new paragraph”, “full stop”, “comma”, “question mark”, “scratch that” and so on become what they say. “Period” is always a word.", _settings.Data.SpokenCommands, v => Save(_settings.Data with { SpokenCommands = v })),
            Panels.SwitchRow("Remove ums and ers", "Standalone hesitations are dropped before anything else sees the text.", _settings.Data.RemoveFillers, v => Save(_settings.Data with { RemoveFillers = v })),
            Panels.SwitchRow("No comma before “and”", "House style, applied to every result, including what the clean-up returns: “A, B and C”.", _settings.Data.NoCommaBeforeAnd, v => Save(_settings.Data with { NoCommaBeforeAnd = v })),
            Panels.SwitchRow("British spellings", "“Optimize”, “color” and “behavior” are written the British way. Code and file names are left alone.", _settings.Data.BritishSpelling, v => Save(_settings.Data with { BritishSpelling = v })),
            Panels.SwitchRow("Join dictations in the same box", "When the next dictation goes straight into the same box, the space between them is typed for you; the full stop goes back if a new sentence starts.", _settings.Data.JoinDictations, v => Save(_settings.Data with { JoinDictations = v })));
    }

    private static int FullStopIndex(TrailingFullStop rule) => rule switch { TrailingFullStop.Never => 1, TrailingFullStop.Keep => 2, _ => 0 };

    private StackPanel BuildSendingSection()
    {
        var sendWord = Debounced(Sized(Field.Text("send", _settings.Data.SendWord), Tokens.Layout.FieldShort), text =>
        {
            var value = text?.Trim() ?? string.Empty;
            if (_settings.Data.SendWord != value) Save(_settings.Data with { SendWord = value });
        });
        var sendAliases = Debounced(Sized(Field.Text("e.g. sand, sent, scent", _settings.Data.SendWordAliases), Tokens.Layout.FieldList), text =>
        {
            var value = text ?? string.Empty;
            if (_settings.Data.SendWordAliases != value) Save(_settings.Data with { SendWordAliases = value });
        });
        var sendOnly = Debounced(Sized(Field.Text("send it", _settings.Data.SendOnlyPhrase), Tokens.Layout.FieldShort), text =>
        {
            var value = text?.Trim() ?? string.Empty;
            if (_settings.Data.SendOnlyPhrase != value) Save(_settings.Data with { SendOnlyPhrase = value });
        });
        var sendOnlyAliases = Debounced(Sized(Field.Text(SpokenSendCommand.DefaultSendOnlyAliases, _settings.Data.SendOnlyAliases), Tokens.Layout.FieldList), text =>
        {
            var value = text ?? string.Empty;
            if (_settings.Data.SendOnlyAliases != value) Save(_settings.Data with { SendOnlyAliases = value });
        });

        return Panels.Column(Tokens.Space.Base,
            Panels.FieldRow("Send word", "End a dictation with this word to type it and press Enter when recording stops. The word itself is removed. Leave it blank to switch it off.", sendWord),
            Panels.FieldRow("Also send if it hears", "Other words the speech model hears instead of the send word, separated by commas. Only matched at the very end of a dictation.", sendAliases),
            Panels.FieldRow("Send phrase", "Say this at the end to send the dictation, or on its own to send what's already in the box. The phrase is removed. Leave it blank to switch it off.", sendOnly),
            Panels.FieldRow("Also counts as the phrase", "What the speech model hears instead when you say the phrase on its own, separated by commas. The history's “What was heard” shows anything worth adding.", sendOnlyAliases));
    }

    private StackPanel BuildAiSection()
    {
        var key = Debounced(Sized(Field.Text("or leave blank to use GEMINI_API_KEY", _settings.Data.GeminiApiKey, secret: true), Tokens.Layout.FieldWide), text =>
        {
            var value = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            if (_settings.Data.GeminiApiKey != value) Save(_settings.Data with { GeminiApiKey = value });
        });

        var model = Debounced(Sized(Field.Text(GeminiCleaner.DefaultModel, _settings.Data.GeminiModel ?? GeminiCleaner.DefaultModel), Tokens.Layout.FieldShort), text =>
        {
            var value = string.IsNullOrWhiteSpace(text) || text.Trim() == GeminiCleaner.DefaultModel ? null : text.Trim();
            if (_settings.Data.GeminiModel != value) Save(_settings.Data with { GeminiModel = value });
        });

        var custom = Debounced(Sized(Field.Multiline("Your own rules, e.g. “Never use exclamation marks”, “Sign off emails with Dave”", _settings.Data.CustomInstructions), Tokens.Layout.FieldTallWidth), text =>
        {
            var value = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            if (_settings.Data.CustomInstructions != value) Save(_settings.Data with { CustomInstructions = value });
        });

        var result = Text.Muted(string.Empty);
        result.IsVisible = false;
        var test = new SgButton("Try it on a sample", SgButton.Kind.Ghost, compact: true, icon: Icons.Play);
        test.Click += async (_, _) =>
        {
            const string sample = "um so can you uh send me the the Q2 numbers by friday scratch that by thursday";
            test.IsEnabled = false;
            result.IsVisible = true;
            result.Text = "Asking Gemini…";
            try
            {
                using var cleaner = new GeminiCleaner(() => _settings.Data.GeminiApiKey, _settings.Data.GeminiModel, customInstructions: () => _settings.Data.CustomInstructions);
                var cleaned = await cleaner.CleanAsync(sample, CancellationToken.None).ConfigureAwait(true);
                result.Text = cleaned is null ? $"That didn't work: {cleaner.LastError ?? "no reply"}" : $"“{sample}”\n→ “{cleaned}”";
            }
            finally
            {
                test.IsEnabled = true;
            }
        };

        return Panels.Column(Tokens.Space.Roomy,
            Panels.SwitchRow("Clean up with Gemini before typing",
                "Tidies every dictation, even a single word: punctuation, self-corrections, numbers and likely mishearings, without changing your meaning. Your text goes to Google's API, about a twentieth of a penny a dictation on Flash. If it doesn't answer within four seconds, or rewrites rather than tidies, your words are typed as heard.",
                _settings.Data.AiCleanup, v => Save(_settings.Data with { AiCleanup = v })),
            Panels.SwitchRow("Review long dictations as a whole",
                "Off cleans a long dictation piece by piece while you're still talking, so the wait at the end is short. On sends the whole thing once you stop, so every sentence is read with its neighbours: the best result, at roughly a second per hundred words.",
                _settings.Data.ReviewWholeDictation, v => Save(_settings.Data with { ReviewWholeDictation = v })),
            Panels.SwitchRow("Let it see where you're typing",
                "Sends the app's name, its window title and up to 1,000 characters before the cursor with the dictation, so a name already in the email or chat you're answering is spelt the same way. Only used for spelling.",
                _settings.Data.CleanupSeesScreen, v => Save(_settings.Data with { CleanupSeesScreen = v })),
            Panels.SwitchRow("Match the writing to the app",
                "An email gets short paragraphs with your greeting and sign-off on their own lines, a Slack or Teams message stays one message, a prompt to Claude or ChatGPT keeps file names and code as said. Your words stay yours. Apps it doesn't know are judged by Jev while you talk.",
                _settings.Data.MatchStyleToApp, v => Save(_settings.Data with { MatchStyleToApp = v })),
            Panels.FieldRow("Gemini API key", null, key),
            Panels.FieldRow("Model", "Leave it as it is unless you mean to change it.", model),
            Panels.FieldRow("Your own instructions", "Rules the clean-up follows on every dictation, in your own words.", custom),
            Panels.FieldRow(string.Empty, null, Panels.Column(Tokens.Space.Snug, test, result)));
    }

    private StackPanel BuildLearningSection() => Panels.Column(Tokens.Space.Roomy,
        Panels.SwitchRow("Learn from my edits",
            "For two minutes after typing, or until you send it or move away, reads the box back to see which words you changed. The changes are kept in the history to measure accuracy; a word you fix to something that sounds like it is learnt for the dictionary. The text stays on this PC; only the fixed words and a line either side go to Jev.",
            _settings.Data.LearnFromEdits, v => Save(_settings.Data with { LearnFromEdits = v })),
        Panels.SwitchRow("Add fixes to the dictionary by themselves",
            "A fix goes straight into the dictionary when Jev is sure it was a mishearing, when you make the same fix twice, or when you fix a word you hardly ever say. Each one shows under “Learnt by itself” with an Undo. Off leaves every fix as a suggestion for you to add.",
            _settings.Data.AddLearntFixes, v => Save(_settings.Data with { AddLearntFixes = v })),
        Panels.SwitchRow("Keep recordings of what I correct",
            "Keeps the audio of a dictation only when you change its words afterwards, so the speech model can one day be tuned to your voice on the mistakes that matter. Everything else is deleted once the edit check is done. Nothing is uploaded.",
            _settings.Data.KeepRecordings, v => Save(_settings.Data with { KeepRecordings = v })));

    private StackPanel BuildJevSection()
    {
        var jevKey = Debounced(Sized(Field.Text("or leave blank to use AI_GATEWAY_API_KEY", _settings.Data.JevApiKey, secret: true), Tokens.Layout.FieldWide), text =>
        {
            var value = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            if (_settings.Data.JevApiKey != value) Save(_settings.Data with { JevApiKey = value });
        });
        var jevBase = Debounced(Sized(Field.Text(JevClient.DefaultBaseUrl, _settings.Data.JevBaseUrl ?? JevClient.DefaultBaseUrl), Tokens.Layout.FieldWide), text =>
        {
            var value = string.IsNullOrWhiteSpace(text) || text.Trim() == JevClient.DefaultBaseUrl ? null : text.Trim();
            if (_settings.Data.JevBaseUrl != value) Save(_settings.Data with { JevBaseUrl = value });
        });
        var jevModel = Debounced(Sized(Field.Text(JevClient.DefaultModel, _settings.Data.JevModel ?? JevClient.DefaultModel), Tokens.Layout.FieldShort), text =>
        {
            var value = string.IsNullOrWhiteSpace(text) || text.Trim() == JevClient.DefaultModel ? null : text.Trim();
            if (_settings.Data.JevModel != value) Save(_settings.Data with { JevModel = value });
        });
        var jevResult = Text.Muted(string.Empty);
        jevResult.IsVisible = false;
        var jevTest = new SgButton("Ask Jev a question", SgButton.Kind.Ghost, compact: true, icon: Icons.Play);
        jevTest.Click += async (_, _) =>
        {
            jevTest.IsEnabled = false;
            jevResult.IsVisible = true;
            jevResult.Text = "Asking…";
            try
            {
                using var jev = new JevClient(() => _settings.Data.JevApiKey, _settings.Data.JevBaseUrl, _settings.Data.JevModel);
                var clock = Stopwatch.StartNew();
                var answers = await jev.DecideAsync("Okay, finish up, send it.",
                    [new DecisionQuestion("send", "noul", "The speaker finishes by telling the dictation app to send the message.")], CancellationToken.None).ConfigureAwait(true);
                jevResult.Text = answers is null
                    ? $"That didn't work: {jev.LastError ?? "no reply"}"
                    : $"“Okay, finish up, send it.” → a send command, {answers[0].Noul:0.00} likely, in {clock.ElapsedMilliseconds} ms";
            }
            finally
            {
                jevTest.IsEnabled = true;
            }
        };

        return Panels.Column(Tokens.Space.Base,
            Panels.FieldRow("Vercel AI Gateway key", null, jevKey),
            Panels.FieldRow("Base URL", "Leave it as it is unless you mean to change it.", jevBase),
            Panels.FieldRow("Model", "Leave it as it is unless you mean to change it.", jevModel),
            Panels.FieldRow(string.Empty, null, Panels.Column(Tokens.Space.Snug, jevTest, jevResult)));
    }

    private StackPanel BuildBehaviourSection()
    {
        var column = Panels.Column(Tokens.Space.Roomy,
            Panels.SwitchRow("Type into the app I'm using", "Off copies to the clipboard and keeps the history without typing or pressing Enter.", _settings.Data.InjectText, v => Save(_settings.Data with { InjectText = v })),
            Panels.SwitchRow("Keep a history", null, _settings.Data.KeepHistory, v => Save(_settings.Data with { KeepHistory = v })),
            // The trailing full stop is chosen once, in Writing. A second switch here wrote
            // a legacy flag the engine no longer read, and the two silently disagreed.
            Panels.SwitchRow("Mute other audio while I talk", "Mutes other apps on the current output while recording, then puts back how they were. Volume levels stay as they are.", _settings.Data.DuckOtherAudio, v => Save(_settings.Data with { DuckOtherAudio = v })));

        if (_composition.Startup is { } startup)
        {
            column.Children.Add(Panels.SwitchRow("Start when I sign in to Windows", "Starts in the tray.", startup.IsEnabled,
                v => { if (!startup.SetEnabled(v)) Log.Warn("could not change start-up registration"); }));
        }

        var quit = new SgButton($"Quit {AppPaths.ProductName}", SgButton.Kind.Danger, compact: true, icon: Icons.Power);
        ToolTip.SetTip(quit, "Closing the window keeps Acapella running in the tray. This stops it completely.");
        quit.Click += (_, _) => App.Quit();
        quit.HorizontalAlignment = HorizontalAlignment.Left;
        column.Children.Add(quit);

        return column;
    }

    private void Save(SettingsData data) => _settings.Update(data);
}
