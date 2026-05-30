# The Curatool — Functional Documentation

## What this app is

The Curatool is a Storyteller's assistant for the social-deduction game **Blood on the Clocktower**. It implements a special draft-style game mode (the **"Curator"** loric) in which, instead of the Storyteller secretly handing out roles, players are walked through the script one at a time and each is offered a small curated set of candidate characters to pick from.

The app's job is to make that draft fair and legal: it knows the rules of every script it is given, tracks who has chosen what, and continuously recalculates which characters are still valid to offer so that the final group of players always forms a legal game.

## What the app does functionally

The experience moves through four stages:

1. **Load a script.** The Storyteller picks a built-in standard script, uploads a script file, or pastes script JSON. The app reads the list of characters on that script and reports any it does not recognise (so the Storyteller can review before drafting).

2. **Configure the session.** The Storyteller sets the player count (5–15) and toggles any special options that the loaded script supports, such as:
   - **Active Lorics** — named modifiers for the session. "The Curator" is always on; others (such as the Sentinel) can be switched on or off.
   - **Marionette** — a pre-draft adjustment that bakes the hidden Marionette into the counts (one extra Townsfolk, one fewer Minion).
   - **Atheist** — switches the game to the all-good Atheist setup.
   - **Legion** — switches the game to Legion mode and lets the Storyteller set how many Legion seats there are.

3. **Run the draft.** Players are placed into a randomised secret draft order. For each player in turn, the app offers up to **three** candidate characters — chosen to span the character types where possible — and the Storyteller records the player's pick. Throughout the draft the app:
   - Keeps a running count of how many Townsfolk, Outsiders, Minions, and Demons have been chosen.
   - Recalculates the **target distribution** every time a character is chosen, because many characters change how many of each type the game needs.
   - Removes any character from the offer pool that would make a legal final game impossible, that is already taken, or that is blocked by another choice.
   - Handles characters that are placed by the Storyteller rather than drafted (for example, hidden or auto-assigned roles).

4. **Review the summary.** When every seat is filled, the app shows the final group makeup, every player's assignment, any Storyteller-only hidden flags, and flags anything still needing the Storyteller's confirmation (such as borrowed abilities that have not yet been assigned).

### Core concepts

- **Setup distribution.** Every player count has a baseline split of Townsfolk / Outsiders / Minions / Demons. Certain characters shift these numbers, and the app tracks **all** legal distributions at once so the draft stays valid no matter which legal target the Storyteller is aiming for.
- **Hidden flags.** Some choices carry Storyteller-only secrets (for example, a player who is secretly the Drunk or the Lunatic). These affect the maths without being shown to the player.
- **Availability constraints.** Some characters become unavailable once another character of a certain kind has already been chosen.
- **Out-of-script / auto-added characters.** Some characters require a partner to be present and the app injects that partner if it is missing.

---

## Townsfolk with specific implementation details

| Townsfolk | What the implementation does |
|---|---|
| **Balloonist** | Storyteller choice of **+0 or +1 Outsider** — the extra Outsider is optional, and both the "with" and "without" outcomes are treated as legal. |
| **Huntsman** | Has **two separate but linked effects.** (a) Its Storyteller-choice **+0 / +1 Outsider** rule *enlarges the target* distribution so the game has room for an extra Outsider. (b) It **requires the Damsel** to be on the script to *fill that slot in the current tally*. The Damsel is a normal **on-script Outsider** that the script author is expected to include — unlike the Choirboy's King, the Damsel is **not auto-injected**. The two rules sit on opposite sides of the ledger (target vs. current), so there is no double-counting. |
| **Choirboy** | **Requires the King.** If the King is not on the script, the app automatically injects it as an out-of-script character. |
| **Alchemist** | Marked as a **dynamic-setup** character: at setup the Storyteller assigns it a borrowed ability taken from a **Minion that is on the script but not in play**. Whatever setup effect that borrowed ability carries is then applied. |
| **Atheist** | Has no setup rules of its own, but when the **Atheist session option** is enabled the whole game switches to the all-good Atheist setup (no real Minions or Demons), and the Atheist is otherwise excluded from normal drafting. |

