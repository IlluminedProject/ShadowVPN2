using System.Collections.Concurrent;

namespace ShadowVPN2.Data.Certificates;

public sealed class Http01ChallengeServer {
    private readonly ConcurrentDictionary<string, string> _challenges = new(StringComparer.Ordinal);
    private WebApplication? _application;

    public async Task StartAsync(int port, CancellationToken cancellationToken) {
        if (_application != null) throw new InvalidOperationException("HTTP-01 server is already running");

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
        builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(port));
        var application = builder.Build();
        application.MapGet("/.well-known/acme-challenge/{token}", (string token) =>
            _challenges.TryGetValue(token, out var keyAuthorization)
                ? Results.Text(keyAuthorization, "text/plain")
                : Results.NotFound());
        try {
            await application.StartAsync(cancellationToken);
            _application = application;
        }
        catch {
            await application.DisposeAsync();
            throw;
        }
    }

    public void Set(string token, string keyAuthorization) => _challenges[token] = keyAuthorization;

    public async Task StopAsync(CancellationToken cancellationToken) {
        var application = Interlocked.Exchange(ref _application, null);
        _challenges.Clear();
        if (application == null) return;
        await application.StopAsync(cancellationToken);
        await application.DisposeAsync();
    }
}
