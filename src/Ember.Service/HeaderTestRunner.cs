using Ember.Core;

namespace Ember.Service;

public sealed record HeaderTestResult(bool Passed, string Message, double BaselineRpm, double ChangedRpm, double RestoredRpm,
    double? OriginalPercent = null);

/// <summary>
/// The guided one-header test: change the speed by 20 points (towards the middle, never below the floor), watch for
/// the RPM to follow within 10 s, then hand the header back to the board and watch the RPM return. The header is
/// always released afterwards, also on failure or cancellation. One test at a time; none during a safety override.
/// </summary>
public sealed class HeaderTestRunner
{
    const double Step = 20, MinMove = 0.15, GpuTestPercent = 80;
    static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);
    const int WaitTicks = 10;

    readonly IFanController _fans;
    readonly Func<string, double?> _readRpm, _readPercent;
    readonly Func<TimeSpan, CancellationToken, Task> _delay;
    readonly Func<bool> _safetyOverride;
    int _busy;

    public HeaderTestRunner(IFanController fans, Func<string, double?> readRpm, Func<string, double?> readPercent,
        Func<TimeSpan, CancellationToken, Task> delay, Func<bool>? safetyOverride = null)
    {
        _fans = fans;
        _readRpm = readRpm;
        _readPercent = readPercent;
        _delay = delay;
        _safetyOverride = safetyOverride ?? (() => false);
    }

    public bool IsBusy => Volatile.Read(ref _busy) == 1;

    /// <summary>Hands the header back the way this board needs: write the safe speed, then call the library restore.</summary>
    void Restore(string headerName, string headerId, double? original)
    {
        _fans.SetPercent(headerId, ReleaseRules.ValueFor(headerName, original));
        _fans.Release(headerId);
    }

    static HeaderTestResult Fail(string message, double b = 0, double c = 0, double r = 0) => new(false, message, b, c, r);

    public async Task<HeaderTestResult> RunAsync(string headerId, CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return Fail("Another header test is already running.");
        var written = false;
        var restoreName = "";
        double? restoreFrom = null;
        try
        {
            if (_safetyOverride()) return Fail("A safety override is active; try again when temperatures are normal.");
            var header = _fans.Enumerate().FirstOrDefault(h => h.Id == headerId);
            if (header is null || !header.HasControl) return Fail("This header cannot be controlled.");
            if (!header.HasRpm) return Fail("This header has no RPM reading, so the test cannot tell if the fan responds.");

            if (headerId.StartsWith("/gpu-", StringComparison.Ordinal))
            {
                // A graphics card fan often sits still at idle and only holds a steady speed from about 60%, so the usual "go 20 points
                // lower and watch it follow" test does not fit. Ask for a clearly steady speed, watch the RPM rise, give the fan back.
                var before = _readRpm(headerId) ?? 0;
                restoreName = header.Name;
                restoreFrom = null;
                _fans.SetPercent(headerId, GpuTestPercent);
                written = true;
                var reached = before;
                var turning = false;
                for (var i = 0; i < WaitTicks && !turning; i++)
                {
                    await _delay(Tick, ct).ConfigureAwait(false);
                    reached = _readRpm(headerId) ?? reached;
                    if (_safetyOverride()) return Fail("Stopped because a safety override or a competing app started.");
                    turning = reached >= Math.Max(300, before * (1 + MinMove));
                }
                Restore(header.Name, headerId, null);
                written = false;
                return turning
                    ? new HeaderTestResult(true, $"Passed: {before:0} RPM, {reached:0} RPM at {GpuTestPercent:0}%. The card has its fan back.", before, reached, reached)
                    : Fail($"The fan did not follow: {before:0} RPM before, {reached:0} RPM at {GpuTestPercent:0}%.", before, reached);
            }

            var baseline = _readRpm(headerId);
            if (baseline is not > 0) return Fail("No fan is spinning on this header.");
            var current = _readPercent(headerId) ?? 50;
            restoreName = header.Name;
            restoreFrom = current;
            var floor = header.Name == "CPU Fan" ? ReleaseRules.CpuFloor : ReleaseRules.SystemFloor;
            var command = current - Step >= floor ? current - Step : Math.Min(100, current + Step);
            var lowering = command < current;

            _fans.SetPercent(headerId, command);
            written = true;

            double changed = baseline.Value;
            var moved = false;
            for (var i = 0; i < WaitTicks && !moved; i++)
            {
                await _delay(Tick, ct).ConfigureAwait(false);
                changed = _readRpm(headerId) ?? changed;
                if (_safetyOverride()) return Fail("Stopped because a safety override or a competing app started.");
                moved = lowering ? changed <= baseline.Value * (1 - MinMove) : changed >= baseline.Value * (1 + MinMove);
            }

            Restore(header.Name, headerId, current);
            written = false;
            if (!moved) return Fail($"The fan did not follow: {baseline:0} RPM before, {changed:0} RPM after.", baseline.Value, changed);

            var restored = changed;
            var back = false;
            var halfway = Math.Abs(changed - baseline.Value) / 2;
            for (var i = 0; i < WaitTicks && !back; i++)
            {
                await _delay(Tick, ct).ConfigureAwait(false);
                restored = _readRpm(headerId) ?? restored;
                // System fans return to the speed they had; the CPU fan is left at 100%, so it only has to be no slower than before.
                back = header.Name == "CPU Fan" ? restored >= baseline.Value * (1 - MinMove) : Math.Abs(restored - baseline.Value) <= halfway;
            }
            return back
                ? new HeaderTestResult(true, $"Passed: {baseline:0} RPM, {changed:0} RPM at {command:0}%, back to {restored:0} RPM.", baseline.Value, changed, restored, current)
                : Fail($"The fan did not go back to its saved speed ({restored:0} RPM).", baseline.Value, changed, restored);
        }
        finally
        {
            if (written)
            {
                try { Restore(restoreName, headerId, restoreFrom); }
                catch (Exception)
                {
                    try { _fans.SetPercent(headerId, 100); } catch (Exception) { /* nothing more can be done */ }
                }
            }
            Volatile.Write(ref _busy, 0);
        }
    }
}
