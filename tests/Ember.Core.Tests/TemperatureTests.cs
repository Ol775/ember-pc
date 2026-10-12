namespace Ember.Core.Tests;

public class TemperatureTests
{
    [Fact] public void Converts_to_fahrenheit() => Assert.Equal(212, Temperature.Convert(100, TemperatureUnit.Fahrenheit));
    [Fact] public void Formats_celsius() => Assert.Equal("54°C", Temperature.Format(54.4, TemperatureUnit.Celsius));
    [Fact] public void Formats_fahrenheit() => Assert.Equal("130°F", Temperature.Format(54.4, TemperatureUnit.Fahrenheit));
    [Fact] public void Null_formats_as_em_dash() => Assert.Equal("—", Temperature.Format(null, TemperatureUnit.Celsius));
}