> All other Townsfolk on the supported scripts have no special setup behaviour — they simply occupy a Townsfolk slot.

---

## Outsiders with specific implementation details

| Outsider | What the implementation does |
|---|---|
| **Drunk** | Occupies an **Outsider slot** while being shown as a **Townsfolk token** (the Drunk is secretly an Outsider). In states where all Townsfolk slots are already filled but an Outsider slot still remains, any Townsfolk offered is implicitly the Drunk and is flagged as such automatically. |
| **Hermit** | Offers the Storyteller a **choice**: either change nothing, or **convert one Outsider slot into a Townsfolk slot**. Both outcomes are treated as legal. |
| **Lunatic** | Occupies an **Outsider slot** while being shown as a **Demon token** (the Lunatic is secretly an Outsider who believes they are the Demon). In states where the Demon slot is already filled but an Outsider slot still remains, the Lunatic is the only valid Demon-token offer and is flagged as such automatically. |

> Other Outsiders simply occupy an Outsider slot with no special maths.

---

## Minions with specific implementation details

| Minion | What the implementation does |
|---|---|
| **Baron** | Adds **+2 Outsiders** (converting two Townsfolk slots into Outsider slots). Always applied. |
| **Godfather** | Storyteller choice of **−1 or +1 Outsider**. Both outcomes are treated as legal. |
| **Summoner** | **Replaces the Demon** — converts the Demon slot into a Townsfolk slot (a Summoner game starts with no Demon). It is **blocked from being offered once any Demon has already been chosen**. |
| **Marionette** | **Excluded from drafting** — it is never offered to players. Instead it is handled through the session-level Marionette option, which applies a **+1 Townsfolk / −1 Minion** pre-draft adjustment. |
| **Boffin** | Marked as a **dynamic-setup** character: the Storyteller assigns it a borrowed ability taken from a **Townsfolk or Outsider that is on the script but not in play**, and any setup effect of that borrowed ability is then applied. |

> Other Minions simply occupy a Minion slot with no special maths.

---

## Demons with specific implementation details

| Demon | What the implementation does |
|---|---|
| **Fang Gu** | Adds **+1 Outsider** (always applied). |
| **Vigormortis** | Removes **−1 Outsider** (always applied). |
| **Lil' Monsta** | Adds an extra **Minion in place of the Demon** (the Minions collectively babysit Lil' Monsta). This swap is offered as an additional legal distribution whenever Lil' Monsta is on the script. |
| **Kazali** | Lets the Storyteller freely set the **number of Outsiders** (an open-ended Outsider adjustment) and **removes all Minions**, converting them to Townsfolk. It is **blocked once any Minion has already been chosen**. |
| **Lord of Typhon** | Lets the Storyteller freely set the **number of Outsiders** and adds **+1 Minion**. It is **blocked once any Minion has already been chosen**, and its Minions are **Storyteller-assigned** rather than drafted. |
| **Legion** | Drives a whole **Legion game mode**: most players become evil "Legion" seats. When Legion mode is active the app drafts only the good roles directly and fills the evil Legion seats separately, using a Legion-specific distribution and a configurable Legion count. |

> Other Demons simply occupy the single Demon slot with no special maths.

---

## Session-wide modifiers (Lorics & options)

| Modifier | What the implementation does |
|---|---|
| **The Curator** | The base loric for this game mode — always active. |
| **Sentinel** | Storyteller choice of **−1, 0, or +1 Outsider** for the session. |
| **Marionette option** | Pre-draft **+1 Townsfolk / −1 Minion** adjustment (see Marionette above). |
| **Atheist option** | Switches the session to the all-good Atheist setup. |
| **Legion option** | Switches the session to Legion mode with a configurable Legion count. |
