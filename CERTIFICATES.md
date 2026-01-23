# Certificate Management System

## Overview

The IAM system now includes automatic certificate management with Azure Managed Identity integration. Certificates are created per customer with 2-hour validity, automatically uploaded to Azure AD App Registration, and made available to orchestrators for container authentication.

## Architecture

### IAM API
- **Uses Azure Managed Identity** to authenticate to Microsoft Graph API
- **Creates self-signed certificates** per customer with configurable validity (default: 2 hours)
- **Uploads certificates** to Azure AD App Registration automatically
- **Maintains certificates** through background worker (auto-cleanup expired certs)
- **Exposes certificates** via REST API endpoint

### Orchestrator
- **Downloads certificates** from API per customer before job execution
- **Stores certificates in memory** (byte array)
- **Mounts certificates** into Docker containers as files
- **Passes certificate credentials** via environment variables
- **Automatic cleanup** of temporary certificate files

### Containers
- Receive certificate at `/tmp/client-cert.pfx`
- Access certificate password via `CLIENT_CERT_PASSWORD` env variable
- Can use certificate for Azure AD authentication

## Configuration

### IAM API (appsettings.json)

```json
{
  "AzureAd": {
    "TenantId": "your-tenant-id",
    "ClientId": "your-app-registration-id",
    "CertificateValidityHours": 2
  }
}
```

**Environment Variables (Azure Container Apps):**
```bash
AzureAd__TenantId=your-tenant-id
AzureAd__ClientId=your-app-registration-id
AzureAd__CertificateValidityHours=2
```

### Azure Setup Requirements

1. **Enable Managed Identity** on your Azure Container App
2. **Grant permissions** to the Managed Identity:
   - Azure AD role: **Application Administrator** or **Owner** on the App Registration
3. **App Registration** must exist with the ClientId configured

### Orchestrator Configuration

No additional configuration required. The orchestrator automatically:
- Downloads certificates when executing jobs with a customer name
- Passes certificates to containers
- Cleans up temporary files

## API Endpoints

### Get Certificate for Customer
```http
GET /api/certificates/{customerName}
Authorization: Bearer {token}
```

**Response:**
```json
{
  "customerName": "alpha",
  "certificateData": "MIID...base64...==",
  "thumbprint": "ABC123...",
  "expiresAt": "2026-01-23T14:00:00Z",
  "password": "generated-secure-password"
}
```

### List All Certificates
```http
GET /api/certificates
Authorization: Bearer {token}
```

**Response:**
```json
[
  {
    "customerName": "alpha",
    "thumbprint": "ABC123...",
    "createdAt": "2026-01-23T12:00:00Z",
    "expiresAt": "2026-01-23T14:00:00Z",
    "isExpired": false,
    "expiresInMinutes": 45
  }
]
```

### Manual Cleanup
```http
POST /api/certificates/cleanup
Authorization: Bearer {token}
```

## Certificate Lifecycle

### Creation
1. Orchestrator requests job execution for customer "alpha"
2. API checks if valid certificate exists for "alpha"
3. If not (or expiring soon), API generates new self-signed certificate:
   - Subject: `CN=IAM-{customerName}`
   - Key size: 2048-bit RSA
   - Validity: 2 hours (configurable)
   - Usage: Digital Signature, Key Encipherment, Client Authentication
4. Certificate uploaded to Azure AD App Registration via Microsoft Graph
5. Certificate stored in memory with metadata

### Usage
1. Orchestrator downloads certificate before job execution
2. Certificate written to temp file (e.g., `/tmp/cert-{jobId}.pfx`)
3. Docker container created with:
   - Volume mount: `/tmp/cert-{jobId}.pfx:/tmp/client-cert.pfx:ro`
   - Environment variables:
     - `CLIENT_CERT_PATH=/tmp/client-cert.pfx`
     - `CLIENT_CERT_PASSWORD={password}`
