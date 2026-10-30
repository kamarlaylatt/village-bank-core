# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore on its own layer, so it only reruns when a project file changes.
COPY src/VillageBank.Api/VillageBank.Api.csproj src/VillageBank.Api/
RUN dotnet restore src/VillageBank.Api/VillageBank.Api.csproj

COPY src/ src/
RUN dotnet publish src/VillageBank.Api/VillageBank.Api.csproj -c Release -o /app/publish --no-restore

# Debian based runtime image: it includes tzdata, which the app needs for the Asia/Vientiane business day.
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

# The aspnet image ships a non-root user named "app".
USER app

# Anything after the image name is passed to the app, so
# `docker compose run --rm api migrate` runs the migrate command instead of the web server.
ENTRYPOINT ["dotnet", "VillageBank.Api.dll"]
