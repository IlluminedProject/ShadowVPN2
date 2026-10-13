using ShadowVPN2.Data.Cluster;

namespace ShadowVPN2.Infrastructure.Configurations;

/// <summary>
///     Holds join credentials needed while RavenDB starts, without making an incomplete
///     join look like a completed local configuration on disk.
/// </summary>
public sealed class JoinStartupState {
    private readonly object _sync = new();

    private string? _temporaryAwgPrivateKey;
    private string? _temporaryCertificatePath;
    private string? _temporaryCertificatePem;
    private int _temporaryNodeNumber;
    private IReadOnlyList<NodeCertificateTrust> _temporaryTrustPeers = [];

    public bool IsJoining {
        get {
            lock (_sync)
                return _temporaryCertificatePath != null;
        }
    }

    public string? TemporaryCertificatePath {
        get {
            lock (_sync)
                return _temporaryCertificatePath;
        }
    }

    public IReadOnlyList<NodeCertificateTrust> TemporaryTrustPeers {
        get {
            lock (_sync)
                return _temporaryTrustPeers;
        }
    }

    public int TemporaryNodeNumber {
        get {
            lock (_sync)
                return _temporaryNodeNumber;
        }
    }

    public string? TemporaryAwgPrivateKey {
        get {
            lock (_sync) return _temporaryAwgPrivateKey;
        }
    }

    public string? TemporaryCertificatePem {
        get {
            lock (_sync) return _temporaryCertificatePem;
        }
    }

    public void Configure(string certificatePath, int nodeNumber, IEnumerable<NodeCertificateTrust> trustPeers,
        string awgPrivateKey, string certificatePem) {
        Configure(certificatePath, nodeNumber, trustPeers);
        lock (_sync) {
            _temporaryAwgPrivateKey = awgPrivateKey;
            _temporaryCertificatePem = certificatePem;
        }
    }

    public void Configure(string certificatePath, int nodeNumber, IEnumerable<NodeCertificateTrust> trustPeers) {
        ArgumentException.ThrowIfNullOrWhiteSpace(certificatePath);
        if (nodeNumber < 1)
            throw new ArgumentOutOfRangeException(nameof(nodeNumber));

        lock (_sync) {
            _temporaryCertificatePath = certificatePath;
            _temporaryNodeNumber = nodeNumber;
            _temporaryTrustPeers = trustPeers.ToArray();
        }
    }

    public void Clear() {
        lock (_sync) {
            _temporaryCertificatePath = null;
            _temporaryNodeNumber = 0;
            _temporaryTrustPeers = [];
            _temporaryAwgPrivateKey = null;
            _temporaryCertificatePem = null;
        }
    }
}