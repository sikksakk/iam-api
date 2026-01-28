# Azure Container Registry Integration

This document describes the ACR integration features that allow the API to dynamically manage container registry access through tokens and scope maps.

## Overview

The API can now:
- List available container images and tags from Azure Container Registry
- Create scope maps to define granular repository access permissions
- Generate short-lived tokens for orchestrators to pull specific images
- Automatically clean up expired tokens and unused scope maps
- Track token usage for auditing and optimization

## Prerequisites

1. **Managed Identity**: The API must have a managed identity with the following permissions on the Azure Container Registry:
   - `AcrPull` - To list images and repositories
   - `AcrDelete` - To manage tokens and scope maps
   - Contributor or equivalent role on the ACR resource

2. **Registry Configuration**: When creating an ACR-type registry, provide:
   - `Name`: The ACR registry name
   - `Server`: The ACR login server (e.g., `myregistry.azurecr.io`)
   - `SubscriptionId`: Azure subscription ID
   - `ResourceGroup`: Resource group containing the ACR
   - `Type`: Set to `AzureContainerRegistry`
   - `UseGraphManagement`: Set to `true` to enable token/scope map management

## API Endpoints

### Registry Management

#### Create ACR Registry
```http
POST /api/registries
Content-Type: application/json
Authorization: Bearer <token>

{
  "name": "myacr",
  "server": "myacr.azurecr.io",
  "type": "AzureContainerRegistry",
  "subscriptionId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "resourceGroup": "my-resource-group",
  "useGraphManagement": true
}
```

### Image Listing

#### List All Images
```http
GET /api/registries/{registryId}/images
Authorization: Bearer <token>
```

Response:
```json
{
  "registryName": "myacr",
  "retrievedAt": "2026-01-28T10:30:00Z",
  "images": [
    {
      "repository": "customer-scripts/alpha",
      "tags": ["1.0", "1.1", "latest"],
      "lastUpdateTime": "2026-01-27T14:20:00Z"
    }
  ]
}
```

#### List Image Tags
```http
GET /api/registries/{registryId}/images/{repository}/tags
Authorization: Bearer <token>
```

### Scope Map Management

Scope maps define what repositories and actions a token has access to.

#### Create Scope Map
```http
POST /api/registries/{registryId}/scopemaps
Content-Type: application/json
Authorization: Bearer <token>

{
  "name": "customer-alpha-readonly",
  "description": "Read-only access to customer alpha images",
  "repositories": [
    "customer-scripts/alpha",
    "base-images/powershell"
  ],
  "actions": ["content/read", "metadata/read"]
}
```

Available actions:
- `content/read` - Pull images
- `content/write` - Push images
- `content/delete` - Delete images
- `metadata/read` - Read image metadata
- `metadata/write` - Write image metadata

#### List Scope Maps
```http
GET /api/registries/{registryId}/scopemaps
Authorization: Bearer <token>
```

#### Get Scope Map
```http
GET /api/registries/scopemaps/{scopeMapId}
Authorization: Bearer <token>
```

#### Delete Scope Map
```http
DELETE /api/registries/scopemaps/{scopeMapId}
Authorization: Bearer <token>
```

Note: Cannot delete a scope map that has active tokens.

### Token Management

Tokens provide temporary credentials for pulling images.

#### Create Token
```http
POST /api/registries/{registryId}/tokens
Content-Type: application/json
Authorization: Bearer <token>

{
  "name": "orchestrator-token-job-123",
  "scopeMapId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "expiryInDays": 7,
  "assignToJobId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
}
```

Response includes the password (only shown once):
```json
{
  "id": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "name": "orchestrator-token-job-123",
  "username": "orchestrator-token-job-123",
  "password": "generated-password-here",
  "scopeMapId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "status": "Active",
  "createdAt": "2026-01-28T10:30:00Z",
  "expiresAt": "2026-02-04T10:30:00Z"
}
```

#### List Tokens
```http
GET /api/registries/{registryId}/tokens
Authorization: Bearer <token>
```

#### Get Token
```http
GET /api/registries/tokens/{tokenId}
Authorization: Bearer <token>
```

#### Mark Token as Used
```http
POST /api/registries/tokens/{tokenId}/used
Authorization: Bearer <token>
```

Updates the last used timestamp for tracking.

#### Disable Token
```http
POST /api/registries/tokens/{tokenId}/disable
Authorization: Bearer <token>
```

