using System.Globalization;

namespace Ember.Core;

public enum TemperatureUnit { Celsius, Fahrenheit }

public static class Temperature
{
    public static double Convert(double celsius, TemperatureUnit u) =>
        u == TemperatureUnit.Fahrenheit ? celsius * 9 / 5 + 32 : celsius;

    public static string Format(double? celsius, TemperatureUnit u) =>
        celsius is null
            ? "—"
            : Math.Round(Convert(celsius.Value, u), MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture)
              + (u == TemperatureUnit.Fahrenheit ? "°F" : "°C");
}
