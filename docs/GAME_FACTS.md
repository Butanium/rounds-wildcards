# STATS.md: ROUNDS card-mod technical reference (UnboundLib 3.2.13, C# 5)

**ACCESSIBILITY:** the dumps list non-public members without marking them. I checked accessibility by compiling a probe with the real `csc.exe` against the installed DLLs. The probe is in `scratchpad\probe\P.cs` and `P2.cs`.

**Non-public (compile error if accessed directly):**
- `PlayerManager.GetPlayerWithID` and `GetPlayerWithActorID`
- `CharacterStatModifiers.ConfigureMassAndSize` and `.data`
- `GunAmmo.currentAmmo` and `.SetActiveBullets`
- `PlayerVelocity.simulated`, `.velocity` and `.mass`
- `ApplyCardStats.ApplyStats`
- `HealthHandler.RPCA_Die`
- `Gun.ResetStats`, `CharacterStatModifiers.ResetStats` and `Block.ResetStats`

Access these with UnboundLib's `obj.GetFieldValue("f")`, `obj.SetFieldValue("f", v)` and `obj.InvokeMethod("M", args...)` [API]. For example, `a.InvokeMethod("SetActiveBullets", false)` compiles.

**Public (the probe compiled):**
- `PlayerManager`: `GetPlayersInTeam`, `GetClosestPlayer`, `players`
- `HealthHandler`: `Heal`, `CallTakeDamage`, `TakeDamage` (7-argument overload), `DoDamage`, `CallTakeForce`, `isRespawning`
- `GunAmmo.ReDrawTotalBullets`
- `CharacterStatModifiers`: `remainingRespawns`, `RPCA_AddSlow`, `AddSlowAddative`, `WasUpdated`, `DealtDamageAction`
- Handlers: `stunHandler.AddStun`, `silenceHandler.RPCA_AddSilence`, `PlayerCollision.IgnoreWallForFrames`
- `Block.Cooldown()` and `Block.BlockAction`
- `ProjectileHit.dealDamageMultiplierr` and `ProjectileHit.view`
- `Gun.ShootPojectileAction`, `CardChoice.instance.cards`, `data.HealthPercentage`
- UnboundLib: `CardManager.GetCardInfoWithName(string)` (absent from the dump but it compiles), `NetworkingManager.RPC` and `[UnboundRPC]`
- `GameModeManager.AddHook(GameModeHooks.HookPointStart, Func<IGameModeHandler, IEnumerator>)`
- `Unbound.Instance.ExecuteAfterSeconds`, which needs a reference to BepInEx.dll.

Confidence tags:
- **[SRC]**: read in source code: UnboundLib on GitHub, or ModdingUtils/PCE as an example. These are not dependencies we can use.
- **[API]**: the signature is confirmed in `api_game.txt` / `api_unbound.txt`.
- **[COMM]**: community knowledge of the decompiled game. Several mods corroborate it, but I could not read the game source.
- **UNVERIFIED**: my best recollection only. Test it in game before relying on it.

Main sources:
- UnboundLib: https://github.com/willis81808/UnboundLib, in particular `UnboundLib/Cards/CustomCard.cs`, `Patches/ApplyCardStats.cs`, `Patches/Player.cs` and `Networking/NetworkingManager.cs`.
- ModdingUtils (reference only): https://github.com/pdcook/ModdingUtils, in particular `MonoBehaviours/ReversibleEffect.cs`, `Extensions/TemporaryModifiers.cs`, `Utils/PlayerStatus.cs` and `Patches/*`.
- PCE: https://github.com/pdcook/PCE, in particular `MonoBehaviours/PopEffect.cs`, `PacPlayerEffect.cs`, `Cards/PacBulletsCard.cs` and `MurderCard.cs`.
- Thunderstore decompiled mods:
  - Additive_Stats, a transpiler that lists the `*=` stores in ApplyStats/CopyGunStats: https://old.thunderstore.io/c/rounds/p/Root/Additive_Stats/source/
  - WeaponsManager, which mirrors the GunAmmo part of ApplyStats: https://old.thunderstore.io/c/rounds/p/RS_Mind/WeaponsManager/source/
  - LarrysCards, Supcom2Cards and KegsRandomStuffs, for the explosion pattern: https://old.thunderstore.io/c/rounds/p/Breadcast/LarrysCards/source/
- A decompiled game mirror exists at http://git.warmcat.org/ROUNDS/tree/GameCode/ApplyCardStats.cs, but it was unreachable from here.

---------------------------------------------------------------------------------------------------
## 0. Card lifecycle (what runs where, when)

**SetupCard runs often, so never put side effects in it.** [SRC]
- `CustomCard.Awake()` calls `SetupCard(cardInfo, gun, cardStats, statModifiers, block)`.
- This happens on every instance of the card object: the prefab built at load time, and every card spawned in the pick screen or the card bar.
- Only set fields on the card's own `gun`, `statModifiers` and `block` there, plus `cardInfo.allowMultiple` and similar.

