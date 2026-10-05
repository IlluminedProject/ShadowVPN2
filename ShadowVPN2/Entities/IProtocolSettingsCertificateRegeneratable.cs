namespace ShadowVPN2.Entities;

public interface IProtocolSettingsCertificateRegeneratable {
    void GenerateSelfSignedCertificate();
}