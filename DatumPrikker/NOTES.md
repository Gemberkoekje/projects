# DatumPrikker: notes per slice

_To understand this, start with [PLAN.md](PLAN.md) (slices and decisions), then read
`DatumPrikker.core/Models/Poll.cs`, `Answer.cs` and `DatumPrikker.WebApp/Program.cs`._

## Slice 1: restart, the poll flow in one app (2026-11-09)

### What was done

- **The v1 code is gone** (Aspire AppHost/ApiService/Web/ServiceDefaults/Tests, EF Core, Redis).
  It is still in git history, last at `9b5372a8`. The new solution copies Kluskat's layout, build
  props, analyzer config, Dockerfile and compose file (D1, D2).
- **Domain** (`core`): `Poll` (+ `DateOption`), `Answer` (+ `OptionAnswer`), `PollResults`
  (+ `OptionTally`), `Account`, `Email`, `ShareToken`. All the rules live there as pure functions
  that return `Result`s: `Poll.Create`, `Poll.Close`, `Answer.Give` and `PollResults.Tally`.
- **Application:** `CreatePoll`, `GiveAnswer` and `ClosePoll`, plus `GetPoll` and `GetResults`,
  each with a handler. The ports are `IPollStore` and `IAnswerStore`.
- **Infrastructure:** `MartenPollStore`, `MartenAnswerStore`, and `MartenXmlRepository` (the key
  ring in Postgres, copied from Kluskat).
- **Web:** Razor Pages.
  - `/` is the landing page.
  - `/Aanmelden` shows the configured providers, plus the dev link in Development.
  - `/Dev/Aanmelden` is the dev login, and a 404 outside Development.
  - `/Afmelden` signs out.
  - `/Nieuw` makes a poll, with JS to add and remove date rows.
  - `/p/{token}` is where people answer.
  - `/p/{token}/uitslag` is for the owner: the share link with a copy button, the best dates, a
    who-said-what table, and a close button.
  - `/Fout` covers 404, 429 and exceptions.
- **CI:** `.github/workflows/datumprikker-build-and-push.yml` builds and tests PRs, and pushes the
  image to GHCR on main. It was planned for the cluster slice (now 4) but is cheap now.
- **Tests:** 51, all over in-memory stores. They cover the domain rules, last write wins (keeping
  `CreatedOn`), owner-only results and closing, malformed tokens, which providers get offered,
  account claims, parsing date rows, and a System.Text.Json round trip of the documents.

### Checked by hand (local Postgres, browser)

Every step below went as expected:

1. Signed in with the dev login (no provider configured, so the login page shows only the dev link).
2. Made a poll, after a deliberately half-filled date row was refused with "Datum 4: kies een dag."
3. Signed out and answered anonymously as Carla, then as Dirk.
4. Answered as Carla again with `CARLA@example.com`. The log says "changed", and the results show
   her second answer.
5. Signed in as someone else: the results page says owner-only, with a 403.
6. Signed in as the owner: the results show the correct tally and the best dates (a tie of two).
7. Closed the poll: the answer page says it's closed.
8. An unknown token gets the friendly 404.
9. After an app restart you stay signed in, because the key ring is in Postgres.

### Gotchas

- **A hand-written `<form action="/Afmelden">` gets no antiforgery token.** The form tag helper
  only adds one when the action comes from `asp-page`, `asp-action` and the like. Sign-out answered
  with an empty 400. Fixed with `asp-page="/Afmelden"`.
- **Status-code re-execution keeps the method.** A failed POST (an antiforgery 400, a rate-limit
  429) re-runs `/Fout` as a POST, so `/Fout` needs an `OnPost` and `[IgnoreAntiforgeryToken]`
  (suppressed S4502 with a justification). Without them the error page itself returned an empty 400.
- **NUnit shares one fixture instance across its tests.** Fields such as the in-memory stores leak
  between tests unless you set `[assembly: FixtureLifeCycle(LifeCycle.InstancePerTestCase)]` (in
  `AssemblyInfo.cs`).
- **The analyzers here are strict:**
  - StyleCop 1.1.118 rejects `new()` (SA1000) and `required` (SA1206).
  - Qowaiv wants `[Mutable("…")]` on form-binding classes (QW0011/QW0012), and leading zeros in date
    literals (QW0021).
  - Positional records trip QW0016.
