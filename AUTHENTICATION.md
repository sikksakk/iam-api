# IAM API Authentication System

## Overview
The IAM API now includes JWT-based authentication to secure all API endpoints. Users must login before accessing the job management interface.

## Default Credentials

**Username:** `admin`  
**Password:** `IAM#2026!SecureP@ssw0rd`

> **Important:** For production deployments, change the JWT secret in `appsettings.json` or use environment variables.

## Features

- **JWT Token Authentication**: Secure token-based authentication with 8-hour expiration
- **Login Screen**: Beautiful, responsive login interface at `/login.html`
- **Auto-redirect**: Unauthenticated users are automatically redirected to login
- **Session Management**: Token stored in localStorage with automatic validation
- **Protected Endpoints**: All API controllers require authentication
- **User Display**: Shows logged-in username in the header
- **Logout Functionality**: Clean logout with session clearing

## API Endpoints

### Authentication Endpoints

#### POST /api/auth/login
Login with username and password.

**Request:**
```json
{
  "username": "admin",
  "password": "IAM#2026!SecureP@ssw0rd"
}
```

**Response:**
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "username": "admin",
  "role": "Administrator",
  "expiresAt": "2026-01-23T20:00:00Z"
}
```

#### POST /api/auth/validate
Validate current JWT token (requires authentication).

**Response:**
```json
{
  "valid": true,
  "username": "admin"
}
```

### Protected Endpoints

All existing endpoints now require authentication:
- `/api/jobs/*` - Job management
- `/api/orchestrators/*` - Orchestrator registration and heartbeat
- `/api/logs/*` - Log retrieval

## Using the API with Authentication

### Browser Usage
1. Navigate to the API root URL (e.g., `http://localhost:5000`)
2. You'll be redirected to `/login.html`
3. Enter credentials and click "Sign In"
4. Upon successful login, you'll be redirected to the main interface
5. All API calls automatically include the JWT token

### Programmatic Access

Include the JWT token in the Authorization header:

```bash
# Login first
curl -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"IAM#2026!SecureP@ssw0rd"}'

# Use the returned token
curl http://localhost:5000/api/jobs \
  -H "Authorization: Bearer YOUR_JWT_TOKEN_HERE"
```

### Orchestrator Integration

Update orchestrators to include authentication when calling the API:

```csharp
var client = new HttpClient();
var loginRequest = new { username = "admin", password = "IAM#2026!SecureP@ssw0rd" };
var loginResponse = await client.PostAsJsonAsync("http://api:5000/api/auth/login", loginRequest);
var loginData = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

client.DefaultRequestHeaders.Authorization = 
    new AuthenticationHeaderValue("Bearer", loginData.Token);

// Now make authenticated requests
await client.PostAsJsonAsync("http://api:5000/api/orchestrators/heartbeat", orchestratorInfo);
```

## Configuration

### JWT Settings (appsettings.json)

```json
{
  "Jwt": {
    "Secret": "IAM-SuperSecure-JWT-Key-2026-ForProductionUseEnvironmentVariable!",
    "Issuer": "IAM-API",
    "Audience": "IAM-API-Users"
  }
}
```

### Environment Variables (Production)

For production, override the JWT secret using environment variables:

```bash
export Jwt__Secret="YOUR_PRODUCTION_SECRET_KEY_HERE"
```

Or in Docker:
```yaml
environment:
  - Jwt__Secret=YOUR_PRODUCTION_SECRET_KEY_HERE
```

## Security Considerations

1. **Change Default Credentials**: Create new users or change the admin password in `AuthService.cs`
2. **Use Strong JWT Secret**: The secret should be at least 32 characters and randomly generated
3. **HTTPS Only**: In production, always use HTTPS to protect tokens in transit
4. **Token Expiration**: Tokens expire after 8 hours; users must re-login
5. **Store Secrets Securely**: Use Azure Key Vault, AWS Secrets Manager, or similar for production secrets

## Adding New Users

Edit `Services/AuthService.cs` constructor to add more users:

```csharp
// Add a new user
var newUser = new User
{
    Username = "operator",
    PasswordHash = HashPassword("SecurePassword123!"),
    Role = "Operator",
    CreatedAt = DateTime.UtcNow
};
_users[newUser.Username] = newUser;
```

## Troubleshooting

### "Invalid username or password"
- Check that you're using the correct credentials
- Verify the password includes special characters exactly as specified

### "Unauthorized" errors after login
- Check browser console for authentication errors
- Verify the JWT token is being included in requests
- Check if the token has expired (8-hour limit)

### Auto-logout behavior
- Tokens expire after 8 hours
- Invalid tokens trigger automatic logout and redirect to login page
- Clearing localStorage will also log you out

## Files Modified/Created

- **Controllers/AuthController.cs** - New authentication endpoint
- **Services/AuthService.cs** - Authentication logic and user management
- **Services/IAuthService.cs** - Authentication service interface
- **Models/User.cs** - User models and DTOs
- **wwwroot/login.html** - Login page UI
- **wwwroot/index.html** - Added authentication checks and logout
- **Program.cs** - Added JWT middleware configuration
- **appsettings.json** - Added JWT configuration
- **iam-api.csproj** - Added JWT NuGet packages
