using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using IamApi.Models;
using Microsoft.Graph;

namespace IamApi.Services;

public class ContainerRegistryService : IContainerRegistryService
{
    private readonly ILogger<ContainerRegistryService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IDataStore _dataStore;
    private readonly HttpClient _httpClient;
    private TokenCredential? _credential;

    public ContainerRegistryService(
        ILogger<ContainerRegistryService> logger,
        IConfiguration configuration,
        IDataStore dataStore,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _configuration = configuration;
        _dataStore = dataStore;
        _httpClient = httpClientFactory.CreateClient();
    }

    private TokenCredential GetCredential()
    {
        if (_credential == null)
        {
            var managedIdentityClientId = _configuration["EntraId:ManagedIdentityClientId"];
            if (!string.IsNullOrEmpty(managedIdentityClientId))
            {
                _logger.LogInformation("Using USER-ASSIGNED Managed Identity for ACR operations");
                _credential = new ManagedIdentityCredential(managedIdentityClientId);
            }
            else
            {
                _logger.LogInformation("Using SYSTEM-ASSIGNED Managed Identity for ACR operations");
                _credential = new ManagedIdentityCredential();
            }
        }
        return _credential;
    }

    private async Task<string> GetAccessTokenAsync()
    {
        var credential = GetCredential();
        var tokenRequestContext = new TokenRequestContext(new[] { "https://management.azure.com/.default" });
        var token = await credential.GetTokenAsync(tokenRequestContext, CancellationToken.None);
        return token.Token;
    }

    public async Task<ContainerImageListResponse> ListImagesAsync(Guid registryId)
    {
        var registry = _dataStore.GetRegistry(registryId);
        if (registry == null)
        {
            throw new InvalidOperationException($"Registry {registryId} not found");
        }

        if (registry.Type != RegistryType.AzureContainerRegistry)
        {
            throw new InvalidOperationException($"Registry {registry.Name} is not an Azure Container Registry");
        }

        _logger.LogInformation("Listing images for registry {Registry}", registry.Name);

        try
        {
            var images = await ListRepositoriesAsync(registry);
            var response = new ContainerImageListResponse
            {
                RegistryName = registry.Name,
                RetrievedAt = DateTime.UtcNow,
                Images = new List<ContainerImage>()
            };

            foreach (var repo in images)
            {
                try
                {
                    var tags = await ListRepositoryTagsAsync(registry, repo);
                    response.Images.Add(new ContainerImage
                    {
                        Repository = repo,
                        Tags = tags
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get tags for repository {Repository}", repo);
                }
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list images for registry {Registry}", registry.Name);
            throw;
        }
    }

    public async Task<List<string>> ListImageTagsAsync(Guid registryId, string repository)
    {
        var registry = _dataStore.GetRegistry(registryId);
        if (registry == null)
        {
            throw new InvalidOperationException($"Registry {registryId} not found");
        }

        return await ListRepositoryTagsAsync(registry, repository);
    }

    private async Task<List<string>> ListRepositoriesAsync(ContainerRegistry registry)
    {
        var url = $"https://{registry.Server}/acr/v1/_catalog";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        
        // ACR uses its own authentication
        var acrToken = await GetAcrRefreshTokenAsync(registry);
        var accessToken = await ExchangeAcrTokenAsync(registry, acrToken);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        _logger.LogInformation("Calling ACR catalog API: {Url}", url);
        var response = await _httpClient.SendAsync(request);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("ACR catalog request failed with status {StatusCode}: {Error}", 
                response.StatusCode, errorContent);
            throw new InvalidOperationException($"ACR catalog request failed: {response.StatusCode} - {errorContent}");
        }

        var content = await response.Content.ReadAsStringAsync();
        _logger.LogInformation("ACR catalog response: {Content}", content);
        var result = JsonSerializer.Deserialize<AcrCatalogResponse>(content);
        _logger.LogInformation("Parsed {Count} repositories from catalog", result?.Repositories?.Count ?? 0);
        
        return result?.Repositories ?? new List<string>();
    }

    private async Task<List<string>> ListRepositoryTagsAsync(ContainerRegistry registry, string repository)
    {
        var url = $"https://{registry.Server}/acr/v1/{repository}/_tags";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        
        var acrToken = await GetAcrRefreshTokenAsync(registry);
        var accessToken = await ExchangeAcrTokenAsync(registry, acrToken, repository);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<AcrTagsResponse>(content);
        
        return result?.Tags?.Select(t => t.Name).ToList() ?? new List<string>();
    }

    private async Task<string> GetAcrRefreshTokenAsync(ContainerRegistry registry)
    {
        // Get AAD token for ACR - use the ARM scope since the identity has Contributor + AcrPull roles
        var credential = GetCredential();
        var tokenContext = new TokenRequestContext(new[] { "https://management.azure.com/.default" });
        var token = await credential.GetTokenAsync(tokenContext, CancellationToken.None);
        
        var tenantId = _configuration["EntraId:TenantId"];
        _logger.LogInformation("ACR exchange: server={Server}, tenant={TenantId} (configured={HasTenant})", 
            registry.Server, 
            string.IsNullOrEmpty(tenantId) ? "(empty)" : tenantId[..Math.Min(8, tenantId.Length)] + "...",
            !string.IsNullOrEmpty(tenantId));
        
        // Exchange for ACR refresh token
        var url = $"https://{registry.Server}/oauth2/exchange";
        var formData = new Dictionary<string, string>
        {
            { "grant_type", "access_token" },
            { "service", registry.Server },
            { "access_token", token.Token }
        };
        
        // Only add tenant if configured
        if (!string.IsNullOrEmpty(tenantId))
        {
            formData["tenant"] = tenantId;
        }

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(formData)
        };

        var response = await _httpClient.SendAsync(request);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("ACR token exchange failed with status {StatusCode}: {Error}", 
                response.StatusCode, errorContent);
            throw new InvalidOperationException($"ACR token exchange failed: {response.StatusCode} - {errorContent}");
        }

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<AcrTokenResponse>(content);
        
        return result?.RefreshToken ?? throw new InvalidOperationException("Failed to get ACR refresh token - no refresh_token in response");
    }

