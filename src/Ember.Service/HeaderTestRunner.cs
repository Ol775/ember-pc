namespace Ember.Service;

public sealed record HeaderTestResult(bool Passed, string Message, double BaselineRpm, double ChangedRpm, double RestoredRpm);

/// <summary>
/// The guided one-header test: change the speed by 20 points (towards the middle, never below the floor), watch for
/// the RPM to follow within 10 s, then hand the header back to the board and watch the RPM return. The header is
/// always released afterwards, also on failure or cancellation. One test at a time; none during a safety override.
/// </summary>
public sealed class HeaderTestRunner
{
    const double Step = 20, MinMove = 0.15, Floor = 20, PumpFloor = 60;
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

    static HeaderTestResult Fail(string message, double b = 0, double c = 0, double r = 0) => new(false, message, b, c, r);

    public async Task<HeaderTestResult> RunAsync(string headerId, CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return Fail("Another header test is already running.");
        var written = false;
        try
        {
            if (_safetyOverride()) return Fail("A safety override is active; try again when temperatures are normal.");
            var header = _fans.Enumerate().FirstOrDefault(h => h.Id == headerId);
            if (header is null || !header.HasControl) return Fail("This header cannot be controlled.");
            if (!header.HasRpm) return Fail("This header has no RPM reading, so the test cannot tell if the fan responds.");

            var baseline = _readRpm(headerId);
            if (baseline is not > 0) return Fail("No fan is spinning on this header.");
            var current = _readPercent(headerId) ?? 50;
            var floor = header.Name == "Pump Fan" ? PumpFloor : Floor;
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

            _fans.ReleaseToAuto(headerId);
            written = false;
            if (!moved) return Fail($"The fan did not follow: {baseline:0} RPM before, {changed:0} RPM after.", baseline.Value, changed);

            var restored = changed;
            var back = false;
            var halfway = Math.Abs(changed - baseline.Value) / 2;
            for (var i = 0; i < WaitTicks && !back; i++)
            {
                await _delay(Tick, ct).ConfigureAwait(false);
                restored = _readRpm(headerId) ?? restored;
                back = Math.Abs(restored - baseline.Value) <= halfway;
            }
            return back
                ? new HeaderTestResult(true, $"Passed: {baseline:0} RPM, {changed:0} RPM at {command:0}%, back to {restored:0} RPM.", baseline.Value, changed, restored)
                : Fail($"The fan did not return to automatic control ({restored:0} RPM).", baseline.Value, changed, restored);
        }
        finally
        {
            if (written)
            {
                try { _fans.ReleaseToAuto(headerId); } catch (Exception) { /* nothing more can be done */ }
            }
            Volatile.Write(ref _busy, 0);
        }
    }
}
