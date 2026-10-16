using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using ShadowVPN2.Data.SingBox.Models;

namespace ShadowVPN2.Data.SingBox;

public sealed class NaiveProxySharedPortExtension(IServer server) : ISingBoxExtension {
    private readonly Dictionary<int, int> backendPorts = [];
    private readonly Dictionary<(string Certificate, string Key), X509Certificate2> certificates = [];
    private Dictionary<int, (int BackendPort, X509Certificate2 Certificate)> activeRoutes = [];
    private Dictionary<int, (int BackendPort, X509Certificate2 Certificate)> pendingRoutes = [];

    public Task ConfigureAsync(SingBoxContext context, CancellationToken cancellationToken) {
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
        var routes = new Dictionary<int, (int BackendPort, X509Certificate2 Certificate)>();
        foreach (var inbound in context.Config.Inbounds.OfType<NaiveProxyInboundConfig>()) {
            cancellationToken.ThrowIfCancellationRequested();
            if (inbound.ListenPort is not { } publicPort || inbound.Network != "tcp" || inbound.Users.Count == 0 ||
                inbound.Tls is not { Enabled: true } tls ||
                addresses is null || !addresses.Any(address =>
                    Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
                    uri.Port == inbound.ListenPort)) continue;

            if (!backendPorts.TryGetValue(publicPort, out var backendPort)) {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                backendPort = ((IPEndPoint)socket.LocalEndPoint!).Port;
                backendPorts.Add(publicPort, backendPort);
            }

            var pem = (Certificate: string.Join('\n', tls.Certificate ??
                                                      throw new InvalidOperationException(
                                                          "Shared NaiveProxy listener requires a PEM certificate")),
                Key: string.Join('\n', tls.Key ??
                                       throw new InvalidOperationException(
                                           "Shared NaiveProxy listener requires a PEM key")));
            if (!certificates.TryGetValue(pem, out var certificate)) {
                certificate = X509Certificate2.CreateFromPem(pem.Certificate, pem.Key);
                if (OperatingSystem.IsWindows()) {
                    using var pemCertificate = certificate;
                    certificate = X509CertificateLoader.LoadPkcs12(pemCertificate.Export(X509ContentType.Pfx), null,
                        X509KeyStorageFlags.Exportable);
                }

                certificates.Add(pem, certificate);
            }

            inbound.Listen = "127.0.0.1";
            inbound.ListenPort = backendPort;
            inbound.Tls = null;
            routes.Add(publicPort, (backendPort, certificate));
        }

        pendingRoutes = routes;
        return Task.CompletedTask;
    }

    public Task ApplyAsync(SingBoxContext context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var routes = new Dictionary<int, (int BackendPort, X509Certificate2 Certificate)>();
        foreach (var (publicPort, route) in pendingRoutes) {
            if (context.Config.Inbounds.OfType<NaiveProxyInboundConfig>().Any(inbound =>
                    inbound.Network == "tcp" && inbound.Listen == "127.0.0.1" &&
                    inbound.ListenPort == route.BackendPort && inbound.Tls is null && inbound.Users.Count > 0))
                routes.Add(publicPort, route);
        }

        Volatile.Write(ref activeRoutes, routes);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() {
        Volatile.Write(ref activeRoutes, []);
        pendingRoutes.Clear();
        foreach (var certificate in certificates.Values) certificate.Dispose();
        certificates.Clear();
        return ValueTask.CompletedTask;
    }

    public bool TryGetBackend(int publicPort, out int backendPort) {
        if (Volatile.Read(ref activeRoutes).TryGetValue(publicPort, out var route)) {
            backendPort = route.BackendPort;
            return true;
        }

        backendPort = 0;
        return false;
    }

    public X509Certificate2? GetFallbackCertificate(int publicPort) {
        return Volatile.Read(ref activeRoutes).TryGetValue(publicPort, out var route) ? route.Certificate : null;
    }
}