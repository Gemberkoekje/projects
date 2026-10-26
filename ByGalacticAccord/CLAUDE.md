# By Galactic Accord — instructions for agents

## The code map

- **Read [`CODEMAP.md`](CODEMAP.md) first.** It says what is where, what calls what, which events schedule
  which, and the only paths money and goods move through. Use it to find the file, then read the file:
  the code is the truth.
- **Keep it true.** Any commit that adds, removes, renames or moves a file or a type, changes who calls a
  public member, adds or removes an event, or changes a money or goods path updates `CODEMAP.md` in the
  same commit, including its "last checked" line. If you find the map wrong, fix it.

## Working here

- The design is [`../by_galactic_accord_gdd_v0.3.md`](../by_galactic_accord_gdd_v0.3.md); Part II is Slice 1.
  Code comments with a bare `§n` cite the old v0.2 design; `CODEMAP.md` §10 translates. New comments cite
  v0.3 as "design §n".
- Work follows the steps of [`plan.md`](plan.md). A finished step gets a log in `log/` (see
  [`log/README.md`](log/README.md)) and a status line in the plan that links to it.
- **A finished step lands directly on `main`, with no PR**, once it passes the "green before main" check
  in plan §2 (Release build, fast and soak tests). Then work stops for the user's review.
- `dotnet build`, `dotnet test` (fast), `dotnet test --filter Category=Soak` (long runs), and
  `dotnet run --project src/ByGalacticAccord.Cli -- soak 1000000 --seed 42`, all from this folder.
