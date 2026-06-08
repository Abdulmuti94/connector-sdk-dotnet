# Build context: this directory (bc-connector/)
# docker build -t bc-connector .

# ── Stage 1: build ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY . .

RUN dotnet publish BcConnector.csproj \
    -c Release \
    -o /app \
    --no-self-contained

# ── Stage 2: runtime ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
WORKDIR /app

COPY --from=build /app .

# Run as non-root ($APP_UID is set by the base image, defaults to 1654).
USER $APP_UID

ENTRYPOINT ["dotnet", "BcConnector.dll"]