Disables the token without deleting it.

#### Delete Token
```http
DELETE /api/registries/tokens/{tokenId}
Authorization: Bearer <token>
```

Permanently deletes the token from Azure and the database.

### Cleanup

#### Manual Cleanup
```http
POST /api/registries/cleanup
Authorization: Bearer <token>
```

Runs cleanup to remove:
- Expired tokens
- Disabled tokens older than 7 days
- Unused scope maps (no associated tokens)

Response:
```json
{
  "tokensDeleted": 5,
  "scopeMapsDeleted": 2,
  "message": "Cleanup completed: 5 tokens and 2 scope maps deleted"
}
```

## Workflow Example: Job-Specific Token

Here's a typical workflow for creating a job with a dedicated token:

1. **Create a Scope Map** for the required repositories:
```http
POST /api/registries/{registryId}/scopemaps
{
  "name": "job-12345-scope",
  "description": "Access for job 12345",
  "repositories": ["customer-scripts/alpha"],
  "actions": ["content/read"]
}
```

2. **Create a Token** linked to the scope map:
```http
POST /api/registries/{registryId}/tokens
{
  "name": "job-12345-token",
  "scopeMapId": "{scopeMapId}",
  "expiryInDays": 1,
  "assignToJobId": "{jobId}"
}
```

3. **Use the Token** in the orchestrator to pull images:
```bash
docker login myacr.azurecr.io -u job-12345-token -p {password}
docker pull myacr.azurecr.io/customer-scripts/alpha:latest
```

4. **Mark as Used** when the orchestrator uses it:
```http
POST /api/registries/tokens/{tokenId}/used
```

5. **Cleanup** happens automatically or can be triggered manually:
```http
POST /api/registries/cleanup
```

## Integration with Orchestrator

The orchestrator should:

1. Before pulling images, call the token endpoint to get credentials
2. Use the token credentials for docker login
3. After successful pull, mark the token as used
4. Let the token expire naturally or delete it when done

Example orchestrator code:
```csharp
// Get token for job
var tokenResponse = await apiClient.CreateTokenAsync(registryId, new CreateTokenRequest
{
    Name = $"job-{jobId}-{DateTime.UtcNow:yyyyMMddHHmmss}",
    ScopeMapId = scopeMapId,
    ExpiryInDays = 1,
    AssignToJobId = jobId
});

// Use token for docker login
await ExecuteCommandAsync($"docker login {registry.Server} -u {tokenResponse.Username} -p {tokenResponse.Password}");

// Pull image
await ExecuteCommandAsync($"docker pull {registry.Server}/{image}:{tag}");

// Mark token as used
await apiClient.MarkTokenUsedAsync(tokenResponse.Id);

// Run job...

// Clean up (optional - will auto-expire)
await apiClient.DeleteTokenAsync(tokenResponse.Id);
```

## Security Considerations

1. **Token Expiry**: Always set appropriate expiry times (1-7 days recommended)
2. **Least Privilege**: Create scope maps with minimal required permissions
3. **Token Rotation**: Don't reuse tokens across jobs
4. **Password Storage**: Token passwords are only shown once - store securely
5. **Cleanup**: Regularly run cleanup to remove expired resources

## Configuration

In `appsettings.json`:

```json
{
  "AzureContainerRegistry": {
    "DefaultSubscriptionId": "your-subscription-id",
    "CleanupIntervalHours": 24,
    "TokenExpiryDays": 30
  },
  "EntraId": {
    "ManagedIdentityClientId": "your-managed-identity-client-id"
  }
}
```

## Monitoring

Track the following metrics:
- Number of active tokens per registry
- Token usage patterns (via lastUsedAt)
- Cleanup statistics (tokens/scope maps deleted)
- Failed token operations (check logs)

## Troubleshooting

### "Failed to get ACR refresh token"
- Verify managed identity has AcrPull permission
- Check that the registry server is correct
- Ensure managed identity is properly configured

### "Cannot delete scope map as it has associated tokens"
- Disable or delete all tokens using the scope map first
- Then delete the scope map

### "Registry must have SubscriptionId and ResourceGroup configured"
- Update the registry record to include these fields
- Required for Azure Management API operations

## Future Enhancements

Potential improvements:
- Automatic token creation when jobs are created
- Token pooling for better performance
- Support for password regeneration
- Webhook notifications for token expiry
- Integration with Azure Key Vault for password storage