- **Empty form fields bind as `null`** even on non-nullable strings. The input classes use
  `[DisplayFormat(ConvertEmptyStringToNull = false)]`.
- Marten LINQ is only ever asked about plain strings and Guids (the email is stored normalised as a
  string), so no Qowaiv value type needs a Marten registration.

### Left for later slices

- Slice 2: "Mijn afspraken" and claimed answers (D4). The answer page's nudge is a single line for
  now. Returning respondents don't see their earlier answer yet: an anonymous form starts empty.
- Slice 3: admin and access (only approved people make polls).
- Slice 4: the cluster. The image already builds, and `/healthz` exists for the probes.
- Slice 5: the look. The CSS is deliberately plain.

### File map

- `DatumPrikker.core/Models/`: `Poll`, `PollDraft`, `DateOption`, `Answer`, `AnswerDraft`,
  `OptionAnswer`, `PollResults`, `OptionTally`, `Account`, `Email`, `ShareToken`.
- `DatumPrikker.core/Enums/Availability.cs`.
- `DatumPrikker.application/{Commands,Queries,Ports}/`: one record and one handler per file.
- `DatumPrikker.infrastructure/Adapters/`: the three Marten adapters.
- `DatumPrikker.WebApp/Program.cs`: the composition root, auth, rate limit and Marten schema.
- `DatumPrikker.WebApp/Auth/`: `LoginProviders` (D5) and `AccountClaims` (D6).
- `DatumPrikker.WebApp/Pages/`: the pages listed above. `Weergave.cs` holds the Dutch formatting.
- `DatumPrikker.test/`: specs per layer, plus `TestHelpers/` (in-memory stores, fixtures).

## Slice 2: accounts (2026-11-09)

### What was done

- **Claimed answers (D4).** `Answer.IsClaimed` (given while logged in) and
  `Answer.ReplaceableBy(account)`. `GiveAnswerHandler` refuses an anonymous replacement of a claimed
  answer with `Answer.ClaimedMessage`.
  - Logging in and answering again claims an anonymous answer.
  - Another account with the same email may replace a claimed answer: logged in, you can only answer
    as your own address.
- **Mijn afspraken** at `/Afspraken`, and on the home page when logged in. `ListAppointmentsHandler`
  merges the polls you made (by account id) with the polls answered under your email (D6).
  - The list puts open polls first, then the latest activity (made, or your last answer).
  - Each poll shows its state and date span.
  - The owner also sees the answer count and a results link.
  - A respondent sees their own answer summarised ("1× ja, 1× misschien"), with a link to change it.
- **Your earlier answer fills the form** on the answer page, with "Je antwoordde al op …" and an
  "Antwoord bijwerken" button.
  - Logged in, it's found by the account's email.
  - Anonymous, it's found by `RespondentMemory` (D19), a protected cookie with the name and email
    this browser last answered with. "Niet jij? Begin met een leeg formulier" forgets it.
- **Nudges, never walls:**
  - the answer page says "Log in, dan vullen we je gegevens in … Hoeft niet: antwoorden kan ook zo";
  - after an anonymous answer: "Bewaar deze afspraak in je overzicht: log in, met hetzelfde
    e-mailadres";
  - after a logged-in answer: a link to Mijn afspraken.
- **The header** shows "Mijn afspraken" and "Uitloggen" when logged in. "Nieuwe prikker" moved onto
  the home and afspraken pages, which keeps the header to two buttons on a phone.
- **Ports:** `IPollStore.ListOwnedByAsync` and `LoadManyAsync`; `IAnswerStore.ListByEmailAsync` and
  `CountForPollsAsync`. Marten serves these from the existing indexes on `OwnerAccountId`, `PollId`
  and `Email`.
- **Tests:** 69 (18 new). They cover the claim rule in the domain and the handler, merging,
  ordering and counting afspraken, finding an earlier answer by any spelling of the email, the
  cookie (round trip, unreadable, forged, other key ring), and the Dutch date span, summary and
  local time.

### Checked by hand (local Postgres, browser)

Every step below went as expected:

1. Arie made a poll.
2. Carla answered anonymously. The thank-you offered "log in" to save the poll in her overview.
3. On her next visit the page said "Je antwoordde al op 9 november om 21:05", with her choices and
   comment filled in.