    private async Task<string> ExchangeAcrTokenAsync(ContainerRegistry registry, string refreshToken, string? scope = null)
    {
        var url = $"https://{registry.Server}/oauth2/token";
        var formData = new Dictionary<string, string>
        {
            { "grant_type", "refresh_token" },
            { "service", registry.Server },
            { "refresh_token", refreshToken }
        };

        if (!string.IsNullOrEmpty(scope))
        {
            formData["scope"] = $"repository:{scope}:pull";
        }
        else
        {
            // For catalog listing, we need the registry:catalog:* scope
            formData["scope"] = "registry:catalog:*";
        }

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(formData)
        };

        var response = await _httpClient.SendAsync(request);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("ACR access token exchange failed with status {StatusCode}: {Error}", 
                response.StatusCode, errorContent);
            throw new InvalidOperationException($"ACR access token exchange failed: {response.StatusCode} - {errorContent}");
        }

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<AcrTokenResponse>(content);
        
        return result?.AccessToken ?? throw new InvalidOperationException("Failed to get ACR access token - no access_token in response");
    }

    public async Task<AcrScopeMap> CreateScopeMapAsync(Guid registryId, CreateScopeMapRequest request)
    {
        var registry = _dataStore.GetRegistry(registryId);
        if (registry == null)
        {
            throw new InvalidOperationException($"Registry {registryId} not found");
        }

        if (string.IsNullOrEmpty(registry.SubscriptionId) || string.IsNullOrEmpty(registry.ResourceGroup))
        {
            throw new InvalidOperationException("Registry must have SubscriptionId and ResourceGroup configured for scope map management");
        }

        _logger.LogInformation("Creating scope map {Name} for registry {Registry}", request.Name, registry.Name);

        // Build actions list
        var actions = new List<string>();
        foreach (var repo in request.Repositories)
        {
            foreach (var action in request.Actions)
            {
                actions.Add($"repositories/{repo}/{action}");
            }
        }

        // Create scope map via Azure Management API
        var url = $"https://management.azure.com/subscriptions/{registry.SubscriptionId}" +
                  $"/resourceGroups/{registry.ResourceGroup}/providers/Microsoft.ContainerRegistry" +
                  $"/registries/{registry.Name}/scopeMaps/{request.Name}?api-version=2023-07-01";

        var payload = new
        {
            properties = new
            {
                description = request.Description,
                actions = actions
            }
        };

        var accessToken = await GetAccessTokenAsync();
        var httpRequest = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.SendAsync(httpRequest);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("Failed to create scope map {Name}: {StatusCode} - {Error}", request.Name, response.StatusCode, errorContent);
            throw new InvalidOperationException($"Failed to create scope map: {response.StatusCode} - {errorContent}");
        }

        var content = await response.Content.ReadAsStringAsync();
        _logger.LogDebug("Scope map creation response: {Content}", content);
        var azureResponse = JsonSerializer.Deserialize<AzureScopeMapResponse>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        
        _logger.LogInformation("Azure returned scope map ResourceId: {ResourceId}", azureResponse?.Id ?? "(null)");

        var scopeMap = new AcrScopeMap
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            Actions = actions,
            RegistryId = registryId,
            ResourceId = azureResponse?.Id ?? string.Empty,
            CreatedAt = DateTime.UtcNow
        };

        _dataStore.AddScopeMap(scopeMap);
        _logger.LogInformation("Created scope map {Name} with local ID {Id} and ResourceId {ResourceId}", scopeMap.Name, scopeMap.Id, scopeMap.ResourceId);

        return scopeMap;
    }

    public async Task<AcrScopeMap?> GetScopeMapAsync(Guid scopeMapId)
    {
        return await Task.FromResult(_dataStore.GetScopeMap(scopeMapId));
    }

    public async Task<List<AcrScopeMap>> ListScopeMapsAsync(Guid registryId)
    {
        var registry = _dataStore.GetRegistry(registryId);
        if (registry == null)
        {
            throw new InvalidOperationException($"Registry {registryId} not found");
        }

        if (string.IsNullOrEmpty(registry.SubscriptionId) || string.IsNullOrEmpty(registry.ResourceGroup))
        {
            _logger.LogWarning("Registry {Name} missing SubscriptionId or ResourceGroup", registry.Name);
            return new List<AcrScopeMap>();
        }

        _logger.LogInformation("Listing scope maps for registry {Registry} from Azure", registry.Name);

        var url = $"https://management.azure.com/subscriptions/{registry.SubscriptionId}" +
                  $"/resourceGroups/{registry.ResourceGroup}/providers/Microsoft.ContainerRegistry" +
                  $"/registries/{registry.Name}/scopeMaps?api-version=2023-07-01";

        var accessToken = await GetAccessTokenAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("Failed to list scope maps: {StatusCode} - {Error}", response.StatusCode, errorContent);
            throw new InvalidOperationException($"Failed to list scope maps: {response.StatusCode}");
        }

        var content = await response.Content.ReadAsStringAsync();
        _logger.LogDebug("Scope maps response: {Content}", content);
        
        var result = JsonSerializer.Deserialize<AzureListResponse<AzureScopeMapResource>>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        
        var scopeMaps = new List<AcrScopeMap>();
        if (result?.Value != null)
        {
            foreach (var item in result.Value)
            {
                // Skip system scope maps (they start with _repositories)
                if (item.Name.StartsWith("_"))
                    continue;
                    
                scopeMaps.Add(new AcrScopeMap
                {
                    Id = Guid.NewGuid(), // Local ID
                    Name = item.Name,
                    Description = item.Properties?.Description ?? "",
                    Actions = item.Properties?.Actions ?? new List<string>(),
                    RegistryId = registryId,
                    ResourceId = item.Id ?? "",
                    CreatedAt = item.Properties?.CreationDate ?? DateTime.UtcNow,
                    AssociatedTokenIds = new List<Guid>()
                });
            }
        }
        
        _logger.LogInformation("Found {Count} scope maps for registry {Registry}", scopeMaps.Count, registry.Name);
        return scopeMaps;
    }

    public async Task<bool> DeleteScopeMapAsync(Guid scopeMapId)
    {
        var scopeMap = _dataStore.GetScopeMap(scopeMapId);
        if (scopeMap == null)
        {
            return false;
        }

        // Check if any tokens are using this scope map
        if (scopeMap.AssociatedTokenIds.Any())
        {
            throw new InvalidOperationException($"Cannot delete scope map {scopeMap.Name} as it has {scopeMap.AssociatedTokenIds.Count} associated tokens");
        }

        var registry = _dataStore.GetRegistry(scopeMap.RegistryId);
        if (registry == null)
        {
            throw new InvalidOperationException($"Registry {scopeMap.RegistryId} not found");
        }

        _logger.LogInformation("Deleting scope map {Name}", scopeMap.Name);

        // Delete from Azure
        var url = $"https://management.azure.com/subscriptions/{registry.SubscriptionId}" +
                  $"/resourceGroups/{registry.ResourceGroup}/providers/Microsoft.ContainerRegistry" +
                  $"/registries/{registry.Name}/scopeMaps/{scopeMap.Name}?api-version=2023-07-01";

        var accessToken = await GetAccessTokenAsync();
        var request = new HttpRequestMessage(HttpMethod.Delete, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request);
        
        if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _dataStore.RemoveScopeMap(scopeMapId);
            _logger.LogInformation("Deleted scope map {Name}", scopeMap.Name);
            return true;
        }

        _logger.LogError("Failed to delete scope map {Name}: {StatusCode}", scopeMap.Name, response.StatusCode);
        return false;
    }

    public async Task<TokenResponse> CreateTokenAsync(Guid registryId, CreateTokenRequest request)
    {
        var registry = _dataStore.GetRegistry(registryId);
        if (registry == null)
        {
            throw new InvalidOperationException($"Registry {registryId} not found");
        }

        var scopeMap = _dataStore.GetScopeMap(request.ScopeMapId);
        if (scopeMap == null)
        {
            throw new InvalidOperationException($"Scope map {request.ScopeMapId} not found");
        }

        if (scopeMap.RegistryId != registryId)
        {
            throw new InvalidOperationException($"Scope map {request.ScopeMapId} does not belong to registry {registryId}");
        }

        _logger.LogInformation("Creating token {Name} for registry {Registry} with scopeMapResourceId {ScopeMapResourceId}", 
            request.Name, registry.Name, scopeMap.ResourceId);

        // Create token via Azure Management API
        var url = $"https://management.azure.com/subscriptions/{registry.SubscriptionId}" +
                  $"/resourceGroups/{registry.ResourceGroup}/providers/Microsoft.ContainerRegistry" +
                  $"/registries/{registry.Name}/tokens/{request.Name}?api-version=2023-07-01";

        var payload = new
        {
            properties = new
            {
                scopeMapId = scopeMap.ResourceId,
                status = "enabled"
            }
        };
        
        var payloadJson = JsonSerializer.Serialize(payload);
        _logger.LogDebug("Token creation payload: {Payload}", payloadJson);

        var accessToken = await GetAccessTokenAsync();
        var httpRequest = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.SendAsync(httpRequest);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("Failed to create token {Name}: {StatusCode} - {Error}", request.Name, response.StatusCode, errorContent);
            throw new InvalidOperationException($"Failed to create token: {response.StatusCode} - {errorContent}");
        }

        var content = await response.Content.ReadAsStringAsync();
        _logger.LogDebug("Token creation response: {Content}", content);
        var azureResponse = JsonSerializer.Deserialize<AzureTokenResponse>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        // Wait for token to be fully provisioned by polling its GET endpoint
        var tokenGetUrl = $"https://management.azure.com/subscriptions/{registry.SubscriptionId}" +
                         $"/resourceGroups/{registry.ResourceGroup}/providers/Microsoft.ContainerRegistry" +
                         $"/registries/{registry.Name}/tokens/{request.Name}?api-version=2023-07-01";
        
        const int maxProvisionRetries = 15;
        const int provisionDelayMs = 2000;
        bool tokenProvisioned = false;
        
        for (int attempt = 1; attempt <= maxProvisionRetries; attempt++)
        {
            _logger.LogInformation("Checking token {Name} provisioning status (attempt {Attempt}/{MaxRetries})", 
                request.Name, attempt, maxProvisionRetries);
                
            var getRequest = new HttpRequestMessage(HttpMethod.Get, tokenGetUrl);
            getRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            
            var getResponse = await _httpClient.SendAsync(getRequest);
            
            if (getResponse.IsSuccessStatusCode)
            {
                var tokenContent = await getResponse.Content.ReadAsStringAsync();
                _logger.LogDebug("Token status response: {Content}", tokenContent);
                
                // Check if token has a provisioningState of Succeeded
                using var doc = JsonDocument.Parse(tokenContent);
                if (doc.RootElement.TryGetProperty("properties", out var props) &&
                    props.TryGetProperty("provisioningState", out var state))
                {
                    var provisioningState = state.GetString();
                    _logger.LogInformation("Token {Name} provisioning state: {State}", request.Name, provisioningState);
                    
                    if (provisioningState == "Succeeded")
                    {
                        tokenProvisioned = true;
                        break;
                    }
                    else if (provisioningState == "Failed")
                    {
                        _logger.LogError("Token {Name} provisioning failed", request.Name);
                        throw new InvalidOperationException($"Token provisioning failed for {request.Name}");
                    }
                }
            }
            else
            {
                _logger.LogWarning("Failed to get token status: {StatusCode}", getResponse.StatusCode);
            }
            
            if (attempt < maxProvisionRetries)
            {
                _logger.LogWarning("Token {Name} not yet provisioned (attempt {Attempt}/{MaxRetries}), waiting {Delay}ms...", 
                    request.Name, attempt, maxProvisionRetries, provisionDelayMs);
                await Task.Delay(provisionDelayMs);
            }
        }
        
        if (!tokenProvisioned)
        {
            _logger.LogError("Token {Name} did not become provisioned after {MaxRetries} attempts", request.Name, maxProvisionRetries);
            throw new InvalidOperationException($"Token {request.Name} did not become provisioned in time");
        }
        
        _logger.LogInformation("Token {Name} is fully provisioned, generating credentials...", request.Name);

        // Generate password for the token - NOTE: generateCredentials is at REGISTRY level, not TOKEN level
        var tokenResourceId = $"/subscriptions/{registry.SubscriptionId}/resourceGroups/{registry.ResourceGroup}" +
                             $"/providers/Microsoft.ContainerRegistry/registries/{registry.Name}/tokens/{request.Name}";
        var passwordUrl = $"https://management.azure.com/subscriptions/{registry.SubscriptionId}" +
                         $"/resourceGroups/{registry.ResourceGroup}/providers/Microsoft.ContainerRegistry" +
                         $"/registries/{registry.Name}/generateCredentials?api-version=2023-07-01";

        var passwordPayload = new
        {
            tokenId = tokenResourceId,
            name = "password1",
            expiry = request.ExpiryInDays.HasValue ? DateTime.UtcNow.AddDays(request.ExpiryInDays.Value).ToString("o") : (string?)null
        };
        
        _logger.LogDebug("generateCredentials URL: {Url}", passwordUrl);
        _logger.LogDebug("generateCredentials payload: {Payload}", JsonSerializer.Serialize(passwordPayload));

        HttpResponseMessage? passwordResponse = null;
        const int maxRetries = 5;
        const int delayMs = 2000;
        
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            _logger.LogInformation("Generating credentials for token {Name} (attempt {Attempt}/{MaxRetries})", 
                request.Name, attempt, maxRetries);
                
            var passwordRequest = new HttpRequestMessage(HttpMethod.Post, passwordUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(passwordPayload), Encoding.UTF8, "application/json")
            };
            passwordRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            passwordResponse = await _httpClient.SendAsync(passwordRequest);
            
            if (passwordResponse.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully generated credentials for token {Name} on attempt {Attempt}", request.Name, attempt);
                break;
            }
            
            if (passwordResponse.StatusCode == System.Net.HttpStatusCode.NotFound && attempt < maxRetries)
            {
                _logger.LogWarning("Token {Name} not yet available (attempt {Attempt}/{MaxRetries}), waiting {Delay}ms...", 
                    request.Name, attempt, maxRetries, delayMs);
                await Task.Delay(delayMs);
                continue;
            }
            
            // For non-404 errors or last attempt, break out
            break;
        }
        
        if (passwordResponse == null || !passwordResponse.IsSuccessStatusCode)
        {
            var errorContent = passwordResponse != null ? await passwordResponse.Content.ReadAsStringAsync() : "No response";
            _logger.LogError("Failed to generate credentials for token {Name}: {StatusCode} - {Error}", 
                request.Name, passwordResponse?.StatusCode, errorContent);
            throw new InvalidOperationException($"Failed to generate token credentials: {passwordResponse?.StatusCode} - {errorContent}");
        }

        var passwordContent = await passwordResponse.Content.ReadAsStringAsync();
        var passwordResult = JsonSerializer.Deserialize<AzureTokenCredentialsResponse>(passwordContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var token = new AcrToken
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Username = request.Name,
            Password = passwordResult?.Passwords?.FirstOrDefault()?.Value,
            RegistryId = registryId,
            ScopeMapId = request.ScopeMapId,
            ResourceId = azureResponse?.Id ?? string.Empty,
            Status = TokenStatus.Active,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = request.ExpiryInDays.HasValue ? DateTime.UtcNow.AddDays(request.ExpiryInDays.Value) : null,
            AssignedToOrchestratorId = request.AssignToOrchestratorId,
            AssignedToJobId = request.AssignToJobId
        };

        _dataStore.AddToken(token);

        // Update scope map reference
        scopeMap.AssociatedTokenIds.Add(token.Id);
        _dataStore.UpdateScopeMap(scopeMap);

        _logger.LogInformation("Created token {Name} with ID {Id}", token.Name, token.Id);

        return new TokenResponse
        {
            Id = token.Id,
            Name = token.Name,
            Username = token.Username,
            Password = token.Password ?? string.Empty,
            ScopeMapId = token.ScopeMapId,
            Status = token.Status,
            CreatedAt = token.CreatedAt,
            ExpiresAt = token.ExpiresAt
        };
    }

    public async Task<AcrToken?> GetTokenAsync(Guid tokenId)
    {
        return await Task.FromResult(_dataStore.GetToken(tokenId));
    }

    public async Task<List<AcrToken>> ListTokensAsync(Guid registryId)
    {
        var registry = _dataStore.GetRegistry(registryId);
        if (registry == null)
        {
            throw new InvalidOperationException($"Registry {registryId} not found");
        }

        if (string.IsNullOrEmpty(registry.SubscriptionId) || string.IsNullOrEmpty(registry.ResourceGroup))
        {
            _logger.LogWarning("Registry {Name} missing SubscriptionId or ResourceGroup", registry.Name);
            return new List<AcrToken>();
        }

        _logger.LogInformation("Listing tokens for registry {Registry} from Azure", registry.Name);

        var url = $"https://management.azure.com/subscriptions/{registry.SubscriptionId}" +
                  $"/resourceGroups/{registry.ResourceGroup}/providers/Microsoft.ContainerRegistry" +
                  $"/registries/{registry.Name}/tokens?api-version=2023-07-01";

        var accessToken = await GetAccessTokenAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("Failed to list tokens: {StatusCode} - {Error}", response.StatusCode, errorContent);
            throw new InvalidOperationException($"Failed to list tokens: {response.StatusCode}");
        }

        var content = await response.Content.ReadAsStringAsync();
        _logger.LogDebug("Tokens response: {Content}", content);
        
        var result = JsonSerializer.Deserialize<AzureListResponse<AzureTokenResource>>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        
        var tokens = new List<AcrToken>();
        if (result?.Value != null)
        {
            foreach (var item in result.Value)
            {
                var status = item.Properties?.Status?.Equals("enabled", StringComparison.OrdinalIgnoreCase) == true 
                    ? TokenStatus.Active 
                    : TokenStatus.Disabled;
                    
                tokens.Add(new AcrToken
                {
                    Id = Guid.NewGuid(), // Local ID
                    Name = item.Name,
                    Username = item.Name, // Token name is the username
                    RegistryId = registryId,
                    ResourceId = item.Id ?? "",
                    Status = status,
                    CreatedAt = item.Properties?.CreationDate ?? DateTime.UtcNow,
                    ExpiresAt = null // Azure doesn't return expiry directly on tokens
                });
            }
        }
        
        _logger.LogInformation("Found {Count} tokens for registry {Registry}", tokens.Count, registry.Name);
        return tokens;
    }

    public async Task<bool> DisableTokenAsync(Guid tokenId)
    {
        var token = _dataStore.GetToken(tokenId);
        if (token == null)
        {
            return false;
        }

        _logger.LogInformation("Disabling token {Name}", token.Name);

        token.Status = TokenStatus.Disabled;
        _dataStore.UpdateToken(token);

        return await Task.FromResult(true);
    }

    public async Task<bool> DeleteTokenAsync(Guid tokenId)
    {
        var token = _dataStore.GetToken(tokenId);
        if (token == null)
        {
            return false;
        }

        var registry = _dataStore.GetRegistry(token.RegistryId);
        if (registry == null)
        {
            throw new InvalidOperationException($"Registry {token.RegistryId} not found");
        }

        _logger.LogInformation("Deleting token {Name}", token.Name);

        // Delete from Azure
        var url = $"https://management.azure.com/subscriptions/{registry.SubscriptionId}" +
                  $"/resourceGroups/{registry.ResourceGroup}/providers/Microsoft.ContainerRegistry" +
                  $"/registries/{registry.Name}/tokens/{token.Name}?api-version=2023-07-01";

        var accessToken = await GetAccessTokenAsync();
        var request = new HttpRequestMessage(HttpMethod.Delete, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request);
        
        if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Update scope map reference
            var scopeMap = _dataStore.GetScopeMap(token.ScopeMapId);
            if (scopeMap != null)
            {
                scopeMap.AssociatedTokenIds.Remove(tokenId);
                _dataStore.UpdateScopeMap(scopeMap);
            }

            _dataStore.RemoveToken(tokenId);
            _logger.LogInformation("Deleted token {Name}", token.Name);
            return true;
        }

        _logger.LogError("Failed to delete token {Name}: {StatusCode}", token.Name, response.StatusCode);
        return false;
    }

    public async Task<(int tokensDeleted, int scopeMapsDeleted)> CleanupExpiredResourcesAsync()
    {
        _logger.LogInformation("Starting cleanup of expired tokens and unused scope maps");

        int tokensDeleted = 0;
        int scopeMapsDeleted = 0;

        // Get all tokens across all registries
        var registries = _dataStore.GetRegistries();
        foreach (var registry in registries)
        {
            if (registry.Type != RegistryType.AzureContainerRegistry)
                continue;

            var tokens = _dataStore.GetTokens(registry.Id);
            foreach (var token in tokens)
            {
                // Delete expired tokens
                if (token.ExpiresAt.HasValue && token.ExpiresAt.Value < DateTime.UtcNow)
                {
                    _logger.LogInformation("Deleting expired token {Name}", token.Name);
                    if (await DeleteTokenAsync(token.Id))
                    {
                        tokensDeleted++;
                    }
                }
                // Delete old disabled tokens (older than 7 days)
                else if (token.Status == TokenStatus.Disabled && 
                         token.LastUsedAt.HasValue && 
                         token.LastUsedAt.Value.AddDays(7) < DateTime.UtcNow)
                {
                    _logger.LogInformation("Deleting old disabled token {Name}", token.Name);
                    if (await DeleteTokenAsync(token.Id))
                    {
                        tokensDeleted++;
                    }
                }
            }

            // Delete unused scope maps (no associated tokens)
            var scopeMaps = _dataStore.GetScopeMaps(registry.Id);
            foreach (var scopeMap in scopeMaps)
            {
                if (!scopeMap.AssociatedTokenIds.Any())
                {
                    _logger.LogInformation("Deleting unused scope map {Name}", scopeMap.Name);
                    if (await DeleteScopeMapAsync(scopeMap.Id))
                    {
                        scopeMapsDeleted++;
                    }
                }
            }
        }

        _logger.LogInformation("Cleanup completed: {TokensDeleted} tokens and {ScopeMapsDeleted} scope maps deleted", 
            tokensDeleted, scopeMapsDeleted);

        return (tokensDeleted, scopeMapsDeleted);
    }

    public async Task UpdateTokenLastUsedAsync(Guid tokenId)
    {
        var token = _dataStore.GetToken(tokenId);
        if (token != null)
        {
            token.LastUsedAt = DateTime.UtcNow;
            _dataStore.UpdateToken(token);
            
            // Also update scope map last used
            var scopeMap = _dataStore.GetScopeMap(token.ScopeMapId);
            if (scopeMap != null)
            {
                scopeMap.LastUsedAt = DateTime.UtcNow;
                _dataStore.UpdateScopeMap(scopeMap);
            }
        }
        await Task.CompletedTask;
    }

    // Internal response models for Azure API
    private class AcrCatalogResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("repositories")]
        public List<string>? Repositories { get; set; }
    }

    private class AcrTagsResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("tags")]
        public List<AcrTag>? Tags { get; set; }
    }

    private class AcrTag
    {
        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    private class AcrTokenResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }
    }

    private class AzureScopeMapResponse
    {
        public string? Id { get; set; }
    }

    private class AzureTokenResponse
    {
        public string? Id { get; set; }
    }

    private class AzureTokenCredentialsResponse
    {
        public List<AzurePassword>? Passwords { get; set; }
    }

    private class AzurePassword
    {
        public string? Value { get; set; }
    }

    // Azure Management API list response wrapper
    private class AzureListResponse<T>
    {
        public List<T>? Value { get; set; }
    }

    private class AzureScopeMapResource
    {
        public string? Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public AzureScopeMapProperties? Properties { get; set; }
    }

    private class AzureScopeMapProperties
    {
        public string? Description { get; set; }
        public List<string>? Actions { get; set; }
        public DateTime? CreationDate { get; set; }
    }

    private class AzureTokenResource
    {
        public string? Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public AzureTokenProperties? Properties { get; set; }
    }

    private class AzureTokenProperties
    {
        public string? Status { get; set; }
        public string? ScopeMapId { get; set; }
        public DateTime? CreationDate { get; set; }
    }
}
