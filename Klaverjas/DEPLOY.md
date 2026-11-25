# Deploying Klaverjas

_To understand this, read [PLAN.md](PLAN.md) D2, D5 and D7 first. The full runbook lives with the
manifests in gembernodes: `apps/klaverjas/README.md` (start there), plus
`apps/klaverjas/create-database.sql` and `ingress/klaverjas-route.yaml`._

Klaverjas runs on the gembernodes k3s cluster at https://samenspelers.gemberkoekje.nl, the club's
name (Q2, 2026-11-25). Its first address, https://klaverjas.gemberkoekje.nl, where it replaced the
design mock, redirects there with the path kept, so the first slips still work. It's one pod (the old one stops before a new one
starts) on the public Gateway, against the shared PostgreSQL. The image is `ghcr.io/gemberkoekje/klaverjas:<sha>`,
pushed by `.github/workflows/klaverjas-build-and-push.yml` on every merge to main that touches
`Klaverjas/`. `gemberkoekje.nl/klaverjas` redirects there too: the app has no path base.

## Your steps, in order

1. **Database and login** on the shared PostgreSQL: run `create-database.sql` in DBeaver.
2. **The 1Password item** `klaverjas-secrets` (vault Gembercluster):
   - `ConnectionStrings__Postgres` (required);
   - `Auth__Password` (required): Dad's first password for `/beheer`;
   - `Mail__User` (optional): the Gmail address the mail goes from. Its app password is the existing
     `mail-secret` item, which this app reads too.
3. **Merge the gembernodes PR** once its "Images exist" check is green, then:
   `flux reconcile kustomization flux-system --with-source`.
4. **The live check:** sign in at `/beheer`; in Instellingen set the players' password and Dad's
   address under "Antwoorden gaan naar"; put a week online; press "Proefmail naar mij" and check
   that it arrives with the PDF.
5. **Once Dad has chosen his own password** (Instellingen → "Uw wachtwoord"), change `Auth__Password`
   to a long random one only you keep: it keeps opening `/beheer` as the way back in. After the
   operator's sync (at most 10 minutes): `kubectl -n klaverjas rollout restart deployment klaverjas`.

## Configuration in production

| Key | Required | What |
|---|---|---|
| `ConnectionStrings__Postgres` | yes | The app's own database on the shared PostgreSQL |
| `Auth__Password` | yes | Opens `/beheer`: Dad's until he chooses his own, then the site keeper's (D7) |
| `Mail__User` | no | The Gmail account the standings are mailed from; also the sender address |
| `Mail__Password` | no | That account's app password, from the shared `mail-secret` item (`SMTP_PASSWORD`) |

`Mail:Host` (`smtp.gmail.com`), `Mail:Port` (587) and `Mail:StartTls` (on) are the defaults in
`appsettings.json`. Without `Mail__User` and `Mail__Password` mailing is off and the screens say so;
everything else works. The players' password and Dad's own password are not configuration: Dad
sets them in Instellingen (D7).

The deployment sets `ASPNETCORE_ENVIRONMENT=Production` and `ASPNETCORE_HTTP_PORTS=8080`. TLS ends at
the Gateway; the app reads `X-Forwarded-Proto`/`-For`, so the links in the mail and on the slips say
https with the real hostname, and the sign-in rate limits see each visitor's own address.

## Updating

Bump the image tag in gembernodes `apps/klaverjas/deployment.yaml` to a sha the workflow has pushed,
and merge. Marten updates the schema on start, and the key ring lives in the database, so nobody is
signed out (players stay signed in for a year).
