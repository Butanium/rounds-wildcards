# WildCards — ROUNDS card mod

BepInEx + UnboundLib mod adding 10 cards to ROUNDS (Landfall). Repo: https://github.com/Butanium/rounds-wildcards (public, commits as Butanium's no-reply address).

## Everyday commands (Git Bash)
- `bash build.sh` — compile `bin/WildCards.dll` + `bin/WildCards-<ver>.zip`. Uses Windows' built-in .NET Framework `csc.exe` → **C# 5 only** (no `$""`, `?.`, `nameof`, `=>` members, auto-prop initializers, `out var`, tuples, pattern matching). No .NET SDK (installing it was blocked).
- `CARD=Portal bash build.sh` — compile check with only `src/Cards/New/Portal.cs` among new cards (for parallel agents).
- `powershell -File install.ps1 [-Profile "WildCards 1v1"]` — copy build into an r2modman profile + mods.yml entry. **Fails while ROUNDS is running** (DLL locked).
- `powershell -File launch.ps1 [-Restart]` — launch ROUNDS modded with the profile (desktop shortcut "ROUNDS WildCards" does the same). Close the game after any launch made only for testing/dumping.
- `bash release.sh <ver> "<message>"` — bump version (src/Plugin.cs + package/manifest.json), build, commit, push, install (if game closed), `gh release` with the zip.

## Layout
- `src/Plugin.cs` (config: `WildCardChance` offer boost, `Debug.DumpVanillaArt`), `src/Common.cs` (`WildCard` base, `StackEffect` per-player component, `Fx` helpers: JumpVel, Teleport, Get/SetVel via reflection, ammo helpers; `WildState` game hooks), `src/Patches.cs` (`Hooks.*` events: BulletInit, Damaged, ProjectileHitAction, PointStart, GameStart; offer-boost patch), `src/CardRegistry.cs` (auto-registers every `WildCard` subclass + calls its static `Init()`), `src/CardArt.cs` (embedded PNG → UI Image, 1100×865 like vanilla), `src/ArtDumper.cs`.
- Cards: `src/Cards/*.cs` (Rocket Jump, Meteor, Boomerang, Balloon, Switcheroo) and `src/Cards/New/*.cs` (Portal, Grapple, Rewind, HotPotato, Wraparound; one namespace each).
- Art: `art/svg/<card_title_snake>.svg` → `bash art/render.sh <name>` (headless Edge, 1100×865 transparent) → `art/png/` (embedded by build). `docs/make_showcase.ps1` regenerates the README image. `art/ref/` = vanilla art dumped from the game (gitignored, Landfall copyright).
- `docs/GAME_FACTS.md` — researched engine facts (stat semantics, networking). `docs/design/` — design bar, round-2 specs, implementer brief.
- `dev/` (gitignored, regenerate): `api_game.txt` / `api_unbound.txt` via `tools/dump_api.ps1`; `ILDump.exe` via `bash tools/build_ildump.sh` → `dev/ILDump.exe "<ROUNDS_Data\Managed>" Type:Method` prints real game IL.

## Verified game facts (from IL)
- Jump Δv = `upForce*0.01*stats.jump` (`Fx.JumpVel`). PlayerVelocity: private `velocity`, −20/s² base pull; `Gravity` adds force ∝ `sinceGrounded^exponent` (zero `sinceGrounded` to cancel).
- Networking: OnAddCard runs on all clients. `CallTakeDamage`/`CallTakeForce`/`CallDoBlock` broadcast → call from ONE client. `DoDamage`/`TakeDamage`/`Heal` are local but run on every client via RPC. `Gun.BulletInit` runs on every client per bullet (after `GunAmmo.Shoot`). Players are simulated by their owner (`data.view.IsMine`). Custom sync: `[UnboundRPC] static` + `NetworkingManager.RPC(typeof(X), "Name", args)` (namespace `UnboundLib`).
- `CardInfo.allowMultiple=false` (set in SetupCard) = once-only; vanilla `SpawnUniqueCard` enforces it.
- Vanilla on-block cards cost +0.25s block cooldown; Defender/Teleport give −30%.
- Out-of-bounds box x∈[−35.56,35.56], y∈[−20,20]. Card art root 1100×865 under `Canvas/Front/Background/Art`.

## Design bar (from the user's playtests)
Only cards that add a **new verb / visible event** (Switcheroo, Meteor, Portal...). No narrow stat modifiers, no "bigger/unblockable/more damage", nothing resembling a vanilla card. Descriptions 3–6 words like vanilla; numbers go in stat lines. Every effect must be readable on screen and counterable. Card art must match vanilla style (full-color flat cartoon ball characters, see `art/ref/`) — and needs a real compare-to-reference feedback loop; one review pass gave "mid" results.

## Status / open ideas
- Playtest feedback so far: fun overall; SWITCHEROO now telegraphed + block-to-cancel (once-only); BOOMERANG reworked (full range, wall turn, ghost return); WRAPAROUND side-wrap only for players; BALLOON buffed + visual.
- Untested in game as of 1.4.6: latest Boomerang/Switcheroo/Wraparound/balloon visual/art placement.
- Ideas: make PORTAL ROUNDS once-only; lower Switcheroo/Rewind block-cooldown cost to vanilla +0.25s; redo art with an iterative reference loop; reserve cards: LEASH, BOUNCE PADS, BULLET TIME (needs own-bullets-only redesign).
