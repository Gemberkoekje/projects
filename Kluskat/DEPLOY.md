# Deploying Kluskat on gembernodes

Kluskat runs as one pod in namespace `kluskat`, against the cluster's shared PostgreSQL, at
**https://kluskat.gemberkoekje.nl** (the `*.gemberkoekje.nl` wildcard already points at the
cluster). Manifests live in the gembernodes repo under `apps/kluskat/`.

## One-time setup

### 1. Database and login

```bash
psql -h 192.168.1.232 -U postgres -d postgres
```

```sql
-- The app's login: not a superuser, can't create databases or roles (the defaults).
CREATE ROLE kluskat LOGIN;
\password kluskat
CREATE DATABASE kluskat OWNER kluskat;
-- Every login may connect to a new database; only kluskat may connect to this one.
REVOKE CONNECT, TEMPORARY ON DATABASE kluskat FROM PUBLIC;
```

Make the password with, for example, `openssl rand -hex 24` (hex keeps the connection string
valid). Marten creates its tables at the first start; as the database owner it may.

### 2. The 1Password item

Item **`kluskat-secrets`** in the **Gembercluster** vault, with exactly these two field labels:

- `ConnectionStrings__Postgres`:
  `Host=postgresql.flux-system.svc.cluster.local;Port=5432;Database=kluskat;Username=kluskat;Password=<from step 1>`
- `Auth__Password`: the password you type on your phone, once.

Other fields on the item are ignored. A missing one keeps the pod in `CreateContainerConfigError`.

## Every deploy

1. Push `projects` main. The **Build and Push Kluskat** workflow tests, publishes and pushes
   `ghcr.io/gemberkoekje/kluskat:<commit sha>` (and `:latest`).
2. Bump the image tag in gembernodes `apps/kluskat/deployment.yaml` to that sha and merge.
   Flux rolls it out within a few minutes.

```bash
kubectl -n kluskat get pods                       # Running, 1/1
kubectl -n kluskat logs deploy/kluskat | grep -m1 Startvulling
```

The first start seeds the 65 klussen (staggered); later starts leave the list alone.

## On the phone

Open https://kluskat.gemberkoekje.nl, sign in, then **Add to Home Screen** (Safari: Share → Add to
Home Screen; Chrome: ⋮ → Install app). It opens full-screen with the cat icon, and the sign-in
lasts a year.
