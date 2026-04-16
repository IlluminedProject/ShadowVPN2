FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["ShadowVPN2/ShadowVPN2.csproj", "ShadowVPN2/"]
RUN dotnet restore "ShadowVPN2/ShadowVPN2.csproj"
COPY . .
WORKDIR "/src/ShadowVPN2"
RUN dotnet build "./ShadowVPN2.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./ShadowVPN2.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
# Ensure Data directory exists and has correct permissions
RUN mkdir -p /app/Data && chown -R $APP_UID:$APP_UID /app/Data
VOLUME ["/app/Data"]
ENTRYPOINT ["dotnet", "ShadowVPN2.dll"]
