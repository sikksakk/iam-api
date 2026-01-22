# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY iam-api.csproj .
RUN dotnet restore

# Copy everything else and build
COPY . .
RUN dotnet publish -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Copy published app
COPY --from=build /app/publish .

# Expose ports - Azure App Service uses 8080
EXPOSE 8080
EXPOSE 80

# Azure App Service will set WEBSITES_PORT=8080 automatically
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "iam-api.dll"]
