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

# Expose port 8080 for Azure App Service (set WEBSITES_PORT=8080 in App Settings)
EXPOSE 8080

# Default listening port inside the container
ENV ASPNETCORE_URLS=http://+:8080

# App Service runs behind a reverse proxy; trust forwarded headers.
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true

ENTRYPOINT ["dotnet", "iam-api.dll"]
