using System.Text.Json;

namespace Ember.Core.Tests;

public class StreamDeckMessagesTests
{
    static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Register_plugin_json_shape()
    {
        var j = Parse(StreamDeckMessages.RegisterPlugin("abc123"));
        Assert.Equal("registerPlugin", j.GetProperty("event").GetString());
        Assert.Equal("abc123", j.GetProperty("uuid").GetString());
    }

    [Fact]
    public void Set_title_targets_hardware_and_software()
    {
        var j = Parse(StreamDeckMessages.SetTitle("ctx1", "Hello"));
        Assert.Equal("setTitle", j.GetProperty("event").GetString());
        Assert.Equal("ctx1", j.GetProperty("context").GetString());
        Assert.Equal("Hello", j.GetProperty("payload").GetProperty("title").GetString());
        Assert.Equal(0, j.GetProperty("payload").GetProperty("target").GetInt32());
    }

    [Fact]
    public void Set_image_is_an_svg_data_uri_that_decodes_back_to_the_svg()
    {
        const string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\"><text>A &amp; B #1</text></svg>";
        var j = Parse(StreamDeckMessages.SetImage("ctx1", svg));
        var image = j.GetProperty("payload").GetProperty("image").GetString()!;
        const string prefix = "data:image/svg+xml;charset=utf8,";
        Assert.StartsWith(prefix, image);
        Assert.Equal(svg, Uri.UnescapeDataString(image[prefix.Length..]));
    }

    [Fact]
    public void Show_alert_names_the_context()
    {
        var j = Parse(StreamDeckMessages.ShowAlert("ctx9"));
        Assert.Equal("showAlert", j.GetProperty("event").GetString());
        Assert.Equal("ctx9", j.GetProperty("context").GetString());
    }

    [Fact]
    public void Send_to_property_inspector_carries_action_context_and_payload()
    {
        var j = Parse(StreamDeckMessages.SendToPropertyInspector("dev.ember.pc.scene", "ctx1", new { scenes = new[] { "A", "B" } }));
        Assert.Equal("sendToPropertyInspector", j.GetProperty("event").GetString());
        Assert.Equal("dev.ember.pc.scene", j.GetProperty("action").GetString());
        Assert.Equal("B", j.GetProperty("payload").GetProperty("scenes")[1].GetString());
    }

    [Fact]
    public void Key_down_event_is_parsed_with_its_settings()
    {
        const string json = "{\"event\":\"keyDown\",\"action\":\"dev.ember.pc.scene\",\"context\":\"ctx1\",\"device\":\"d\",\"payload\":{\"settings\":{\"scene\":\"Cool\",\"step\":0.1},\"coordinates\":{\"column\":0,\"row\":0},\"state\":0}}";
        Assert.True(StreamDeckMessages.TryParseEvent(json, out var e));
        Assert.Equal("keyDown", e!.Event);
        Assert.Equal("dev.ember.pc.scene", e.Action);
        Assert.Equal("ctx1", e.Context);
        Assert.Equal("Cool", e.SettingString("scene"));
        Assert.Equal(0.1, e.SettingNumber("step"));
        Assert.Null(e.SettingString("missing"));
    }

    [Fact]
    public void Will_appear_without_settings_is_fine()
    {
        Assert.True(StreamDeckMessages.TryParseEvent("{\"event\":\"willAppear\",\"action\":\"a\",\"context\":\"c\",\"payload\":{}}", out var e));
        Assert.Null(e!.SettingString("scene"));
    }

    [Fact]
    public void Device_events_without_a_context_parse()
    {
        Assert.True(StreamDeckMessages.TryParseEvent("{\"device\":\"d\",\"event\":\"deviceDidConnect\"}", out var e));
        Assert.Null(e!.Context);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"nope\":1}")]
    [InlineData("{\"event\":5}")]
    public void Garbage_never_throws_and_is_rejected(string json) => Assert.False(StreamDeckMessages.TryParseEvent(json, out _));

    [Fact]
    public void Setting_of_the_wrong_type_reads_as_missing()
    {
        Assert.True(StreamDeckMessages.TryParseEvent("{\"event\":\"keyDown\",\"payload\":{\"settings\":{\"scene\":5,\"step\":\"x\"}}}", out var e));
        Assert.Null(e!.SettingString("scene"));
        Assert.Null(e.SettingNumber("step"));
    }
}
