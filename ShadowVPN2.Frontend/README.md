# ShadowVPN2 Frontend

This project owns the application UI (nodes, clients, setup, subscriptions and
administration) as a Razor Class Library. Its components run as interactive
server-side Blazor components in the executable host.

The executable host remains `ShadowVPN2/ShadowVPN2.csproj`. Its project reference
to this project includes the Razor components and static assets, so
the normal host build and publish commands produce one runnable application:

```sh
dotnet run --project ShadowVPN2/ShadowVPN2.csproj
dotnet publish ShadowVPN2/ShadowVPN2.csproj -c Release
```

The host owns backend dependency injection, Identity, RavenDB, REST endpoints,
and SignalR. Frontend service interfaces are defined in this project and their
implementations are registered by the host over the existing backend services.
The Razor application shell, routeable pages, layouts, account UI and frontend
static assets are owned by this project. The host remains responsible for the
Identity runtime, login/passkey/external-provider endpoints and provider callbacks.

Application routes are served at the application root from this assembly. The
frontend does not call its own REST API for internal UI operations and cannot be
started independently; the executable host is the only runnable project.
