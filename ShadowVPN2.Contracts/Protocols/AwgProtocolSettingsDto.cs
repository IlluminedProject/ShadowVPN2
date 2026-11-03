using System.Text.Json.Serialization;

namespace ShadowVPN2.Contracts.Protocols;

public sealed class AwgProtocolSettingsDto : ProtocolSettingsDto, IListenPortSettings, IAwgSettings {
    [JsonIgnore]
    public bool IsAmneziaWg {
        get => Jc.HasValue || Jmin.HasValue || Jmax.HasValue
               || S1.HasValue || S2.HasValue || S3.HasValue || S4.HasValue
               || !string.IsNullOrWhiteSpace(H1) || !string.IsNullOrWhiteSpace(H2)
               || !string.IsNullOrWhiteSpace(H3) || !string.IsNullOrWhiteSpace(H4)
               || !string.IsNullOrWhiteSpace(I1) || !string.IsNullOrWhiteSpace(I2)
               || !string.IsNullOrWhiteSpace(I3) || !string.IsNullOrWhiteSpace(I4)
               || !string.IsNullOrWhiteSpace(I5);
    }

    public string? H1 { get; set; }
    public string? H2 { get; set; }
    public string? H3 { get; set; }
    public string? H4 { get; set; }
    public uint? Jc { get; set; }
    public uint? Jmin { get; set; }
    public uint? Jmax { get; set; }
    public int? S1 { get; set; }
    public int? S2 { get; set; }
    public int? S3 { get; set; }
    public int? S4 { get; set; }
    public string? I1 { get; set; }
    public string? I2 { get; set; }
    public string? I3 { get; set; }
    public string? I4 { get; set; }
    public string? I5 { get; set; }

    public int ListenPort { get; set; }
}