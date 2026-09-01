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
RUN curl -Lo /usr/local/bin/sing-box https://github.com/hoaxisr/amnezia-box/releases/download/1.14.0-rc.1-awgm.15/singbox-1.14.0-rc.1-awgm.15-linux-amd64 \
    && chmod +x /usr/local/bin/sing-box \
    && setcap cap_net_admin,cap_net_raw=+ep /usr/local/bin/sing-box

# Ensure Data directory exists and has correct permissions for volumes
RUN mkdir -p /data && chown -R $APP_UID:$APP_UID /data

USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["ShadowVPN2/ShadowVPN2.csproj", "ShadowVPN2/"]
RUN dotnet restore "ShadowVPN2/ShadowVPN2.csproj"
COPY . .
WORKDIR "/src/ShadowVPN2"
RUN dotnet build "./ShadowVPN2.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS dev
COPY --from=base /usr/local/bin/sing-box /usr/local/bin/sing-box
RUN apt-get update && apt-get install -y --no-install-recommends \
    libcap2-bin iproute2 iptables \
    && rm -rf /var/lib/apt/lists/* \
    && setcap cap_net_admin,cap_net_raw=+ep /usr/local/bin/sing-box
RUN mkdir -p /data
WORKDIR /src/ShadowVPN2
ENTRYPOINT ["dotnet", "watch", "run", "--no-launch-profile"]

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./ShadowVPN2.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENV SHADOWVPN_DATA_DIR=/data
VOLUME ["/data"]

# RavenDB Embedded creates its server package directories below the application folder.
USER root
RUN mkdir -p /app/RavenDBServer && chown -R $APP_UID:$APP_UID /app/RavenDBServer
USER $APP_UID

HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
  CMD curl -f https://localhost:8080/api/node/status || exit 1

ENTRYPOINT ["dotnet", "ShadowVPN2.dll"]
