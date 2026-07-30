# Build from the REPO ROOT, not this folder: docker build -f AlgoForge/Dockerfile -t algoforge-web .
# (paths below are relative to that root, so the other projects can be added to the
# restore/copy steps later without moving this file).
#
# Multi-stage: the SDK image (large, has the compiler) builds the app; only the published
# output and the much smaller ASP.NET runtime image actually ship.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY AlgoForge/AlgoForge.csproj AlgoForge/
RUN dotnet restore AlgoForge/AlgoForge.csproj

COPY AlgoForge/ AlgoForge/
RUN dotnet publish AlgoForge/AlgoForge.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# App_Data is where uploads and (in Development only) the email outbox live. On most
# hosts this container's filesystem is wiped on every redeploy or restart -- this
# directory existing is enough to run, but real uploaded photos need to move to actual
# object storage (Azure Blob Storage or equivalent) before this is relied on past a demo.
RUN mkdir -p /app/App_Data

# Render injects $PORT and expects the app to listen there; Azure App Service instead
# reads a fixed port from the WEBSITES_PORT app setting, which you point at 8080 to match
# the default below. Falling back to 8080 when $PORT is unset covers both.
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "dotnet AlgoForge.dll --urls http://+:${PORT:-8080}"]
