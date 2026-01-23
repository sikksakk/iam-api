# Troubleshooting: Managed Identity Not Available

## Problem

You're seeing this error:
```
ManagedIdentityCredential authentication unavailable. No response received from the managed identity endpoint.
Connection refused (169.254.169.254:80)
```

This means **System-Assigned Managed Identity is NOT enabled** on your Azure Container App.

## Solution

### Step 1: Enable Managed Identity

#### Using Azure Portal:
1. Go to **Azure Portal** → **Container Apps**
2. Select your **IAM API** container app
3. In the left menu, go to **Settings** → **Identity**
4. Under **System assigned** tab
5. Toggle **Status** to **On**
6. Click **Save**
7. Wait for the operation to complete
8. Copy the **Object (principal) ID** that appears (you'll need this)

#### Using Azure CLI:
```bash
# Enable system-assigned identity
az containerapp identity assign \
  --name <your-iam-api-app-name> \
  --resource-group <your-resource-group> \
  --system-assigned

# Get the principal ID
az containerapp identity show \
  --name <your-iam-api-app-name> \
  --resource-group <your-resource-group> \
  --query principalId -o tsv
```

### Step 2: Grant Permissions to Managed Identity

The Managed Identity needs permission to manage certificates on your App Registration.

#### Option A: Grant Application Administrator Role (Easiest)

```bash
# Get the principal ID from Step 1
PRINCIPAL_ID="<your-principal-id>"

# Get the Application Administrator role ID
ROLE_ID=$(az ad sp list \
  --display-name "Microsoft Graph" \
  --query "[0].appRoles[?value=='Application.ReadWrite.All'].id" \
  -o tsv | head -1)

# Or use Application Administrator directory role
az rest --method POST \
  --url "https://graph.microsoft.com/v1.0/roleManagement/directory/roleAssignments" \
  --headers "Content-Type=application/json" \
  --body "{
    \"principalId\": \"$PRINCIPAL_ID\",
    \"roleDefinitionId\": \"9b895d92-2cd3-44c7-9d02-a6ac2d5ea5c3\",
    \"directoryScopeId\": \"/\"
  }"
```

The role ID `9b895d92-2cd3-44c7-9d02-a6ac2d5ea5c3` is the **Application Administrator** role.

#### Option B: Grant Owner on Specific App Registration (More Secure)

```bash
# Get your App Registration Object ID (NOT client ID)
APP_OBJECT_ID=$(az ad app show \
  --id <your-client-id> \
  --query id -o tsv)

# Add Managed Identity as owner
az ad app owner add \
  --id $APP_OBJECT_ID \
  --owner-object-id <your-principal-id>
```

### Step 3: Verify Configuration

Make sure these environment variables are set:

```bash
az containerapp show \
  --name <your-iam-api-app-name> \
  --resource-group <your-resource-group> \
  --query properties.template.containers[0].env
```

Should include:
- `AzureAd__ClientId` = Your App Registration's **Application (client) ID**
- `AzureAd__TenantId` = Your Azure AD **Directory (tenant) ID**

If missing, add them:

```bash
az containerapp update \
  --name <your-iam-api-app-name> \
  --resource-group <your-resource-group> \
  --set-env-vars \
    "AzureAd__TenantId=<your-tenant-id>" \
    "AzureAd__ClientId=<your-client-id>"
```

### Step 4: Restart Container App

```bash
az containerapp revision restart \
  --name <your-iam-api-app-name> \
  --resource-group <your-resource-group>
```

Or in Azure Portal:
1. Go to your Container App
2. Click **Restart**
3. Wait for restart to complete

### Step 5: Test Certificate Creation

After enabling Managed Identity and granting permissions:

```bash
# Get auth token
TOKEN=$(curl -X POST https://<your-api-url>/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"IAM#2026!SecureP@ssw0rd"}' \
  | jq -r .token)

# Request certificate
curl https://<your-api-url>/api/certificates/alpha \
  -H "Authorization: Bearer $TOKEN" \
  -v
```

## Expected Logs After Fix

### Success:
```
[Debug] Initializing Microsoft Graph client with Managed Identity
[Information] Graph client initialized successfully with Managed Identity
[Debug] Configuration check - ClientId: True, TenantId: True, ValidityHours: 2
[Information] Creating new certificate for customer: alpha
[Debug] Getting Graph client for certificate upload
[Debug] Fetching application xxx-xxx-xxx from Azure AD
[Debug] Adding certificate to application. Current certificates: 0
[Debug] Updating application in Azure AD
[Information] Certificate uploaded to Azure AD for app xxx-xxx-xxx
[Information] Certificate created for alpha, thumbprint: ABC123...
```

### If Still Failing - Permission Issue:
```
[Error] Permission denied. Managed Identity needs Application.ReadWrite.All or Owner role on the App Registration.
```
→ Go back to Step 2 and verify permissions

### If Still Failing - App Not Found:
```
[Error] Application {ClientId} not found. Verify the ClientId configuration.
```
→ Check that `AzureAd__ClientId` is set to the **Application (client) ID** (not Object ID)

## Common Mistakes

### ❌ Using Object ID instead of Client ID
- **Wrong**: `AzureAd__ClientId=12345678-abcd-1234-5678-1234567890ab` (Object ID from App Registration Manifest)
- **Right**: `AzureAd__ClientId=87654321-dcba-4321-8765-ba0987654321` (Application ID from Overview)

### ❌ Not waiting for permission propagation
- After granting permissions, wait **5-10 minutes** before testing
- Azure AD permissions take time to propagate

### ❌ Granting permissions to wrong identity
- Make sure you're granting to the **Container App's Managed Identity**
- Not to your user account or other service principals

### ❌ Identity not enabled
- The error `Connection refused (169.254.169.254:80)` specifically means **identity is NOT enabled**
- Double-check Identity settings in portal

## Verification Commands

### Check if Identity is Enabled:
```bash
az containerapp identity show \
  --name <your-app-name> \
  --resource-group <your-resource-group>
```

Should return:
```json
{
  "principalId": "xxx-xxx-xxx",
  "tenantId": "xxx-xxx-xxx",
  "type": "SystemAssigned"
}
```

If it returns `null` or empty → **Identity is NOT enabled**

### Check Role Assignments:
```bash
# List all role assignments for the managed identity
az role assignment list \
  --assignee <principal-id> \
  --all
```

### Check App Registration Owners:
```bash
az ad app owner list \
  --id <your-client-id>
```

Should include your Managed Identity's principal ID.

## Still Not Working?

1. **Check Container App logs** in real-time:
   ```bash
   az containerapp logs show \
     --name <your-app-name> \
     --resource-group <your-resource-group> \
     --follow
   ```

2. **Verify network connectivity** from Container App to Azure AD:
   - Managed Identity endpoint: `http://169.254.169.254/metadata/identity/oauth2/token`
   - This should be accessible from within the container

3. **Check Container App environment**:
   - Ensure it's not in a restrictive VNET that blocks the IMDS endpoint

4. **Contact Azure Support** if:
   - Identity shows as enabled but still getting connection refused
   - Permissions are granted but still getting 403 errors after 30+ minutes