**The card template is `Resources.Load("0 Cards/0. PlainCard")`.** [SRC] (`Unbound.cs:284`)
- Its components start at neutral values, so any field you don't touch is a no-op.
- The neutral values are listed in `CustomCard.ResetOnlyGunStats` and the related reset methods. Those methods are never called in BuildCard, but they document the neutral defaults.

**Picking a card runs ApplyStats on every client.** [COMM]+[SRC]
- Call chain: `ApplyCardStats.Pick` → `RPCA_Pick(int[] actorIDs)` → `ApplyStats()`.
- This is true for every client. ModdingUtils' `Cards.RPCA_AssignCard` reproduces the same flow on all clients: `MU_Cards.cs:744-792`.

**OnAddCard runs before the card's stats are applied.** [SRC]
- UnboundLib calls `OnAddCard(...)` from a Harmony **Prefix** on `ApplyCardStats.ApplyStats`. That means:
  - **OnAddCard runs on ALL clients.**
  - It runs **before** this card's stat deltas are applied. For example, `gun.damage` read inside OnAddCard does not yet include this card's own `gun.damage`.
- The `gun` passed in is `player.GetComponent<Holding>().holdable.GetComponent<Gun>()`, and `gunAmmo` is `gun.GetComponentInChildren<GunAmmo>()`.

**OnRemoveCard only runs at game end.** [SRC] (UnboundLib `Patches/Player.cs`)
- It is called only from a **Postfix on `Player.FullReset()`**, for each card in `data.currentCards`. UnboundLib then clears `currentCards`.
- FullReset happens at new game or rematch. It is **not** called between points or rounds.
- Because it is a Postfix, vanilla FullReset has already reset the gun, stats and block when OnRemoveCard runs. In practice, OnRemoveCard is only for destroying your MonoBehaviours and unhooking your events.

**Cards persist all game. Stats are never reset between points or rounds.** [COMM]
- Players are revived at each point (`HealthHandler.Revive`, `PlayerManager.RevivePlayers`).
- Anything you change at runtime stays changed until you revert it.

**Picking the same card twice calls OnAddCard twice.** Use `GetOrAddComponent<T>()` plus a stack counter.

---------------------------------------------------------------------------------------------------
## 1. ApplyCardStats semantics (card prefab field → player field)

**Notation:**
- `P` is the player's live component and `C` is the card's component.
- "mult" means `P.x *= C.x`, so 1 is neutral.
- "add" means `P.x += C.x`, so 0 is neutral.
- Sources: the AdditiveStats transpiler confirms which stores are `*=`: knockback, attackSpeed, maxHealth and gravityForce. WeaponsManager confirms the GunAmmo block. UnboundLib's reset defaults use 1 for mult fields and 0 for add fields, which matches.

**Base values on the player:** [COMM]

| Stat | Base value |
|---|---|
| Max health | 100 |
| Bullet damage | about 55 × `gun.damage` |
| `gunAmmo.maxAmmo` | 3 |
| Attack cooldown | about 0.3 s × `attackSpeed` |
| Block cooldown | `block.cooldown` = 4 s |
| Reload time | about 2 s |

The 55, 2 s and 0.3 s figures are UNVERIFIED as exact numbers.

### Gun stats (written on the card's `gun` in SetupCard)

**GunAmmo fields (set via `C.gun`):**

| Card field | Effect on player | Neutral | Notes |
|---|---|---|---|
| `gun.ammo` (int) | `gunAmmo.maxAmmo += ammo`, then clamped to **1..90** | 0 | Negative values work, down to a minimum of 1. |
| `gun.reloadTime` | `gunAmmo.reloadTimeMultiplier *= reloadTime` | 1 | **Lower is faster.** It does not touch `gun.reloadTime` on the player. |
| `gun.reloadTimeAdd` | `gunAmmo.reloadTimeAdd += x` | 0 | Value is in seconds. |
| `gun.ammoReg` | `gunAmmo.ammoReg += x` | 0 | |

**Gun fields (`CopyGunStats(C.gun, P.gun)`):** [COMM]

