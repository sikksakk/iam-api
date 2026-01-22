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

# Expose port 8080 for Azure App Service
EXPOSE 8080

# Set listening port for Azure
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "iam-api.dll"]
