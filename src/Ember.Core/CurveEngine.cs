namespace Ember.Core;

/// <summary>
/// Turns a temperature stream into a fan percent: moving-average smoothing, then the curve, with hysteresis so the
/// output only falls once the smoothed temperature is <c>hysteresisC</c> below the temperature that set it.
/// One engine per fan group; not thread-safe.
/// </summary>
public sealed class CurveEngine
{
    readonly int _window;
    readonly double _hysteresisC;
    readonly Queue<double> _samples = new();
    FanCurve? _curve;
    double _output, _anchorTemp;
    bool _hasOutput;

    public CurveEngine(int smoothingSamples = 5, double hysteresisC = 2.0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(smoothingSamples, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(hysteresisC);
        _window = smoothingSamples;
        _hysteresisC = hysteresisC;
    }

    public double Step(double tempC, FanCurve curve)
    {
        if (!double.IsFinite(tempC)) throw new ArgumentException("Temperature must be finite.", nameof(tempC));
        ArgumentNullException.ThrowIfNull(curve);

        _samples.Enqueue(tempC);
        while (_samples.Count > _window) _samples.Dequeue();
        var smoothed = _samples.Average();
        var target = curve.Evaluate(smoothed);

        var curveChanged = !ReferenceEquals(curve, _curve);
        _curve = curve;
        if (!_hasOutput || curveChanged || target > _output || smoothed <= _anchorTemp - _hysteresisC)
        {
            _output = target;
            _anchorTemp = smoothed;
            _hasOutput = true;
        }
        return _output;
    }

    public void Reset()
    {
        _samples.Clear();
        _curve = null;
        _hasOutput = false;
    }
}
