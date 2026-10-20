# Build from the repository root, not from backend/Project1.Api.
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /source

COPY dotnet-tools.json ./
COPY backend/Project1.Api/Project1.Api.csproj backend/Project1.Api/
RUN dotnet restore backend/Project1.Api/Project1.Api.csproj

COPY backend/Project1.Api/ backend/Project1.Api/
RUN dotnet build backend/Project1.Api/Project1.Api.csproj --configuration Release --no-restore

# Optional one-off tool for the independent LOCAL practice profile.
# Production runtime applies pending migrations in Program.cs when explicitly enabled.
FROM build AS migrations
RUN dotnet tool restore
ENTRYPOINT ["dotnet", "tool", "run", "dotnet-ef", "--"]

FROM build AS publish
RUN dotnet publish backend/Project1.Api/Project1.Api.csproj \
    --configuration Release --no-build --no-restore --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
LABEL org.project1.startup-migrations="1"
USER root
RUN apt-get update \
    && apt-get install --yes --no-install-recommends fonts-dejavu-core curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=publish /app/publish/ ./
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Project1.Api.dll"]
