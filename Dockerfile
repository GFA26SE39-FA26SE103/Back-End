FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY . .
RUN dotnet restore src/Supermarket.Api/Supermarket.Api.csproj --configfile NuGet.Config
RUN dotnet publish src/Supermarket.Api/Supermarket.Api.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
USER root
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
RUN mkdir -p /app/.local/keys /app/.local/videos /app/.local/floor-plans && chown -R $APP_UID:$APP_UID /app/.local
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Supermarket.Api.dll"]
