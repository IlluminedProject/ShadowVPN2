// Adapted from RavenDB (https://github.com/IlluminedProject/ravendb), commit 46371e89d5ae3a6202e0018d1a5671b0fc6f2c79
// Copyright RavenDB Ltd.; see LICENSE.txt in this directory

using System.Security.Cryptography.X509Certificates;

namespace ShadowVPN2.Infrastructure.RavenDb.RavenEmbedded;

public class ServerOptions {
    internal static string DefaultServerDirectory = Path.Combine(AppContext.BaseDirectory, "RavenDBServer");

    internal static ServerOptions Default = new();

    public string? LogsPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "RavenDB", "Logs");

    public string? DataDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "RavenDB");

    public string? ServerDirectory { get; set; } = DefaultServerDirectory;

    public string? ServerUrl { get; set; }

    public TimeSpan GracefulShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan ProcessKillTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan MaxServerStartupTimeDuration { get; set; } = TimeSpan.FromMinutes(1);

    public List<string> CommandLineArgs { get; set; } = new();

    public SecurityOptions? Security { get; private set; }

    public string[] BootstrapAdminCertificateThumbprints { get; set; } = [];

    public LicensingOptions Licensing { get; set; } = new();

    public ServerOptions Secured(string certificate, string? certPassword = null) {
        if (certificate == null)
            throw new ArgumentNullException(nameof(certificate));

        if (Security != null) {
            throw new InvalidOperationException(
                "The security has already been setup for this ServerOptions object");
        }

        var cert = X509CertificateLoader.LoadPkcs12FromFile(certificate, certPassword);
        Security = new SecurityOptions {
            CertificatePath = certificate,
            CertificatePassword = certPassword,
            ClientCertificate = cert,
            ServerCertificateThumbprint = cert.Thumbprint
        };

        return this;
    }

    public ServerOptions Secured(string certLoadExec, string certExecArgs, string serverCertThumbprint,
        X509Certificate2 clientCert) {
        if (certLoadExec == null)
            throw new ArgumentNullException(nameof(certLoadExec));
        if (certExecArgs == null)
            throw new ArgumentNullException(nameof(certExecArgs));
        if (serverCertThumbprint == null)
            throw new ArgumentNullException(nameof(serverCertThumbprint));
        if (clientCert == null)
            throw new ArgumentNullException(nameof(clientCert));

        if (Security != null) {
            throw new InvalidOperationException(
                "The security has already been setup for this ServerOptions object");
        }

        Security = new SecurityOptions {
            ClientCertificate = clientCert,
            CertificateLoadExec = certLoadExec,
            CertificateLoadExecArguments = certExecArgs,
            ServerCertificateThumbprint = serverCertThumbprint
        };

        return this;
    }

    public sealed class SecurityOptions {
        internal SecurityOptions() { }

        public string? CertificatePath { get; internal set; }
        public string? CertificatePassword { get; internal set; }
        public X509Certificate2? ClientCertificate { get; internal set; }
        public string? CertificateLoadExec { get; internal set; }
        public string? CertificateLoadExecArguments { get; internal set; }
        public string? ServerCertificateThumbprint { get; internal set; }
    }

    public sealed class LicensingOptions {
        public string? License { get; set; }
        public string? LicensePath { get; set; }
        public bool EulaAccepted { get; set; }
        public bool DisableAutoUpdate { get; set; }
        public bool DisableAutoUpdateFromApi { get; set; }
        public bool DisableLicenseSupportCheck { get; set; } = true;
        public bool ThrowOnInvalidOrMissingLicense { get; set; }
    }
}