4. "Niet jij?" emptied the form.
5. Bea answered logged in: her name was filled in and editable (she changed it), and her email was
   locked.
6. An anonymous answer with `bea@example.com` was refused with the claimed message, and Bea's answer
   stayed.
7. Carla logged in with the same email: her home page listed both polls she had answered
   anonymously, open first.
8. Arie's Mijn afspraken showed both his polls with their answer counts.
9. The results showed Bea's edited name.

### Gotchas

- **A line in a Razor code block that starts with `<br />` ends the markup right there.** The rest
  of the line is parsed as C#, which gave confusing CS0246 errors on Dutch words. Wrap the line in
  one element (`<span class="blok">`).
- **Sonar S2092 wants `Secure` on every cookie.** It is now always on. Browsers accept Secure
  cookies on `http://localhost`, so local runs still work, as checked above.
- `Europe/Amsterdam` resolves on Windows too (.NET uses ICU for IANA ids), so `Weergave.Moment`
  needs no Windows id fallback.

### Left for later slices

- Slice 3: admin and access (D14–D18).
- Slice 4: the cluster.
- Slice 5: the look. The afspraken list and the nudges are plain.

### File map (new or changed)

- `DatumPrikker.core/Models/Answer.cs`: `IsClaimed`, `ReplaceableBy`, `ClaimedMessage`.
- `DatumPrikker.application/ReadModels/Appointment.cs`.
- `DatumPrikker.application/Queries/`: `ListAppointments(+Handler)` and `GetAnswer(+Handler)`.
- `DatumPrikker.WebApp/Auth/RespondentMemory.cs` (D19).
- `DatumPrikker.WebApp/Pages/`: `Afspraken`, `Shared/_Afspraken`, a reworked `Index` and `P/Prikken`.
- `DatumPrikker.WebApp/Weergave.cs`: `Bereik`, `Moment`, `Samenvatting`.

## Slice 3: admin and access (2026-11-09)

### What was done

- **Domain:**
  - `Access`, one per account, id = account id. `Access.Request` asks; a pre-approved request comes
    out approved at once. `Access.Decide` follows the D14 transitions: approve from any state,
    decline only a waiting request, revoke only an approval.
  - `PreApproval`, one per normalised email (D15).
  - `Admins`, parsed from `Admin:Accounts` (D16), with `Includes` and `Require`.
  - The enums `AccessState` and `AccessDecision`.
- **Application:**
  - **Commands:** `RequestAccess`, `DecideAccess`, and `PreApprove`/`RemovePreApproval`, both handled
    by `PreApprovalHandler`.
    - Asking twice keeps one request. After a decline or a revoke, asking again is refused.
    - Every admin-only handler checks `Admins` itself, whatever the page shows.
  - **Queries:** `GetAccess`, `ListAccess`, `ListAllPolls` and `CountPendingRequests` (which answers
    0 for non-admins without asking the store).
  - **The gate** is in `CreatePollHandler`: a signed-in account that's neither admin nor approved
    gets `NoAccess`. An anonymous maker still gets Poll.Create's own "Log in…" message.
  - `GetResultsHandler` lets admins in (D18).
- **Infrastructure:** `MartenAccessStore` (index on `State`) and `MartenPreApprovalStore`;
  `MartenPollStore.ListAllAsync`.
- **Web:**
  - **Nieuwe prikker** shows the form only when you're allowed to make polls. Otherwise it shows a
    "Toegang vragen" form with the "Wie ben je?" note, "Je aanvraag is verstuurd" while you wait,
    or "Je kunt (nu) geen prikkers maken" after a decline or revoke. Each of those also shows
    "Je account-id: …".
  - **`/Beheer`** sits behind the `Beheerder` policy. It has requests (Toestaan/Afwijzen), access
    (Intrekken, and "Toch toestaan" for refused accounts), pre-approved addresses (add/remove), and
    every poll with a link to its results.
  - **The header** shows "Beheer (n)" to admins.
  - **The results page,** for an admin looking at someone else's poll, says "Je bekijkt deze
    prikker van … als beheerder" and has no close button.
  - **A denied page** (403) now shows "Geen toegang".
