using System.Text;
using IamApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Azure App Service for Containers sets the inbound port via WEBSITES_PORT (and sometimes PORT).
// Bind to it when present; otherwise use the normal ASP.NET Core defaults (dev: 5000/5001).
var inboundPort = Environment.GetEnvironmentVariable("WEBSITES_PORT")
    ?? Environment.GetEnvironmentVariable("PORT");
if (int.TryParse(inboundPort, out var port) && port > 0)
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// Add API services
builder.Services.AddSingleton<IDataStore, InMemoryDataStore>();
builder.Services.AddSingleton<IAuthService, AuthService>();
builder.Services.AddSingleton<ICertificateService, CertificateService>();
builder.Services.AddHostedService<IamApi.CertificateMaintenanceWorker>();

// Configure JWT Authentication
var jwtSettings = builder.Configuration.GetSection("Jwt");
var secretKey = jwtSettings["Secret"] ?? throw new InvalidOperationException("JWT Secret not configured");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey))
    };
});

builder.Services.AddAuthorization();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Ensure scheme/remote IP are correct when behind App Service's reverse proxy.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Add CORS for frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Add Blazor WebAssembly hosting
builder.Services.AddRazorPages();

var app = builder.Build();

// Perform certificate cleanup on startup
var logger = app.Services.GetRequiredService<ILogger<Program>>();
var certificateService = app.Services.GetRequiredService<ICertificateService>();
logger.LogInformation("Performing certificate cleanup on startup...");
await certificateService.CleanupExpiredCertificatesAsync();
logger.LogInformation("Startup certificate cleanup completed");

var enableSwagger = builder.Configuration.GetValue("Swagger:Enabled", builder.Environment.IsDevelopment());

// Configure the HTTP request pipeline
if (enableSwagger)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseForwardedHeaders();
app.UseCors("AllowAll");

// Configure static file options to properly serve Blazor WASM files
var provider = new FileExtensionContentTypeProvider();
provider.Mappings[".dat"] = "application/octet-stream";
provider.Mappings[".wasm"] = "application/wasm";
provider.Mappings[".br"] = "application/octet-stream";
provider.Mappings[".dll"] = "application/octet-stream";

app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = provider,
    ServeUnknownFileTypes = true,
    DefaultContentType = "application/octet-stream"
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Fallback to index.html for client-side routing, but not for API routes
app.MapFallbackToFile("index.html").AllowAnonymous();

app.MapGet("/healthz", () => Results.Ok("ok")).AllowAnonymous();
app.MapGet("/health", () => Results.Ok("ok")).AllowAnonymous();

// Managed Identity diagnostics endpoint
app.MapGet("/debug/managed-identity", async (ILogger<Program> logger) =>
{
    try
    {
        logger.LogInformation("Managed Identity diagnostic check requested");
        
        var diagnostics = new Dictionary<string, object?>
        {
            ["timestamp"] = DateTime.UtcNow,
            ["environment"] = new Dictionary<string, string?>
            {
                ["IDENTITY_ENDPOINT"] = Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT"),
                ["IDENTITY_HEADER"] = Environment.GetEnvironmentVariable("IDENTITY_HEADER") != null ? "[SET]" : null,
                ["MSI_ENDPOINT"] = Environment.GetEnvironmentVariable("MSI_ENDPOINT"),
                ["MSI_SECRET"] = Environment.GetEnvironmentVariable("MSI_SECRET") != null ? "[SET]" : null,
                ["AZURE_CLIENT_ID"] = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID"),
                ["AZURE_TENANT_ID"] = Environment.GetEnvironmentVariable("AZURE_TENANT_ID"),
                ["CONTAINER_APP_NAME"] = Environment.GetEnvironmentVariable("CONTAINER_APP_NAME"),
                ["CONTAINER_APP_REVISION"] = Environment.GetEnvironmentVariable("CONTAINER_APP_REVISION")
            },
            ["configuration"] = new Dictionary<string, string?>
            {
                ["EntraId:ClientId"] = builder.Configuration["EntraId:ClientId"],
                ["EntraId:TenantId"] = builder.Configuration["EntraId:TenantId"]
            }
        };

        return Results.Ok(diagnostics);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error during managed identity diagnostics");
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

app.Run();
