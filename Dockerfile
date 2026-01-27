# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy API project file and restore
COPY iam-api.csproj .
RUN dotnet restore

# Copy Blazor WASM project file and restore
COPY Client/Client.csproj Client/
RUN dotnet restore Client/Client.csproj

# Copy API source and build (skip automatic client build)
COPY . .
RUN dotnet publish -c Release -o /app/publish /p:SkipClientBuild=true

# Build Blazor WASM separately and copy wwwroot to publish output
RUN dotnet publish Client/Client.csproj -c Release -o /tmp/client-build && \
    cp -r /tmp/client-build/wwwroot /app/publish/wwwroot

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Copy published app
COPY --from=build /app/publish .

# Expose port 80 for Azure Container Apps
EXPOSE 80

# Default listening port inside the container
ENV ASPNETCORE_URLS=http://+:80

# Container Apps runs behind a reverse proxy; trust forwarded headers.
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true

ENTRYPOINT ["dotnet", "iam-api.dll"]