- **Config:** `Admin:Accounts` is empty in `appsettings.json` and `dev:arie@example.com` in
  `appsettings.Development.json`. Compose and `.env.example` gained `ADMIN_ACCOUNTS`.
- **Tests:** 98 (29 new):
  - `Access_specs`: the D14 transitions, both allowed and refused, plus parsing the admin list;
  - `AccessFlow_specs`: asking twice, pre-approval, no asking after a decline, admin-only commands
    and lists, the gate through every state, revoking keeping existing polls, the badge count and
    the status;
  - admins opening results.

### Checked by hand (local Postgres, browser)

Every step below went as expected:

1. Bea (not an admin) opened Nieuwe prikker: the request form and her account id, with no poll
   form.
2. A crafted POST of the poll form, sent straight to `/Nieuw` with a valid antiforgery token, was
   refused with "geen toestemming".
3. She asked, with a note: "Je aanvraag is verstuurd".
4. She opened `/Beheer`: "Geen toegang".
5. Arie (admin in Development) saw "Beheer (1)" and Bea's request with its note. He approved her,
   and the badge went away. He pre-approved `Dirk@Example.com`, which was stored as
   `dirk@example.com`.
6. Dirk asked and got the poll form straight away.
7. Bea made a poll. Arie revoked her; she shows under "Afgewezen of ingetrokken".
8. Bea's poll still took an anonymous answer.
9. Bea's Nieuwe prikker said "Je kunt (nu) geen prikkers maken", with no ask button.
10. Arie opened Bea's results from the poll list: the admin note, and no close button.
11. 404 pages still work.

### Gotchas

- **`CookieAuthenticationOptions.AccessDeniedPath` is a `PathString`.** `"/Fout?code=403"` became
  `/Fout%3Fcode=403` and a 404. Fixed with a route value: `@page "{code:int?}"` on Fout, and
  `AccessDeniedPath = "/Fout/403"`. Status-code re-execution still passes `?code=`.
- **A Razor local function that contains a tag helper** (`<form asp-page-handler>`) must be
  `async Task` and called with `await` (MVC1006).
- The layout injects `Admins` and `CountPendingRequestsHandler`. Only admins cost a query per page.

### Left for later slices

- Slice 4: the cluster. The 1Password item needs `Admin__Accounts` with your real account id. Log
  in once on the live site, open Nieuwe prikker, and copy the id shown there.
- Slice 5: the look. The admin page is plain.

### File map (new or changed)

- `DatumPrikker.core/Models/`: `Access`, `PreApproval`, `Admins`. `Enums/`: `AccessState`, `AccessDecision`.
- `DatumPrikker.application/Ports/`: `IAccessStore`, `IPreApprovalStore`, and `IPollStore.ListAllAsync`.
- `DatumPrikker.application/Commands/`: `RequestAccess`, `DecideAccess`, `PreApprove`, `RemovePreApproval`,
  their handlers, and the gate in `CreatePollHandler`.
- `DatumPrikker.application/Queries/`: `GetAccess`, `ListAccess`, `ListAllPolls`, `CountPendingRequests`.
- `DatumPrikker.application/ReadModels/`: `AccessStatus`, `AccessOverview`, `PollOverview`.
- `DatumPrikker.infrastructure/Adapters/`: `MartenAccessStore`, `MartenPreApprovalStore`.
- `DatumPrikker.WebApp/`: `Pages/Beheer`, a reworked `Pages/Nieuw`, `Auth/Policies.cs`, the layout badge,
  the Uitslag admin note, and `Fout` with 403.

## Slice 4: on gembernodes (2026-11-09)

### What was done

- **gembernodes PR** (branch `claude/datumprikker-deploy`), a copy of Kluskat's and
  CharacterCreator's setup:
  - `namespaces/datumprikker-namespace.yaml` (pod security `baseline`);
  - `ingress/datumprikker-ingress.yaml` (`nginx-public`, letsencrypt-prod,
    `datumprikker.gemberkoekje.nl`);
  - `apps/datumprikker/`: `deployment.yaml`, `service.yaml`, `datumprikker-secrets.yaml`
    (OnePasswordItem), `kustomization.yaml`, `create-database.sql` and `README.md` (the runbook);
  - the three kustomization entries.
