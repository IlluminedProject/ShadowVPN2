FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
ARG TARGETARCH
RUN apk add --no-cache clang build-base zlib-dev
WORKDIR /src
COPY ShadowVPN2.Contracts/ShadowVPN2.Contracts.csproj ShadowVPN2.Contracts/
COPY ShadowVPN2.Updater/ShadowVPN2.Updater.csproj ShadowVPN2.Updater/
RUN dotnet restore ShadowVPN2.Updater/ShadowVPN2.Updater.csproj -a ${TARGETARCH}
COPY ShadowVPN2.Contracts ShadowVPN2.Contracts
COPY ShadowVPN2.Updater ShadowVPN2.Updater
RUN dotnet publish ShadowVPN2.Updater/ShadowVPN2.Updater.csproj -c Release -a ${TARGETARCH} -o /out -p:PublishAot=true -p:StripSymbols=true -p:IlcOptimizationPreference=Size -p:DebugType=None -p:DebugSymbols=false

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine AS runtime
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
    DOTNET_EnableDiagnostics=0
WORKDIR /app
COPY --from=build /out .
ENTRYPOINT ["/app/ShadowVPN2.Updater"]
