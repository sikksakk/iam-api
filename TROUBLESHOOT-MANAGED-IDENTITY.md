# Troubleshooting Managed Identity Issues

## Current Error

```
ManagedIdentityCredential authentication failed: [Managed Identity] Error Message: 
Unable to load the proper Managed Identity.
Status Code: 400
```

## What This Means

The Container App is correctly configured with managed identity endpoints, but the Azure platform is returning a 400 Bad Request when trying to acquire a token. This typically indicates a configuration issue at the Azure resource level.

## Debug Information from Logs

Based on your logs, the environment is correctly configured:
- ✅ `IDENTITY_ENDPOINT`: http://localhost:12356/msi/token (correct)
- ✅ `IDENTITY_HEADER`: [SET] (correct)
- ✅ `MSI_ENDPOINT`: http://localhost:12356/msi/token (correct)
- ✅ `MSI_SECRET`: [SET] (correct)
- ✅ `CONTAINER_APP_NAME`: iam-api (detected)
- ✅ `CONTAINER_APP_REVISION`: iam-api--0000001 (detected)

## Common Causes for 400 Error

### 1. System-Assigned Managed Identity Not Enabled ⚠️

**Check:**
```bash
az containerapp show \
  --name iam-api \
  --resource-group <your-resource-group> \
  --query identity.type
```

**Expected Output:** `"SystemAssigned"` or `"SystemAssigned, UserAssigned"`

**Fix if not enabled:**
```bash
az containerapp identity assign \
  --name iam-api \
  --resource-group <your-resource-group> \
  --system-assigned
```

### 2. Managed Identity Principal ID Not Retrieved

**Get the Principal ID:**
```bash
az containerapp show \
  --name iam-api \
  --resource-group <your-resource-group> \
  --query identity.principalId -o tsv
```

If this returns empty or null, the managed identity isn't fully configured.

### 3. Recent Deployment Without Identity Propagation

If you just enabled managed identity, it may take 1-2 minutes to propagate. Wait and try again.

### 4. Container App Revision Issues

The managed identity is tied to the revision. Try creating a new revision:
```bash
az containerapp revision list \
  --name iam-api \
  --resource-group <your-resource-group>

# If needed, create new revision with identity
az containerapp update \
  --name iam-api \
  --resource-group <your-resource-group>
```

### 5. Missing Microsoft Graph API Permissions

Even if managed identity is enabled, it needs Graph API permissions:

**Get the managed identity's principal ID:**
```bash
PRINCIPAL_ID=$(az containerapp show \
  --name iam-api \
  --resource-group <your-resource-group> \
  --query identity.principalId -o tsv)

echo "Managed Identity Principal ID: $PRINCIPAL_ID"
```

**Grant Application.ReadWrite.All permission:**
```bash
# Get Microsoft Graph Service Principal ID
GRAPH_SP_ID=$(az ad sp list --query "[?appId=='00000003-0000-0000-c000-000000000000'].id" -o tsv)

# Get Application.ReadWrite.All role ID
APP_ROLE_ID=$(az ad sp show --id $GRAPH_SP_ID \
  --query "appRoles[?value=='Application.ReadWrite.All'].id" -o tsv)

# Grant the permission
az rest --method POST \
  --uri "https://graph.microsoft.com/v1.0/servicePrincipals/$GRAPH_SP_ID/appRoleAssignments" \
  --headers "Content-Type=application/json" \
  --body "{
    \"principalId\": \"$PRINCIPAL_ID\",
    \"resourceId\": \"$GRAPH_SP_ID\",
    \"appRoleId\": \"$APP_ROLE_ID\"
  }"
```

## Diagnostic Endpoints

### Test Managed Identity Status

Visit: `https://<your-app-url>/debug/managed-identity`

This will show:
- Environment variables
- Token acquisition test results
- Detailed error messages

### Check Container App Logs

```bash
az containerapp logs show \
  --name iam-api \
  --resource-group <your-resource-group> \
  --follow
```

Look for:
```
dbug: IamApi.Services.CertificateService[0]
      Managed Identity Environment Check:
```

## Enhanced Debugging

### Enable Even More Logging

Set environment variable in Container App:
```bash
az containerapp update \
  --name iam-api \
  --resource-group <your-resource-group> \
  --set-env-vars "AZURE_IDENTITY_DISABLE_CP1=1" "Logging__LogLevel__Azure.Identity=Verbose"
```

### Test with Azure CLI

From within the container (using Console or SSH):
```bash
# Test if MSI endpoint is reachable
curl -H "Metadata: true" "$IDENTITY_ENDPOINT?resource=https://graph.microsoft.com&api-version=2019-08-01"
```

## Azure Support

If the issue persists, contact Azure Support with:
- **Managed Identity Correlation ID**: `451bc6aa-34a5-4978-a48f-f3b44058aae8` (from your logs)
- Container App name: `iam-api`
- Resource Group
- Subscription ID
- Timestamp of failure: `2026-01-24T20:01:14Z`

## Quick Fix Checklist

- [ ] Verify system-assigned managed identity is enabled
- [ ] Confirm identity principal ID exists
- [ ] Check if identity has Application.ReadWrite.All permission on Microsoft Graph
- [ ] Wait 2-3 minutes after enabling identity
- [ ] Restart the container app revision
- [ ] Check Azure service health for Container Apps/Managed Identity
- [ ] Verify subscription isn't experiencing issues

## Next Steps

1. Run the identity check: `az containerapp show --name iam-api --resource-group <your-rg> --query identity`
2. Visit `/debug/managed-identity` endpoint
3. Check if recent Azure outages affected managed identity service
4. Review container app logs during startup
