using MonitorComfort.Core.Colour;
using MonitorComfort.Core.Monitors;

namespace MonitorComfort.Tests;

public sealed class MonitorProbeTests
{
    private const string Monitor = "ACR0B70";
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    [InlineData(5, 16)]
    [InlineData(6, 30)]
    [InlineData(7, 30)]
    [InlineData(500, 30)]
    public void Unanswered_monitors_are_left_for_longer_after_each_failed_check_up_to_half_an_hour(int failedChecks, int minutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutes), ReplanSchedule.UnansweredDelay(failedChecks));
    }

    [Fact]
    public void Unanswered_delay_needs_at_least_one_failed_check()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ReplanSchedule.UnansweredDelay(0));
    }

    [Fact]
    public void A_monitor_never_seen_needs_a_check_even_when_nothing_is_scheduled()
    {
        var cache = new MonitorSupportCache();

        Assert.Equal(MonitorSupport.Unknown, cache.SupportOf(Monitor));
        Assert.True(cache.NeedsCheck(Monitor, Start, scheduled: false, settling: false));
    }

    [Fact]
    public void A_monitor_that_answered_is_not_checked_again_until_something_invalidates_it()
    {
        var cache = new MonitorSupportCache();
        cache.Record(Monitor, MonitorSupport.BrightnessAndColour, Start, settling: false);

        Assert.False(cache.NeedsCheck(Monitor, Start.AddMinutes(1), scheduled: true, settling: false));
        Assert.False(cache.NeedsCheck(Monitor, Start.AddMinutes(1), scheduled: false, settling: true));
        Assert.Equal(MonitorSupport.BrightnessAndColour, cache.SupportOf(Monitor));
    }

    [Fact]
    public void A_failed_read_or_write_marks_the_monitor_for_a_new_check_but_keeps_the_last_answer()
    {
        var cache = new MonitorSupportCache();
        cache.Record(Monitor, MonitorSupport.BrightnessOnly, Start, settling: false);

        cache.Invalidate(Monitor);

        Assert.True(cache.NeedsCheck(Monitor, Start, scheduled: false, settling: false));
        Assert.Equal(MonitorSupport.BrightnessOnly, cache.SupportOf(Monitor));
    }

    [Fact]
    public void A_display_change_makes_every_answering_monitor_be_checked_again()
    {
        var cache = new MonitorSupportCache();
        cache.Record(Monitor, MonitorSupport.BrightnessAndColour, Start, settling: false);
        cache.Record("SAM0001", MonitorSupport.BrightnessOnly, Start, settling: false);

        cache.TopologyChanged();

        Assert.True(cache.NeedsCheck(Monitor, Start, scheduled: false, settling: false));
        Assert.True(cache.NeedsCheck("SAM0001", Start, scheduled: false, settling: false));
        Assert.Equal(MonitorSupport.BrightnessAndColour, cache.SupportOf(Monitor));
    }

    [Fact]
    public void An_unanswered_monitor_is_not_asked_on_unscheduled_applies()
    {
        var cache = new MonitorSupportCache();
        cache.Record(Monitor, MonitorSupport.None, Start, settling: false);

        Assert.False(cache.NeedsCheck(Monitor, Start.AddHours(1), scheduled: false, settling: false));
        Assert.False(cache.NeedsCheck(Monitor, Start.AddHours(1), scheduled: false, settling: true));
    }

    [Fact]
    public void An_unanswered_monitor_is_asked_again_after_one_two_four_eight_sixteen_then_thirty_minutes()
    {
        var cache = new MonitorSupportCache();
        var now = Start;
        foreach (var expected in new[] { 1, 2, 4, 8, 16, 30, 30 })
        {
            cache.Record(Monitor, MonitorSupport.None, now, settling: false);
            Assert.False(cache.NeedsCheck(Monitor, now.AddMinutes(expected).AddSeconds(-1), scheduled: true, settling: false));
            Assert.True(cache.NeedsCheck(Monitor, now.AddMinutes(expected), scheduled: true, settling: false));
            now = now.AddMinutes(expected);
        }
    }

    [Fact]
    public void Checks_in_the_settling_window_are_always_due_and_do_not_use_up_the_backoff()
    {
        var cache = new MonitorSupportCache();
        for (var plan = 0; plan < 6; plan++)
        {
            Assert.True(cache.NeedsCheck(Monitor, Start.AddSeconds(plan), scheduled: true, settling: true));
            cache.Record(Monitor, MonitorSupport.None, Start.AddSeconds(plan), settling: true);
        }

        var after = Start.AddSeconds(100);
        Assert.True(cache.NeedsCheck(Monitor, after, scheduled: true, settling: false));
        cache.Record(Monitor, MonitorSupport.None, after, settling: false);

        Assert.False(cache.NeedsCheck(Monitor, after.AddSeconds(59), scheduled: true, settling: false));
        Assert.True(cache.NeedsCheck(Monitor, after.AddSeconds(60), scheduled: true, settling: false));
    }

    [Fact]
    public void A_display_change_restarts_the_backoff_of_an_unanswered_monitor()
    {
        var cache = new MonitorSupportCache();
        var now = Start;
        for (var failure = 0; failure < 4; failure++)
        {
            cache.Record(Monitor, MonitorSupport.None, now, settling: false);
        }

        cache.TopologyChanged();

        Assert.True(cache.NeedsCheck(Monitor, now, scheduled: true, settling: true));
        Assert.True(cache.NeedsCheck(Monitor, now, scheduled: true, settling: false));
        cache.Record(Monitor, MonitorSupport.None, now, settling: false);
        Assert.False(cache.NeedsCheck(Monitor, now.AddSeconds(59), scheduled: true, settling: false));
        Assert.True(cache.NeedsCheck(Monitor, now.AddSeconds(60), scheduled: true, settling: false));
    }

    [Fact]
    public void A_monitor_that_answers_again_starts_its_backoff_over_if_it_stops()
    {
        var cache = new MonitorSupportCache();
        for (var failure = 0; failure < 3; failure++)
        {
            cache.Record(Monitor, MonitorSupport.None, Start, settling: false);
        }

        cache.Record(Monitor, MonitorSupport.BrightnessOnly, Start, settling: false);
        Assert.False(cache.NeedsCheck(Monitor, Start, scheduled: true, settling: false));

        cache.Record(Monitor, MonitorSupport.None, Start, settling: false);

        Assert.False(cache.NeedsCheck(Monitor, Start.AddSeconds(59), scheduled: true, settling: false));
        Assert.True(cache.NeedsCheck(Monitor, Start.AddSeconds(60), scheduled: true, settling: false));
    }

    [Fact]
    public void Record_reports_only_the_first_answer_and_changes()
    {
        var cache = new MonitorSupportCache();

        var results = new[]
        {
            cache.Record(Monitor, MonitorSupport.BrightnessOnly, Start, settling: false),
            cache.Record(Monitor, MonitorSupport.BrightnessOnly, Start, settling: false),
            cache.Record(Monitor, MonitorSupport.None, Start, settling: false),
            cache.Record(Monitor, MonitorSupport.None, Start, settling: false),
        };

        Assert.Equal([true, false, true, false], results);
    }

    [Fact]
    public void Monitor_ids_are_matched_ignoring_case()
    {
        var cache = new MonitorSupportCache();
        cache.Record(Monitor, MonitorSupport.BrightnessOnly, Start, settling: false);

        Assert.Equal(MonitorSupport.BrightnessOnly, cache.SupportOf("acr0b70"));
        Assert.False(cache.NeedsCheck("acr0b70", Start, scheduled: true, settling: false));
    }

    [Fact]
    public void The_user_colour_preset_is_remembered_until_a_failure_or_display_change()
    {
        var cache = new MonitorSupportCache();
        cache.Record(Monitor, MonitorSupport.BrightnessAndColour, Start, settling: false);
        Assert.False(cache.InUserPreset(Monitor));

        cache.SetInUserPreset(Monitor, true);
        Assert.True(cache.InUserPreset(Monitor));
        Assert.False(cache.InUserPreset("SAM0001"));

        cache.Invalidate(Monitor);
        Assert.False(cache.InUserPreset(Monitor));

        cache.SetInUserPreset(Monitor, true);
        cache.TopologyChanged();
        Assert.False(cache.InUserPreset(Monitor));

        cache.SetInUserPreset(Monitor, true);
        cache.SetInUserPreset(Monitor, false);
        Assert.False(cache.InUserPreset(Monitor));
    }

    [Fact]
    public void Forgetting_user_presets_clears_every_monitor()
    {
        var cache = new MonitorSupportCache();
        cache.SetInUserPreset(Monitor, true);
        cache.SetInUserPreset("SAM0001", true);

        cache.ForgetUserPresets();

        Assert.False(cache.InUserPreset(Monitor));
        Assert.False(cache.InUserPreset("SAM0001"));
    }

    [Fact]
    public void Gains_that_moved_by_two_or_more_since_the_last_write_were_changed_externally()
    {
        var written = new MonitorLevels(40, new RgbGains(50, 41, 33));

        Assert.True(WritePolicy.GainsChangedExternally(written, new MonitorLevels(40, new RgbGains(52, 41, 33))));
        Assert.True(WritePolicy.GainsChangedExternally(written, new MonitorLevels(40, new RgbGains(50, 41, 31))));
    }

    [Fact]
    public void Matching_gains_and_other_brightness_are_not_an_external_gain_change()
    {
        var written = new MonitorLevels(40, new RgbGains(50, 41, 33));

        Assert.False(WritePolicy.GainsChangedExternally(written, new MonitorLevels(40, new RgbGains(51, 40, 34))));
        Assert.False(WritePolicy.GainsChangedExternally(written, new MonitorLevels(80, new RgbGains(50, 41, 33))));
        Assert.False(WritePolicy.GainsChangedExternally(null, written));
        Assert.False(WritePolicy.GainsChangedExternally(written, null));
    }
}
