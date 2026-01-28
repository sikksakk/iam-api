using IamApi.Models;

namespace IamApi.Services;

public interface IContainerRegistryService
{
    /// <summary>
    /// List all container images and their tags in a registry
    /// </summary>
    Task<ContainerImageListResponse> ListImagesAsync(Guid registryId);
    
    /// <summary>
    /// List images in a specific repository
    /// </summary>
    Task<List<string>> ListImageTagsAsync(Guid registryId, string repository);
    
    /// <summary>
    /// Create a new scope map with specified repository access
    /// </summary>
    Task<AcrScopeMap> CreateScopeMapAsync(Guid registryId, CreateScopeMapRequest request);
    
    /// <summary>
    /// Get a scope map by ID
    /// </summary>
    Task<AcrScopeMap?> GetScopeMapAsync(Guid scopeMapId);
    
    /// <summary>
    /// List all scope maps for a registry
    /// </summary>
    Task<List<AcrScopeMap>> ListScopeMapsAsync(Guid registryId);
    
    /// <summary>
    /// Delete a scope map (only if no tokens are using it)
    /// </summary>
    Task<bool> DeleteScopeMapAsync(Guid scopeMapId);
    
    /// <summary>
    /// Create a new token with specified scope map
    /// </summary>
    Task<TokenResponse> CreateTokenAsync(Guid registryId, CreateTokenRequest request);
    
    /// <summary>
    /// Get a token by ID
    /// </summary>
    Task<AcrToken?> GetTokenAsync(Guid tokenId);
    
    /// <summary>
    /// List all tokens for a registry
    /// </summary>
    Task<List<AcrToken>> ListTokensAsync(Guid registryId);
    
    /// <summary>
    /// Disable a token (soft delete)
    /// </summary>
    Task<bool> DisableTokenAsync(Guid tokenId);
    
    /// <summary>
    /// Delete a token permanently
    /// </summary>
    Task<bool> DeleteTokenAsync(Guid tokenId);
    
    /// <summary>
    /// Clean up expired tokens and unused scope maps
    /// </summary>
    Task<(int tokensDeleted, int scopeMapsDeleted)> CleanupExpiredResourcesAsync();
    
    /// <summary>
    /// Update token last used timestamp
    /// </summary>
    Task UpdateTokenLastUsedAsync(Guid tokenId);
}
