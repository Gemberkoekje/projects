# 14 – Security Considerations

## Goals
- No secrets in source control, container images, or logs.
- Principle of least privilege for the Kubernetes service account.
- Internal API protected against unauthorised access even inside the cluster.
- Agent and account tokens never exposed to the Razor Pages UI.

---

## 14.1 Secret Management

### Development
Use `dotnet user-secrets` (see `12-local-dev.md §4`). Secrets live in the OS user profile and
are **never** in the project directory.

### Production (Kubernetes)
Secrets are provided via Kubernetes `Secret` objects mounted as environment variables.
See `08-kubernetes.md §8.2` for the full `secret.yaml` template.

**Rules:**
- `k8s/secret.yaml` is listed in `.gitignore`.
- Never `kubectl apply` a Secret that contains plaintext values committed anywhere.
- Prefer an external secret operator (e.g. [External Secrets Operator](https://external-secrets.io/)
  pulling from Azure Key Vault or HashiCorp Vault) for teams with strict compliance requirements.

### Verification
```powershell
# Confirm no secrets have leaked into git history
git log --all -p | Select-String -Pattern "AccountToken|AgentToken|Password|ApiKey"
```

---

## 14.2 Internal API Key

The `X-Api-Key` header guard (`ApiKeyMiddleware`) protects all non-health endpoints.

- The key is a random string (minimum 32 characters) generated once and stored as a Kubernetes Secret.
- Rotate by updating the Secret and rolling the pod (no code change needed).
- Add rate limiting on the internal API itself to prevent brute-force key discovery:

```csharp
// Program.cs – limit API key brute-force attempts
app.UseRateLimiter(new RateLimiterOptions().AddFixedWindowLimiter("api-key-limit", opts =>
{
    opts.PermitLimit         = 20;
    opts.Window              = TimeSpan.FromMinutes(1);
    opts.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    opts.QueueLimit           = 0;
}));
```

Apply this limiter specifically to the `/control` and `/settings` endpoint groups.

---

## 14.3 Kubernetes RBAC

The `spacetraders-api` pod does not need to talk to the Kubernetes API. Use a restricted
`ServiceAccount`:

```yaml
# k8s/serviceaccount.yaml
apiVersion: v1
kind: ServiceAccount
metadata:
  name: spacetraders-sa
  namespace: spacetraders
automountServiceAccountToken: false   # no K8s API access needed
```

Reference in the Deployment spec:
```yaml
spec:
  serviceAccountName: spacetraders-sa
```

---

## 14.4 Network Policies

Restrict ingress/egress to only what is needed:

```yaml
# k8s/networkpolicy.yaml
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata:
  name: spacetraders-netpol
  namespace: spacetraders
spec:
  podSelector: {}
  policyTypes: [Ingress, Egress]
  ingress:
    - from:
        - namespaceSelector:
            matchLabels:
              kubernetes.io/metadata.name: spacetraders
  egress:
    # Allow DNS
    - ports: [{ port: 53, protocol: UDP }]
    # Allow HTTPS to SpaceTraders API
    - to:
        - ipBlock:
            cidr: 0.0.0.0/0
      ports: [{ port: 443, protocol: TCP }]
    # Allow PostgreSQL
    - to:
        - podSelector:
            matchLabels:
              app: spacetraders-pg
      ports: [{ port: 5432, protocol: TCP }]
```

---

## 14.5 Token Exposure Prevention

- The `AgentToken` is stored in the `stored_credentials` table and loaded into
  `IAgentTokenProvider` (singleton in memory). It is **never** serialised into API responses
  or ViewModels exposed to the Razor Pages UI.
- The `AccountToken` is consumed once at registration and never persisted beyond the EF Core
  transaction that writes the agent token. After bootstrap completes, `AccountToken` should
  be considered consumed and can be rotated at `my.spacetraders.io` if desired.
- Log statements must not include token values. Use a structured logging destructuring policy to
  redact any property named `*Token`, `*Secret`, or `*Password`:

```csharp
// Serilog – add to Program.cs when configuring Serilog
.Destructure.ByTransforming<SpaceTradersApiOptions>(o => new
{
    o.BaseUrl,
    AgentToken   = "***",
    AccountToken = "***",
})
```

---

## 14.6 Dependency Supply Chain

- Pin NuGet package versions in `.csproj` files (already good practice).
- Enable the NuGet vulnerability audit in the solution:
  ```xml
  <!-- Directory.Build.props -->
  <PropertyGroup>
    <NuGetAudit>true</NuGetAudit>
    <NuGetAuditLevel>moderate</NuGetAuditLevel>
  </PropertyGroup>
  ```
- Run `dotnet list package --vulnerable` in CI to catch newly reported CVEs.

---

## 14.7 Related Documents

- `08-kubernetes.md` – Secret and ConfigMap manifests
- `06-api.md §6.3` – `ApiKeyMiddleware` implementation
- `12-local-dev.md §4` – `dotnet user-secrets` setup
