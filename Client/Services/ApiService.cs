using System.Net.Http.Headers;
using System.Net.Http.Json;
using IamApi.Client.Models;
using Microsoft.AspNetCore.Components;
using Blazored.LocalStorage;

namespace IamApi.Client.Services;

public class ApiService
{
    private readonly HttpClient _http;
    private readonly NavigationManager _navigation;
    private readonly ILocalStorageService _localStorage;

    public ApiService(HttpClient http, NavigationManager navigation, ILocalStorageService localStorage)
    {
        _http = http;
        _navigation = navigation;
        _localStorage = localStorage;
    }

    private async Task SetAuthHeader()
    {
        var token = await _localStorage.GetItemAsync<string>("jwt_token");
        if (!string.IsNullOrEmpty(token))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        else
        {
            Console.WriteLine("Warning: No JWT token found in local storage");
        }
    }

    public async Task<string?> GetToken()
    {
        return await _localStorage.GetItemAsync<string>("jwt_token");
    }

    public async Task SetToken(string token)
    {
        await _localStorage.SetItemAsync("jwt_token", token);
    }

    public async Task RemoveToken()
    {
        await _localStorage.RemoveItemAsync("jwt_token");
    }

    private async Task<T?> HandleResponse<T>(HttpResponseMessage response)
    {
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            await RemoveToken();
            _navigation.NavigateTo("/login");
            return default;
        }

        response.EnsureSuccessStatusCode();

        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            return default;

