using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using IamApi.Models;
using Microsoft.IdentityModel.Tokens;

namespace IamApi.Services;

public sealed class AuthService : IAuthService
{
    private readonly IConfiguration _configuration;
    private readonly Dictionary<string, User> _users;

    public AuthService(IConfiguration configuration)
    {
        _configuration = configuration;
        _users = new Dictionary<string, User>();
        
        // Initialize with a default user
        // Username: admin
        // Password: IAM#2026!SecureP@ssw0rd
        var defaultUser = new User
        {
            Username = "admin",
            PasswordHash = HashPassword("IAM#2026!SecureP@ssw0rd"),
            Role = "Administrator",
            CreatedAt = DateTime.UtcNow
        };
        
        _users[defaultUser.Username] = defaultUser;
    }

    public async Task<LoginResponse?> AuthenticateAsync(string username, string password)
    {
        await Task.CompletedTask; // For consistency with async pattern
        
        var user = GetUserByUsername(username);
        if (user == null || !ValidatePassword(password, user.PasswordHash))
        {
            return null;
        }

        var token = GenerateJwtToken(user);
        var expiresAt = DateTime.UtcNow.AddHours(8);

        return new LoginResponse
        {
            Token = token,
            Username = user.Username,
            Role = user.Role,
            ExpiresAt = expiresAt
        };
    }

    public string GenerateJwtToken(User user)
    {
        var jwtSettings = _configuration.GetSection("Jwt");
        var secretKey = jwtSettings["Secret"] ?? throw new InvalidOperationException("JWT Secret not configured");
        var issuer = jwtSettings["Issuer"] ?? "IAM-API";
        var audience = jwtSettings["Audience"] ?? "IAM-API-Users";

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Username),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public bool ValidatePassword(string password, string hash)
    {
        var passwordHash = HashPassword(password);
        return passwordHash == hash;
    }

    public User? GetUserByUsername(string username)
    {
        return _users.TryGetValue(username, out var user) ? user : null;
    }

    private static string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(hashedBytes);
    }
}
