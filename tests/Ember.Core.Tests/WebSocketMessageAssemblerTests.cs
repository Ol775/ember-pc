using System.Text;

namespace Ember.Core.Tests;

public class WebSocketMessageAssemblerTests
{
    static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void A_single_complete_message_is_returned()
    {
        var a = new WebSocketMessageAssembler(1024);
        Assert.Equal("hello", a.Add(B("hello"), true));
    }

    [Fact]
    public void Fragments_are_joined_when_the_last_one_arrives()
    {
        var a = new WebSocketMessageAssembler(1024);
        Assert.Null(a.Add(B("hel"), false));
        Assert.Null(a.Add(B("lo "), false));
        Assert.Equal("hello world", a.Add(B("world"), true));
    }

    [Fact]
    public void A_character_split_across_fragments_is_decoded_whole()
    {
        var bytes = B("é"); // two bytes
        var a = new WebSocketMessageAssembler(1024);
        Assert.Null(a.Add(bytes.AsSpan(0, 1), false));
        Assert.Equal("é", a.Add(bytes.AsSpan(1), true));
    }

    [Fact]
    public void A_message_over_the_limit_is_dropped_even_when_later_fragments_are_small()
    {
        var a = new WebSocketMessageAssembler(10);
        Assert.Null(a.Add(B("0123456789"), false));
        Assert.Null(a.Add(B("AB"), false)); // pushes it over
        Assert.Null(a.Add(B("x"), true)); // a small tail must not turn the truncated start into a message
    }

    [Fact]
    public void The_next_message_after_an_oversized_one_is_fine()
    {
        var a = new WebSocketMessageAssembler(10);
        a.Add(B("0123456789AB"), true);
        Assert.Equal("ok", a.Add(B("ok"), true));
    }

    [Fact]
    public void An_empty_message_is_ignored() =>
        Assert.Null(new WebSocketMessageAssembler(10).Add([], true));

    [Fact]
    public void A_message_exactly_at_the_limit_is_kept() =>
        Assert.Equal("0123456789", new WebSocketMessageAssembler(10).Add(B("0123456789"), true));
}
