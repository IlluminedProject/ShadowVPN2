using AwesomeAssertions;
using ShadowVPN2.Contracts.Subscriptions;
using Xunit;

namespace ShadowVPN2.Contracts.Tests;

public sealed class ConfigBuilderTests {
    [Fact]
    public void Empty_builder_produces_empty_configuration() {
        new ConfigBuilder().Build().Should().BeEmpty();
    }

    [Fact]
    public void Sections_and_directives_have_exact_newline_separation() {
        var config = new ConfigBuilder()
            .AddSection("Interface")
            .Add("PrivateKey", "private-key")
            .Add("MTU", 1408)
            .AddSection("Peer")
            .Add("PublicKey", "public-key")
            .Add("PersistentKeepalive", 25)
            .Build();

        config.Should().Be(string.Join(Environment.NewLine,
            "[Interface]", "PrivateKey = private-key", "MTU = 1408", "",
            "[Peer]", "PublicKey = public-key", "PersistentKeepalive = 25", ""));
    }

    [Fact]
    public void Consecutive_sections_have_one_blank_line_between_them() {
        var config = new ConfigBuilder()
            .AddSection("Interface")
            .AddSection("Peer")
            .Build();

        config.Should().Be(string.Join(Environment.NewLine, "[Interface]", "", "[Peer]", ""));
    }

    [Fact]
    public void Optional_null_and_blank_values_add_no_directives_or_newlines() {
        var config = new ConfigBuilder()
            .AddOptional("Jc", null)
            .AddSection("Interface")
            .AddOptional("Jmin", null)
            .AddOptional("S1", null)
            .AddOptional("H1", null)
            .AddOptional("H2", "")
            .AddOptional("H3", " \t\r\n ")
            .Add("PrivateKey", "private-key")
            .AddSection("Peer")
            .AddOptional("I1", " ")
            .Add("PublicKey", "public-key")
            .Build();

        config.Should().Be(string.Join(Environment.NewLine,
            "[Interface]", "PrivateKey = private-key", "", "[Peer]", "PublicKey = public-key", ""));
    }

    [Fact]
    public void Optional_explicit_numeric_and_string_zeros_are_retained() {
        var config = new ConfigBuilder()
            .AddSection("Interface")
            .AddOptional("Jc", (uint?)0)
            .AddOptional("S1", (int?)0)
            .AddOptional("H1", "0")
            .Build();

        config.Should().Be(string.Join(Environment.NewLine,
            "[Interface]", "Jc = 0", "S1 = 0", "H1 = 0", ""));
    }
}