| Card field | Effect on player | Neutral | Notes |
|---|---|---|---|
| `damage` | mult, **diluted by projectiles** | 1 | If the card adds projectiles (`C.np != 0`) and the player has more than one: `f = C.np / (C.np + P.np)` and `P.damage *= 1 - f*(1 - C.damage)`. Otherwise it is a plain multiply. |
| `attackSpeed` | mult | 1 | **This is a cooldown multiplier, so lower = faster.** 0.5 means twice the fire rate. |
| `knockback` | mult | 1 | |
| `projectileSpeed` | mult | 1 | |
| `projectielSimulatonSpeed` | mult | 1 | The typo is in the game's own field name. |
| `gravity` (bullet) | mult | 1 | 0 means no bullet drop. |
| `multiplySpread` | mult | 1 | |
| `damageAfterDistanceMultiplier` | mult | 1 | |
| `bulletDamageMultiplier` | mult | 1 | |
| `speedMOnBounce` | mult | 1 | |
| `dmgMOnBounce` | mult | 1 | |
| `hitMovementMultiplier` | mult | 1 | |
| `attackSpeedMultiplier` | mult | 1 | UNVERIFIED |
| `recoilMuiltiplier` | `P.bodyRecoil *= C.recoilMuiltiplier` | 1 | Per the GunBodyRecoilPatch source. |
| `chargeDamageMultiplier` | mult | 1 | Charge guns only. |
| `numberOfProjectiles` (int) | add | **0 on cards** | The player starts at 1. Remember the damage dilution above. |
| `bursts` (int) | add | 0 | Extra shots per trigger pull. |
| `timeBetweenBullets` | add | 0 | Delay between burst shots. |
| `reflects` (int) | add | 0 | Bounces. |
| `smartBounce`, `randomBounces`, `bulletPortal` (int) | add | 0 | |
| `spread`, `evenSpread` | add | 0 | `spread` is roughly an angle fraction. |
| `drag`, `dragMinSpeed` | add | 0 | `dragMinSpeed` is UNVERIFIED. |
| `size`, `projectileSize` | add | 0 | |
| `slow` | add | 0 | Movement slow applied to the target on hit. |
| `percentageDamage` | add | 0 | Fraction of the **target's max HP** per hit. 0.05 means 5%. |
| `cos`, `overheatMultiplier`, `explodeNearEnemyRange`, `explodeNearEnemyDamage`, `chargeNumberOfProjectilesTo` | add | 0 | |
| `timeToReachFullMovementMultiplier` | add | 0 | UNVERIFIED |
| `unblockable`, `ignoreWalls`, `dontAllowAutoFire`, `waveMovement`, `teleport`, `spawnSkelletonSquare` | OR (only set when true) | false | Once on, cards can't turn them off. |
| `destroyBulletAfter` | **overwrite** if nonzero | 0 | Value is in seconds. The last card picked wins. |
| `lockGunToDefault` + `forceSpecificAttackSpeed` | Sets `P.defaultCooldown = forceSpecificAttackSpeed` and locks the gun | false / 0 | Used by some "laser" or "minigun" designs. |
| `objectsToSpawn` (ObjectsToSpawn[]) | Appended. Repeats of the same entry increment `.stacks` | empty | See §4. |
| `projectileColor` | Blended or set when not `Color.black` | black | Exact blending is UNVERIFIED. |
| `projectiles[0].objectToSpawn` | Replaces the player's bullet prefab if the card's array is non-empty | empty | Leave this empty. |

### statModifiers (CharacterStatModifiers on the card) [COMM]

| Card field | Effect on player | Neutral | Notes |
|---|---|---|---|
| `health` | **`data.maxHealth *= health`** | 1 | `stats.health` is not itself changed. The player is revived after picking, so HP refills. |
| `sizeMultiplier` | mult, then `ConfigureMassAndSize()` | 1 | Mass also scales with max HP and size. |
| `movementSpeed` | mult | 1 | |
| `jump` | mult | 1 | Jump force. |
| `gravity` | **`Gravity.gravityForce *= gravity`** | 1 | Player gravity. ModdingUtils patches `ResetStats` to restore `gravityForce`, which implies vanilla ResetStats does not restore it. UNVERIFIED |
| `numberOfJumps` (int) | **`data.jumps += n`** | 0 | |
| `regen` | **`healthHandler.regeneration += x`** | 0 | HP per second. |
| `lifeSteal` | add | 0 | Fraction of damage dealt that heals you. |
| `secondsToTakeDamageOver` | add | 0 | Damage you take is spread over this many seconds. Vanilla "Decay" uses this. |
| `respawns` (int) | add | 0 | Phoenix uses this. `remainingRespawns` is refilled on full revive. |
| `refreshOnDamage` | OR | false | |
| `automaticReload` | AND: set to false if the card has false | **true** | |
| `AddObjectToPlayer` (GameObject) | Instantiated as a child of the player and added to `objectsAddedToPlayer` | null | |
| `slow`, `slowSlow`, `fastSlow`, `tasteOfBloodSpeed`, `rageSpeed`, `attackSpeedMultiplier` | Runtime state, not applied from cards | – | Don't set these in SetupCard. |

After applying, the game calls `stats.WasUpdated()` and then `health.Revive()`.

### block (Block on the card) [COMM]

| Card field | Effect on player | Neutral | Notes |
|---|---|---|---|
| `cdMultiplier` | mult | 1 | **Lower is a faster block recharge.** |
| `cdAdd` | add | 0 | Seconds added to block cooldown. Cooldown is `(cooldown + cdAdd) * cdMultiplier`, which is UNVERIFIED. |
| `additionalBlocks` (int) | add | 0 | Extra blocks per press. |
| `healing` | add | 0 | HP healed on block. |
| `forceToAdd`, `forceToAddUp` | add | 0 | Dash force on block. |
| `autoBlock` | OR | false | |
| `objectsToSpawn` (List<GameObject>) | Appended | empty | Spawned at block, like Explosive block. |

