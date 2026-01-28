using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using IamApi.Models;
using IamApi.Services;

namespace IamApi.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class RegistriesController : ControllerBase
{
    private readonly IDataStore _dataStore;
    private readonly IContainerRegistryService _registryService;
    private readonly ILogger<RegistriesController> _logger;

    public RegistriesController(
        IDataStore dataStore, 
        IContainerRegistryService registryService,
        ILogger<RegistriesController> logger)
    {
        _dataStore = dataStore;
        _registryService = registryService;
        _logger = logger;
    }

    [HttpGet]
    public ActionResult<IEnumerable<ContainerRegistry>> GetAll()
    {
        return Ok(_dataStore.GetRegistries());
    }

    [HttpGet("{id}")]
    public ActionResult<ContainerRegistry> GetById(Guid id)
    {
        var registry = _dataStore.GetRegistry(id);
        if (registry == null)
            return NotFound();

        return Ok(registry);
    }

    [HttpPost]
    public ActionResult<ContainerRegistry> Create(CreateRegistryRequest request)
    {
        var registry = new ContainerRegistry
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Server = request.Server,
            Username = request.Username,
            Password = request.Password,
            ResourceGroup = request.ResourceGroup,
            SubscriptionId = request.SubscriptionId,
            UseGraphManagement = request.UseGraphManagement,
            Type = request.Type,
            CreatedAt = DateTime.UtcNow
        };

        // Build Azure Resource ID if we have the required properties
        if (registry.Type == RegistryType.AzureContainerRegistry && 
            !string.IsNullOrEmpty(registry.SubscriptionId) && 
            !string.IsNullOrEmpty(registry.ResourceGroup))
        {
            registry.AzureResourceId = $"/subscriptions/{registry.SubscriptionId}/resourceGroups/{registry.ResourceGroup}/providers/Microsoft.ContainerRegistry/registries/{registry.Name}";
        }

        _dataStore.AddRegistry(registry);
        _logger.LogInformation("Created registry {Name} with server {Server}", registry.Name, registry.Server);

        return CreatedAtAction(nameof(GetById), new { id = registry.Id }, registry);
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(Guid id)
    {
        var registry = _dataStore.GetRegistry(id);
        if (registry == null)
            return NotFound();

        _dataStore.RemoveRegistry(id);
        _logger.LogInformation("Deleted registry {Name}", registry.Name);

        return NoContent();
    }

    // ========== Image Management ==========
    
    [HttpGet("{id}/images")]
    public async Task<ActionResult<ContainerImageListResponse>> ListImages(Guid id)
    {
        var registry = _dataStore.GetRegistry(id);
        if (registry == null)
            return NotFound();

        try
        {
            var images = await _registryService.ListImagesAsync(id);
            return Ok(images);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list images for registry {RegistryId}", id);
            return StatusCode(500, new { error = "Failed to list images" });
        }
    }

    [HttpGet("{id}/images/{repository}/tags")]
    public async Task<ActionResult<List<string>>> ListImageTags(Guid id, string repository)
    {
        var registry = _dataStore.GetRegistry(id);
        if (registry == null)
            return NotFound();

        try
        {
            var tags = await _registryService.ListImageTagsAsync(id, repository);
            return Ok(tags);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list tags for repository {Repository}", repository);
            return StatusCode(500, new { error = "Failed to list tags" });
        }
    }

    // ========== Scope Map Management ==========
    
    [HttpGet("{id}/scopemaps")]
    public async Task<ActionResult<List<AcrScopeMap>>> ListScopeMaps(Guid id)
    {
        var registry = _dataStore.GetRegistry(id);
        if (registry == null)
            return NotFound();

        var scopeMaps = await _registryService.ListScopeMapsAsync(id);
        return Ok(scopeMaps);
    }

    [HttpGet("scopemaps/{scopeMapId}")]
    public async Task<ActionResult<AcrScopeMap>> GetScopeMap(Guid scopeMapId)
    {
        var scopeMap = await _registryService.GetScopeMapAsync(scopeMapId);
        if (scopeMap == null)
            return NotFound();

        return Ok(scopeMap);
    }