4. Container can use certificate for authentication
5. After job completion, temp file deleted

### Maintenance
- **Background worker** runs every 5 minutes
- Checks for expired certificates
- Deletes expired certificates from:
  - Azure AD App Registration (via Graph API)
  - In-memory cache
- Logs cleanup operations

### Renewal
- Certificates renewed automatically when:
  - Existing certificate has < 10 minutes remaining
  - Certificate requested but expired
- Old certificate deleted from Azure AD before creating new one

## Container Usage

### PowerShell Example
```powershell
# Read certificate from mounted path
$certPath = $env:CLIENT_CERT_PATH
$certPassword = ConvertTo-SecureString $env:CLIENT_CERT_PASSWORD -AsPlainText -Force
$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($certPath, $certPassword)

# Use with Azure AD authentication
Connect-MgGraph -ClientId $env:clientId -TenantId $env:tenantId -Certificate $cert
```

### C# Example
```csharp
var certPath = Environment.GetEnvironmentVariable("CLIENT_CERT_PATH");
var certPassword = Environment.GetEnvironmentVariable("CLIENT_CERT_PASSWORD");
var cert = new X509Certificate2(certPath, certPassword);

// Use with Azure authentication
var credential = new ClientCertificateCredential(
    tenantId, 
    clientId, 
    cert);
```

## Security Features

1. **Short-lived certificates**: 2-hour validity reduces exposure window
2. **Automatic rotation**: Certificates renewed before expiration
3. **Secure password generation**: Random 32-character passwords
4. **Memory-only storage**: Certificates never persisted to disk (except temp mount)
5. **Automatic cleanup**: Expired certificates removed from Azure AD
6. **Read-only mounts**: Containers cannot modify certificates
7. **Managed Identity**: No stored credentials for Azure AD access

## Monitoring & Logging

### API Logs
```
[Information] Creating new certificate for customer: alpha
[Information] Certificate created for alpha, thumbprint: ABC123..., expires: 2026-01-23T14:00:00Z
[Information] Certificate uploaded to Azure AD for app {ClientId}
[Information] Cleaned up 2 expired certificates
```

### Orchestrator Logs
```
[Information] Downloading certificate for customer: alpha
[Information] Certificate downloaded - Thumbprint: ABC123..., Expires: 2026-01-23T14:00:00
[Information] Certificate mounted for job {JobId}
```

### Job Logs (visible in UI)
```
Certificate acquired for alpha (expires: 2026-01-23 14:00:00 UTC)
```

## Troubleshooting

### Certificate creation fails
**Symptoms:** API returns 500 or null certificate

**Check:**
1. Managed Identity enabled on Container App
2. Managed Identity has permissions on App Registration
3. `AzureAd:ClientId` configured correctly
4. Check API logs for Graph API errors

### Orchestrator can't download certificate
**Symptoms:** Warning log "Could not obtain certificate"

**Check:**
1. API is reachable from orchestrator
2. Authentication credentials correct
3. Customer name matches exactly

### Container can't access certificate
**Symptoms:** Container crashes or auth fails

**Check:**
1. Certificate path exists: `/tmp/client-cert.pfx`
2. Environment variables set: `CLIENT_CERT_PATH`, `CLIENT_CERT_PASSWORD`
3. Container has PowerShell or .NET runtime to load certificate
4. Certificate not expired (check job logs for expiry time)

## Performance Considerations

- **In-memory storage**: Certificates stored in memory for fast access
- **Lazy creation**: Certificates only created when requested
- **Cached auth**: Orchestrator caches Graph API token
- **Minimal overhead**: Certificate download adds ~1-2 seconds to job startup

## Future Enhancements

- [ ] Support for Azure Key Vault certificate storage
- [ ] Certificate revocation tracking
- [ ] Multiple certificate profiles per customer
- [ ] Certificate usage metrics and auditing
- [ ] Custom certificate validity per customer
- [ ] Certificate rotation notifications