**Summary rule:** fields with neutral 1 are multipliers, fields with neutral 0 are additive, and bools are OR. UnboundLib's reset values agree with this rule for every field it lists. Example SetupCard:
```csharp
gun.damage = 1.3f;          // +30% damage
gun.attackSpeed = 0.8f;     // +25% fire rate
gun.ammo = 2;               // +2 ammo
gun.reloadTime = 1.25f;     // +25% reload time
statModifiers.health = 0.8f; block.cdMultiplier = 0.75f;
cardInfo.allowMultiple = true;
```

---------------------------------------------------------------------------------------------------
## 2. Runtime (non-SetupCard) stat changes

**The live stats objects:**
- Gun: `player.data.weaponHandler.gun`.
- GunAmmo: `gun.GetComponentInChildren<GunAmmo>()`.
- Stats: `player.data.stats`.
- HP: `player.data.health` and `player.data.maxHealth`.
- Block: `player.data.block`.
- Gravity: `player.GetComponent<Gravity>()`.

All of these are [API].

**Apply stat changes identically on all clients.** ApplyStats already does this, so live values agree across clients.
- Bullet stats are read on each client when the bullet is initialised, via `Gun.BulletInit` → `ApplyProjectileStats`. That runs on all clients.
- **Never** change gun or damage stats only on `IsMine`. Other clients would compute bullets with stale values.
- Trigger runtime changes from events that fire on every client:
  - BlockAction, ShootPojectileAction, DealtDamageAction / WasDealtDamageAction
  - Game-mode hooks
  - OnAddCard
- An `UnboundRPC` sent from the owner also works.
- Movement-only stats (`stats.movementSpeed`, `jump`, `gravity.gravityForce`) are simulated by the owner and synced by position. Gating them is harmless, but doing them everywhere is simplest.

**Revert pattern.** Store the delta, not the old value, so stacking with other cards stays correct. ModdingUtils `TemporaryModifiers` does the same.
```csharp
float dmgDelta;                         // field on your MonoBehaviour
void Apply()  { dmgDelta = gun.damage * 0.5f; gun.damage += dmgDelta; }   // +50%
void Revert() { gun.damage -= dmgDelta; dmgDelta = 0f; }
```

**Ammo changes** need extra calls:
- After changing `gunAmmo.maxAmmo`, call `gunAmmo.ReDrawTotalBullets()` [API] so the bullet UI matches.
- `currentAmmo` is a field [API] but **non-public**: use `gunAmmo.GetFieldValue("currentAmmo")` / `SetFieldValue`. ModdingUtils also calls `gunAmmo.InvokeMethod("SetActiveBullets", false)` (non-public) to redraw.

**Max-HP changes** keep the HP ratio, the way ModdingUtils does it:
```csharp
float ratio = data.health / data.maxHealth; data.maxHealth += delta; data.health = data.maxHealth * ratio;
stats.InvokeMethod("ConfigureMassAndSize");   // NON-PUBLIC: ModdingUtils calls it via reflection
```

**Size changes:** after changing `stats.sizeMultiplier` at runtime, call `stats.WasUpdated()` and `stats.InvokeMethod("ConfigureMassAndSize")` (reflection; it is non-public).

**Reset functions:**
- `Gun.ResetStats()`, `CharacterStatModifiers.ResetStats()` and `Block.ResetStats()` exist [API] but are non-public.
- They are called by `Player.FullReset()` at game start, not per point.
- Don't call them yourself: the game won't re-apply cards. ModdingUtils re-runs ApplyStats for every card after a reset, and we don't have ModdingUtils.

**Per-point reset:** use hooks such as `GameModeManager.AddHook(GameModeHooks.HookPointStart, MyHook)` [API]. The signature is `Func<IGameModeHandler, IEnumerator>`, and hooks run on all clients. Revert temporary buffs in `HookPointEnd` / `HookRoundEnd` / `HookGameEnd`, and on `OnDisable` (death).

---------------------------------------------------------------------------------------------------
## 3. Networking

### Damage path

**HealthHandler signatures** [API]:
```
void CallTakeDamage(Vector2 damage, Vector2 position, GameObject damagingWeapon, Player damagingPlayer, bool lethal)
void RPCA_SendTakeDamage(Vector2 damage, Vector2 position, bool lethal, int playerID)    // [PunRPC]
void TakeDamage(Vector2 damage, Vector2 position, GameObject damagingWeapon, Player damagingPlayer, bool lethal, bool ignoreBlock)
void TakeDamage(Vector2 damage, Vector2 position, Color dmgColor, GameObject damagingWeapon, Player damagingPlayer, bool lethal, bool ignoreBlock)
void DoDamage(Vector2 damage, Vector2 position, Color blinkColor, GameObject damagingWeapon, Player damagingPlayer, bool healthRemoval, bool lethal, bool ignoreBlock)
void TakeDamageOverTime(Vector2 damage, Vector2 position, float time, float interval, Color color, GameObject damagingWeapon, Player damagingPlayer, bool lethal)
void Heal(float healAmount)
void CallTakeForce(Vector2 force, ForceMode2D forceMode, bool forceIgnoreMass, bool ignoreBlock, float setFlying)
void Revive(bool isFullRevive)     // RPCA_Die(Vector2), RPCA_Die_Phoenix(Vector2)
```
C# 5 note: the dump strips default values. Pass every argument explicitly.

