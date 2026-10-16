FROM mcr.microsoft.com/dotnet/sdk:10.0 AS ravendb-source
ARG RAVENDB_REVISION=2b692f492db3ebfbe1143433543dc107d176c357
WORKDIR /src/ravendb
RUN test -n "$RAVENDB_REVISION" \
    && git init . \
    && git remote add origin https://github.com/IlluminedProject/ravendb.git \
    && git fetch --depth=1 origin "$RAVENDB_REVISION" \
    && git checkout --detach FETCH_HEAD

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS ravendb-build
WORKDIR /src/ravendb
COPY --from=ravendb-source /src/ravendb .
RUN --mount=type=cache,id=shadowvpn2-ravendb-nuget,target=/root/.nuget/packages,sharing=locked \
    --mount=type=cache,id=shadowvpn2-ravendb-models,target=/src/ravendb/.cache,sharing=locked \
    dotnet publish src/Raven.Server/Raven.Server.csproj -c Release -r linux-x64 \
    --self-contained false -p:RavenEnableLocalEmbeddings=false -o /out/ravendb
RUN rm -f /out/ravendb/libzstd.win.* /out/ravendb/libzstd.mac.* /out/ravendb/libzstd.arm.* \
    /out/ravendb/alpine-librdkafka.so /out/ravendb/centos8-librdkafka.so

FROM golang:1.26.8-bookworm AS singbox-build
ARG AMNEZIA_BOX_REVISION=a63f4dd4a4f44b7fea0a7d2ab630e38d610cbf7d
WORKDIR /src/amnezia-box
RUN git init . \
    && git remote add origin https://github.com/IlluminedProject/amnezia-box.git \
    && git fetch --depth=1 origin "$AMNEZIA_BOX_REVISION" \
    && git checkout --detach FETCH_HEAD
