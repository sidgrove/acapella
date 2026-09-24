using System.Diagnostics;
using System.Globalization;
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

/// <summary>Settings: the key, the microphone, the model, AI clean-up, behaviour.</summary>
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

    /// <summary>Builds the settings window.</summary>
    public SettingsView(Composition composition)
    {
        _composition = composition;
        _settings = composition.Settings;

        _model = new ModelPart(composition);
        _model.ModelChanged += (_, _) => ModelChanged?.Invoke(this, EventArgs.Empty);

        var body = Panels.Column(Tokens.Space.Card,
            Card.Standard(Panels.Section("Push to talk", "Which key, and how it works.", new KeyPart(composition))),
            Card.Standard(Panels.Section("Microphone", "Applies to the next recording.", BuildMicrophoneSection())),
            Card.Standard(Panels.Section("Sound effects", "Short cues through your default speakers or headphones.", Panels.Column(Tokens.Space.Roomy,
                Panels.SwitchRow("Recording sounds", "Soft, quiet notes when listening starts and stops.", _settings.Data.RecordingSounds, v => Save(_settings.Data with { RecordingSounds = v })),
                Panels.SwitchRow("Send sound", "A subtle note when Acapella sends your text.", _settings.Data.SendSound, v => Save(_settings.Data with { SendSound = v }))))),
            Card.Standard(Panels.Section("Speech model", "Runs on this machine. With Gemini transcription on, it draws the live preview and types whenever the cloud fails.", _model)),
            Card.Standard(Panels.Section("Writing", "Rules applied on this machine, before anything else.", BuildWritingSection())),
            Card.Standard(Panels.Section("AI clean-up", "Optional. Tidies the transcript with Gemini before typing.", BuildAiSection())),
            Card.Standard(Panels.Section("Behaviour", null, BuildBehaviourSection())));
        body.Margin = new Thickness(Tokens.Space.Section, Tokens.Space.Roomy, Tokens.Space.Section, Tokens.Space.Section);

        Content = new ScrollViewer { Content = body };
    }

    private StackPanel BuildWritingSection()
    {
        var sendWord = Debounced(new TextBox { Text = _settings.Data.SendWord, Watermark = "blob" }, text =>
        {
            var value = text?.Trim() ?? string.Empty;
            if (_settings.Data.SendWord != value) Save(_settings.Data with { SendWord = value });
        });
        var sendAliases = Debounced(new TextBox { Text = _settings.Data.SendWordAliases, Watermark = "e.g. sand, sent, scent (as many as you like, separated by commas)" }, text =>
        {
            var value = text ?? string.Empty;
            if (_settings.Data.SendWordAliases != value) Save(_settings.Data with { SendWordAliases = value });
        });
        var sendOnly = Debounced(new TextBox { Text = _settings.Data.SendOnlyPhrase, Watermark = "send it" }, text =>
        {
            var value = text?.Trim() ?? string.Empty;
            if (_settings.Data.SendOnlyPhrase != value) Save(_settings.Data with { SendOnlyPhrase = value });
        });
        var sendOnlyAliases = Debounced(new TextBox { Text = _settings.Data.SendOnlyAliases, Watermark = SpokenSendCommand.DefaultSendOnlyAliases }, text =>
        {
            var value = text ?? string.Empty;
            if (_settings.Data.SendOnlyAliases != value) Save(_settings.Data with { SendOnlyAliases = value });
        });
        var fullStops = new Segmented(["Drop after one sentence", "Never end with one", "Keep"], (int)FullStopIndex(_settings.Data.FullStops));
        fullStops.Selected += (_, i) =>
        {
            var chosen = i switch { 1 => TrailingFullStop.Never, 2 => TrailingFullStop.Keep, _ => TrailingFullStop.DropAfterSingleSentence };
            if (_settings.Data.FullStops != chosen) Save(_settings.Data with { FullStops = chosen });
        };

        return Panels.Column(Tokens.Space.Roomy,
            Panels.Column(Tokens.Space.Snug,
                Panels.Labelled("Send word", sendWord),
                Panels.Labelled("Also send if you hear", sendAliases),
                Text.Muted("As many alternative words as you like, separated by commas. Only matched at the end of the dictation, never between sentences or paragraphs."),
                Text.Muted("End your dictation with this word to insert the text and press Enter when recording stops. The word is removed. Leave blank to disable."),
                Panels.Labelled("Send phrase", sendOnly),
                Text.Muted("Say this at the end to send your dictation, or on its own to send existing text. The phrase is removed. Leave blank to disable."),
                Panels.Labelled("Also counts as the send phrase", sendOnlyAliases),
                Text.Muted("What the speech model hears instead when you say the phrase on its own, separated by commas. Check the raw text in History for anything to add here."),
                Text.Body("Full stop at the very end"),
                fullStops,
                Text.Muted("Chat messages and fragments read better without one. Questions and exclamation marks always stay.")),
            Panels.SwitchRow("Spoken commands", "“New line”, “new paragraph”, “full stop”, “comma”, “question mark”, “scratch that” and so on become what they say. “Period” is always a word.", _settings.Data.SpokenCommands, v => Save(_settings.Data with { SpokenCommands = v })),
            Panels.SwitchRow("Remove ums and ers", "Standalone hesitations are dropped before anything else sees the text.", _settings.Data.RemoveFillers, v => Save(_settings.Data with { RemoveFillers = v })),
            Panels.SwitchRow("No comma before “and”", "House style, applied to every result, including what the AI clean-up returns: “A, B and C”.", _settings.Data.NoCommaBeforeAnd, v => Save(_settings.Data with { NoCommaBeforeAnd = v })),
            Panels.SwitchRow("British spellings", "“Optimize”, “color” and “behavior” from the speech model are written the British way. Code and file names are left alone.", _settings.Data.BritishSpelling, v => Save(_settings.Data with { BritishSpelling = v })),
            Panels.SwitchRow("Join dictations in the same field", "When the next dictation goes straight into the same box, the space between them is typed for you, and the full stop the rule above removed goes back if a new sentence starts.", _settings.Data.JoinDictations, v => Save(_settings.Data with { JoinDictations = v })));
    }

    private static int FullStopIndex(TrailingFullStop rule) => rule switch { TrailingFullStop.Never => 1, TrailingFullStop.Keep => 2, _ => 0 };
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
        list.Children.Add(new MicrophonePicker(devices, chosen, id =>
        {
            if (_settings.Data.MicrophoneDeviceId != id) Save(_settings.Data with { MicrophoneDeviceId = id });
        }));
        if (devices.Count == 0)
        {
            list.Children.Add(Text.Muted("No active microphone found. Plug one in, or check Settings → Privacy & security → Microphone."));
        }

        if (chosen is not null && devices.All(d => d.Id != chosen))
        {
            list.Children.Add(Text.Muted("The microphone you chose isn't connected right now; the Windows default is used until it is."));
        }

        return list;
    }

    private StackPanel BuildAiSection()
    {
        var key = Debounced(Field.Text("Gemini API key, or leave blank to use GEMINI_API_KEY", _settings.Data.GeminiApiKey, secret: true), text =>
        {
            var value = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            if (_settings.Data.GeminiApiKey != value) Save(_settings.Data with { GeminiApiKey = value });
        });

        var model = Debounced(Field.Text(GeminiCleaner.DefaultModel, _settings.Data.GeminiModel ?? GeminiCleaner.DefaultModel), text =>
        {
            var value = string.IsNullOrWhiteSpace(text) || text.Trim() == GeminiCleaner.DefaultModel ? null : text.Trim();
            if (_settings.Data.GeminiModel != value) Save(_settings.Data with { GeminiModel = value });
        });

        var custom = Debounced(Field.Multiline("Your own rules, e.g. “Never use exclamation marks”, “Write dates as 9 Sept”, “Sign off emails with Dave”", _settings.Data.CustomInstructions), text =>
        {
            var value = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            if (_settings.Data.CustomInstructions != value) Save(_settings.Data with { CustomInstructions = value });
        });

        var jevKey = Debounced(Field.Text("Vercel AI Gateway key, or leave blank to use AI_GATEWAY_API_KEY", _settings.Data.JevApiKey, secret: true), text =>
        {
            var value = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            if (_settings.Data.JevApiKey != value) Save(_settings.Data with { JevApiKey = value });
        });
        var jevBase = Debounced(Field.Text(JevClient.DefaultBaseUrl, _settings.Data.JevBaseUrl ?? JevClient.DefaultBaseUrl), text =>
        {
            var value = string.IsNullOrWhiteSpace(text) || text.Trim() == JevClient.DefaultBaseUrl ? null : text.Trim();
            if (_settings.Data.JevBaseUrl != value) Save(_settings.Data with { JevBaseUrl = value });
        });
        var jevModel = Debounced(Field.Text(JevClient.DefaultModel, _settings.Data.JevModel ?? JevClient.DefaultModel), text =>
        {
            var value = string.IsNullOrWhiteSpace(text) || text.Trim() == JevClient.DefaultModel ? null : text.Trim();
            if (_settings.Data.JevModel != value) Save(_settings.Data with { JevModel = value });
        });
        var jevResult = Text.Muted(string.Empty);
        var jevTest = new SgButton("Test Jev", SgButton.Kind.Ghost);
        jevTest.Click += async (_, _) =>
        {
            jevTest.IsEnabled = false;
            jevResult.Text = "Asking…";
            try
            {
                using var jev = new JevClient(() => _settings.Data.JevApiKey, _settings.Data.JevBaseUrl, _settings.Data.JevModel);
                var clock = Stopwatch.StartNew();
                var answers = await jev.DecideAsync("Okay, finish up, send it.",
                    [new DecisionQuestion("send", "noul", "The speaker finishes by telling the dictation app to send the message.")], CancellationToken.None).ConfigureAwait(true);
                jevResult.Text = answers is null
                    ? $"Failed: {jev.LastError ?? "no reply"}"
                    : $"“Okay, finish up, send it.” → send command: {answers[0].Noul:0.00} probability, in {clock.ElapsedMilliseconds} ms";
            }
            finally
            {
                jevTest.IsEnabled = true;
            }
        };

        var result = Text.Muted(string.Empty);
        var test = new SgButton("Test with a sample", SgButton.Kind.Ghost);
        test.Click += async (_, _) =>
        {
            const string sample = "um so can you uh send me the the Q2 numbers by friday scratch that by thursday";
            test.IsEnabled = false;
            result.Text = "Sending…";
            try
            {
                using var cleaner = new GeminiCleaner(() => _settings.Data.GeminiApiKey, _settings.Data.GeminiModel, customInstructions: () => _settings.Data.CustomInstructions);
                var cleaned = await cleaner.CleanAsync(sample, CancellationToken.None).ConfigureAwait(true);
                result.Text = cleaned is null ? $"Failed: {cleaner.LastError ?? "no reply"}" : $"“{sample}”\n→ “{cleaned}”";
            }
            finally
            {
                test.IsEnabled = true;
            }
        };

        return Panels.Column(Tokens.Space.Base,
            Panels.SwitchRow("Also transcribe in the cloud as you speak",
                "Streams your voice to ElevenLabs Scribe while the key is held. Its words and the local model's both go to the clean-up, which takes each word from whichever makes more sense: about a third fewer mistakes on your own dictations. Your dictionary is sent as its word list. Uses the ELEVENLABS_API_KEY on this PC, costs about 35p an hour of speech, and falls back to the local model if it fails or is late.",
                _settings.Data.CloudTranscription, v => Save(_settings.Data with { CloudTranscription = v })),
            Panels.SwitchRow("Clean up with Gemini before typing",
                "Tidies every non-empty dictation, even a single word: punctuation, self-corrections, numbers and likely mishearings, without changing your meaning. Your text goes to Google's API — about a twentieth of a penny per dictation on Flash. If it doesn't answer in eight seconds, or rewrites rather than tidies, the local text is typed instead.",
                _settings.Data.AiCleanup, v => Save(_settings.Data with { AiCleanup = v })),
            Panels.SwitchRow("Review long dictations as a whole",
                "Off cleans a long dictation piece by piece while you are still talking, so the wait at the end is short. On sends the whole thing in one go once you stop, so every sentence is read with its neighbours: the best result, at roughly a second per hundred words.",
                _settings.Data.ReviewWholeDictation, v => Save(_settings.Data with { ReviewWholeDictation = v })),
            Panels.Labelled("API key", key),
            Panels.Labelled("Model", model),
            Panels.Labelled("Your own instructions", custom),
            test,
            result,
            Text.Body("Jev decisions"),
            Text.Muted("TypeSafe's Jev answers small yes/no questions in about a tenth of a second: was that a send command, does this sentence carry on, is “period” the noun. For now it runs alongside the rules and its answers go to the log; nothing waits for it. Leave the key blank to switch it off."),
            Panels.Labelled("Jev key", jevKey),
            Panels.Labelled("Jev base URL", jevBase),
            Panels.Labelled("Jev model", jevModel),
            jevTest,
            jevResult);
    }

    private StackPanel BuildBehaviourSection()
    {
        var column = Panels.Column(Tokens.Space.Roomy,
            Panels.SwitchRow("Type into the focused app", "Off copies to the clipboard and keeps history without typing or pressing Enter.", _settings.Data.InjectText, v => Save(_settings.Data with { InjectText = v })),
            Panels.SwitchRow("Keep a history", null, _settings.Data.KeepHistory, v => Save(_settings.Data with { KeepHistory = v })),
            Panels.SwitchRow("Keep recordings for accuracy testing",
                "Saves the last month's audio on this PC, so changes to the speech model or the clean-up can be tested on your own voice. Nothing is uploaded.",
                _settings.Data.KeepRecordings, v => Save(_settings.Data with { KeepRecordings = v })),
            // The trailing full stop is chosen once, in Writing. A second switch here wrote
            // a legacy flag the engine no longer read, and the two silently disagreed.
            Panels.SwitchRow("Mute other audio while I talk", "Mutes other apps on the current output while recording, then restores their previous mute state. Volume levels stay unchanged.", _settings.Data.DuckOtherAudio, v => Save(_settings.Data with { DuckOtherAudio = v })));

        if (_composition.Startup is { } startup)
        {
            column.Children.Add(Panels.SwitchRow("Start when I sign in to Windows", "Starts in the tray.", startup.IsEnabled,
                v => { if (!startup.SetEnabled(v)) Log.Warn("could not change start-up registration"); }));
        }

        var quit = new SgButton($"Quit {AppPaths.ProductName}", SgButton.Kind.Danger);
        quit.Click += (_, _) => App.Quit();
        column.Children.Add(Panels.Split(Text.Muted("Closing the window keeps it running in the tray. This stops it completely."), quit));

        return column;
    }

    private void Save(SettingsData data) => _settings.Update(data);
}
