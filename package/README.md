# WildCards

10 ROUNDS cards, each built around a new action rather than a stat change. Everyone in the lobby needs the same version.

| Card | What it does |
|---|---|
| PORTAL ROUNDS | Your bullets open an orange and a blue portal where they hit walls. Players and bullets travel through them, enemies included. |
| GRAPPLE | Jump in mid-air with no jumps left to fire a hook. Hold jump to swing and reel in, let go to fling. Hooking a player yanks them toward you. |
| REWIND | A ghost of you trails 2s behind, visible to everyone. Blocking snaps you back to it. |
| HOT POTATO | Your hit plants a 5s bomb. The holder passes it by shooting or touching someone. It explodes on whoever holds it at the end. |
| WRAPAROUND | You and your bullets wrap around the screen edges. Fall off the bottom and you drop in from the top. |
| SWITCHEROO | Blocking swaps places with whoever hurt you in the last 5s. |
| METEOR | Hold down in the air to dive. Landing sends out a shockwave that damages and launches nearby enemies. |
| ROCKET JUMP | Shooting pushes you away from your aim. Shoot the floor to fly. |
| BALLOON ROUNDS | Hits pop enemies upward and make them float briefly. |
| BOOMERANG | Bullets curve back to you. Catch them to get the ammo back. |

## Testing
`BepInEx/config/com.wildcards.rounds.cards.cfg` → `WildCardChance` (default 0.4) is the chance that each offered card is replaced by a WildCards card. Set it to 0 for normal odds.
