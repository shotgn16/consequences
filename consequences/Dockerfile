# Consequences: build once, run anywhere (Docker, Home Assistant App, Kubernetes...)
# Used both by `docker build ./consequences` and by the Home Assistant Supervisor.

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Consequences/Consequences.csproj src/Consequences/
RUN dotnet restore src/Consequences/Consequences.csproj
COPY src/ src/
RUN dotnet publish src/Consequences/Consequences.csproj -c Release -o /app --no-restore /p:UseAppHost=false

# ---- run ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# /data holds settings.json and the Hall of Fame. Home Assistant mounts its own /data here.
RUN mkdir -p /data && chown app:app /data

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_gcServer=0 \
    CONSEQ_DATA_PATH=/data

EXPOSE 8080

LABEL org.opencontainers.image.title="Consequences" \
      org.opencontainers.image.description="The folded-paper party game, online, with drawing" \
      io.hass.type="app" \
      io.hass.version="1.0.0"

ENTRYPOINT ["dotnet", "Consequences.dll"]
