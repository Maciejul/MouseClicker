using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MouseClicker.Tests;

internal sealed class FakeInput : IInputSink
{
    public List<string> Events { get; } = new();

    public void SendKey(ushort vk, bool down) => Events.Add($"key {vk:X} {(down ? "down" : "up")}");

    public void SendMouse(ClickButton button, bool down) => Events.Add($"mouse {button} {(down ? "down" : "up")}");
}

public class HoldEngineTests
{
    private readonly FakeInput _input = new();
    private readonly FakeTimeProvider _time = new();

    private HoldEngine Create()
    {
        var engine = new HoldEngine(_input, _time);
        engine.Configure(0x57, HoldTarget.Key, ClickButton.Left);
        return engine;
    }

    [Fact]
    public void Press_then_release_sends_down_then_up()
    {
        HoldEngine engine = Create();
        engine.Press();
        engine.Release();
        Assert.Equal(new[] { "key 57 down", "key 57 up" }, _input.Events);
    }

    [Fact]
    public void Both_target_presses_key_and_button_and_releases_button_first()
    {
        var engine = new HoldEngine(_input, _time);
        engine.Configure(0x57, HoldTarget.Both, ClickButton.Right);
        engine.Press();
        engine.Release();
        Assert.Equal(new[] { "key 57 down", "mouse Right down", "mouse Right up", "key 57 up" }, _input.Events);
    }

    [Fact]
    public void Timed_hold_releases_when_time_runs_out()
    {
        HoldEngine engine = Create();
        engine.StartTimed(5);
        _time.Advance(TimeSpan.FromSeconds(4.9));
        Assert.False(engine.Tick());
        Assert.True(engine.IsHolding);

        _time.Advance(TimeSpan.FromSeconds(0.2));
        Assert.True(engine.Tick());
        Assert.False(engine.IsHolding);
    }

    [Fact]
    public void Extend_is_capped()
    {
        HoldEngine engine = Create();
        engine.StartTimed(5);
        engine.Extend(10, capSeconds: 8);
        Assert.Equal(8, engine.RemainingWholeSeconds);
    }

    [Fact]
    public void Changing_the_key_mid_hold_releases_the_old_one()
    {
        HoldEngine engine = Create();
        engine.Press();
        engine.Configure(0x41, HoldTarget.Key, ClickButton.Left);
        Assert.False(engine.IsHolding);
        Assert.Equal("key 57 up", _input.Events[^1]);
    }
}

public class HoldTriggerControllerTests
{
    private readonly FakeInput _input = new();
    private readonly FakeTimeProvider _time = new();
    private readonly AppSettings _settings = new() { HoldTimedEnabled = true, ExtendOnTap = false, SingleTap = SingleTapBehavior.Toggle };
    private readonly HoldEngine _holder;
    private readonly HoldTriggerController _trigger;
    private readonly List<string> _messages = new();

    public HoldTriggerControllerTests()
    {
        _holder = new HoldEngine(_input, _time);
        _holder.Configure(0x57, HoldTarget.Key, ClickButton.Left);
        _trigger = new HoldTriggerController(_holder, () => _settings, () => "W key", _time);
        _trigger.Message += _messages.Add;
    }

    private void Tap(int gapMs = 100)
    {
        _trigger.TriggerDown();
        _trigger.TriggerUp();
        _time.Advance(TimeSpan.FromMilliseconds(gapMs));
    }

    [Fact]
    public void Single_tap_toggles()
    {
        Tap();
        Assert.True(_holder.IsHolding);
        _time.Advance(TimeSpan.FromSeconds(1));
        Tap();
        Assert.False(_holder.IsHolding);
    }

    [Fact]
    public void Double_tap_starts_a_timed_hold()
    {
        _settings.DoubleTapSeconds = 5;
        Tap();
        Tap();
        Assert.True(_holder.IsTimed);
        Assert.Equal(5, _holder.RemainingWholeSeconds);
    }

    [Fact]
    public void Triple_tap_uses_the_triple_time()
    {
        _settings.TripleTapSeconds = 10;
        Tap();
        Tap();
        Tap();
        Assert.Equal(10, _holder.RemainingWholeSeconds);
    }

    [Fact]
    public void While_down_hold_ends_with_the_key()
    {
        _settings.SingleTap = SingleTapBehavior.WhileDown;
        _trigger.TriggerDown();
        Assert.True(_holder.IsHolding);
        _trigger.TriggerUp();
        Assert.False(_holder.IsHolding);
    }

    [Fact]
    public void Single_tap_does_nothing_when_set_to_nothing()
    {
        _settings.SingleTap = SingleTapBehavior.Nothing;
        Tap();
        Assert.False(_holder.IsHolding);
    }

    [Fact]
    public void Tap_during_timed_hold_with_extend_stops_if_no_second_tap_follows()
    {
        _settings.ExtendOnTap = true;
        Tap();
        Tap();
        Assert.True(_holder.IsTimed);

        _time.Advance(TimeSpan.FromSeconds(1));
        Tap(gapMs: 0);
        _trigger.Poll();
        Assert.True(_holder.IsHolding);

        _time.Advance(TimeSpan.FromMilliseconds(HoldTriggerController.TapWindowMs + 10));
        _trigger.Poll();
        Assert.False(_holder.IsHolding);
        Assert.Contains("Released W key", _messages);
    }

    [Fact]
    public void Second_tap_during_timed_hold_with_extend_adds_time()
    {
        _settings.ExtendOnTap = true;
        _settings.ExtendSeconds = 5;
        _settings.DoubleTapSeconds = 5;
        Tap();
        Tap();
        _time.Advance(TimeSpan.FromSeconds(1));
        int before = _holder.RemainingWholeSeconds;

        Tap();
        Tap();
        Assert.True(_holder.RemainingWholeSeconds > before);
        Assert.True(_holder.IsHolding);
    }

    [Fact]
    public void Poll_releases_an_expired_timed_hold()
    {
        _settings.DoubleTapSeconds = 2;
        Tap();
        Tap();
        _time.Advance(TimeSpan.FromSeconds(3));
        _trigger.Poll();
        Assert.False(_holder.IsHolding);
    }
}
