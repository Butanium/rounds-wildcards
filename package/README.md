# WildCards

10 ROUNDS cards, each built around a new action rather than a stat change. Everyone in the lobby needs the same version.

| Card | What it does |
|---|---|
| PORTAL ROUNDS | Your bullets open an orange and a blue portal where they hit walls. Players and bullets travel through them, enemies included. |
| GRAPPLE | Jump in mid-air with no jumps left to fire a hook. Hold jump to swing and reel in, let go to fling. Hooking a player yanks them toward you. |
| REWIND | A ghost of you trails 2s behind, visible to everyone. Blocking snaps you back to it. |
| HOT POTATO | Your hit plants a 5s bomb. The holder passes it by shooting or touching someone. It explodes on whoever holds it at the end. |
| WRAPAROUND | You wrap around the left and right screen edges; your bullets wrap around every edge (before bouncing). Falling off the bottom still kills. |
| SWITCHEROO | Blocking links you to whoever hurt you in the last 5s. 0.6s later you swap places, unless they are blocking at that moment. |
| METEOR | Hold down in the air to dive. Landing sends out a shockwave that damages and launches nearby enemies. |
| ROCKET JUMP | Shooting pushes you away from your aim. Shoot the floor to fly. |
| BALLOON ROUNDS | Hits pop enemies high into the air and make them float upward for 1.3s. |
| BOOMERANG | Bullets turn around at walls (or after a moment) and fly back to you through terrain, +30% damage. Catch them to get the ammo back. |

## Testing
`BepInEx/config/com.wildcards.rounds.cards.cfg` → `WildCardChance` (default 0.1) is the chance that each offered card is replaced by a WildCards card. Set it to 0 for normal odds.