    [HttpPost("{id}/scopemaps")]
    public async Task<ActionResult<AcrScopeMap>> CreateScopeMap(Guid id, CreateScopeMapRequest request)
    {
        var registry = _dataStore.GetRegistry(id);
        if (registry == null)
            return NotFound();

        if (registry.Type != RegistryType.AzureContainerRegistry)
        {
            return BadRequest(new { error = "Scope maps are only supported for Azure Container Registry" });
        }

        try
        {
            var scopeMap = await _registryService.CreateScopeMapAsync(id, request);
            return CreatedAtAction(nameof(GetScopeMap), new { scopeMapId = scopeMap.Id }, scopeMap);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create scope map {Name}", request.Name);
            return StatusCode(500, new { error = "Failed to create scope map" });
        }
    }

    [HttpDelete("scopemaps/{scopeMapId}")]
    public async Task<IActionResult> DeleteScopeMap(Guid scopeMapId)
    {
        try
        {
            var result = await _registryService.DeleteScopeMapAsync(scopeMapId);
            if (!result)
                return NotFound();

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete scope map {ScopeMapId}", scopeMapId);
            return StatusCode(500, new { error = "Failed to delete scope map" });
        }
    }

    // ========== Token Management ==========
    
    [HttpGet("{id}/tokens")]
    public async Task<ActionResult<List<AcrToken>>> ListTokens(Guid id)
    {
        var registry = _dataStore.GetRegistry(id);
        if (registry == null)
            return NotFound();

        var tokens = await _registryService.ListTokensAsync(id);
        
        // Don't return passwords in the list
        foreach (var token in tokens)
        {
            token.Password = null;
        }
        
        return Ok(tokens);
    }

    [HttpGet("tokens/{tokenId}")]
    public async Task<ActionResult<AcrToken>> GetToken(Guid tokenId)
    {
        var token = await _registryService.GetTokenAsync(tokenId);
        if (token == null)
            return NotFound();

        // Don't return password in GET requests
        token.Password = null;
        return Ok(token);
    }

    [HttpPost("{id}/tokens")]
    public async Task<ActionResult<TokenResponse>> CreateToken(Guid id, CreateTokenRequest request)
    {
        var registry = _dataStore.GetRegistry(id);
        if (registry == null)
            return NotFound();

        if (registry.Type != RegistryType.AzureContainerRegistry)
        {
            return BadRequest(new { error = "Tokens are only supported for Azure Container Registry" });
        }

        try
        {
            var tokenResponse = await _registryService.CreateTokenAsync(id, request);
            return CreatedAtAction(nameof(GetToken), new { tokenId = tokenResponse.Id }, tokenResponse);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create token {Name}", request.Name);
            return StatusCode(500, new { error = "Failed to create token" });
        }
    }

    [HttpPost("tokens/{tokenId}/disable")]
    public async Task<IActionResult> DisableToken(Guid tokenId)
    {
        try
        {
            var result = await _registryService.DisableTokenAsync(tokenId);
            if (!result)
                return NotFound();

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to disable token {TokenId}", tokenId);
            return StatusCode(500, new { error = "Failed to disable token" });
        }
    }

    [HttpDelete("tokens/{tokenId}")]
    public async Task<IActionResult> DeleteToken(Guid tokenId)
    {
        try
        {
            var result = await _registryService.DeleteTokenAsync(tokenId);
            if (!result)
                return NotFound();

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete token {TokenId}", tokenId);
            return StatusCode(500, new { error = "Failed to delete token" });
        }
    }

    [HttpPost("tokens/{tokenId}/used")]
    public async Task<IActionResult> MarkTokenUsed(Guid tokenId)
    {
        await _registryService.UpdateTokenLastUsedAsync(tokenId);
        return NoContent();
    }

    // ========== Cleanup ==========
    
    [HttpPost("cleanup")]
    public async Task<ActionResult> CleanupExpired()
    {
        try
        {
            var (tokensDeleted, scopeMapsDeleted) = await _registryService.CleanupExpiredResourcesAsync();
            return Ok(new 
            { 
                tokensDeleted, 
                scopeMapsDeleted,
                message = $"Cleanup completed: {tokensDeleted} tokens and {scopeMapsDeleted} scope maps deleted"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup expired resources");
            return StatusCode(500, new { error = "Failed to cleanup expired resources" });
        }
    }
}
