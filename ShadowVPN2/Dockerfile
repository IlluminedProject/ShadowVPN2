FROM mcr.microsoft.com/dotnet/aspnet:10.0.8 AS base
USER root
RUN apt-get update && apt-get install -y --no-install-recommends \
    curl \
    ca-certificates \
    libcap2-bin \
    iproute2 \
    iptables \
    && rm -rf /var/lib/apt/lists/*

# Download and install amnezia-box (sing-box fork)
ARG AMNEZIA_BOX_VERSION=1.14.1-awgm.17
RUN curl -fLo /usr/local/bin/sing-box https://github.com/hoaxisr/amnezia-box/releases/download/${AMNEZIA_BOX_VERSION}/singbox-${AMNEZIA_BOX_VERSION}-linux-amd64 \
    && chmod +x /usr/local/bin/sing-box \
    && setcap cap_net_admin,cap_net_raw=+ep /usr/local/bin/sing-box

ARG FREETURN_VERSION=v3.4.0
RUN curl -fLo /usr/local/bin/free-turn-proxy \
        https://github.com/samosvalishe/free-turn-proxy/releases/download/${FREETURN_VERSION}/server-linux-amd64 \
    && chmod +x /usr/local/bin/free-turn-proxy

# Ensure Data directory exists and has correct permissions for volumes
RUN install -d -o $APP_UID -g $APP_UID /data

USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["ShadowVPN2/ShadowVPN2.csproj", "ShadowVPN2/"]
COPY . .
RUN --mount=type=cache,id=shadowvpn2-nuget,target=/root/.nuget/packages \
    dotnet restore "ShadowVPN2/ShadowVPN2.csproj"
WORKDIR "/src/ShadowVPN2"
RUN --mount=type=cache,id=shadowvpn2-nuget,target=/root/.nuget/packages \
    dotnet build "./ShadowVPN2.csproj" -c $BUILD_CONFIGURATION --no-restore

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dev
USER root
COPY --from=base /usr/local/bin/sing-box /usr/local/bin/sing-box
COPY --from=base /usr/local/bin/free-turn-proxy /usr/local/bin/free-turn-proxy
RUN apt-get update && apt-get install -y --no-install-recommends \
    libcap2-bin iproute2 iptables \
    && rm -rf /var/lib/apt/lists/* \
    && setcap cap_net_admin,cap_net_raw=+ep /usr/local/bin/sing-box
RUN mkdir -p /data
WORKDIR /src/ShadowVPN2
ENTRYPOINT ["dotnet", "watch", "run", "--no-launch-profile"]

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN --mount=type=cache,id=shadowvpn2-nuget,target=/root/.nuget/packages \
    dotnet publish "./ShadowVPN2.csproj" -c $BUILD_CONFIGURATION -o /app/publish \
    --no-restore --no-build /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --chown=1654:1654 --from=publish /app/publish .
ENV SHADOWVPN_DATA_DIR=/data
VOLUME ["/data"]

# RavenDB Embedded creates its server package directories below the application folder.
USER root
RUN install -d -o 1654 -g 1654 /app/RavenDBServer
USER $APP_UID

HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
  CMD curl -f https://localhost:8080/api/node/status || exit 1

ENTRYPOINT ["dotnet", "ShadowVPN2.dll"]
