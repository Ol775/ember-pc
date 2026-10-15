using System.Net;
using System.Text.Json;
using Ember.Lighting.Meross;

namespace Ember.Lighting.Tests;

public class MerossTests
{
    [Fact]
    public void Md5_is_lowercase_hex() => Assert.Equal("5d41402abc4b2a76b9719d911017c592", MerossCloudClient.Md5Hex("hello"));

    [Fact]
    public void Sign_joins_secret_timestamp_nonce_and_params() =>
        Assert.Equal(MerossCloudClient.Md5Hex("23x17ahWarFH6w29" + "1700000000000" + "ABC" + "e30="), MerossCloudClient.Sign(1700000000000, "ABC", "e30="));

    [Fact]
    public void Local_message_is_signed_with_id_key_and_timestamp()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1700000000);
        using var doc = JsonDocument.Parse(MerossLocalClient.BuildMessage("Appliance.Control.ToggleX", "SET", new { a = 1 }, "KEY", "10.0.0.5", now, "abc"));
        var h = doc.RootElement.GetProperty("header");
        Assert.Equal(MerossCloudClient.Md5Hex("abcKEY1700000000"), h.GetProperty("sign").GetString());
        Assert.Equal("http://10.0.0.5/config", h.GetProperty("from").GetString());
        Assert.Equal("SET", h.GetProperty("method").GetString());
    }

    [Fact]
    public void Devices_are_parsed_and_bad_rows_skipped()
    {
        using var doc = JsonDocument.Parse("""[{"uuid":"u1","devName":"Desk","deviceType":"msl120","onlineStatus":1},{"uuid":"","deviceType":"x"},{"uuid":"u2","deviceType":"mss110","onlineStatus":0},5]""");
        var list = MerossCloudClient.ParseDevices(doc.RootElement);
        Assert.Equal(2, list.Count);
        Assert.True(list[0].IsLight);
        Assert.True(list[0].Online);
        Assert.Equal("Meross mss110", list[1].Name);
        Assert.False(list[1].IsLight);
    }

    [Fact]
    public void Account_needs_token_and_key()
    {
        using var ok = JsonDocument.Parse("""{"token":"t","key":"k"}""");
        Assert.Equal("k", MerossCloudClient.ParseAccount(ok.RootElement, "https://iotx-eu.meross.com").Key);
        using var bad = JsonDocument.Parse("""{"token":"t"}""");
        Assert.Throws<MerossException>(() => MerossCloudClient.ParseAccount(bad.RootElement, "x"));
    }

    [Fact]
    public void Sign_in_sends_the_two_step_code_only_when_given()
    {
        Assert.False(MerossCloudClient.SignInParams("a@b.c", "pw", null).ContainsKey("mfaCode"));
        Assert.False(MerossCloudClient.SignInParams("a@b.c", "pw", " ").ContainsKey("mfaCode"));
        var p = MerossCloudClient.SignInParams("a@b.c", "pw", " 123456 ");
        Assert.Equal("123456", p["mfaCode"]);
        Assert.Equal(MerossCloudClient.Md5Hex("pw"), p["password"]);
    }

    [Fact]
    public void Session_round_trips_and_rejects_junk()
    {
        var a = new MerossAccount("t", "k", "https://iotx-us.meross.com");
        Assert.Equal(a, MerossAccount.Restore(a.Serialise()));
        Assert.Null(MerossAccount.Restore("nonsense"));
        Assert.Null(MerossAccount.Restore(null));
        Assert.Null(MerossAccount.Restore("""{"Token":"t","Key":"k","Domain":"http://x"}"""));
    }

    [Fact]
    public void Uuid_is_read_from_system_all()
    {
        using var doc = JsonDocument.Parse("""{"all":{"system":{"hardware":{"uuid":"abc"}}}}""");
        Assert.Equal("abc", MerossLocalClient.UuidOf(doc.RootElement));
        using var none = JsonDocument.Parse("{}");
        Assert.Null(MerossLocalClient.UuidOf(none.RootElement));
    }

    [Fact]
    public void Scan_covers_the_subnet_except_this_pc_and_only_private_ranges()
    {
        var hosts = MerossSource.Subnet24(IPAddress.Parse("192.168.1.20")).ToList();
        Assert.Equal(253, hosts.Count);
        Assert.DoesNotContain("192.168.1.20", hosts);
        Assert.True(MerossSource.IsPrivate(IPAddress.Parse("172.20.1.1")));
        Assert.False(MerossSource.IsPrivate(IPAddress.Parse("8.8.8.8")));
    }
}
