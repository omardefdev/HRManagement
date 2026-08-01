# INT-004 — Live Government Connectors

## Purpose

Provide a mode-switched connector layer that wraps the existing EXT-001..007 evaluators
and file adapters, allowing each government integration point to operate in one of four
modes without code forks.

## Integration Modes

| Mode | Value | Description |
|------|-------|-------------|
| `File` | 0 | Uses file-based adapters from `HRManagement.Integration.FileAdapters` |
| `Stub` | 1 | Returns canned responses for local development/testing |
| `Sandbox` | 2 | Connects to sandbox endpoints with tagged responses |
| `Live` | 3 | Connects to real national endpoints (requires EXT certification) |

## Production Guard

`GovernmentConnectorStartupValidator` runs as an `IHostedService` at startup and enforces:

- **Required connectors** (`treasury`, `civil-registry`) must not use `Stub` or `Sandbox`
  mode when `ASPNETCORE_ENVIRONMENT=Production`.
- **Live+!Sandbox** for Treasury and Civil Registry is blocked until the corresponding
  EXT card national endpoints are certified.
- **Missing registrations** for any built-in connector descriptor cause a startup failure.

When `EnforceOnStartup=true` (default), violations throw `InvalidOperationException`.

## Connectors

### TreasuryLiveConnector (EXT-003)

- Implements `IGovernmentConnector` and `IPayrollDisbursementGateway`.
- In `Live` mode with `SandboxEnabled=true` or in `Sandbox` mode, returns sandbox acks.
- In `Live` mode with `SandboxEnabled=false`, throws `NotSupportedException` until
  EXT-003a–e treasury workers are certified.

### CivilRegistryLiveConnector (EXT-002)

- Implements `IGovernmentConnector` and `INationalIdentityVerifier`.
- In `Live` mode with `SandboxEnabled=true` or in `Sandbox` mode, returns sandbox verification.
- In `Live` mode with `SandboxEnabled=false`, throws `NotSupportedException` until
  EXT-002a–d civil registry workers are certified.

### FederationConnectorDescriptor (EXT-001)

- Implements `IGovernmentConnector` as a descriptor only (no live implementation in-repo).
- `RequiredInProduction=false` — federation IdP endpoints are external to this system.

## Configuration

```json
{
  "GovernmentConnectors": {
    "EnforceOnStartup": true,
    "Treasury": {
      "ConnectorId": "treasury",
      "Mode": "File",
      "Sandbox": true,
      "RequiredInProduction": true
    },
    "CivilRegistry": {
      "ConnectorId": "civil-registry",
      "Mode": "File",
      "Sandbox": true,
      "RequiredInProduction": true
    },
    "Federation": {
      "ConnectorId": "federation",
      "Mode": "File",
      "Sandbox": true,
      "RequiredInProduction": false
    }
  }
}
```

## DI Registration

```csharp
builder.AddGovernmentConnectors();
```

Registers all connectors, the `LiveConnectorRegistry`, the startup validator, and
conditionally wires `IPayrollDisbursementGateway` / `INationalIdentityVerifier` when
the respective connector is in `Live` or `Sandbox` mode.

## Key Types

| Type | Location |
|------|----------|
| `IntegrationConnectorMode` | `ServiceDefaults/Connectors/IntegrationConnectorMode.cs` |
| `IGovernmentConnector` | `ServiceDefaults/Connectors/IGovernmentConnector.cs` |
| `GovernmentConnectorOptions` | `ServiceDefaults/Connectors/GovernmentConnectorOptions.cs` |
| `ConnectorBindingOptions` | `ServiceDefaults/Connectors/GovernmentConnectorOptions.cs` |
| `LiveConnectorRegistry` | `ServiceDefaults/Connectors/LiveConnectorRegistry.cs` |
| `GovernmentConnectorDescriptor` | `ServiceDefaults/Connectors/LiveConnectorRegistry.cs` |
| `GovernmentConnectorStartupValidator` | `ServiceDefaults/Connectors/GovernmentConnectorStartupValidator.cs` |
| `TreasuryLiveConnector` | `ServiceDefaults/Connectors/TreasuryLiveConnector.cs` |
| `CivilRegistryLiveConnector` | `ServiceDefaults/Connectors/CivilRegistryLiveConnector.cs` |
| `FederationConnectorDescriptor` | `ServiceDefaults/Connectors/FederationConnectorDescriptor.cs` |
| `GovernmentConnectorExtensions` | `ServiceDefaults/Connectors/GovernmentConnectorExtensions.cs` |

## Dependencies

- `EXT-001..007` evaluator/provider stubs under `ServiceDefaults/Ext*`
- `HRManagement.Integration.Abstractions` (`IPayrollDisbursementGateway`, `INationalIdentityVerifier`)
- `HRManagement.Integration.FileAdapters` (file-mode fallback)

## Validation

- **Unit tests:** `Int004ConnectorTests` — mode switching, production guard rejects Stub/Sandbox
- **Script:** `scripts/int004-validate.ps1` — verifies connector registration and config structure
