# ACR Integration Implementation Summary

## Changes Made

The IAM API now has full integration with Azure Container Registry, allowing it to:

1. **List Images and Tags**: Query ACR to discover available container images and their versions
2. **Manage Scope Maps**: Create fine-grained access policies for specific repositories
3. **Generate Tokens**: Dynamically create temporary credentials for orchestrators
4. **Track Usage**: Monitor when tokens are used and automatically clean up expired resources
5. **Remove Resources**: Delete tokens and scope maps when no longer needed

## Files Added

### Models
- **Models/AcrModels.cs**: New models for ACR integration
  - `ContainerImage`: Represents an image repository with tags
  - `AcrScopeMap`: Defines repository access permissions
  - `AcrToken`: Temporary authentication token
  - `TokenStatus`: Token lifecycle states
  - Request/Response DTOs for API operations

### Services
- **Services/IContainerRegistryService.cs**: Service interface for ACR operations
- **Services/ContainerRegistryService.cs**: Implementation using Azure Management API and ACR REST API
  - Lists repositories and tags using ACR REST API
  - Manages tokens and scope maps via Azure Management API
  - Handles authentication using Managed Identity
  - Implements automatic cleanup logic

### Documentation
- **ACR-INTEGRATION.md**: Complete guide for using ACR features
  - API endpoint documentation
  - Workflow examples
  - Security considerations
  - Configuration guide
  - Troubleshooting tips

## Files Modified

### Models
- **Models/ContainerRegistry.cs**: Extended with ACR-specific properties
  - Added `ResourceGroup`, `SubscriptionId`, `AzureResourceId`
  - Added `UseGraphManagement` flag
  - Added `RegistryType` enum (Generic vs AzureContainerRegistry)

### Services
- **Services/IDataStore.cs**: Added methods for scope maps and tokens
- **Services/InMemoryDataStore.cs**: Implemented in-memory storage for scope maps and tokens
- **Services/CosmosDbDataStore.cs**: Added Cosmos DB containers and operations for scope maps and tokens

### Controllers
- **Controllers/RegistriesController.cs**: Completely enhanced with new endpoints
  - Image listing: `/api/registries/{id}/images`
  - Tag listing: `/api/registries/{id}/images/{repository}/tags`
  - Scope map CRUD: `/api/registries/{id}/scopemaps`
  - Token management: `/api/registries/{id}/tokens`
  - Cleanup endpoint: `/api/registries/cleanup`

### Configuration
- **Program.cs**: Registered `IContainerRegistryService` with dependency injection
- **appsettings.json**: Added ACR configuration section with defaults

## New API Endpoints

### Image Discovery
- `GET /api/registries/{id}/images` - List all images and tags
- `GET /api/registries/{id}/images/{repository}/tags` - List tags for a repository

### Scope Map Management
- `POST /api/registries/{id}/scopemaps` - Create scope map
- `GET /api/registries/{id}/scopemaps` - List scope maps
- `GET /api/registries/scopemaps/{scopeMapId}` - Get scope map
- `DELETE /api/registries/scopemaps/{scopeMapId}` - Delete scope map

### Token Management
- `POST /api/registries/{id}/tokens` - Create token
- `GET /api/registries/{id}/tokens` - List tokens
- `GET /api/registries/tokens/{tokenId}` - Get token
- `POST /api/registries/tokens/{tokenId}/used` - Mark token as used
- `POST /api/registries/tokens/{tokenId}/disable` - Disable token
- `DELETE /api/registries/tokens/{tokenId}` - Delete token

### Maintenance
- `POST /api/registries/cleanup` - Clean up expired resources

## Key Features

### Dynamic Token Creation
Orchestrators can request short-lived tokens with specific scope:
```csharp
POST /api/registries/{id}/tokens
{
  "name": "job-12345-token",
  "scopeMapId": "...",
  "expiryInDays": 1,
  "assignToJobId": "..."
}
```

