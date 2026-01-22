# Azure App Service Deployment Guide

This guide explains how to deploy the IAM API to Azure App Service using the container image from Azure Container Registry (ACR).

## Prerequisites

- Azure subscription
- Azure Container Registry (ACR) with the `iam-api` image
- Azure CLI installed locally (optional, for CLI deployment)

## GitHub Actions Setup

### 1. Configure GitHub Secrets

Add the following secrets to your GitHub repository:

1. Go to your repository → Settings → Secrets and variables → Actions
2. Add these secrets:
   - `ACR_USERNAME`: Your ACR username (usually the ACR name)
   - `ACR_PASSWORD`: Your ACR password (from Access Keys in Azure Portal)

### 2. Automatic Builds

The GitHub Actions workflow (`.github/workflows/build-and-push.yml`) will automatically:
- Build the Docker image on every push to main/master
- Tag with both `latest` and the git commit SHA
- Push to your ACR at `fortytwoiam.azurecr.io/iam-api:latest`

You can also manually trigger the workflow from the Actions tab.

## Azure App Service Deployment

### Option 1: Azure Portal (Web UI)

#### Step 1: Create App Service

1. Go to [Azure Portal](https://portal.azure.com)
2. Click **Create a resource** → **Web App**
3. Fill in the basic details:
   - **Subscription**: Select your subscription
   - **Resource Group**: Create new or use existing
   - **Name**: `iam-api` (or your preferred name)
   - **Publish**: **Docker Container**
   - **Operating System**: **Linux**
   - **Region**: Choose your preferred region
   - **Pricing Plan**: Choose based on your needs (B1 or higher recommended)

4. Click **Next: Docker**

#### Step 2: Configure Container

1. **Options**: Single Container
2. **Image Source**: Azure Container Registry
3. **Registry**: Select `fortytwoiam`
4. **Image**: `iam-api`
5. **Tag**: `latest`
6. **Startup Command**: Leave empty (uses Dockerfile ENTRYPOINT)

7. Click **Review + create** → **Create**

#### Step 3: Configure Application Settings

Once deployed, configure the container settings:

1. Go to your App Service → **Configuration** → **General settings**
2. **Startup Command**: Leave empty (uses container ENTRYPOINT)
3. **App settings** (required):
   ```
   WEBSITES_PORT = 8080
   WEBSITES_ENABLE_APP_SERVICE_STORAGE = false
   ASPNETCORE_ENVIRONMENT = Production
   ```
4. Click **Save**

**Important**: `WEBSITES_ENABLE_APP_SERVICE_STORAGE=false` prevents Azure from mounting persistent storage at `/home/site/wwwroot`, which would overwrite your container's app files.

#### Step 4: Enable Continuous Deployment (Optional)

1. Go to **Deployment Center**
2. **Container settings** → Enable **Continuous Deployment**: **On**
3. Copy the **Webhook URL**
4. In your ACR, go to **Webhooks** → **Add**
   - **Name**: `iam-api-webhook`
   - **Service URI**: Paste the webhook URL
   - **Actions**: `push`
   - **Scope**: `iam-api:latest`
5. Click **Create**

Now every time a new image is pushed to ACR, your App Service will automatically update.

### Option 2: Azure CLI

```bash
# Variables
RESOURCE_GROUP="iam-rg"
APP_NAME="iam-api"
ACR_NAME="fortytwoiam"
LOCATION="westeurope"
PLAN_NAME="iam-plan"

# Create resource group
az group create --name $RESOURCE_GROUP --location $LOCATION

# Create App Service Plan (Linux, B1 tier)
az appservice plan create \
  --name $PLAN_NAME \
  --resource-group $RESOURCE_GROUP \
  --is-linux \
  --sku B1

# Create App Service with ACR image
az webapp create \
  --resource-group $RESOURCE_GROUP \
  --plan $PLAN_NAME \
  --name $APP_NAME \
  --deployment-container-image-name $ACR_NAME.azurecr.io/iam-api:latest

# Configure ACR credentials
az webapp config container set \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --docker-custom-image-name $ACR_NAME.azurecr.io/iam-api:latest \
  --docker-registry-server-url https://$ACR_NAME.azurecr.io \
  --docker-registry-server-user <ACR_USERNAME> \
  --docker-registry-server-password <ACR_PASSWORD>

# Set required application settings
az webapp config appsettings set \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --settings \
    WEBSITES_PORT=8080 \
    WEBSITES_ENABLE_APP_SERVICE_STORAGE=false \
    ASPNETCORE_ENVIRONMENT=Production

# Enable continuous deployment webhook
az webapp deployment container config \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --enable-cd true

# Get the webhook URL and configure it in ACR
az webapp deployment container show-cd-url \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP
```

### Option 3: Using Service Principal (Recommended for Production)

Instead of using ACR admin credentials, use a service principal:

```bash
# Get ACR resource ID
ACR_ID=$(az acr show --name $ACR_NAME --query id --output tsv)

# Create service principal with pull permissions
SP_OUTPUT=$(az ad sp create-for-rbac --name iam-api-sp --role acrpull --scope $ACR_ID)

# Extract credentials
SP_APP_ID=$(echo $SP_OUTPUT | jq -r '.appId')
SP_PASSWORD=$(echo $SP_OUTPUT | jq -r '.password')

# Configure App Service with service principal
az webapp config container set \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --docker-custom-image-name $ACR_NAME.azurecr.io/iam-api:latest \
  --docker-registry-server-url https://$ACR_NAME.azurecr.io \
  --docker-registry-server-user $SP_APP_ID \
  --docker-registry-server-password $SP_PASSWORD
```

## Post-Deployment

### Access Your API

Your API will be available at:
```
https://iam-api.azurewebsites.net
```

Test the Swagger UI:
```
https://iam-api.azurewebsites.net/swagger
```

### Monitor Logs

View application logs:
```bash
az webapp log tail --name $APP_NAME --resource-group $RESOURCE_GROUP
```

Or in the Azure Portal:
1. Go to your App Service
2. **Monitoring** → **Log stream**

### Custom Domain (Optional)

1. Go to **Custom domains**
2. Add your custom domain
3. Configure DNS settings as instructed
4. Add SSL certificate (free with App Service Managed Certificate)

## Scaling

### Scale Up (Vertical)
Change to a higher tier (e.g., S1, P1V2):
```bash
az appservice plan update \
  --name $PLAN_NAME \
  --resource-group $RESOURCE_GROUP \
  --sku S1
```

### Scale Out (Horizontal)
Increase the number of instances:
```bash
az appservice plan update \
  --name $PLAN_NAME \
  --resource-group $RESOURCE_GROUP \
  --number-of-workers 3
```

### Auto-scaling
1. Go to your App Service Plan
2. **Scale out (App Service plan)**
3. Enable **Custom autoscale**
4. Configure scale rules based on CPU, memory, or custom metrics

## Troubleshooting

### Container fails to start

Check logs:
```bash
az webapp log tail --name $APP_NAME --resource-group $RESOURCE_GROUP
```

### Common Issues

1. **Port mismatch**: Ensure `WEBSITES_PORT=8080` is set in Application Settings
2. **ACR credentials**: Verify credentials are correct
3. **Image not found**: Check image name and tag in ACR
4. **Persistent storage mount**: If `WEBSITES_ENABLE_APP_SERVICE_STORAGE=true`, Azure mounts over `/home/site/wwwroot`. Set to `false` for custom containers.
5. **App not found**: Container uses `/app` for app files, not `/home/site/wwwroot`
6. **Health check failures**: Add health check endpoint if needed

### Health Checks

Add a health check endpoint (optional):
1. Go to **Health check**
2. Enable health check
3. Path: `/health` (if you implement a health endpoint)

## Cost Optimization

- **Development**: Use B1 tier (~$13/month)
- **Production**: Use S1 or P1V2 tier (better performance)
- **Idle apps**: Consider stopping when not in use
- **Reserved instances**: Save up to 55% with 1-year or 3-year reservations

## Related Documentation

- [Azure App Service Documentation](https://docs.microsoft.com/en-us/azure/app-service/)
- [Deploy a custom container](https://docs.microsoft.com/en-us/azure/app-service/quickstart-custom-container)
- [Azure Container Registry](https://docs.microsoft.com/en-us/azure/container-registry/)