**What each call does:** [COMM]
- **`CallTakeDamage`** sends `RPCA_SendTakeDamage` to **RpcTarget.All**. It skips if the target is blocking or the damage is zero. Every client then runs `TakeDamage` (with ignoreBlock=true) → `DoDamage`, which subtracts HP and fires DealtDamage / WasDealtDamage, lifesteal and so on.
- **So call `CallTakeDamage` from exactly ONE client.** Use the attacker's owner (`attacker.data.view.IsMine`); PCE and most mods do this. Calling it from every client applies the damage N times.
- `TakeDamage` / `DoDamage` are **local only**. Use them only inside code that already runs on every client, such as an RPC or an all-clients event. PCE does `health.TakeDamage(v, pos, Color.red, null, null, false, true)` inside an all-client effect.
- **Damage direction:** the damage is a Vector2 whose magnitude is the HP removed, so `dir.normalized * amount`. It also matters for death direction and visuals.
- **Death:** when HP drops below 0, `RPCA_Die` (or `RPCA_Die_Phoenix` if `remainingRespawns > 0`) is sent by the victim's owner. UNVERIFIED exactly who sends it.
- **Insta-kill:** PCE Murder uses `victim.data.view.RPC("RPCA_Die", RpcTarget.All, new object[]{ new Vector2(0,1) })`. Call it from one client only.

**`Heal(float)` has no RPC.** [API] Call it on all clients from an all-clients event. HP is not continuously synced, so healing on one client causes desync. UNVERIFIED that HP is never synced. Never assume it is.

### Bullet spawning [COMM]

1. **The shooter's client:** `Gun.Attack` → `FireBurst` → `PhotonNetwork.Instantiate` of the bullet.
2. **Every client:** `ProjectileInit.RPCA_Init(senderID, nrOfProj, dmgM, randomSeed)`, signature [API], → `Gun.BulletInit(bullet, n, dmgM, seed, useAmmo)` [API] → `ApplyProjectileStats` → **`gun.ShootPojectileAction(bullet)` fires on ALL clients** with the local copy of the projectile.
3. **Hits:**
   - `ProjectileHit.Hit` is acted on by the bullet's owner (`ProjectileHit.view.IsMine`).
   - That owner sends `RPCA_DoHit(hitPoint, hitNormal, vel, viewID, colliderID, wasBlocked)` [API] to all clients.
   - Hit visuals, ObjectsToSpawn and RayHitEffects run on every client.
   - Damage is then applied once. UNVERIFIED whether by the owner's CallTakeDamage or by local TakeDamage everywhere; you should not need to know.
4. **Consequences:**
   - In `ShootPojectileAction` you may modify the bullet (ProjectileHit, MoveTransform, add components) **on all clients identically**. Use no `UnityEngine.Random` there, or seed it.
   - To react to "my bullet hit a player", prefer `stats.DealtDamageAction` (Action<Vector2,bool>) on the shooter. It fires inside DoDamage, which runs on all clients.
   - Read the victim from `CharacterStatModifiers.DealtDamage(Vector2, bool, Player damagedPlayer)`, via a Harmony postfix or a `DealtDamageEffect` subclass. `DealtDamageAction` does not pass the victim.

### UnboundLib RPC (no PhotonView needed) [API]+[SRC]

**API** (namespace `UnboundLib`; the attribute is in `UnboundLib.Networking`):
```
static void NetworkingManager.RPC(Type targetType, string methodName, params object[] data)      // all clients incl. sender
static void NetworkingManager.RPC_Others(Type targetType, string methodName, params object[] data)
static void NetworkingManager.RPC_Unreliable(...), RPC_Others_Unreliable(...)
static void NetworkingManager.RegisterEvent(string eventName, NetworkingManager.PhotonEvent handler) // handler(object[] args)
static void NetworkingManager.RaiseEvent(string eventName, params object[] data)  / RaiseEventOthers(...)
```

**Rules:**
- The target method must be **static** and carry `[UnboundRPC]`. It is found by reflection on targetType, and it may be non-public.
- Offline, `RPC` invokes the method directly.
- Arguments must be Photon-serialisable: int, float, bool, string, Vector2/3, and arrays of those. Pass `player.playerID`, never a Player object.

