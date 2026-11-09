# Deploying DatumPrikker

_To understand this, read [PLAN.md](PLAN.md) D13 and D16 first. The full runbook lives with the
manifests in gembernodes: `apps/datumprikker/README.md` (start there), plus
`apps/datumprikker/create-database.sql`, `ingress/datumprikker-ingress.yaml` and
`namespaces/datumprikker-namespace.yaml`._

DatumPrikker runs on the gembernodes k3s cluster at https://datumprikker.gemberkoekje.nl. It's one
pod (`Recreate`) on the public ingress, against the shared PostgreSQL. The image is
`ghcr.io/gemberkoekje/datumprikker:<sha>`, pushed by `.github/workflows/datumprikker-build-and-push.yml`
on every merge to main that touches `DatumPrikker/`.

## Your steps, in order

1. **Database and login** on the shared PostgreSQL: run `create-database.sql` in DBeaver.
2. **Sign-in apps:** a Google OAuth client (redirect URI `/signin-google`) and/or a Microsoft
   Entra app registration (redirect URI `/signin-microsoft`), both on
   `https://datumprikker.gemberkoekje.nl`. These are optional for the first deploy, but nobody can
   log in without one.
3. **The 1Password item** `datumprikker-secrets` (vault Gembercluster):
   - `ConnectionStrings__Postgres` (required);
   - `Authentication__Google__ClientId`/`ClientSecret` and
     `Authentication__Microsoft__ClientId`/`ClientSecret` (optional, both of a pair or neither).
4. **Merge the gembernodes PR** once its "Images exist" check is green, then:
   `flux reconcile kustomization flux-system --with-source`.
5. **Become admin:** log in on the site and open Nieuwe prikker, which shows your account id. Put
   that id in the item as `Admin__Accounts`, wait for the operator's sync (at most 10 minutes),
   then `kubectl -n datumprikker rollout restart deployment datumprikker`.

## Configuration in production

| Key | Required | What |
|---|---|---|
| `ConnectionStrings__Postgres` | yes | The app's own database on the shared PostgreSQL |
| `Admin__Accounts` | no | Admin account ids, comma-separated (D16); empty means nobody can approve |
| `Authentication__Google__ClientId` / `__ClientSecret` | no | Google sign-in, offered only when both are set (D5) |
| `Authentication__Microsoft__ClientId` / `__ClientSecret` | no | Microsoft sign-in, offered only when both are set (D5) |

The deployment sets `ASPNETCORE_ENVIRONMENT=Production`, which hides the dev login. It also sets
`ASPNETCORE_HTTP_PORTS=8080`. TLS ends at the ingress; the app reads `X-Forwarded-Proto`/`-For`,
so the OAuth redirect URIs say https and the answer rate limit sees each visitor's own address.

## Updating

Bump the image tag in gembernodes `apps/datumprikker/deployment.yaml` to a sha the workflow has
pushed, and merge. Marten updates the schema on start, and the key ring lives in the database, so
nobody is signed out.
