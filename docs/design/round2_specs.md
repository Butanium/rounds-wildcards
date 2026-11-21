# Round 2 — FINAL picks (lead designer)

New set after this round: SWITCHEROO, METEOR, ROCKET JUMP, BALLOON ROUNDS, BOOMERANG + **PORTAL ROUNDS, GRAPPLE, REWIND, HOT POTATO, WRAPAROUND**.

I picked 5 cards, not 6. A sixth candidate, LEASH (#12), is good, but it repeats GRAPPLE's "rope that yanks the enemy" fantasy and would be a third block-trigger card next to SWITCHEROO and REWIND. A card that feels like a duplicate is weaker than a set of 5.

Variety check:
| Slot | Card | New verb / event |
|---|---|---|
| Bullets/world | PORTAL ROUNDS | Shoot portals; players and bullets travel through |
| Movement | GRAPPLE | Swing and reel on a rope, or hook an enemy |
| Block | REWIND | Snap back to your public 2s ghost |
| Interaction | HOT POTATO | Gun-tag with a ticking bomb |
| World topology | WRAPAROUND | Pac-Man edges for you and your bullets |

Trigger budget: block is used by SWITCHEROO and REWIND, and both stack (see REWIND). Jump-in-air with no jumps left is GRAPPLE only. Hold down is METEOR only. Nothing else claims an input.

Codebase facts the specs rely on, checked this round in `api_game.txt`, the IL and `src/Common.cs`:
- `Fx.Teleport` **zeroes velocity**, so always call `Fx.SetVel` AFTER `Fx.Teleport` when momentum must be kept.
- `ProjectileHit.RPCA_DoHit(Vector2 hitPoint, Vector2 hitNormal, Vector2 vel, int viewID, int colliderID, bool wasBlocked)`. In the IL, `viewID != -1` means a PhotonView target (a player or a networked prop). `viewID == -1 && colliderID != -1` means a static map collider, indexed from `currentMap.Map.GetComponentsInChildren<Collider2D>()`. So **never parent new colliders under the Map** (that shifts the indices). None of the picks below spawns a collider.
- `RayCastTrail.lastPos` (public Vector3). `RayCastTrail.Update` raycasts from `lastPos` to the current position. **Any bullet teleport must set `lastPos = newPos`**, or the bullet raycasts across the whole map.
- `OutOfBoundsHandler.LateUpdate` uses fixed world bounds: x in [-35.56, 35.56], y in [-20, 20] (InverseLerp, then clamp). They are constants, so they are the same on every map.
- Members that exist: `PlayerJump.JumpAction` (Action), `PlayerJump.Jump(bool forceJump, float multiplier)`, `GeneralInput.jumpWasPressed/jumpIsPressed`, `CharacterData.currentJumps/sinceGrounded/groundMask/hand/aimDirection`, `MoveTransform.simulationSpeed`, `HealthHandler.CallTakeForce(Vector2 force, ForceMode2D mode, bool forceIgnoreMass, bool ignoreBlock, float setFlying)`, `HealthHandler.CallTakeDamage(Vector2, Vector2, GameObject, Player, bool)`, `HealthHandler.DoDamage(Vector2 damage, Vector2 position, Color blinkColor, GameObject damagingWeapon, Player damagingPlayer, bool healthRemoval, bool lethal, bool ignoreBlock)`, `HealthHandler.reviveAction`, `PlayerManager.GetPlayerWithID(int)`, `Player.playerID/teamID`, `Block.BlockAction` (Action<BlockTrigger.BlockTriggerType>; the existing pattern is in CombatCards.cs:123).
- Round reset: there is already a `GameModeHooks.HookPointStart` hook in `Common.cs` (`WildState.OnPointStart`). Each card below registers a static `ClearAll()` there.
- Networking: `[UnboundRPC]` static methods called through `NetworkingManager.RPC(typeof(X), nameof(M), args...)`. Pass playerIDs, never objects.

---

## 1. PORTAL ROUNDS
- **Description:** "Bullets open linked portals"
- **Rarity:** Rare
- **Theme:** MagicPink
- **Stats:** `+ Portals: Shoot walls` / `- Reload time: +0.5s`

### Mechanic
- **Placement:** only your own bullets that hit **static map geometry** place portals (RPCA_DoHit with `viewID == -1 && colliderID != -1`). Only the first surface hit of each bullet counts. Placement has a 0.75s cooldown per owner, so Spray, Buckshot and Burst place at most 1 portal per volley.
- Placements alternate between slot A (orange) and slot B (blue). A new placement replaces the older portal in that slot.
- **Portal shape:** a disc of radius 1.0, centred at `hitPoint + hitNormal*0.5`, storing `normal = hitNormal`.
- Portals do nothing until both A and B exist. While only one exists, it is drawn at 40% alpha.
- **Players (any player, including enemies):** if a player's centre is within 0.9 of portal P, they exit at `Q.pos + Q.normal*1.3`. Their velocity is rotated by the signed angle from `-P.normal` to `Q.normal` (`Quaternion.FromToRotation`). Speed is kept, with a minimum of `0.6*JumpVel` so walking in carries you out. `sinceGrounded = 0`. Each player then has a 0.6s portal cooldown.
- **Bullets (any owner, so enemy bullets can use your portals too):** a bullet within the radius gets the same exit point and the same rotation of `MoveTransform.velocity`. Each bullet has a 0.3s portal cooldown and can teleport at most 3 times.
- Portals last until replaced. All portals are cleared at point start and when the owner dies.
- **Stacking:** 2 copies make the radius 1.3 (player trigger 1.2). More copies do nothing extra.
- **Visual tell:** two glowing rings with a swirling particle centre, tinted orange and blue with a thin outline in the owner's colour. Each teleport plays a short particle pop at both rings.

### Implementation
1. **State:** `static Dictionary<int ownerID, PortalPair>` holds `PortalPair { Vector2 posA, nA, posB, nB; bool hasA, hasB; int nextSlot; GameObject visA, visB; }`.
2. **Placement:** Harmony postfix on `ProjectileHit.RPCA_DoHit`. Do work only if `__instance.ownPlayer != null && Fx.IsMine(__instance.ownPlayer) && the owner has PortalEffect && viewID == -1 && colliderID != -1`.
   - Mark the bullet with a `PortalBulletTag` component so later hits of the same bullet are ignored.
   - Check the 0.75s cooldown, then call `NetworkingManager.RPC(typeof(PortalNet), nameof(PortalNet.Place), ownerID, slot, (Vector2)pos, (Vector2)normal)`.
   - Every client, including the sender, stores the portal and moves the visual. The owner is the single authority, so the slot order is identical everywhere.
3. **Players:** a `PortalTraveler` MonoBehaviour added to every player (on all clients) runs **only when `data.view.IsMine`**. Each Update it checks all portal pairs for an overlap. On overlap it calls `Fx.Teleport(player, exit)`, then `Fx.SetVel(data, rotatedVel)` (Teleport zeroes velocity, so SetVel must come after), and sets `data.sinceGrounded = 0`. Position sync shows the move to everyone. Also send an `[UnboundRPC] Pop(ownerID, slot)` for the particle effect (cosmetic).
4. **Bullets:** in the `Gun.BulletInit` postfix (all clients), add a `PortalBullet` component to every bullet while any portal pair is active. Its Update checks overlap on the local copy.
   - On overlap: set `transform.position = exit`, `move.velocity = rot*move.velocity`, and **`GetComponent<RayCastTrail>().lastPos = exit`**, and call `TrailRenderer.Clear()` on any child trail.
   - Bullets run the same deterministic sim on every client, and hits are decided by the shooter's copy (`ProjectileHit.Hit` gates its RPC on `view.IsMine`), so the shooter's version is the one that counts.
   - `MoveTransform` may cache a distance or position for gravity. If bullets jitter after a teleport, also reset any `distanceTravelled`-type field (check `MoveTransform` in api_game.txt during the build).
5. **Exit-inside-wall safety:** before a player teleport, run `Physics2D.OverlapCircle(exit, 0.5f, data.groundMask)`. If it hits, push the exit along the normal up to +1.5, and skip the teleport if it is still blocked.
6. **Cleanup:** `ClearAll()` on HookPointStart. `PlayerManager.AddPlayerDiedAction` clears that owner's pair.
- Estimate: about 2h. This is the headline card, and the cost is worth it.

---

## 2. GRAPPLE
- **Description:** "Jump in mid-air fires a hook"
- **Rarity:** Rare
- **Theme:** TechWhite
- **Stats:** `+ Grappling hook: 14 range` / `- Hook cooldown: 2.5s`
  - The cost is the cooldown. The pitched -15% movement speed is dropped: a stat tax on a movement card works against the fantasy.

### Mechanic
- **Trigger:** press JUMP while airborne with no jumps left (`!data.isGrounded && data.currentJumps <= 0 && input.jumpWasPressed`). Normal jumps are never touched, and a ground jump never fires the hook.
- **Hook:** a visible hook head travels along `aimDirection` at 90 u/s, up to 14 units (+6 per extra copy).
- **Terrain hit (`data.groundMask`):** the hook anchors and `ropeLen = current distance`. While JUMP is **held**:
  - Rope constraint: if `dist > ropeLen`, remove the outward radial velocity, which makes a pendulum swing.
  - `ropeLen` reels in at 12 u/s, down to a minimum of 1.5.
  - `data.sinceGrounded = 0` every frame, so the Gravity ramp never builds.
  - **Release** JUMP to detach with full momentum (swing-fling). The rope also breaks after 3.0s.
- **Player hit (any other player):** a one-time yank toward the hooker with `CallTakeForce(dir * 2.2*JumpVel, ForceMode2D.Impulse, forceIgnoreMass:true, ignoreBlock:false, setFlying:0.6f)`. It deals 0 damage. If the target is blocking, the yank is cancelled by vanilla `ignoreBlock=false` handling, and the rope visibly snaps.
- **Miss:** the hook retracts over 0.3s.
- **Cooldown:** 2.5s after release, miss or yank (-0.5s per extra copy, minimum 1.0s). On landing, jumps refill as usual.
- **Stacking at 2 copies:** range 20, cooldown 2.0s.
- **Combos:** METEOR (swing high, release, hold down, dive-slam), SWITCHEROO, Buckshot ("GET OVER HERE" into point-blank), WRAPAROUND, PORTAL ROUNDS.
- **Visual tell:** a white LineRenderer from `data.hand` to the hook head or anchor, a small metal hook sprite, a clink spark when it anchors, and a slack-to-taut colour flash on a yank.

### Implementation
1. `GrappleEffect : StackEffect` on the player. Its logic runs **only if `data.view.IsMine`**: read input, simulate the hook head with a per-frame `Physics2D.Raycast(head, dir, step, data.groundMask | playerLayerMask)`, and use `GetComponentInParent<Player>()` to detect players, ignoring self.
2. Swing runs in FixedUpdate (owner): `v = Fx.GetVel(data); d = anchor - pos; if (|d| > ropeLen) { n = d.normalized; float out = Vector2.Dot(v, -n); if (out > 0) v += n*out; v += n*(|d|-ropeLen)*20*dt; } Fx.SetVel(data, v); data.sinceGrounded = 0;`
3. Yank: `target.data.healthHandler.CallTakeForce(...)` is called once from the hooker's client. It broadcasts and respects the target's block.
4. Visuals on all clients: `[UnboundRPC] RopeState(int playerID, int state /*0 fire,1 anchor,2 yank,3 release*/, Vector2 point, int targetID)`. Each client draws the LineRenderer from that player's `data.hand` to the anchor point or the target's transform (both are synced), and animates the head locally from the fire time. Run a local 3s safety timeout on every client.
5. Optional: suppress the vanilla failed-jump effects. None exist at 0 jumps (`PlayerJump.Jump` early-outs), so no patch is needed. Verify during the build.
6. Cleanup on revive, death and point start.
- Estimate: about 2h. Cut line-of-sight breaking from the pitch to keep the scope down.

---

## 3. REWIND
- **Description:** "Blocking rewinds you 2 seconds"
- **Rarity:** Uncommon
- **Theme:** ColdBlue
- **Stats:** `+ Rewind: 2s` / `- Block cooldown: +0.75s`
- Merged from #14 (owner buffer, cooldown) and #2 (public ghost that is always shown). #19's heal is rejected because it is a stat button.

### Mechanic
- A translucent ghost of you (40% alpha disc in your colour, with a faint 0.5s trail) always follows **2.0s behind** you, visible to everyone.
- **On block:** you teleport to the ghost's position and take the velocity you had then, with `sinceGrounded = 0`. Health is **not** changed. The normal shield still happens at the departure point.
- Internal rewind cooldown is 4s. During it the ghost turns grey, and it lights back up when ready. A block during the cooldown is a normal block.
- After a rewind the history is cleared, and the ghost re-grows from your new position over 2s.
- **Stacking at 2 copies:** rewind 3.0s, cooldown 3s. More copies do nothing extra.
- **With SWITCHEROO:** SWITCHEROO's swap resolves first, then REWIND takes you back to your own past, so the enemy ends up where you stood and you end up at your ghost. This is intended chaos. To keep that order, subscribe REWIND to `BlockAction` after SWITCHEROO, so its handler runs later in the delegate chain. If both handlers are owner-side teleports in the same frame, the last write wins, so REWIND always resolves last.
- **With METEOR:** rewinding mid-dive cancels the dive. This is fine.
- **Visual tell:** a cyan VHS-style streak from the departure point to the arrival point, plus a rewind "zzzip" particle burst at both ends.

### Implementation
1. **Owner (`view.IsMine`):** a ring buffer of `(Vector2 pos, Vector2 vel)` written every FixedUpdate, 3.0s deep (150 entries at 50Hz).
2. **Block:** subscribe to `data.block.BlockAction` (pattern in CombatCards.cs:123). If IsMine and off cooldown and the buffer holds at least 2.0s of history:
   - `Fx.Teleport(player, old.pos)`, then `Fx.SetVel(data, old.vel)`, then `data.sinceGrounded = 0`.
   - Clear the buffer and start the cooldown.
   - Send `[UnboundRPC] RewindFx(int playerID, Vector2 from, Vector2 to)` so every client plays the streak and greys out the ghost for the cooldown.
3. **Ghost (all clients):** a `RewindGhost` component buffers the holder's synced `transform.position` at 20Hz and draws the ghost sprite at `now - 2s`. It is cosmetic only, so a few ms of offset between clients is irrelevant.
   - On RewindFx, clear the local buffer as well, so the ghost re-grows from the new position.
   - Use a tinted disc, not a copy of the particle skin.
4. Clear the buffers on `reviveAction` and point start (no rewinding into the previous round).
- Estimate: about 1h.

---

## 4. HOT POTATO
- **Description:** "Hits pass a ticking bomb"
- **Rarity:** Uncommon
- **Theme:** DestructiveRed
- **Stats:** `+ Bomb: 5s fuse` / `- Attack speed: -15%` / `- The bomb can come back`

### Mechanic
- **Planting:** your first bullet hit on an enemy in a point plants **your** bomb on them. Each card owner has 1 bomb, and its fuse is a fixed **5.0s that never resets**.
- **Passing:** the holder passes the bomb by dealing any bullet damage to another player, or by touching one (centres within 1.3 units). There is a 0.6s lockout after each pass, so it can't bounce straight back.
  - If the bomb reaches the owner, the owner has to pass it too.
  - In FFA it can be passed to anyone.
- **Fuse end:** the bomb explodes on the holder.
  - The holder takes damage equal to 45% of their max HP, plus a big knockback away from the blast (2x JumpVel, `setFlying 0.5`).
  - Others within radius 4 take half of that, and this includes the owner.
  - The damage is credited to the owner.
- **Re-arm:** 4s after an explosion, your next hit plants a new bomb. If the holder dies, the bomb is removed and re-arms in 4s.
- **Stacking at 2 copies:** the explosion deals 60% max HP and has radius 5. More copies do nothing extra.
- **Visual tell:** a bomb sprite over the holder's head with fuse sparks, and a big number countdown. In the last 1.5s it flashes and beeps faster. Each pass leaves a whoosh arc from the old holder to the new holder. The explosion reuses the vanilla Explosive bullet effect.

### Implementation
1. The **owner's client** is the only authority for `{holderID, fuseEnd, lockoutUntil, rearmAt}`.
2. **Hit pass and plant:** Harmony postfix on `HealthHandler.DoDamage` (it runs on all clients, so act only on the owner's client).
   - `target = __instance.GetComponent<Player>()`, `src = damagingPlayer`.
   - Plant if there is no holder, `src == owner`, `target != owner`, and the time is past `rearmAt`.
   - Pass if `src == holder`, `target != holder`, and the time is past `lockoutUntil`.
   - **Ignore damage while a static `HotPotato.exploding` flag is set**, so the bomb's own blast never counts as a pass.
3. **Touch pass:** in the owner's Update, compare the synced `transform.position` of the holder against every other living player, and pass when the distance is under 1.3.
4. Each change is sent as `[UnboundRPC] SetHolder(int ownerID, int holderID, float fuseRemaining)`. Every client parents the bomb visual to the holder and runs the countdown locally.
5. **Detonation (owner's client only):**
   - Set `exploding = true`.
   - Call `holder.data.healthHandler.CallTakeDamage(dir*dmg, pos, null, owner, true)` and `CallTakeForce(...)` for the holder, plus half of both for each player within the radius.
   - Set `exploding = false`.
   - Send `[UnboundRPC] Boom(Vector2 pos)`. Every client spawns the visual from `Resources.Load<GameObject>("0 cards/Explosive bullet")`'s `Gun.objectsToSpawn[0].effect`, with any damage component disabled or removed before activation, or uses a plain particle burst if that proves fiddly.
6. Clear everything on HookPointStart, owner death, and `AddPlayerDiedAction` of the holder.
- Estimate: about 1.5h. Most of it is the countdown UI.

---

## 5. WRAPAROUND
- **Description:** "Screen edges wrap around"
- **Rarity:** Uncommon
- **Theme:** EvilPurple
- **Stats:** `+ You and your bullets wrap` / `- Bullet speed: -20%`

### Mechanic
- The arena bounds are the out-of-bounds constants: x in [-35.56, 35.56], y in [-20, 20].
- **You:**
  - Crossing `x > 34.8` teleports you to `x = -34.3` with the same y, and the reverse on the other side.
  - Crossing `y < -19.3` teleports you to `y = 19.0` with the same x, so you fall in from the sky. The top edge works the same way, from `y > 19.3` to `y = -18.8`.
  - Velocity is kept, and `sinceGrounded` is not reset, so a long fall keeps its speed and lands hard (good with METEOR).
  - Safety: if `Physics2D.OverlapCircle(dest, 0.7, groundMask)` hits terrain, the wrap is skipped and vanilla out-of-bounds handling applies, so a sealed edge can't trap you inside a wall.
  - Per-player wrap cooldown: 0.4s.
- **Your bullets:** they wrap the same way, once per bullet (twice with 2 copies). The -20% bullet speed keeps a wrapped bullet dodgeable.
- **Stacking at 2 copies:** bullets wrap twice. More copies do nothing extra.
- **Visual tell:** a purple ring flashes for 0.3s at the exit edge, then a matching ring at the arrival point. A wrapped bullet gets a brief purple flare.
- **Counterplay:** the arrival point is a deterministic mirror and it is marked, so the opponent can camp the opposite edge. The card does nothing on maps whose edges are sealed by terrain, which is an accepted limitation.

### Implementation
1. **Player:** a `WrapEffect` with an owner-only (`data.view.IsMine`) **Update** (Update runs before `OutOfBoundsHandler.LateUpdate`, so no out-of-bounds damage is ever applied).
   - Compute the mirrored position and run the overlap check.
   - `v = Fx.GetVel(data); Fx.Teleport(player, dest); Fx.SetVel(data, v);`
   - Send `[UnboundRPC] WrapFx(int playerID, Vector2 from, Vector2 to)` for the rings.
2. **Bullets:** in the `Gun.BulletInit` postfix (all clients), if the bullet's owner has WrapEffect, add a `WrapBullet` component with `wrapsLeft = stacks`.
   - Its Update mirrors the position by the same constants (margin 0.3 inside the bounds).
   - On a wrap it also sets **`RayCastTrail.lastPos = newPos`** and calls `Clear()` on every child `TrailRenderer`.
   - This is deterministic on every client. Rings for bullets are spawned locally, with no RPC.
3. Verify during the build that bullets are not destroyed at the bounds before the wrap. `RemoveAfterSeconds` is time-based, so this should be fine, but check that no bounds-kill exists in `MoveTransform` or `ProjectileHit`.
- Estimate: about 1h.

---

## Rejected, worth keeping for later
- **LEASH (#12):** a two-way rope created on hit, where either player blocks to yank. It's the strongest reserve card; held back only because it overlaps GRAPPLE's yank and adds a third block trigger.
- **BOUNCE PADS (#13):** neutral spring pads where your bullets land. Very feasible (9), but it sits in the same "launch people" space as ROCKET JUMP and BALLOON. Revisit if one of those is cut.
- **SKYWALK (#9):** air jumps leave temporary platforms. A good builder verb, but spawned colliders risk the RPCA_DoHit colliderID indexing. Revisit with platforms parented outside the Map and hit-tested by a custom layer.
- **GRAVITY FLIP, arena-wide (#17):** blocking flips gravity for everyone for 2.5s. A big table-yell event, but per-client bullet gravity drift and top-edge out-of-bounds behaviour are unverified. A possible players-only version later.
- **BULLET TIME (#3):** blocking freezes nearby bullets and throws them back. The best whoa moment in the list, but vetoed: the shooter's client decides hits, so reassigning ownership desyncs. Would need a "freeze only your own bullets" redesign.
- **RODEO (#6):** ride enemies by landing on their heads. Comedy gold, but vetoed: pinning the rider to a lagged synced position jitters online.
- **SCAFFOLD (#18):** blocking turns your bullets in flight into planks. Same collider-index risk as SKYWALK; keep only one builder if revisited.
- **GRAPPLE on block (#1):** superseded by the jump-triggered #5.
- **REWIND with heal (#19) and #2:** merged into the final REWIND.
- **HOT POTATO (#11):** superseded by #15's tuning.
- **TETHER (#4) and chained-at-start LEASH (#16):** weaker versions of the chain idea. #16 is a forced map modifier.
- **BULLET RIDE (#0):** vetoed as a Teleport-like warp that overlaps SWITCHEROO.
- **Personal GRAVITY FLIP (#7):** vetoed for few usable ceilings, a fragile ground hack, and a clash with METEOR's down input.