```csharp
using UnboundLib; using UnboundLib.Networking; using Photon.Pun;
public static class MyNet {
  public static void SendHeal(Player p, float amt) {       // call from ONE client (p.data.view.IsMine)
    NetworkingManager.RPC(typeof(MyNet), "RPCA_Heal", new object[] { p.playerID, amt });
  }
  [UnboundRPC] private static void RPCA_Heal(int playerID, float amt) {
    Player p = null;   // GetPlayerWithID is NON-PUBLIC (mods reach it via reflection), so loop instead
    foreach (Player q in PlayerManager.instance.players) { if (q.playerID == playerID) p = q; }
    if (p != null) p.data.healthHandler.Heal(amt);
  }
}
```

**Photon RPC alternative (PCE style):**
- Add a `[PunRPC] public void RPCA_X(...)` method to a MonoBehaviour **on the player root**. That root carries `data.view`.
- Call it with `player.data.view.RPC("RPCA_X", RpcTarget.All, new object[]{...})` from the owner only.
- The component must exist on every client, which it does if you add it in OnAddCard.

### Randomness

- Decide on one client (the owner, or `PhotonNetwork.IsMasterClient`), then broadcast the result.
- Alternatively, seed `new System.Random(seed)` from shared data, such as `playerID` plus a counter incremented by an all-clients event.

---------------------------------------------------------------------------------------------------
## 4. Common patterns

### 4a. Per-player behaviour (add on pick, remove on reset)

```csharp
public override void OnAddCard(Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health,
                               Gravity gravity, Block block, CharacterStatModifiers characterStats) {
  MyEffect e = player.gameObject.GetOrAddComponent<MyEffect>();   // UnboundLib ExtensionMethods [API]
  e.stacks++;                                                     // runs on all clients -> same state everywhere
}
public override void OnRemoveCard(Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health,
                                  Gravity gravity, Block block, CharacterStatModifiers characterStats) {
  MyEffect e = player.gameObject.GetComponent<MyEffect>();
  if (e != null) UnityEngine.Object.Destroy(e);
}
public class MyEffect : MonoBehaviour {
  public int stacks; Player player; Action<BlockTrigger.BlockTriggerType> onBlock;
  void Start() {                                  // Start, not Awake: fields are set after AddComponent
    player = GetComponent<Player>();
    onBlock = OnBlock;                            // keep the delegate so -= works
    player.data.block.BlockAction = (Action<BlockTrigger.BlockTriggerType>)Delegate.Combine(player.data.block.BlockAction, onBlock);
  }
  void OnDestroy() {
    if (player != null) player.data.block.BlockAction = (Action<BlockTrigger.BlockTriggerType>)Delegate.Remove(player.data.block.BlockAction, onBlock);
  }
  void OnBlock(BlockTrigger.BlockTriggerType t) { /* fires on all clients (RPCA_DoBlock) */ }
}
```
- You can also write `block.BlockAction += onBlock;`, since these are plain fields.
- **Always unhook in OnDestroy.** Gun, Block and Stats live as long as the player does.

### 4b. Hooking bullets

`gun.ShootPojectileAction` is an `Action<GameObject>` that fires on all clients.

```csharp
void OnShoot(GameObject bullet) {
  ProjectileHit ph = bullet.GetComponent<ProjectileHit>();
  MoveTransform mt = bullet.GetComponent<MoveTransform>();
  if (ph != null) { ph.damage *= 1.2f; ph.unblockable = true; ph.stun = 0.2f; }  // fields [API]
  if (mt != null) { mt.gravity *= 0.5f; mt.localForce *= 1.5f; /* speed */ }
  bullet.AddComponent<MyBulletBehaviour>();                // custom per-bullet logic (Update, OnDestroy)
}
```

**Useful ProjectileHit fields** [API]:
- Damage: `damage`, `dealDamageMultiplierr` (the double r is in the game), `percentageDamage`, `force`
- Status effects: `stun`, `movementSlow`, `unblockable`, `holdPlayerFor`
- Spawning: `objectsToSpawn`, `isAllowedToSpawnObjects`
- Ownership: `ownPlayer`, `ownWeapon`, `view`, `spawnedAttack`
- Visuals: `projectileColor`
- Hit callbacks: `hitAction` (Action), `hitActionWithData` (Action<HitInfo>), and `AddHitAction(Action)` / `AddHitActionWithData(Action<HitInfo>)`. These fire from RPCA_DoHit on all clients. UNVERIFIED that `hitActionWithData` passes the HitInfo of a player hit.

**MoveTransform:** `velocity`, `localForce`, `worldForce`, `gravity`, `drag`, `simulationSpeed`, `distanceTravelled`.

**Bounces:** `RayHitReflect` (bounces) has `reflects`, `speedM`, `dmgM` and `reflectAction`.

**Bullets you didn't fire:**
- To affect bullets from a **Gun.objectsToSpawn** entry instead, use `new ObjectsToSpawn { AddToProjectile = someGameObjectWithYourComponent }`, as PCE PacBullets does.
- In that case `AddToProjectile` is instantiated as a child of every bullet.

### 4c. Explosions (reuse the vanilla prefab; many mods use this exact path)

