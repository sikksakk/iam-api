# Orchestrator Online Status Feature

## Overview

The API now tracks which orchestrators are online by receiving periodic heartbeats from each orchestrator instance. The frontend displays all online orchestrators in real-time.

## How It Works

### 1. Orchestrator Heartbeat
Each orchestrator sends a heartbeat to the API every 10 seconds (based on the `PollingIntervalSeconds` configuration):

```json
POST /api/orchestrators/heartbeat
{
  "id": "HOSTNAME-abc123",
  "customerName": "alpha",
  "hostName": "HOSTNAME",
  "version": "1.0.0",
  "lastHeartbeat": "2026-01-22T10:30:00Z"
}
```

### 2. API Tracking
The API stores orchestrator heartbeats in memory and provides an endpoint to query online orchestrators:

```
GET /api/orchestrators?minutesThreshold=2
```

This returns all orchestrators that have sent a heartbeat within the last 2 minutes (default).

### 3. Frontend Display
The web UI automatically refreshes every 5 seconds and displays:
- Orchestrator hostname
- Customer assignment (or "ALL" if no customer specified)
- Version
- Last seen time (relative, e.g., "15s ago")
- Green pulsing indicator showing online status

## Testing

### Step 1: Start the API
```powershell
cd c:\git\github\sikksakk\iam-api
dotnet run
```

The API will be available at http://localhost:5000

### Step 2: Start One or More Orchestrators

**For customer "alpha":**
```powershell
cd c:\git\github\sikksakk\iam-orchestrator
$env:CustomerSettings__CustomerName="alpha"
dotnet run
```

**For customer "bravo" (in a new terminal):**
```powershell
cd c:\git\github\sikksakk\iam-orchestrator
$env:CustomerSettings__CustomerName="bravo"
dotnet run
```

**For all customers (in a new terminal):**
```powershell
cd c:\git\github\sikksakk\iam-orchestrator
$env:CustomerSettings__CustomerName=""
dotnet run
```

### Step 3: View Online Orchestrators
1. Open http://localhost:5000 in your browser
2. You should see the "Online Orchestrators" section at the top
3. Each running orchestrator will appear as a card with:
   - Green pulsing status indicator
   - Hostname
   - Customer name
   - Version
   - Last seen timestamp

### Step 4: Test Offline Detection
1. Stop one of the orchestrators (Ctrl+C)
2. Wait about 2 minutes
3. Refresh the browser - the stopped orchestrator should disappear from the list

## Configuration

### Orchestrator Configuration
Set the customer name in `appsettings.json`:

```json
{
  "CustomerSettings": {
    "CustomerName": "alpha"
  }
}
```

Or via environment variable:
```powershell
$env:CustomerSettings__CustomerName="alpha"
```

### API Configuration
The online threshold can be adjusted via query parameter:

```
GET /api/orchestrators?minutesThreshold=5
```

This will show orchestrators that checked in within the last 5 minutes.

## Multi-Tenant Deployment

In a production environment, you would typically run:
- One API instance
- Multiple orchestrator instances (one per customer)

Each orchestrator is uniquely identified by: `{hostname}-{8-char-guid}`

Example deployment:
```powershell
# Orchestrator for customer "alpha"
docker run -d --name iam-orchestrator-alpha \
  -e CustomerSettings__CustomerName=alpha \
  -e ApiSettings__BaseUrl=https://iam-api.azurewebsites.net \
  iam-orchestrator

# Orchestrator for customer "bravo"
docker run -d --name iam-orchestrator-bravo \
  -e CustomerSettings__CustomerName=bravo \
  -e ApiSettings__BaseUrl=https://iam-api.azurewebsites.net \
  iam-orchestrator
```

## API Endpoints

### Get Online Orchestrators
```
GET /api/orchestrators?minutesThreshold=2
```

**Response:**
```json
[
  {
    "id": "MYPC-abc12345",
    "customerName": "alpha",
    "hostName": "MYPC",
    "version": "1.0.0",
    "lastHeartbeat": "2026-01-22T10:30:15Z"
  },
  {
    "id": "SERVER01-def67890",
    "customerName": "bravo",
    "hostName": "SERVER01",
    "version": "1.0.0",
    "lastHeartbeat": "2026-01-22T10:30:18Z"
  }
]
```

### Send Heartbeat
```
POST /api/orchestrators/heartbeat
Content-Type: application/json

{
  "id": "HOSTNAME-abc123",
  "customerName": "alpha",
  "hostName": "HOSTNAME",
  "version": "1.0.0",
  "lastHeartbeat": "2026-01-22T10:30:00Z"
}
```

**Response:** `200 OK`

### Delete Orchestrator (Manual Cleanup)
```
DELETE /api/orchestrators/{id}
```

**Response:** `204 No Content`

## Frontend Display

The online orchestrators section appears at the top of the page, above the job creation form. It includes:

- **Header:** "Online Orchestrators" with a green pulsing indicator
- **Cards:** Each orchestrator is displayed in a card with:
  - Hostname with green status indicator
  - Customer name (or "ALL" if processing all customers)
  - Version number
  - Relative time since last heartbeat
- **Empty State:** "No orchestrators online" when no active orchestrators

The display auto-refreshes every 5 seconds to keep the status current.
