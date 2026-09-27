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
Identity's login/passkey/external-provider and account-management pages remain
host-owned because they issue and manage the Identity cookie and provider callbacks.

Application routes are served at the application root. The frontend does not
call its own REST API for internal UI operations.