```csharp
GameObject expCard = (GameObject)Resources.Load("0 cards/Explosive bullet");
ObjectsToSpawn src = expCard.GetComponent<Gun>().objectsToSpawn[0];   // .effect = explosion prefab, .AddToProjectile = trail
// (a) as on-hit spawn: copy into the card's gun in SetupCard
//     gun.objectsToSpawn = new ObjectsToSpawn[] { src };   // stacks like vanilla
// (b) custom-tuned prefab, built once (cache it in a static):
GameObject boom = UnityEngine.Object.Instantiate(src.effect);
boom.transform.position = new Vector3(1000f, 0f, 0f);
boom.hideFlags = HideFlags.HideAndDontSave;                // 61 in decompiled mods
UnityEngine.Object.DestroyImmediate(boom.GetComponent<RemoveAfterSeconds>());
Explosion ex = boom.GetComponent<Explosion>(); ex.damage = 40f; ex.range = 4f; ex.force = 2000f;
// (c) one-off spawn at a point, ON ALL CLIENTS (call from an all-clients event):
GameObject go = UnityEngine.Object.Instantiate(boomPrefab, pos, Quaternion.identity);
SpawnedAttack sa = go.GetComponent<SpawnedAttack>(); if (sa == null) sa = go.AddComponent<SpawnedAttack>();
sa.spawner = player;  // owner attribution, team colour, self-damage handling
// Explosion.Start() auto-explodes if ex.auto
```

**Explosion fields** [API]: `damage`, `range`, `force`, `stun`, `slow`, `silence`, `ignoreTeam`, `ignoreWalls`, `scaleDmg`, `scaleRadius`, `auto`, `locallySimulated`.

**Who applies explosion damage is UNVERIFIED.** The likely path is via `SpawnedAttack.IsMine()`, so only the spawner's client applies it. **Test online that damage is not doubled.** Safest variant:
1. Spawn it on all clients with `ex.damage = 0` as a pure visual.
2. Have the owner apply damage with `CallTakeDamage` to players within range.

Spawning through `gun.objectsToSpawn` is the most robust option, because the game handles it.

### 4d. Healing, damage, force

```csharp
target.data.healthHandler.Heal(25f);                                       // local only, so run on all clients
if (attacker.data.view.IsMine)                                             // ONE client
  target.data.healthHandler.CallTakeDamage(dir.normalized * 30f, (Vector2)target.transform.position,
      attacker.data.weaponHandler.gun.gameObject, attacker, true);
if (target.data.view.IsMine)  // the force RPC also goes to all; the victim's owner is cleanest  (UNVERIFIED)
  target.data.healthHandler.CallTakeForce(dir * 5000f, ForceMode2D.Impulse, false, false, 0.5f);
```

### 4e. Teleport (PCE PopEffect / PacPlayerEffect)

```csharp
[PunRPC] public void RPCA_Teleport(Vector2 pos) {          // on a MonoBehaviour on the player root
  player.GetComponentInParent<PlayerCollision>().IgnoreWallForFrames(2);   // [API]
  player.transform.position = pos;
}
// caller (owner only): if (player.data.view.IsMine) player.data.view.RPC("RPCA_Teleport", RpcTarget.All, new object[]{ newPos });
```
Optionally zero the velocity. `player.data.playerVel.velocity` is **non-public**, so use `player.data.playerVel.SetFieldValue("velocity", Vector2.zero)`.

### 4f. Slow, stun, silence

| Effect | Call | Notes |
|---|---|---|
| Slow | `stats.RPCA_AddSlow(float slowToAdd, bool isFastSlow)` [API] | A PunRPC on the player. Call it locally from all-client code, or as `target.data.view.RPC("RPCA_AddSlow", RpcTarget.All, new object[]{0.5f, false})` from one client. Also `AddSlowAddative(float slowToAdd, float maxValue, bool isFastSlow)` [API]. |
| Stun | `target.data.stunHandler.AddStun(float seconds)` [API] | **Local only (no RPC).** Call it on all clients. |
| Silence | `target.data.silenceHandler.RPCA_AddSilence(float seconds)` [API] | Prevents blocking (UNVERIFIED semantics). |
| Gun on-hit stats | `gun.slow` (card stat), `ProjectileHit.stun`, `ProjectileHit.movementSlow` | Vanilla on-hit route. |

### 4g. Finding players [API]

**PlayerManager (`PlayerManager.instance`):**
- `players` is a `List<Player>`.
- `GetPlayerWithID(int)`, `GetPlayerWithActorID(int)`, `GetPlayersInTeam(int teamID)`. **GetPlayerWithID is non-public** (PCE and ModdingUtils use InvokeMember), so loop over `players` instead. The others' visibility is UNVERIFIED.
- `GetClosestPlayer(Vector2 pos, bool needVision)`, `GetClosestPlayerInTeam(Vector3, int team, bool needVision)`
- `CanSeePlayer(Vector2 from, Player p)` returns `CanSeeInfo`.
- `PlayerDiedAction` is an `Action<Player,int>`; register with `AddPlayerDiedAction(...)`.

