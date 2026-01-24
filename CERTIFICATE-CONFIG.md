# Certificate Management Configuration Guide

## Problem: "EntraId:ClientId not configured" Warning

If you see this warning, certificate management is disabled. Follow these steps to configure it:

## Azure Configuration Steps

### 1. Get Your App Registration Details

In Azure Portal:
1. Go to **Azure Active Directory** → **App registrations**
2. Find your app registration
3. Copy the following values:
   - **Application (client) ID** - This is your `ClientId`
   - **Directory (tenant) ID** - This is your `TenantId`

### 2. Enable Managed Identity

For Azure Container Apps:
1. Go to your Container App in Azure Portal
2. Navigate to **Settings** → **Identity**
3. Under **System assigned**, toggle **Status** to **On**
4. Click **Save**
5. Copy the **Object (principal) ID** that appears

### 3. Grant Permissions to Managed Identity

#### Option A: Using Azure Portal
1. Go to **Azure Active Directory** → **App registrations**
2. Select your app registration
3. Go to **Manifest**
4. Note the `id` field (this is the App Registration Object ID)
5. Go to **Azure Active Directory** → **Roles and administrators**
6. Search for and select **Application Administrator**
7. Click **Add assignments**
8. Search for your Container App's managed identity (by the principal ID)
9. Add it

#### Option B: Using Azure CLI
```bash
# Get the managed identity principal ID
PRINCIPAL_ID=$(az containerapp identity show \
  --name your-container-app-name \
  --resource-group your-resource-group \
  --query principalId -o tsv)

# Get the App Registration object ID (not client ID!)
APP_OBJECT_ID=$(az ad app show \
  --id your-client-id \
  --query id -o tsv)

# Grant Owner role on the app registration
az ad app owner add \
  --id $APP_OBJECT_ID \
  --owner-object-id $PRINCIPAL_ID
```

### 4. Configure IAM API

Set these environment variables in your Azure Container App:

```bash
AzureAd__TenantId=your-tenant-id-here
AzureAd__ClientId=your-client-id-here
AzureAd__CertificateValidityHours=2
```

**In Azure Portal:**
1. Go to your IAM API Container App
2. Navigate to **Containers** → **Environment variables**
3. Add:
   - Name: `AzureAd__TenantId`, Value: `your-tenant-id`
   - Name: `AzureAd__ClientId`, Value: `your-client-id`
   - Name: `AzureAd__CertificateValidityHours`, Value: `2`
4. Click **Save**
5. The container will restart automatically

**Using Azure CLI:**
```bash
az containerapp update \
  --name your-iam-api-app \
  --resource-group your-resource-group \
  --set-env-vars \
    "EntraId__TenantId=your-tenant-id" \
    "EntraId__ClientId=your-client-id" \
    "EntraId__CertificateValidityHours=2"
```

## Local Development Configuration

For local development, add to `appsettings.Development.json`:

```json
{
  "AzureAd": {
    "TenantId": "your-tenant-id",
    "ClientId": "your-client-id",
    "CertificateValidityHours": 2
  }
}
```

**Note:** Local development requires Azure CLI authentication:
```bash
az login
```

## Verification

After configuration, check the logs:

### Expected Success Logs (IAM API):
```
[Information] GetOrCreateCertificateAsync called for customer: alpha
[Debug] Configuration check - ClientId: True, TenantId: True, ValidityHours: 2
[Information] Creating new certificate for customer: alpha
[Debug] Generating self-signed certificate with 2 hour validity
[Debug] Certificate generated - Thumbprint: ABC123..., Size: 2048 bytes
[Debug] Attempting to upload certificate to Azure AD for ClientId: xxx
[Debug] Certificate uploaded successfully with KeyId: xxx
[Information] Certificate created for alpha, thumbprint: ABC123..., expires: 2026-01-23T17:00:00Z
```

### If Still Failing:

**Check permissions:**
```bash
# Verify managed identity has permission
az role assignment list \
  --assignee $PRINCIPAL_ID \
  --all
```

**Check app registration access:**
```bash
# Verify you can access the app
az ad app show --id your-client-id
```

**Common errors:**

1. **"Insufficient privileges"** 
   - Managed identity needs Application Administrator role
   - Or Owner role on the specific App Registration

2. **"Application not found"**
   - Wrong ClientId
   - App in different tenant

3. **"Authorization_RequestDenied"**
   - Managed identity not granted permissions
   - Wait 5-10 minutes after granting permissions (Azure AD propagation)

## Testing

Test certificate creation manually:

```bash
# Get auth token
TOKEN=$(curl -X POST https://your-api.azurecontainerapps.io/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"IAM#2026!SecureP@ssw0rd"}' \
  | jq -r .token)

# Request certificate
curl https://your-api.azurecontainerapps.io/api/certificates/alpha \
  -H "Authorization: Bearer $TOKEN" \
  | jq
```

Expected response:
```json
{
  "customerName": "alpha",
  "certificateData": "MIID...base64...==",
  "thumbprint": "ABC123DEF456...",
  "expiresAt": "2026-01-23T17:00:00Z",
  "password": "random-password"
}
```

## Important Notes

1. **ClientId vs ObjectId**: 
   - Use **Application (client) ID** for configuration
   - Use **Object ID** for permission assignments

2. **Managed Identity**: Must be system-assigned on the Container App

3. **Permissions**: Application Administrator role is broad. For production, use Owner role on specific App Registration only.

4. **Certificate validity**: 2 hours is recommended. Longer validity = larger security window if compromised.

5. **Propagation delay**: After granting permissions, wait 5-10 minutes before testing.
