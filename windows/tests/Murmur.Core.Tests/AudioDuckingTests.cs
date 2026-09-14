using System.Text.Json;
using Murmur.Core;
using Murmur.Testing;
using Shouldly;
using Xunit;

namespace Murmur.CoreTests;

/// <summary>Other audio goes down when a recording starts and comes back when it ends, however it ends.</summary>
public sealed class AudioDuckingTests
{
    [Fact]
    public async Task Microphone_is_capturing_before_other_apps_are_muted()
    {
        var capture = FakeAudioCapture.Tone(1);
        var hotkey = new FakeHotkeySource();
        var ducker = new CaptureOrderDucker(capture);
        await using var engine = new DictationEngine(capture, hotkey,
            new FakeTranscriber("hello"), new RecordingTextInjector(), () => [])
        { Ducker = ducker };
        hotkey.Press();
        await Wait.UntilAsync(() => capture.Delivered);
        ducker.WasCapturing.ShouldBeTrue();
        hotkey.Release();
        await Wait.UntilAsync(() => engine.State == DictationState.Idle);
    }

    private sealed class CaptureOrderDucker(FakeAudioCapture capture) : Murmur.Abstractions.IAudioDucker
    {
        public bool WasCapturing { get; private set; }
        public (float Peak, string? Source) Duck() { WasCapturing = capture.IsCapturing; return (0, null); }
        public void Restore() { }
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        await Wait.UntilAsync(() => condition());
    }

    [Fact]
    public async Task Disabling_the_app_during_recording_restores_audio_and_stops_capture()
    {
        var hotkey = new FakeHotkeySource();
        var ducker = new FakeAudioDucker();
        await using var engine = new DictationEngine(FakeAudioCapture.Tone(2), hotkey,
            new FakeTranscriber("hello"), new RecordingTextInjector(), () => [])
        { Mode = ActivationMode.Tap, Ducker = ducker };
        ducker.Calls.ShouldBeEmpty();
        hotkey.Press();
        await WaitForAsync(() => engine.State == DictationState.Recording);
        engine.IsEnabled = false;
        ducker.Calls.ShouldBe(["duck", "restore"]);
        await WaitForAsync(() => engine.State == DictationState.Idle);
        hotkey.Press();
        ducker.Calls.ShouldBe(["duck", "restore"]);
    }

    [Fact]
    public async Task Turning_off_audio_muting_restores_immediately_without_stopping_recording()
    {
        var hotkey = new FakeHotkeySource();
        var ducker = new FakeAudioDucker();
        await using var engine = new DictationEngine(FakeAudioCapture.Tone(2), hotkey,
            new FakeTranscriber("hello"), new RecordingTextInjector(), () => [])
        { Mode = ActivationMode.Tap, Ducker = ducker };
        hotkey.Press();
        await WaitForAsync(() => engine.State == DictationState.Recording);
        engine.DuckAudio = false;
        ducker.Calls.ShouldBe(["duck", "restore"]);
        engine.State.ShouldBe(DictationState.Recording);
        hotkey.PressCancel();
        await WaitForAsync(() => engine.State == DictationState.Idle);
        ducker.Calls.ShouldBe(["duck", "restore"]);
    }
    [Fact]
    public async Task Recording_ducks_and_finishing_restores()
    {
        var hotkey = new FakeHotkeySource();
        var ducker = new FakeAudioDucker();
        var capture = FakeAudioCapture.Tone(2);
        await using var engine = new DictationEngine(capture, hotkey, new FakeTranscriber("hello"), new RecordingTextInjector(), () => [])
        {
            Mode = ActivationMode.Hold,
            Ducker = ducker,
        };

        hotkey.Press();
        await WaitForAsync(() => engine.State == DictationState.Recording);
        ducker.Calls.ShouldBe(["duck"]);

        hotkey.Release();
        await WaitForAsync(() => engine.State == DictationState.Idle);
        ducker.Calls.ShouldBe(["duck", "restore"]);
    }

    [Fact]
    public async Task Cancelling_restores()
    {
        var hotkey = new FakeHotkeySource();
        var ducker = new FakeAudioDucker();
        var capture = FakeAudioCapture.Tone(2);
        await using var engine = new DictationEngine(capture, hotkey, new FakeTranscriber("hello"), new RecordingTextInjector(), () => [])
        {
            Mode = ActivationMode.Tap,
            Ducker = ducker,
        };

        hotkey.Press();
        await WaitForAsync(() => engine.State == DictationState.Recording);
        hotkey.PressCancel();
        await WaitForAsync(() => engine.State == DictationState.Idle);

        ducker.Calls.ShouldBe(["duck", "restore"]);
    }

    [Fact]
    public async Task Switched_off_it_never_touches_the_ducker()
    {
        var hotkey = new FakeHotkeySource();
        var ducker = new FakeAudioDucker();
        var capture = FakeAudioCapture.Tone(2);
        await using var engine = new DictationEngine(capture, hotkey, new FakeTranscriber("hello"), new RecordingTextInjector(), () => [])
        {
            Mode = ActivationMode.Hold,
            Ducker = ducker,
            DuckAudio = false,
        };

        hotkey.Press();
        await WaitForAsync(() => engine.State == DictationState.Recording);
        hotkey.Release();
        await WaitForAsync(() => engine.State == DictationState.Idle);

        ducker.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void Old_settings_files_default_the_switch_on()
    {
        const string old = """{"PushToTalkKey":124}""";
        var data = JsonSerializer.Deserialize(old, SettingsJsonContext.Default.SettingsData);
        data.ShouldNotBeNull().DuckOtherAudio.ShouldBeTrue();
    }
}
