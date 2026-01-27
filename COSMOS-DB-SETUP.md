# Cosmos DB Setup Guide

## Overview

The IAM API now supports persistent data storage using Azure Cosmos DB. By default, the application uses in-memory storage (data is lost on restart). Configure Cosmos DB to persist:

- Jobs
- Logs
- Orchestrators
- Container Registries
- Customers

## Quick Setup

### 1. Create Cosmos DB Account

```bash
# Variables
RESOURCE_GROUP="your-resource-group"
COSMOS_ACCOUNT="iam-cosmosdb-account"
LOCATION="norwayeast"

# Create Cosmos DB account (serverless is cost-effective for small workloads)
az cosmosdb create \
  --name $COSMOS_ACCOUNT \
  --resource-group $RESOURCE_GROUP \
  --locations regionName=$LOCATION \
  --capabilities EnableServerless \
  --default-consistency-level Session
```

### 2. Get Connection String

```bash
# Get primary connection string
az cosmosdb keys list \
  --name $COSMOS_ACCOUNT \
  --resource-group $RESOURCE_GROUP \
  --type connection-strings \
  --query "connectionStrings[0].connectionString" -o tsv
```

### 3. Configure Application

#### Option A: Using Azure Container Apps Secrets (Recommended)

```bash
# Add secret to Container App
az containerapp secret set \
  --name iam-api \
  --resource-group $RESOURCE_GROUP \
  --secrets cosmosdb-connection-string="<YOUR_CONNECTION_STRING>"

# Set environment variable
az containerapp update \
  --name iam-api \
  --resource-group $RESOURCE_GROUP \
  --set-env-vars CosmosDb__ConnectionString=secretref:cosmosdb-connection-string
```

#### Option B: Using appsettings (Development Only)

Update `appsettings.Development.json`:

```json
{
  "CosmosDb": {
    "ConnectionString": "AccountEndpoint=https://iam-cosmosdb-account.documents.azure.com:443/;AccountKey=your-key-here;",
    "DatabaseName": "IamDb"
  }
}
```

⚠️ **Never commit connection strings to source control!**

### 4. Database Initialization

The application automatically:
- Creates the database `IamDb` if it doesn't exist
- Creates containers with appropriate partition keys:
  - `Jobs` (partition key: `/id`)
  - `Logs` (partition key: `/jobId`)
  - `Orchestrators` (partition key: `/id`)
  - `Registries` (partition key: `/id`)
  - `Customers` (partition key: `/id`)

## Configuration Options

### appsettings.json

```json
{
  "CosmosDb": {
    "ConnectionString": "",           // Leave empty to use in-memory storage
    "DatabaseName": "IamDb"           // Database name (created automatically)
  }
}
```

### Environment Variables

- `CosmosDb__ConnectionString` - Full connection string
- `CosmosDb__DatabaseName` - Database name (optional, defaults to "IamDb")

## Verification

Check the console output when the application starts:

```
✓ Using Cosmos DB data store (Database: IamDb)
```

Or if Cosmos DB is not configured:

```
⚠ Using IN-MEMORY data store (data will not persist). Configure CosmosDb:ConnectionString to use Cosmos DB.
```

## Cost Optimization

### Serverless vs Provisioned Throughput

**Serverless** (Recommended for small workloads):
- Pay per request (RU/s consumed)
- No minimum cost
- Best for: dev/test, small production workloads with variable traffic

**Provisioned Throughput**:
- Fixed RU/s allocation (minimum 400 RU/s per container = ~$24/month)
- Best for: predictable, consistent traffic

### Initial Container Throughput

Containers are created with **400 RU/s** (minimum for manual throughput). This is approximately:
- $0.008/hour = $5.76/month per container
- 5 containers = ~$29/month

To reduce costs, switch to **serverless** or use **autoscale**:

```bash
# Convert to serverless (requires recreating account)
# Or use autoscale (scales down to 10% of max)
az cosmosdb sql container throughput update \
  --account-name $COSMOS_ACCOUNT \
  --resource-group $RESOURCE_GROUP \
  --database-name IamDb \
  --name Jobs \
  --max-throughput 1000  # Max RU/s, scales down to 100 RU/s
```

## Monitoring

### View Data in Azure Portal

1. Navigate to Cosmos DB account
2. Go to **Data Explorer**
3. Expand database **IamDb**
4. Browse containers and query data

### Query Examples

```sql
-- Get all pending jobs
SELECT * FROM c WHERE c.status = "Pending" ORDER BY c.createdAt ASC

-- Get recent logs for a job
SELECT * FROM c WHERE c.jobId = "your-job-id" ORDER BY c.timestamp DESC

-- Get active orchestrators (heartbeat within 1 minute)
SELECT * FROM c WHERE c.lastHeartbeat >= DateTimeAdd("minute", -1, GetCurrentDateTime())
```

## Backup and Recovery

Cosmos DB provides:
- **Continuous backup** (default for serverless)
- Point-in-time restore up to 30 days
- Geo-redundancy (optional)

### Enable Geo-Redundancy

```bash
az cosmosdb update \
  --name $COSMOS_ACCOUNT \
  --resource-group $RESOURCE_GROUP \
  --locations regionName=norwayeast failoverPriority=0 \
               regionName=westeurope failoverPriority=1
```

## Migration from In-Memory

When switching from in-memory to Cosmos DB:
1. No data migration needed (in-memory data is ephemeral)
2. Update configuration with connection string
3. Restart application
4. Database and containers are created automatically
5. Start creating new jobs/customers

## Troubleshooting

### Connection Issues

Check:
1. Connection string is correct (from Azure Portal)
2. Firewall rules allow your IP (Azure Container Apps uses Azure network)
3. Application logs show "Using Cosmos DB data store"

### Performance Issues

- Check RU consumption in Azure Portal metrics
- Consider increasing throughput or switching to autoscale
- Review partition key strategy (current setup optimized for typical usage)

### Data Not Persisting

Verify:
1. `CosmosDb__ConnectionString` environment variable is set
2. Application logs show successful Cosmos DB initialization
3. No error logs related to Cosmos DB operations

## Security Best Practices

1. ✅ Use **Azure Container App secrets** for connection strings
2. ✅ Enable **firewall** rules (allow Azure services)
3. ✅ Use **Managed Identity** when possible (requires additional setup)
4. ❌ Never commit connection strings to source control
5. ✅ Regularly rotate **account keys**
6. ✅ Enable **diagnostic logging** for auditing

## Advanced: Managed Identity Integration

For enhanced security, use Managed Identity instead of connection strings:

```csharp
// Future enhancement - not yet implemented
var credential = new DefaultAzureCredential();
var cosmosClient = new CosmosClient(
    accountEndpoint: "https://iam-cosmosdb-account.documents.azure.com:443/",
    tokenCredential: credential
);
```

Requires assigning Container App's Managed Identity the **Cosmos DB Built-in Data Contributor** role.
