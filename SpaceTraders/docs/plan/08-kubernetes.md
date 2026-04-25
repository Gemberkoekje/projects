# 08 – Kubernetes Deployment

## Goals
- Single pod deployment to start; horizontal scaling optional later (requires switching SQLite → PostgreSQL).
- Secrets never in source control.
- Health probes aligned with ASP.NET Core health checks.
- Persistent storage for SQLite database.
- Operator can change settings via `kubectl` or the internal API without redeploying.

---

## 8.1 Container Images

Two images, one build context. **No PVC needed** – state is in PostgreSQL.

| Image | Project | Port |
|-------|---------|------|
| `spacetraders-api` | `SpaceTraders.API` | 8080 |
| `spacetraders-app` | `SpaceTraders.App` | 8081 |

A separate PostgreSQL instance (or managed service e.g. CloudNativePG, Azure Database for PostgreSQL) is used for persistence.

### Dockerfile (multi-stage, shared)
```dockerfile
# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
ARG PROJECT_PATH
RUN dotnet publish $PROJECT_PATH -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "SpaceTraders.API.dll"]
```

---

## 8.2 Kubernetes Resources

### Namespace
```yaml
# k8s/namespace.yaml
apiVersion: v1
kind: Namespace
metadata:
  name: spacetraders
```

### Secret (manual apply – never commit values)
```yaml
# k8s/secret.yaml  (template – fill values before applying)
apiVersion: v1
kind: Secret
metadata:
  name: spacetraders-secrets
  namespace: spacetraders
type: Opaque
stringData:
  ConnectionStrings__DefaultConnection: "Host=postgres-svc;Database=spacetraders;Username=st;Password=..."
  SpaceTraders__AccountToken: "<your-account-token>"
  SpaceTraders__AgentName:    "<desired-agent-callsign>"
  SpaceTraders__AgentFaction: "COSMIC"
  SPACETRADERS_INTERNAL_API_KEY: "<random-api-key>"
```

> The agent token is **not** in the Secret – it is bootstrapped at runtime and stored in the PostgreSQL `stored_credentials` table.

### ConfigMap (non-secret configuration)
```yaml
# k8s/configmap.yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: spacetraders-config
  namespace: spacetraders
data:
  ASPNETCORE_ENVIRONMENT: "Production"
  SpaceTradersApi__BaseUrl: "https://api.spacetraders.io/v2/"
  Logging__LogLevel__Default: "Information"
```

### PersistentVolumeClaim
No PVC required. All state is in PostgreSQL.

### PostgreSQL (CloudNativePG recommended)
```yaml
# k8s/postgres.yaml – using CloudNativePG operator
apiVersion: postgresql.cnpg.io/v1
kind: Cluster
metadata:
  name: spacetraders-pg
  namespace: spacetraders
spec:
  instances: 1
  storage:
    size: 5Gi
  bootstrap:
    initdb:
      database: spacetraders
      owner: st
      secret:
        name: spacetraders-pg-credentials
```
Alternatively, use a managed service (Azure Database for PostgreSQL Flexible Server, etc.) and put the connection string in the Secret.

### Deployment – API
```yaml
# k8s/deployment-api.yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: spacetraders-api
  namespace: spacetraders
spec:
  replicas: 1           # See §8.4 for horizontal scaling with PostgreSQL
  selector:
    matchLabels:
      app: spacetraders-api
  template:
    metadata:
      labels:
        app: spacetraders-api
    spec:
      containers:
        - name: api
          image: spacetraders-api:latest
          ports:
            - containerPort: 8080
          envFrom:
            - configMapRef:
                name: spacetraders-config
            - secretRef:
                name: spacetraders-secrets
          volumeMounts: []          # no volume mounts needed – state is in PostgreSQL
          livenessProbe:
            httpGet:
              path: /health/live
              port: 8080
            initialDelaySeconds: 10
            periodSeconds: 15
          readinessProbe:
            httpGet:
              path: /health/ready
              port: 8080
            initialDelaySeconds: 5
            periodSeconds: 10
          startupProbe:
            httpGet:
              path: /health/startup
              port: 8080
            failureThreshold: 30
            periodSeconds: 5
          resources:
            requests:
              cpu: "100m"
              memory: "128Mi"
            limits:
              cpu: "500m"
              memory: "512Mi"
      volumes: []   # no volumes needed
```

### Deployment – App (Dashboard)
```yaml
# k8s/deployment-app.yaml
# Same structure, image: spacetraders-app, port 8081
# No secrets needed – reads from API or shared DB
```

### Services
```yaml
# k8s/service-api.yaml
apiVersion: v1
kind: Service
metadata:
  name: spacetraders-api
  namespace: spacetraders
spec:
  selector:
    app: spacetraders-api
  ports:
    - port: 80
      targetPort: 8080
  type: ClusterIP
---
# k8s/service-app.yaml
# type: LoadBalancer or ClusterIP + Ingress
```

### Ingress (optional, for dashboard access)
```yaml
# k8s/ingress.yaml
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: spacetraders-ingress
  namespace: spacetraders
  annotations:
    nginx.ingress.kubernetes.io/rewrite-target: /
spec:
  rules:
    - host: spacetraders.internal
      http:
        paths:
          - path: /api
            pathType: Prefix
            backend:
              service:
                name: spacetraders-api
                port:
                  number: 80
          - path: /
            pathType: Prefix
            backend:
              service:
                name: spacetraders-app
                port:
                  number: 80
```

---

## 8.3 Apply Order

```powershell
kubectl apply -f k8s/namespace.yaml
kubectl apply -f k8s/secret.yaml          # fill in real values first
kubectl apply -f k8s/configmap.yaml
kubectl apply -f k8s/postgres.yaml        # or point connection string at managed service
kubectl apply -f k8s/deployment-api.yaml
kubectl apply -f k8s/deployment-app.yaml
kubectl apply -f k8s/service-api.yaml
kubectl apply -f k8s/service-app.yaml
kubectl apply -f k8s/ingress.yaml         # if using ingress
```

---

## 8.4 Horizontal Scaling

Because state is in PostgreSQL (not a local file), scaling is straightforward:
1. Set `replicas: N` on the API deployment.
2. Wolverine's durable outbox uses the same PostgreSQL DB – no extra coordination needed.
3. Add a `leader-election` annotation or use Wolverine's built-in leader election to ensure only one pod runs the `GameLoopService` at a time (avoid duplicate ship commands).

---

## 8.5 Folder Structure

```
k8s/
├── namespace.yaml
├── secret.yaml          ← template, .gitignored
├── configmap.yaml
├── postgres.yaml        ← CloudNativePG cluster (or skip if using managed service)
├── deployment-api.yaml
├── deployment-app.yaml
├── service-api.yaml
├── service-app.yaml
└── ingress.yaml
```

Add `k8s/secret.yaml` to `.gitignore`:

```
# .gitignore
k8s/secret.yaml
```
