# ROUNDS card mod — design round 2 (HIGH BAR)

## What the players told us after playtesting v1
- LIKED: **SWITCHEROO** (blocking swaps places with whoever hurt you) and **METEOR** (hold down in the air to dive; landing slams nearby enemies and triggers your block effects).
- DISLIKED: GRUDGE (conditional damage bonus), and in general "narrow modifiers": *"all your ideas are like make the bullet bigger / unblockable -> less fun. We're looking for fun NEW stuff, not some narrow modifier. Have a high bar for accepting a new card."*
- Also: descriptions must be SHORT like vanilla ("Bullets bounce", "Blocking teleports you"). 3–6 words. Numbers go in stat lines.
- Also asked about BOOMERANG: "isn't it just Tracker but less good?" -> they compare against vanilla cards; anything resembling a vanilla card is rejected.

## The bar (a card must pass ALL)
1. **New verb**: it gives the player a new action or creates a new on-screen event that vanilla ROUNDS doesn't have (swap places, dive-slam). NOT: +/- damage, size, speed, unblockable, crit-on-condition, "last bullet is special", per-point gambles on stats, "frenzy at low HP".
2. **Readable & visible**: everyone on screen instantly sees what happened (players move, things appear, physics change). No invisible number changes.
3. **Creates moments/stories**: "did you see that?!" outplays, funny accidents, counterplay for the victim.
4. **Not a vanilla clone** (know the vanilla list: Teleport, Shockwave, Saw, Toxic cloud, Echo, Chase, Supernova, Thruster, Homing, Bouncy, Ricochet, Trickster, Explosive bullet, Grow, Decay, Poison, Freeze/Frost slam, Emp, Bombs away, Radar shot, Healing field, Shield charge, Implode, Phoenix, Abyssal countdown, Defender, Overpower, Static field, Silence, Lifestealer, Leech, Brawler, Mayhem, Big bullet, Fast forward, Timed detonation, Demonic pact, Dazzle, Shields up, Remote, Riccochet, Spray, Burst, Buckshot, Scavenger, Steady shot, Tactical reload, Glass cannon, Tank, Chilling presence, Cold bullets, Careful planning, Combine, Barrage, Pristine perseverance, Refresh, Empower, Huge, Quick reload, Quick shot, Sneaky, Parry, Bouncy...). Variations of these are rejected.
5. **Implementable** with what we have (below), multiplayer-safe.
6. Works in 1v1 (primary) and FFA.

Already in the set (don't duplicate): SWITCHEROO, METEOR, ROCKET JUMP (shots push you away from aim, shoot floor to fly), BALLOON ROUNDS (hits pop enemy up and make them float briefly), BOOMERANG (bullets curve back to you; catch for ammo).

## Technical facts (verified from game IL)
- Player velocity: private `PlayerVelocity.velocity` (Vector2), access via reflection helper `Fx.GetVel/SetVel(data, v)`. Simulated ONLY by the owning client (gate with `data.view.IsMine`); position syncs automatically.
- One jump = velocity change of `upForce*0.01*stats.jump` (≈ `Fx.JumpVel(player)`). PlayerVelocity applies a constant -20/s² pull; `Gravity` adds force ∝ `sinceGrounded^exponent` (resetting `data.sinceGrounded=0` cancels that part).
- Teleport own player: `Fx.Teleport(player, pos)` (owner only).
- Damage: `healthHandler.CallTakeDamage(dirVec*dmg, pos, null, attacker, lethal)` — broadcasts; call from exactly ONE client (attacker owner). `HealthHandler.CallTakeForce(force, ForceMode2D, ignoreMass, ignoreBlock, setFlying)` broadcasts knockback.
- `Block.CallDoBlock(false,true,BlockTrigger.BlockTriggerType.Default,Vector3.zero,true)` = networked "free block effects" (no shield).
- `Block.ResetCD(true)` recharges block (owner).
- Every bullet: Harmony postfix on `Gun.BulletInit(GameObject bullet, ...)` runs on every client; bullet has MoveTransform (public `velocity`), ProjectileHit (`damage`, `unblockable`, `ownPlayer`), RayHitReflect if bounces (`reflectAction`), SpawnedAttack (`SetColor`). Per-bullet logic must be deterministic from bullet age/velocity.
- Every damage: Harmony on `HealthHandler.DoDamage` runs on all clients.
- Events: Block.BlockAction, CharacterStatModifiers.OnReloadDoneAction / OutOfAmmpAction, HealthHandler.reviveAction, PlayerManager.AddPlayerDiedAction, CharacterData.isWallGrab/isGrounded/wallNormal, data.input.direction / aimDirection, data.lastSourceOfDamage.
- Spawning vanilla effects: `Resources.Load("0 cards/Explosive bullet")` etc. gives card prefabs whose Gun.objectsToSpawn / Block objects can be reused (visual + effect).
- A custom networked event needs UnboundLib `[UnboundRPC]` static + `NetworkingManager.RPC(...)` (pass playerIDs) — allowed if needed.
- Tools: API dumps api_game.txt / api_unbound.txt, STATS.md, and an IL dumper: `scratchpad\ildump\ILDump.exe "<Managed dir>" Type:Method` to read real game code.
- scratchpad = <scratch> ; Managed dir = C:\Program Files (x86)\Steam\steamapps\common\ROUNDS\ROUNDS_Data\Managed