- **The deployment** runs one pod (`Recreate`), non-root with all capabilities dropped, probed on
  `/healthz`, at 25m/128Mi requests and 500m/384Mi limits. The image is
  `ghcr.io/gemberkoekje/datumprikker:a14f0cac…` (the merge of #242).
  - Only `ConnectionStrings__Postgres` is a required secret key.
  - `Admin__Accounts` and the four OAuth keys are `optional: true`. The pod starts without them,
    which the admin bootstrap needs: your account id only exists after a first login.
- **This repo:** `DEPLOY.md` (your steps, the production configuration, updating), and the plan's
  status row. No code changed: forwarded headers, the key ring in Postgres and `/healthz` were
  already there from slice 1.

### Checked

- `kubectl kustomize` renders `apps/datumprikker`, `apps`, `ingress` and `namespaces`.
- `scripts/validate.py` needs `kubeconform`, which isn't installed here, so gembernodes CI runs it
  on the PR.
- `datumprikker.gemberkoekje.nl` already resolves to the public ingress's address (the same as
  kluskat's), so no DNS record is needed.
- Kluskat calls `FindSystemTimeZoneById("Europe/Amsterdam")` on the same aspnet base image in
  production, so `Weergave.Moment` will find the zone in the container.
- **Not checked:** the app on the cluster itself, because that waits on your steps (database,
  1Password item, OAuth apps).

### Gotchas

- **The 1Password operator syncs every 10 minutes and doesn't restart pods.** After changing a
  field (adding `Admin__Accounts`, a provider, a renewed Microsoft secret), wait for
  `Updating kubernetes secret 'datumprikker-secrets'` in its log, then run
  `kubectl -n datumprikker rollout restart deployment datumprikker`.
- **The first push makes a new GHCR package,** private and linked to this repo. gembernodes'
  "Images exist" check then only warns that it can't see the image. The cluster pulls with the
  `ghcr-login` secret, which can.
- **A Microsoft client secret expires** (24 months at most); Microsoft sign-in stops when it does.
- **A new text field in 1Password gets the label `text`.** On the first deploy the connection string
  synced as key `text`, and the pod sat in `CreateContainerConfigError`
  (`couldn't find key ConnectionStrings__Postgres`) until the field was renamed. Check the label
  with
  `kubectl -n datumprikker get secret datumprikker-secrets -o go-template='{{range $k, $v := .data}}{{$k}}{{"\n"}}{{end}}'`,
  which lists the key names only.
- **PowerShell has no `$(date +%s)`.** For the refresh annotation use
  `"datumprikker/refresh=$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())"`.

### Live (2026-11-09)

Live since about 20:50Z. All of these checked out:
- `/healthz` answers;
- http redirects to https;
- the certificate is Ready;
- both providers are offered, and the dev login is a 404;
- the OAuth `redirect_uri`s are https (forwarded headers work);
- the pod sees one Google admin id.

### Left for later slices

- Slice 5: the look.

## Slice 5: the look (2026-11-09)

### What was done

- **The design** (`wwwroot/css/site.css`, rewritten):
  - **Base:** cream calendar paper with a dot grid, chunky ink outlines with hard offset shadows,
    and rounded cards.
  - **Colours:** violet as the main colour, with coral and sun as accents. Green, amber and coral
    stand for Ja, Misschien and Nee.
  - **Type:** a rounded system font stack, with no web font, so no third-party requests.
  - **Themes:** light and deep indigo, following the system setting.
  - **Mobile first:** wide-screen rules sit at the bottom.
- **The header:** a calendar-and-pin logo (inline SVG, also `wwwroot/favicon.svg`), theme-colour
  meta tags, and the admin badge as a coral counter bubble.
- **Answer page:** one row per date (D20).
  - Each row has a mini calendar (month/day) with the weekday and time, and Ja/Misschien/Nee beside
    it.
  - The row turns the colour of the choice (`:has()`).
  - On a phone the buttons drop below the date and share the full width.
  - The owner shows as a tilted sun sticker.
- **Results:**
  - a "De winnaar" card in sun yellow with a tilted date ticket;
  - "Zo staat het ervoor", a ranking with one row per date, best on top and least favourite at the
    bottom (as the user asked). Each row has its rank (★ for a shared first place), the date, a
    horizontal ja/misschien/nee bar and the counts. The winners' rows are sun yellow, and on a
    phone the bar drops below the date;
  - the table cells as tinted chips.
- **Home:** three tilted calendar tiles (ja/misschien/nee), "Prik samen een datum." with a marker
  underline, and three numbered steps.
- **Lists:** each afspraak gets a mini calendar of its first date. The admin lists and the
  new-poll date rows use the same building blocks.
- **Confetti** (`site.js`, about 50 lines, the Web Animations API) after an answer is stored and
  after a poll is made. It never runs with `prefers-reduced-motion`.
- **`Weergave`:** `Maand`, `DagNummer`, `Weekdag`, `WeekdagKort` and `Procent` (the CSS
  percentages, always with a dot as decimal separator). 5 new tests, 103 in all.

### Checked by hand (local, browser)

- The home page in light and dark.
- The answer page at desktop and phone width, light and dark.
- The thank-you plus confetti (the user saw it; screenshots don't catch animations).
- The results page's winner card, ranking (desktop, and phone in dark) and table.
- The new-poll rows.

### Gotchas

- **The first answer-page design was a grid of calendar tiles with tap-to-cycle.** It was turned
  down as harder to fill in than rows (D20). That's what led to the mock-first rule.
- **White text on the bright choice colours fails contrast.** The tokens `--op-ja`, `--op-misschien`
  and `--op-nee` give white text in light mode (on a deeper green and coral) and dark text in dark
  mode.
- **Razor reads `Beheer@if (…)` as an email address** and prints it literally, so text glued to an
  `@` needs a line break or a tag in between. The user spotted it in the admin header; it was
  fixed before merging.
- **`MapStaticAssets` fingerprints `site.js`/`site.css` at build time.** After changing them,
  rebuild before checking the page.

### Left for later

- Deploying this look needs a gembernodes PR that bumps the image to the merge commit of this PR.
- The open questions in PLAN.md (respondents seeing each other's answers; a deadline and a final
  pick).

## After slice 5: the maker's name (2026-11-09)

The user's Google name showed on their polls as `Gember "Gemberkoekje" Koekje wil een datum prikken`.
Now the maker chooses the name respondents see.

- **Nieuwe prikker has a "Jouw naam" field** (`PollDraft.OwnerName`, at most 80 characters). Left
  empty, the account's name stands in.
- **The field suggests the name on your latest poll** (`GetMakerNameHandler`), so a name changed
  once sticks. With no polls yet, it suggests the sign-in provider's name.
- Polls made before this keep the name they were made with; nothing edits an existing poll's name.
- Tests: 108 (5 new: the typed name, the fallback, the limit, and the suggestion).
- Checked by hand: logged in as `Gember "Gemberkoekje" Koekje`, made a poll as "Gember".
  Respondents see "Gember wil een datum prikken", and the next new poll suggests "Gember".
- PLAN.md isn't touched here, because projects#245 (the later slices) changes it too. This is a
  small addition, not a numbered slice.

## Loose ends, and the mocks for slices 6 and 7 (2026-11-11)

No app code changed.

- **Everything built is live.** gembernodes#156 deployed the look and gembernodes#157 the maker's
  name (image `70cd550e`, the merge of projects#246). The PLAN.md status rows now say so, and the
  maker's name has a row of its own.
- **projects#245** (the plan for slices 6 and 7) is merged into this branch rather than merged on
  its own, so it closes when this one merges.
- **Your answers to its open questions are in D21 and D22:**
  - respondents see the likely date only after answering;
  - the final pick shows on the site only (mail perhaps later);
  - the owner can change or undo the pick, and a new "Prikker heropenen" opens a closed poll.
- **Mocks** (D20's mock-first rule) are in `mocks/`: three for slice 6 and seven for slice 7.
  - They are static HTML that load the real `../DatumPrikker.WebApp/wwwroot/css/site.css`, plus
    `mocks/mock.css` for the new rules, grouped per slice so they can move into `site.css` as they
    are.
  - Open them from a local web server, or straight from disk; the striped strip on top marks them
    as mocks.
  - PLAN.md's slice 6 and 7 sections describe what they show. Its open questions list what to
    settle when approving.
- Gotcha: Chromium shows `<input type="time">` in 12-hour format unless the browser's language is
  Dutch. For screenshots, launch Playwright with `args: ['--lang=nl-NL']`; a context `locale`
  alone doesn't change it.
