# IAM API

A simple .NET 10 Web API for managing jobs and logs for the IAM orchestration system.

## Features

- **In-memory data storage** - No database required for this PoC
- **No authentication** - Simple and fast for development
- **Jobs API** - Create and manage jobs for the orchestrator
- **Logs API** - Containers can report execution logs
- **Web UI** - Simple frontend to create jobs and view logs
- **Containerized** - Ready for Azure App Service deployment

## API Endpoints

### Jobs
- `GET /api/jobs` - Get all jobs
- `GET /api/jobs/pending` - Get pending jobs (for orchestrator)
- `GET /api/jobs/{id}` - Get a specific job
- `POST /api/jobs` - Create a new job
- `PATCH /api/jobs/{id}/status` - Update job status

### Logs
- `GET /api/logs` - Get all logs (optional `?jobId=` filter)
- `GET /api/logs/job/{jobId}` - Get logs for a specific job
- `POST /api/logs` - Create a log entry

## Running Locally

```powershell
dotnet run
```

Navigate to `http://localhost:5000` for the web UI or `http://localhost:5000/swagger` for API documentation.

## Building Docker Image

```powershell
docker build -t iam-api .
docker run -p 8080:80 iam-api
```

## Deploying to Azure App Service

1. Build and push to Azure Container Registry:
```powershell
az acr build --registry <your-acr> --image iam-api:latest .
```

2. Create App Service and deploy:
```powershell
az webapp create --resource-group <rg> --plan <plan> --name <app-name> --deployment-container-image-name <your-acr>.azurecr.io/iam-api:latest
```

## Example Usage

### Create a Job
```bash
curl -X POST http://localhost:5000/api/jobs \
  -H "Content-Type: application/json" \
  -d '{
    "name": "User Sync",
    "customer": "alpha",
    "containerImage": "myacr.azurecr.io/customer-alpha:latest",
    "registryServer": "myacr.azurecr.io",
    "registryUsername": "myacr",
    "registryPassword": "your-acr-password",
    "scriptPath": "/scripts/sync.ps1",
    "jobType": 1,
    "schedule": "0 0 * * *",
    "parameters": {
      "tenantId": "00000000-0000-0000-0000-000000000000",
      "environment": "production"
    }
  }'
```

**Note:** Registry authentication fields (`registryServer`, `registryUsername`, `registryPassword`) are optional and only needed for private container registries like Azure Container Registry.

### Report a Log
```bash
curl -X POST http://localhost:5000/api/logs \
  -H "Content-Type: application/json" \
  -d '{
    "jobId": "00000000-0000-0000-0000-000000000000",
    "message": "Job started successfully",
    "level": 0,
    "source": "orchestrator"
  }'
```
# iam-api
