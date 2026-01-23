using IamApi.Models;

namespace IamApi.Services;

public interface IAuthService
{
    Task<LoginResponse?> AuthenticateAsync(string username, string password);
    string GenerateJwtToken(User user);
    bool ValidatePassword(string password, string hash);
    User? GetUserByUsername(string username);
}