RUN --mount=type=cache,id=shadowvpn2-singbox-mod,target=/go/pkg/mod \
    --mount=type=cache,id=shadowvpn2-singbox-build,target=/root/.cache/go-build \
    CGO_ENABLED=0 go build -trimpath \
    -tags "$(cat release/DEFAULT_BUILD_TAGS),with_purego,with_awg,with_low_memory,with_trusttunnel" \
    -ldflags "-X github.com/sagernet/sing-box/constant.Version=1.15.0-alpha.9-awgm.30-awg-tun-fix $(cat release/LDFLAGS) -s -w -buildid=" \
    -o /out/sing-box ./cmd/sing-box \
    && cp "$(go list -m -f '{{.Dir}}' github.com/sagernet/cronet-go/lib/linux_amd64)/libcronet.so" /out/libcronet.so

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12 AS runtime-dependencies
USER root
RUN apt-get update && apt-get install -y --no-install-recommends \
    curl \
    ca-certificates \
    libcap2-bin \
    iproute2 \
    iptables \
    gosu \
    && rm -rf /var/lib/apt/lists/*

FROM runtime-dependencies AS binaries
COPY --chmod=755 --from=singbox-build /out/sing-box /usr/local/bin/sing-box

FROM runtime-dependencies AS runtime-base
COPY --chown=1654:1654 --from=ravendb-build /out/ravendb /opt/ravendb
COPY --chmod=755 docker/shadowvpn2-entrypoint.sh /usr/local/bin/shadowvpn2-entrypoint

# Install the pinned amnezia-box build with the AWG system TUN fix.
COPY --from=binaries /usr/local/bin/sing-box /usr/local/bin/sing-box
RUN setcap cap_net_admin,cap_net_raw,cap_net_bind_service=+ep /usr/local/bin/sing-box
COPY --from=singbox-build /out/libcronet.so /usr/local/bin/libcronet.so

COPY --chmod=755 tools/free-turn-proxy /usr/local/bin/free-turn-proxy

# Ensure Data directory exists and has correct permissions for volumes
RUN install -d -o $APP_UID -g $APP_UID /data

USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["ShadowVPN2.Contracts/ShadowVPN2.Contracts.csproj", "ShadowVPN2.Contracts/"]
COPY ["ShadowVPN2.Frontend/ShadowVPN2.Frontend.csproj", "ShadowVPN2.Frontend/"]
COPY ["ShadowVPN2/ShadowVPN2.csproj", "ShadowVPN2/"]
RUN --mount=type=cache,id=shadowvpn2-nuget,target=/root/.nuget/packages \
    dotnet restore "ShadowVPN2/ShadowVPN2.csproj" -r linux-x64
COPY . .
WORKDIR "/src/ShadowVPN2"
RUN --mount=type=cache,id=shadowvpn2-nuget,target=/root/.nuget/packages \
    dotnet restore "./ShadowVPN2.csproj" -r linux-x64 \
    && dotnet build "./ShadowVPN2.csproj" -c $BUILD_CONFIGURATION -r linux-x64 --self-contained false --no-restore

FROM runtime-base AS runtime

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS development
USER root
COPY --from=runtime-base /usr/local/bin/sing-box /usr/local/bin/sing-box
COPY --from=runtime-base /usr/local/bin/libcronet.so /usr/local/bin/libcronet.so
COPY --from=runtime-base /usr/local/bin/free-turn-proxy /usr/local/bin/free-turn-proxy
COPY --from=runtime-base /usr/local/bin/shadowvpn2-entrypoint /usr/local/bin/shadowvpn2-entrypoint
COPY --chown=1654:1654 --from=ravendb-build /out/ravendb /opt/ravendb
RUN apt-get update && apt-get install -y --no-install-recommends \
    libcap2-bin iproute2 iptables \
    gosu \
    && rm -rf /var/lib/apt/lists/* \
    && setcap cap_net_admin,cap_net_raw,cap_net_bind_service=+ep /usr/local/bin/sing-box
RUN mkdir -p /data && chmod o+x /root
WORKDIR /src/ShadowVPN2
ENTRYPOINT ["/usr/local/bin/shadowvpn2-entrypoint"]


FROM development AS debug
ARG BUILD_CONFIGURATION=Debug
WORKDIR /src
COPY ["ShadowVPN2.Contracts/ShadowVPN2.Contracts.csproj", "ShadowVPN2.Contracts/"]
COPY ["ShadowVPN2.Frontend/ShadowVPN2.Frontend.csproj", "ShadowVPN2.Frontend/"]
COPY ["ShadowVPN2/ShadowVPN2.csproj", "ShadowVPN2/"]
RUN --mount=type=cache,id=shadowvpn2-debug-nuget,target=/root/.nuget/packages \
    dotnet restore "ShadowVPN2/ShadowVPN2.csproj"
COPY . .
RUN --mount=type=cache,id=shadowvpn2-debug-nuget,target=/root/.nuget/packages \
    dotnet restore "ShadowVPN2/ShadowVPN2.csproj" \
    && dotnet build "ShadowVPN2/ShadowVPN2.csproj" -c $BUILD_CONFIGURATION --no-restore \
    && chmod o+x /root
WORKDIR /src/ShadowVPN2
CMD ["dotnet", "/src/ShadowVPN2/bin/Debug/net10.0/ShadowVPN2.dll"]

FROM development AS hotreload
CMD ["dotnet", "watch", "run", "--no-launch-profile", "--project", "/src/ShadowVPN2/ShadowVPN2.csproj"]

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN --mount=type=cache,id=shadowvpn2-nuget,target=/root/.nuget/packages \
    dotnet restore "./ShadowVPN2.csproj" -r linux-x64 \
    && dotnet publish "./ShadowVPN2.csproj" -c $BUILD_CONFIGURATION -r linux-x64 --self-contained false -o /app/publish \
    --no-restore --no-build /p:UseAppHost=false

FROM runtime-base AS final
WORKDIR /app
COPY --chown=1654:1654 --from=publish /app/publish .
ENV SHADOWVPN_DATA_DIR=/data
VOLUME ["/data"]

USER root

HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
  CMD curl -f https://localhost:8080/api/node/status || exit 1

ENTRYPOINT ["/usr/local/bin/shadowvpn2-entrypoint"]
CMD ["dotnet", "ShadowVPN2.dll"]
