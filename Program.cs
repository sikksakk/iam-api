using System.Text;
using IamApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
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

// Add services to the container
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

var app = builder.Build();

var enableSwagger = builder.Configuration.GetValue("Swagger:Enabled", builder.Environment.IsDevelopment());

// Configure the HTTP request pipeline
if (enableSwagger)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseForwardedHeaders();
app.UseCors("AllowAll");
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/healthz", () => Results.Ok("ok"));

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
                ["EntraId:ManagedIdentityClientId"] = builder.Configuration["EntraId:ManagedIdentityClientId"]
            }
        };
        
        // Test token acquisition
        try
        {
            logger.LogDebug("Testing managed identity token acquisition...");
            var credential = new Azure.Identity.ManagedIdentityCredential();
            var tokenContext = new Azure.Core.TokenRequestContext(new[] { "https://graph.microsoft.com/.default" });
            var token = await credential.GetTokenAsync(tokenContext, default);
            
            diagnostics["token_test"] = new
            {
                success = true,
                expires_at = token.ExpiresOn.UtcDateTime,
                token_length = token.Token.Length
            };
            logger.LogInformation("✓ Token acquired successfully");
        }
        catch (Exception ex)
        {
            diagnostics["token_test"] = new
            {
                success = false,
                error = ex.GetType().Name,
                message = ex.Message
            };
            logger.LogError(ex, "✗ Token acquisition failed");
        }
        
        return Results.Json(diagnostics);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Diagnostic check failed");
        return Results.Problem(ex.Message);
    }
});

// Default route to redirect to login
app.MapGet("/", () => Results.Redirect("/login.html"));

app.Run();