### Granular Access Control
Scope maps define exact permissions:
```csharp
{
  "name": "customer-alpha-readonly",
  "repositories": ["customer-scripts/alpha"],
  "actions": ["content/read"]
}
```

### Automatic Cleanup
The cleanup endpoint removes:
- Expired tokens
- Disabled tokens (>7 days old)
- Unused scope maps (no active tokens)

### Usage Tracking
Track when tokens are used for auditing:
```csharp
POST /api/registries/tokens/{tokenId}/used
```

## Security Model

1. **Managed Identity**: API uses managed identity to authenticate with Azure
2. **Required Permissions**: 
   - `AcrPull` to list images
   - `AcrDelete` to manage tokens/scope maps
   - Contributor or equivalent on ACR resource
3. **Token Expiry**: All tokens should have expiry dates
4. **Least Privilege**: Scope maps grant minimal required access
5. **No Password Reuse**: Passwords only shown once at creation

## Integration with Orchestrator

### Recommended Workflow

1. API creates a scope map for the repositories needed by a job
2. API generates a token linked to that scope map
3. API provides token credentials to orchestrator
4. Orchestrator uses token for docker login
5. Orchestrator marks token as used
6. Token automatically expires after use
7. Cleanup removes expired token and unused scope map

### Example Orchestrator Code

```csharp
// Request token from API
var token = await apiClient.CreateToken(registryId, new CreateTokenRequest
{
    Name = $"job-{jobId}",
    ScopeMapId = scopeMapId,
    ExpiryInDays = 1
});

// Use for docker login
await docker.LoginAsync(registry.Server, token.Username, token.Password);

// Pull image
await docker.PullAsync($"{registry.Server}/{image}:{tag}");

// Mark as used
await apiClient.MarkTokenUsed(token.Id);
```

## Configuration Requirements

### appsettings.json
```json
{
  "EntraId": {
    "ManagedIdentityClientId": "your-managed-identity-client-id"
  },
  "AzureContainerRegistry": {
    "DefaultSubscriptionId": "your-subscription-id",
    "CleanupIntervalHours": 24,
    "TokenExpiryDays": 30
  }
}
```

### Azure Resources
- Managed identity with ACR permissions
- Azure Container Registry with repositories
- App Registration (if not using managed identity)

## Database Changes

### Cosmos DB
Two new containers added:
- `ScopeMaps` (partitioned by registryId)
- `Tokens` (partitioned by registryId)

### In-Memory Store
Two new dictionaries:
- `_scopeMaps`: ConcurrentDictionary<Guid, AcrScopeMap>
- `_tokens`: ConcurrentDictionary<Guid, AcrToken>

## Testing Recommendations

1. **Create Registry**: Test ACR registry creation with all fields
2. **List Images**: Verify image listing from ACR
3. **Scope Maps**: Create, list, and delete scope maps
4. **Tokens**: Full lifecycle (create, use, expire, delete)
5. **Cleanup**: Verify automatic removal of expired resources
6. **Permissions**: Test with different managed identity permissions
7. **Error Handling**: Test with invalid inputs and missing permissions

## Next Steps

1. **Deploy**: Update the API deployment with new code
2. **Configure**: Set managed identity and ACR permissions
3. **Test**: Verify token creation and image listing
4. **Update Orchestrator**: Integrate token-based authentication
5. **Monitor**: Track token usage and cleanup statistics
6. **Automate**: Consider scheduled cleanup jobs

## Monitoring Points

- Number of active tokens per registry
- Token creation/deletion rate
- Cleanup statistics (tokens/scope maps removed)
- Failed authentication attempts
- API errors related to ACR operations

## Known Limitations

1. Token passwords are only shown once at creation
2. Cannot delete scope maps with active tokens
3. Requires specific managed identity permissions
4. ACR API rate limits may apply for high-volume operations

## Future Enhancements

Potential improvements:
- Automatic token creation when jobs are scheduled
- Token pooling for better performance
- Webhook integration for token expiry notifications
- Azure Key Vault integration for password storage
- Support for password regeneration without deleting token