        return await response.Content.ReadFromJsonAsync<T>();
    }

    // Auth
    public async Task<LoginResponse?> Login(LoginRequest request)
    {
        var response = await _http.PostAsJsonAsync("/api/auth/login", request);
        return await HandleResponse<LoginResponse>(response);
    }

    public async Task<UserInfo?> GetCurrentUser()
    {
        await SetAuthHeader();
        var response = await _http.GetAsync("/api/auth/me");
        return await HandleResponse<UserInfo>(response);
    }

    // Orchestrators
    public async Task<List<Orchestrator>?> GetOrchestrators()
    {
        await SetAuthHeader();
        var response = await _http.GetAsync("/api/orchestrators");
        return await HandleResponse<List<Orchestrator>>(response);
    }

    public async Task DeleteOrchestrator(string id)
    {
        await SetAuthHeader();
        var response = await _http.DeleteAsync($"/api/orchestrators/{id}");
        await HandleResponse<object>(response);
    }

    public async Task<CleanupResult?> CleanupOrchestrators()
    {
        await SetAuthHeader();
        var response = await _http.PostAsync("/api/orchestrators/cleanup", null);
        return await HandleResponse<CleanupResult>(response);
    }

    public async Task RequestOrchestratorUpdate(string id)
    {
        await SetAuthHeader();
        var response = await _http.PostAsync($"/api/orchestrators/{id}/update", null);
        await HandleResponse<object>(response);
    }

    // Jobs
    public async Task<List<Job>?> GetJobs()
    {
        await SetAuthHeader();
        var response = await _http.GetAsync("/api/jobs");
        return await HandleResponse<List<Job>>(response);
    }

    public async Task<Job?> CreateJob(CreateJobRequest request)
    {
        await SetAuthHeader();
        var response = await _http.PostAsJsonAsync("/api/jobs", request);
        return await HandleResponse<Job>(response);
    }

    public async Task<Job?> UpdateJob(Guid id, CreateJobRequest request)
    {
        await SetAuthHeader();
        var response = await _http.PutAsJsonAsync($"/api/jobs/{id}", request);
        return await HandleResponse<Job>(response);
    }

    public async Task DeleteJob(Guid id)
    {
        await SetAuthHeader();
        var response = await _http.DeleteAsync($"/api/jobs/{id}");
        await HandleResponse<object>(response);
    }

    // Customers
    public async Task<List<Customer>?> GetCustomers()
    {
        await SetAuthHeader();
        var response = await _http.GetAsync("/api/customers");
        return await HandleResponse<List<Customer>>(response);
    }

    public async Task<Customer?> GetCustomer(string id)
    {
        await SetAuthHeader();
        var response = await _http.GetAsync($"/api/customers/{id}");
        return await HandleResponse<Customer>(response);
    }

    public async Task<Customer?> CreateCustomer(CreateCustomerRequest request)
    {
        await SetAuthHeader();
        var response = await _http.PostAsJsonAsync("/api/customers", request);
        return await HandleResponse<Customer>(response);
    }

    public async Task UpdateCustomer(string id, CreateCustomerRequest request)
    {
        await SetAuthHeader();
        var response = await _http.PutAsJsonAsync($"/api/customers/{id}", request);
        await HandleResponse<object>(response);
    }

    public async Task DeleteCustomer(string id)
    {
        await SetAuthHeader();
        var response = await _http.DeleteAsync($"/api/customers/{id}");
        await HandleResponse<object>(response);
    }

    // Registries
    public async Task<List<ContainerRegistry>?> GetRegistries()
    {
        await SetAuthHeader();
        var response = await _http.GetAsync("/api/registries");
        return await HandleResponse<List<ContainerRegistry>>(response);
    }

    public async Task<ContainerRegistry?> CreateRegistry(CreateRegistryRequest request)
    {
        await SetAuthHeader();
        var response = await _http.PostAsJsonAsync("/api/registries", request);
        return await HandleResponse<ContainerRegistry>(response);
    }

    public async Task DeleteRegistry(string name)
    {
        await SetAuthHeader();
        var response = await _http.DeleteAsync($"/api/registries/{name}");
        await HandleResponse<object>(response);
    }

    // ACR - Image Management
    public async Task<ContainerImageListResponse?> GetRegistryImages(Guid registryId)
    {
        await SetAuthHeader();
        var response = await _http.GetAsync($"/api/registries/{registryId}/images");
        return await HandleResponse<ContainerImageListResponse>(response);
    }

    public async Task<List<string>?> GetImageTags(Guid registryId, string repository)
    {
        await SetAuthHeader();
        var response = await _http.GetAsync($"/api/registries/{registryId}/images/{repository}/tags");
        return await HandleResponse<List<string>>(response);
    }

    // ACR - Scope Maps
    public async Task<List<AcrScopeMap>?> GetScopeMaps(Guid registryId)
    {
        await SetAuthHeader();
        var response = await _http.GetAsync($"/api/registries/{registryId}/scopemaps");
        return await HandleResponse<List<AcrScopeMap>>(response);
    }

    public async Task<AcrScopeMap?> GetScopeMap(Guid scopeMapId)
    {
        await SetAuthHeader();
        var response = await _http.GetAsync($"/api/registries/scopemaps/{scopeMapId}");
        return await HandleResponse<AcrScopeMap>(response);
    }

    public async Task<AcrScopeMap?> CreateScopeMap(Guid registryId, CreateScopeMapRequest request)
    {
        await SetAuthHeader();
        var response = await _http.PostAsJsonAsync($"/api/registries/{registryId}/scopemaps", request);
        return await HandleResponse<AcrScopeMap>(response);
    }

    public async Task DeleteScopeMap(Guid scopeMapId)
    {
        await SetAuthHeader();
        var response = await _http.DeleteAsync($"/api/registries/scopemaps/{scopeMapId}");
        await HandleResponse<object>(response);
    }

    // ACR - Tokens
    public async Task<List<AcrToken>?> GetTokens(Guid registryId)
    {
        await SetAuthHeader();
        var response = await _http.GetAsync($"/api/registries/{registryId}/tokens");
        return await HandleResponse<List<AcrToken>>(response);
    }

    public async Task<AcrToken?> GetToken(Guid tokenId)
    {
        await SetAuthHeader();
        var response = await _http.GetAsync($"/api/registries/tokens/{tokenId}");
        return await HandleResponse<AcrToken>(response);
    }

    public async Task<TokenResponse?> CreateToken(Guid registryId, CreateTokenRequest request)
    {
        await SetAuthHeader();
        var response = await _http.PostAsJsonAsync($"/api/registries/{registryId}/tokens", request);
        return await HandleResponse<TokenResponse>(response);
    }

    public async Task DisableToken(Guid tokenId)
    {
        await SetAuthHeader();
        var response = await _http.PostAsync($"/api/registries/tokens/{tokenId}/disable", null);
        await HandleResponse<object>(response);
    }

    public async Task DeleteToken(Guid tokenId)
    {
        await SetAuthHeader();
        var response = await _http.DeleteAsync($"/api/registries/tokens/{tokenId}");
        await HandleResponse<object>(response);
    }

    public async Task MarkTokenUsed(Guid tokenId)
    {
        await SetAuthHeader();
        var response = await _http.PostAsync($"/api/registries/tokens/{tokenId}/used", null);
        await HandleResponse<object>(response);
    }

    // ACR - Cleanup
    public async Task<RegistryCleanupResult?> CleanupRegistry()
    {
        await SetAuthHeader();
        var response = await _http.PostAsync("/api/registries/cleanup", null);
        return await HandleResponse<RegistryCleanupResult>(response);
    }

    // Logs
    public async Task<List<LogEntry>?> GetLogs(Guid jobId, string? source = null)
    {
        await SetAuthHeader();
        var url = $"/api/logs?jobId={jobId}";
        if (!string.IsNullOrEmpty(source))
            url += $"&source={source}";
        var response = await _http.GetAsync(url);
        return await HandleResponse<List<LogEntry>>(response);
    }

    public async Task<List<ConsoleLogEntry>?> GetConsoleLogs(int count = 500, string? level = null)
    {
        await SetAuthHeader();
        var url = $"/api/logs/console?count={count}";
        if (!string.IsNullOrEmpty(level))
            url += $"&level={level}";
        var response = await _http.GetAsync(url);
        return await HandleResponse<List<ConsoleLogEntry>>(response);
    }

    public async Task ClearConsoleLogs()
    {
        await SetAuthHeader();
        await _http.DeleteAsync("/api/logs/console");
    }

    // Certificates
    public async Task<List<Certificate>?> GetCertificates()
    {
        await SetAuthHeader();
        var response = await _http.GetAsync("/api/certificates");
        return await HandleResponse<List<Certificate>>(response);
    }

    public async Task CleanupExpiredCertificates()
    {
        await SetAuthHeader();
        var response = await _http.PostAsync("/api/certificates/cleanup", null);
        await HandleResponse<object>(response);
    }

    // Version
    public async Task<string> GetBuildVersion()
    {
        try
        {
            var response = await _http.GetAsync("/api/version");
            if (response.IsSuccessStatusCode)
            {
                var versionInfo = await response.Content.ReadFromJsonAsync<VersionInfo>();
                return versionInfo?.Version ?? "unknown";
            }
            return "unknown";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Version fetch error: {ex.Message}");
            return "unknown";
        }
    }
}