**Player and CharacterData fields:**
- Identity: `player.playerID`, `player.teamID`.
- Alive and in the round: `!p.data.dead && p.data.isPlaying`. ModdingUtils also checks `(bool)p.data.playerVel.GetFieldValue("simulated")`, because that field is non-public.
- Health: `data.health`, `data.maxHealth`, `data.HealthPercentage`.
- Other state: `data.isGrounded`, `data.isStunned`, `data.lastSourceOfDamage`, `data.currentCards` (List<CardInfo>).

**Teammates and enemies:**
- Enemies are `p.teamID != me.teamID`.
- With RoundsWithFriends FFA, every player has their own team.
- Teammates are players with the same teamID, excluding yourself by `playerID`.

### 4h. Owner gating and timers

- `player.data.view.IsMine` is true on the client that controls the player.
- For bullets: `ProjectileHit.view.IsMine` or `SpawnedAttack.IsMine()` [API].
- Delays that must survive death: use `Unbound.Instance.ExecuteAfterSeconds(float, Action)` [API], which is an extension on MonoBehaviour. Don't use coroutines on the player.

---------------------------------------------------------------------------------------------------
## 5. Pitfalls

1. **Stat changes in OnAddCard gated by IsMine cause desync.** OnAddCard already runs on all clients. Gate only the side effects that broadcast: RPCs, CallTakeDamage, PhotonNetwork.Instantiate.
2. **Broadcasting from every client multiplies the effect N times.** CallTakeDamage, view.RPC and NetworkingManager.RPC called from inside an all-clients event without an `IsMine` gate run once per client. This is the most common bug: damage ×3 with three players.
3. **Dead players are deactivated.** [COMM] ModdingUtils' ReversibleEffect counts `OnDisable` as "a life ended".
   - `OnDisable` fires, `Update` stops, and **coroutines started on that MonoBehaviour are killed** and do not resume.
   - On revive you get `OnEnable`.
   - Re-arm state in OnEnable, or use `Unbound.Instance` for timers.
   - Phoenix respawn: `HealthHandler.isRespawning` is true while respawning. Damage is ignored then.
4. **Bools are sticky and destroyBulletAfter overwrites.** `unblockable`, `ignoreWalls` and `dontAllowAutoFire` can't be turned off by later cards, and `destroyBulletAfter` from the last card wins.
5. **Damage dilution with projectiles.** A card with `numberOfProjectiles = 2` and `damage = 0.5` behaves differently from what you'd expect after stacking. Check the formula in §1.
6. **`gun.reloadTime` on the card is a multiplier.** It applies to `gunAmmo.reloadTimeMultiplier`, not to `gun.reloadTime`. At runtime, change `gunAmmo.reloadTimeMultiplier` or `gunAmmo.reloadTimeAdd`.
7. **`statModifiers.health` changes max HP.** At runtime, change `data.maxHealth` and `data.health`, not `stats.health`.
8. **maxAmmo is clamped to 1..90 by ApplyStats.** Clamp runtime changes yourself and call `gunAmmo.ReDrawTotalBullets()`.
9. **`numberOfProjectiles` on the player starts at 1.** On cards the neutral value is 0, because it is additive.
10. **Don't touch `projectiles[]` on the card's gun.** If it is non-empty it replaces the player's bullet prefab.
11. **The card bar, the pick screen and the prefab all run SetupCard.** No statics, events or log spam there.
12. **Player objects persist across points and are rebuilt per game** (UNVERIFIED). Always clean up in OnRemoveCard and OnDestroy, and make OnAddCard idempotent with `GetOrAddComponent`.
13. **ObjectsToSpawn stacking.** Adding the same ObjectsToSpawn instance twice increments `.stacks` on the shared object, which is the prefab's object. ModdingUtils patches this. Create a **new** `ObjectsToSpawn` per card prefab if you mutate it.
14. **No C# 6.**
    - Delegates: use `Delegate.Combine` / `-=` with a cached delegate field. No lambdas capturing `this` that you later need to remove.
    - Coroutine hooks: `GameModeManager.AddHook(GameModeHooks.HookPointStart, OnPointStart)` where `static IEnumerator OnPointStart(IGameModeHandler gm) { ...; yield break; }`.
15. **Sandbox and offline.** `PhotonNetwork.OfflineMode` is true there and `view.IsMine` is true for everyone local. `NetworkingManager.RPC` executes directly. PCE checks `GM_Test.instance != null && GM_Test.instance.gameObject.activeInHierarchy` to detect sandbox.
16. **Finding vanilla cards.**
    - `CardManager.GetCardInfoWithName(string)` compiles, but it matches on the object name; the exact key format is UNVERIFIED.
    - Simpler: `CardChoice.instance.cards` (CardInfo[]), matching on `cardInfo.cardName`, which is upper case in vanilla (e.g. "EXPLOSIVE BULLET").
    - For the explosion, `Resources.Load("0 cards/Explosive bullet")` is the proven path.
