# Implementer brief — WildCards round 2

Project: `C:\Users\dumas\claude\rounds-wildcards` (ROUNDS mod, BepInEx + UnboundLib 3.2.13 + HarmonyX). Spec for your card: `scratchpad\R2_FINAL.md` (read your card's section + the "Codebase facts" header).
scratchpad = `C:\Users\dumas\AppData\Local\Temp\claude\29a6498a8035faa6\05a980fb-16d9-4753-a773-aecb0b15f3e2\scratchpad`

## Hard rules
- **Write ONLY your own file** `src/Cards/New/<Name>.cs`. Do NOT edit any other file (other agents work in parallel). If you truly need a core change, describe it in your report instead.
- **Put everything in your own namespace** `namespace WildCards.<Name> { ... }` (e.g. `WildCards.Portal`) so helper class names never collide. Add `using WildCards;`.
- Compile check: `CARD=<Name> bash C:/Users/dumas/claude/rounds-wildcards/build.sh` (compiles core + old cards + only your file). Must print BUILD OK. Iterate until it does.
- **C# 5 only** (.NET Framework csc): no `$""`, no `?.`, no `nameof` (use string literals), no `=>` members, no auto-property initializers, no `out var`, no tuples, no pattern matching, no `using static`, no local functions.
- Card class: `public class <Name>Card : WildCard` (registered automatically by reflection). Optional `public static void Init()` on the card class is called once at startup — subscribe hooks there (guard against double subscription).
- Description 3–6 words, stat lines short (vanilla style). Use `Stat(positive, "Stat", "amount")`.
- Multiplayer: OnAddCard runs on ALL clients. Movement/teleport of a player only on that player's owner (`Fx.IsMine(p)` / `data.view.IsMine`). `CallTakeDamage` / `CallTakeForce` broadcast: call from exactly ONE client. Custom sync via UnboundLib:
  ```csharp
  using UnboundLib.Networking;
  [UnboundRPC] static void RPCA_Thing(int playerID, Vector2 p) { ... }   // static, in a class; found by reflection
  NetworkingManager.RPC(typeof(MyNetClass), "RPCA_Thing", new object[] { player.playerID, pos });   // runs on all clients incl. sender
  ```
  Pass ints/floats/bools/Vector2/Vector3 only. Find players by looping `PlayerManager.instance.players` (GetPlayerWithID is non-public).
- Robustness: wrap per-frame logic in try/catch only where external objects can vanish; null-check players/data; dead players' GameObjects get disabled (OnDisable fires); clean up on `Hooks.PointStart`, revive (`healthHandler.reviveAction`), death (`PlayerManager.instance.AddPlayerDiedAction`), and OnDestroy.

## Core helpers available (src/Common.cs, src/Patches.cs)
- `WildCard : CustomCard` (GetCardArt null, mod name WILD, `Stat(...)` helper). Override SetupCard (stat deltas on prefab: e.g. `gun.reloadTimeAdd = 0.5f`, `block.cdAdd = 0.75f`, `gun.projectileSpeed = 0.8f` (mult), `gun.attackSpeed = 1.15f` (cooldown mult, higher = slower)), OnAddCard, OnRemoveCard, GetTitle, GetDescription, GetRarity, GetStats, GetTheme.
- `StackEffect : MonoBehaviour` with fields `stacks, player, data, gun, gunAmmo`, props `IsMine`, `Alive`, virtual `Init()`, `OnStacksChanged()`. `Fx.Add<T>(player, gun, gunAmmo)` in OnAddCard, `Fx.Remove<T>(player)` in OnRemoveCard, `Fx.Stacks<T>(p)`, `Fx.Get<T>(p)`.
- `Fx.IsMine(p)`, `Fx.JumpVel(p)` (velocity change of one jump, ~25), `Fx.Teleport(p, pos)` (owner only; ZEROES velocity — call `Fx.SetVel` after), `Fx.GetVel(data)/Fx.SetVel(data, v)` (private PlayerVelocity.velocity), `Fx.GetAmmo/SetAmmo/RefreshAmmoIcons(gunAmmo)`.
- `Hooks.BulletInit` (Action<Gun, GameObject bullet, bool useAmmo>, every bullet, all clients), `Hooks.Damaged` (Action<Player victim, Player attacker, Vector2 dmg>, real hits only, all clients), `Hooks.ProjectileHitAction` (Action<ProjectileHit, Vector2 hitPoint, Vector2 hitNormal, Vector2 vel, int viewID, int colliderID, bool wasBlocked>, postfix of RPCA_DoHit, all clients; viewID==-1 && colliderID!=-1 => static map), `Hooks.PointStart`, `Hooks.GameStart` (Action). Subscribe with `+=`.
- Existing cards for reference: `src/Cards/MovementCards.cs` (ROCKET JUMP, METEOR), `BulletCards.cs` (BOOMERANG, BALLOON), `CombatCards.cs` (SWITCHEROO — Block.BlockAction pattern).

## Game facts (verified from IL)
- PlayerVelocity: velocity integrated in FixedUpdate, constant -20/s² pull; Gravity adds ∝ sinceGrounded^exponent (set `data.sinceGrounded = 0` to suppress). Jump dv = upForce*0.01*stats.jump.
- Out of bounds: x ∈ [-35.56, 35.56], y ∈ [-20, 20] (OutOfBoundsHandler.LateUpdate).
- Bullet: `MoveTransform.velocity` (public Vector3), `RayCastTrail.lastPos` (set it after teleporting a bullet!), `ProjectileHit.ownPlayer`, `SpawnedAttack.SetColor(Color)`. Hits are decided on the shooter's client (ProjectileHit gates on view.IsMine) and broadcast via RPCA_DoHit.
- `HealthHandler.CallTakeForce(Vector2 force, ForceMode2D mode, bool forceIgnoreMass, bool ignoreBlock, float setFlying)`: impulse → v += force/mass (with ignoreMass, force scaled by mass/100 first). Respects block when ignoreBlock=false.
- `data.input.jumpWasPressed / jumpIsPressed / direction / aimDirection`, `data.currentJumps`, `data.isGrounded`, `data.hand` (Transform), `data.groundMask`? (verify in api_game.txt).
- Tools: `scratchpad\api_game.txt`, `api_unbound.txt` (grep), IL dumper `scratchpad\ildump\ILDump.exe "C:\Program Files (x86)\Steam\steamapps\common\ROUNDS\ROUNDS_Data\Managed" Type:Method`, `STATS.md`. Verify any member you use (private members won't compile; use HarmonyLib `AccessTools.Field(...)` cached FieldInfo if needed).

## Visuals without assets
- Material: `new Material(Shader.Find("Sprites/Default"))` (cache one static). LineRenderer for ropes/rings (set positionCount, widths, startColor/endColor, sortingOrder high e.g. 50, useWorldSpace). Draw circles as a LineRenderer loop of ~32 points. Simple pulse/scale animation in Update. For text, `TextMesh` (UnityEngine.TextRenderingModule) with `GetComponent<MeshRenderer>().sortingOrder = 60`.
- Visual objects are local per client (not networked); create them in response to synced state.
- Destroy visuals on point start / card removal.
