using System.Text.Json;
using ShadowVPN2.Data;
using ShadowVPN2.Data.SingBox;
using ShadowVPN2.Data.SingBox.Models;
using Xunit;

namespace ShadowVPN2.Tests;

public class SingBoxRuntimeDiffTests {
    private static SingBoxConfig Config(string json) {
        return JsonSerializer.Deserialize<SingBoxConfig>(json,
            SingBoxService.SerializerOptions)!;
    }

    [Fact]
    public void SameConfig_IsNotRuntimeChange() {
        var json = """{"inbounds":[{"type":"hysteria2","tag":"hy2","users":[{"name":"a","password":"p"}]}]}""";
        Assert.False(SingBoxRuntimeDiff.IsRuntimeOnlyChange(Config(json), Config(json)));
    }

    [Fact]
    public void UsersOnlyChange_IsRuntimeChange() {
        var applied =
            """{"log":{"level":"info"},"inbounds":[{"type":"hysteria2","tag":"hy2","users":[{"name":"a","password":"p"}]}]}""";
        var candidate =
            """{"log":{"level":"info"},"inbounds":[{"type":"hysteria2","tag":"hy2","users":[{"name":"b","password":"p2"}]}]}""";
        Assert.True(SingBoxRuntimeDiff.IsRuntimeOnlyChange(Config(applied), Config(candidate)));
    }

    [Fact]
    public void PeersOnlyChange_IsRuntimeChange() {
        var applied =
            """{"endpoints":[{"type":"wireguard","tag":"wg","private_key":"k","peers":[{"public_key":"pk1","allowed_ips":["10.0.0.2/32"]}]}]}""";
        var candidate =
            """{"endpoints":[{"type":"wireguard","tag":"wg","private_key":"k","peers":[{"public_key":"pk2","allowed_ips":["10.0.0.3/32"]}]}]}""";
        Assert.True(SingBoxRuntimeDiff.IsRuntimeOnlyChange(Config(applied), Config(candidate)));
    }

    [Fact]
    public void NonUserChange_IsFullRestart() {
        var applied = """{"inbounds":[{"type":"hysteria2","tag":"hy2","listen_port":4443,"users":[]}]}""";
        var candidate = """{"inbounds":[{"type":"hysteria2","tag":"hy2","listen_port":5555,"users":[]}]}""";
        Assert.False(SingBoxRuntimeDiff.IsRuntimeOnlyChange(Config(applied), Config(candidate)));
    }

    [Fact]
    public void TagChange_IsFullRestart() {
        var applied = """{"inbounds":[{"type":"hysteria2","tag":"a","users":[{"name":"x","password":"p"}]}]}""";
        var candidate = """{"inbounds":[{"type":"hysteria2","tag":"b","users":[{"name":"x","password":"p"}]}]}""";
        Assert.False(SingBoxRuntimeDiff.IsRuntimeOnlyChange(Config(applied), Config(candidate)));
    }
}