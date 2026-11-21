using System;
using System.Collections.Generic;
using UnboundLib;
using UnboundLib.Networking;
using UnityEngine;
using WildCards;

namespace WildCards.Grapple
{
    // ---------------------------------------------------------------- GRAPPLE
    // Press JUMP in mid-air with no jumps left: fire a hook along the aim.
    // Terrain: anchor, swing on a rope while JUMP is held (reels in), release to fling.
    // Player: one-time yank toward you (respects block).
    // The owner simulates everything; visuals are driven on every client by one RPC.
    public class GrappleCard : WildCard
    {
        static bool inited;

        public static void Init()
        {
            if (inited) return;
            inited = true;
            Hooks.PointStart += GrappleNet.ClearAll;
            Hooks.GameStart += GrappleNet.ClearAll;
        }

        public override void SetupCard(CardInfo cardInfo, Gun gun, ApplyCardStats cardStats, CharacterStatModifiers statModifiers, Block block)
        {
        }
        public override void OnAddCard(Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health, Gravity gravity, Block block, CharacterStatModifiers characterStats)
        { Fx.Add<GrappleEffect>(player, gun, gunAmmo); }
        public override void OnRemoveCard(Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health, Gravity gravity, Block block, CharacterStatModifiers characterStats)
        { Fx.Remove<GrappleEffect>(player); }
        protected override string GetTitle() { return "GRAPPLE"; }
        protected override string GetDescription() { return "Jump in mid-air fires a hook"; }
        protected override CardInfo.Rarity GetRarity() { return CardInfo.Rarity.Rare; }
        protected override CardThemeColor.CardThemeColorType GetTheme() { return CardThemeColor.CardThemeColorType.TechWhite; }
        protected override CardInfoStat[] GetStats()
        {
            return new CardInfoStat[] {
                Stat(true, "Grappling hook", "14 range"),
                Stat(false, "Hook cooldown", "2.5s")
            };
        }
    }

    public static class GrappleNet
    {
        public const int Fire = 0, Anchor = 1, Yank = 2, Release = 3, Miss = 4, Snap = 5;

        public static Player FindPlayer(int id)
        {
            if (PlayerManager.instance == null) return null;
            List<Player> ps = PlayerManager.instance.players;
            for (int i = 0; i < ps.Count; i++)
                if (ps[i] != null && ps[i].playerID == id) return ps[i];
            return null;
        }

        /// Owner -> everyone (including the owner). a/b meaning depends on state:
        /// Fire: a = origin, b = direction (range in b's magnitude). Others: a = point.
        public static void Send(Player p, int state, Vector2 a, Vector2 b, int targetID)
        {
            try
            {
                NetworkingManager.RPC(typeof(GrappleNet), "RPCA_GrappleState", new object[] { p.playerID, state, a, b, targetID });
            }
            catch (Exception e)
            {
                Debug.LogError("[WildCards] Grapple RPC " + e);
                RPCA_GrappleState(p.playerID, state, a, b, targetID); // at least show it locally
            }
        }

        [UnboundRPC]
        public static void RPCA_GrappleState(int playerID, int state, Vector2 a, Vector2 b, int targetID)
        {
            try
            {
                Player p = FindPlayer(playerID);
                if (p == null) return;
                GrappleVisual v = p.GetComponent<GrappleVisual>();
                if (v == null) { v = p.gameObject.AddComponent<GrappleVisual>(); v.player = p; }
                v.OnState(state, a, b, FindPlayer(targetID));
            }
            catch (Exception e) { Debug.LogError("[WildCards] Grapple " + e); }
        }

        public static void ClearAll()
        {
            if (PlayerManager.instance == null) return;
            List<Player> ps = PlayerManager.instance.players;
            for (int i = 0; i < ps.Count; i++)
            {
                if (ps[i] == null) continue;
                GrappleEffect e = ps[i].GetComponent<GrappleEffect>();
                if (e != null) e.ResetState(false);
                GrappleVisual v = ps[i].GetComponent<GrappleVisual>();
                if (v != null) v.Hide();
            }
        }

        static Material mat;
        public static Material Mat
        {
            get
            {
                if (mat == null) mat = new Material(Shader.Find("Sprites/Default"));
                return mat;
            }
        }

        public static LineRenderer MakeLine(Transform parent, string name, float width, Color c, int order)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.material = Mat;
            lr.useWorldSpace = true;
            lr.startWidth = width;
            lr.endWidth = width;
            lr.startColor = c;
            lr.endColor = c;
            lr.sortingOrder = order;
            lr.sortingLayerName = "MostFront";
            lr.numCapVertices = 2;
            lr.positionCount = 2;
            return lr;
        }
    }

    /// Owner-side logic (input, hook flight, swing, yank). Exists on every client, acts only on the owner.
    public class GrappleEffect : StackEffect
    {
        const float HookSpeed = 90f;
        const float ReelSpeed = 12f;
        const float MinRope = 1.5f;
        const float MaxAttach = 3.0f;

        enum St { Idle, Flying, Anchored }
        St state = St.Idle;

        Vector2 origin, dir, head, anchor;
        float flown, ropeLen, attachTime, cooldown;

        float Range { get { return 14f + 6f * (stacks - 1); } }
        float Cooldown { get { return Mathf.Max(1f, 2.5f - 0.5f * (stacks - 1)); } }

        Vector2 Hand
        {
            get
            {
                if (data != null && data.hand != null) return data.hand.position;
                return player.transform.position;
            }
        }

        /// 0 = just used, 1 = ready. Owner-side only.
        float cdTotal = 1f;
        public float Readiness { get { return cooldown <= 0f ? 1f : 1f - cooldown / cdTotal; } }
        /// Only real cooldowns (not the short miss lockout) show the ring.
        public bool OnCooldown { get { return cooldown > 0f && cdTotal > 0.5f; } }

        public override void Init()
        {
            if (IsMine && GetComponent<GrappleCooldownUI>() == null)
                gameObject.AddComponent<GrappleCooldownUI>().effect = this;
        }

        public void ResetState(bool startCooldown)
        {
            state = St.Idle;
            flown = 0f;
            cooldown = startCooldown ? Cooldown : 0f;
            cdTotal = startCooldown ? Cooldown : 1f;
        }

        // OnDisable always runs before OnDestroy (Fx.Remove uses DestroyImmediate), so the release must be sent here.
        // When the whole player is disabled (death) every client hides its own visual, so no RPC is needed then.
        void OnDisable()
        {
            if (state != St.Idle && IsMine && player != null && player.gameObject.activeInHierarchy)
                GrappleNet.Send(player, GrappleNet.Release, Hand, Vector2.zero, -1);
            ResetState(false);
        }
        void OnEnable() { ResetState(false); }

        void Update()
        {
            if (!IsMine || player == null) return;
            if (cooldown > 0f) cooldown -= Time.deltaTime;
            if (!Alive)
            {
                if (state != St.Idle) { GrappleNet.Send(player, GrappleNet.Release, Hand, Vector2.zero, -1); ResetState(false); }
                return;
            }

            if (state == St.Idle)
            {
                // Only when the vanilla jump would do nothing: airborne, no jumps, no wall-jump window,
                // and no jump happened this frame (PlayerJump may have run first and used the last jump).
                if (data.input != null && data.input.jumpWasPressed && cooldown <= 0f
                    && !data.isGrounded && data.currentJumps <= 0
                    && data.sinceWallGrab >= 0.1f && data.sinceJump >= 0.1f)
                {
                    Vector2 aim = data.input.aimDirection;
                    if (aim.sqrMagnitude < 0.01f) aim = data.aimDirection;
                    if (aim.sqrMagnitude < 0.01f) aim = Vector2.up;
                    dir = aim.normalized;
                    origin = Hand;
                    head = origin;
                    flown = 0f;
                    state = St.Flying;
                    GrappleNet.Send(player, GrappleNet.Fire, origin, dir * Range, -1);
                }
            }
            else if (state == St.Flying)
            {
                StepHook(Time.deltaTime);
            }
            else if (state == St.Anchored)
            {
                attachTime += Time.deltaTime;
                bool held = data.input != null && data.input.jumpIsPressed;
                if (!held || attachTime >= MaxAttach)
                {
                    GrappleNet.Send(player, GrappleNet.Release, anchor, Vector2.zero, -1);
                    ResetState(true);
                }
            }
        }

        void StepHook(float dt)
        {
            float step = Mathf.Min(HookSpeed * dt, Range - flown);
            if (step <= 0f) { Missed(); return; }

            RaycastHit2D[] hits = Physics2D.RaycastAll(head, dir, step);
            float best = float.MaxValue;
            int bestKind = 0; // 1 terrain, 2 player
            Vector2 bestPoint = Vector2.zero;
            Player bestPlayer = null;
            int ground = GroundMask(data);
            for (int i = 0; i < hits.Length; i++)
            {
                Collider2D c = hits[i].collider;
                if (c == null || hits[i].distance >= best) continue;
                Player hp = c.GetComponentInParent<Player>();
                if (hp != null)
                {
                    if (hp == player || hp.data == null || hp.data.dead || !hp.data.isPlaying) continue;
                    best = hits[i].distance; bestKind = 2; bestPoint = hits[i].point; bestPlayer = hp;
                    continue;
                }
                if (c.isTrigger) continue;
                if (((1 << c.gameObject.layer) & ground) == 0) continue;
                best = hits[i].distance; bestKind = 1; bestPoint = hits[i].point; bestPlayer = null;
            }

            if (bestKind == 1)
            {
                anchor = bestPoint;
                ropeLen = Mathf.Max(MinRope, Vector2.Distance(player.transform.position, anchor));
                attachTime = 0f;
                // Already let go of JUMP? A tap still gives a clink and a short tug; otherwise hold to swing.
                state = St.Anchored;
                GrappleNet.Send(player, GrappleNet.Anchor, anchor, Vector2.zero, -1);
                return;
            }
            if (bestKind == 2)
            {
                YankTarget(bestPlayer, bestPoint);
                return;
            }

            head += dir * step;
            flown += step;
            if (flown >= Range - 0.001f) Missed();
        }

        // CharacterData.groundMask is not public: read it once through reflection.
        static readonly System.Reflection.FieldInfo groundField = HarmonyLib.AccessTools.Field(typeof(CharacterData), "groundMask");
        static int GroundMask(CharacterData d)
        {
            try
            {
                if (groundField != null)
                {
                    int m = ((LayerMask)groundField.GetValue(d)).value;
                    if (m != 0) return m;
                }
            }
            catch { }
            return ~0; // fallback: any solid non-player collider
        }

        void Missed()
        {
            GrappleNet.Send(player, GrappleNet.Miss, head, Vector2.zero, -1);
            // A miss only costs a short lockout (anti-spam), not the full cooldown.
            ResetState(false);
            cooldown = 0.3f;
            cdTotal = 0.3f;
        }

        void YankTarget(Player target, Vector2 point)
        {
            bool blocking = false;
            try { blocking = target.data.block != null && target.data.block.IsBlocking(); } catch { }
            if (blocking)
            {
                GrappleNet.Send(player, GrappleNet.Snap, point, Vector2.zero, target.playerID);
            }
            else
            {
                GrappleNet.Send(player, GrappleNet.Yank, point, Vector2.zero, target.playerID);
                Vector2 to = (Vector2)player.transform.position - (Vector2)target.transform.position;
                Vector2 d = to.sqrMagnitude > 0.0001f ? to.normalized : -dir;
                d = (d + Vector2.up * 0.35f).normalized;
                float dv = 2.2f * Fx.JumpVel(player);
                // Impulse + forceIgnoreMass: velocity change = force / 100 (HealthHandler.TakeForce IL).
                try { target.data.healthHandler.CallTakeForce(d * dv * 100f, ForceMode2D.Impulse, true, false, 0.6f); }
                catch (Exception e) { Debug.LogError("[WildCards] Grapple yank " + e); }
            }
            ResetState(true);
        }

        void FixedUpdate()
        {
            if (state != St.Anchored || !IsMine || !Alive) return;
            float dt = Time.fixedDeltaTime;
            ropeLen = Mathf.Max(MinRope, ropeLen - ReelSpeed * dt);

            Vector2 pos = player.transform.position;
            Vector2 v = Fx.GetVel(data);
            Vector2 d = anchor - pos;
            float dist = d.magnitude;
            if (dist > ropeLen && dist > 0.001f)
            {
                Vector2 n = d / dist;
                float outward = Vector2.Dot(v, -n);
                if (outward > 0f) v += n * outward;
                // Pull back onto the rope: guarantee an inward radial speed proportional to the excess
                // (a velocity floor, not an accumulating spring, so it never overshoots).
                float inward = Vector2.Dot(v, n);
                float want = Mathf.Min((dist - ropeLen) * 10f, 25f);
                if (inward < want) v += n * (want - inward);
            }
            Fx.SetVel(data, v);
            data.sinceGrounded = 0f;
        }
    }

    /// Per-client rope drawing, driven by GrappleNet RPCs. Lives on the player GameObject;
    /// the drawn objects live in a separate root so player scale/flip never affects them.
    /// Owner-only cooldown ring above the player's head: fills while the hook recharges, flashes when ready.
    public class GrappleCooldownUI : MonoBehaviour
    {
        public GrappleEffect effect;
        const int Segments = 28;
        const float Radius = 0.42f;
        LineRenderer back, fill;
        bool wasCooling;
        float flash;

        void Start()
        {
            GameObject root = new GameObject("WildCards_GrappleCooldown");
            root.transform.SetParent(transform, false);
            back = GrappleNet.MakeLine(root.transform, "cdBack", 0.09f, new Color(1f, 1f, 1f, 0.25f), 53);
            fill = GrappleNet.MakeLine(root.transform, "cdFill", 0.11f, Color.white, 54);
            back.loop = true;
            back.positionCount = Segments;
        }

        void LateUpdate()
        {
            if (back == null) return;
            bool alive = effect != null && effect.data != null && !effect.data.dead;
            bool cooling = alive && effect.OnCooldown;
            if (wasCooling && !cooling && alive) flash = 0.35f;   // just became ready
            wasCooling = cooling;
            if (flash > 0f) flash -= Time.deltaTime;

            bool show = cooling || flash > 0f;
            back.enabled = show;
            fill.enabled = show;
            if (!show) return;

            float s = Mathf.Max(transform.localScale.y, 0.5f);
            Vector3 c = transform.position + new Vector3(0f, 1.35f * s, 0f);
            c.z = 0f;
            float r = Radius * (flash > 0f ? 1f + (0.35f - flash) * 1.5f : 1f);

            for (int i = 0; i < Segments; i++)
            {
                float a = Mathf.PI * 0.5f - i * Mathf.PI * 2f / Segments;
                back.SetPosition(i, c + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * r);
            }

            float frac = flash > 0f ? 1f : effect.Readiness;
            int n = Mathf.Max(2, Mathf.CeilToInt(frac * Segments) + 1);
            fill.positionCount = n;
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.PI * 0.5f - (frac * Mathf.PI * 2f) * i / (n - 1);
                fill.SetPosition(i, c + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * r);
            }
            Color col = flash > 0f ? new Color(0.6f, 1f, 0.7f, flash / 0.35f) : new Color(1f, 1f, 1f, 0.9f);
            fill.startColor = col; fill.endColor = col;
        }

        void OnDestroy()
        {
            if (back != null) Destroy(back.transform.parent.gameObject);
        }
    }

    public class GrappleVisual : MonoBehaviour
    {
        public Player player;

        enum V { Hidden, Flying, Anchored, Yank, Retract }
        V mode = V.Hidden;

        GameObject root;
        LineRenderer ropeBack, rope, hook, ring;

        Vector2 origin, dir, head, retractFrom;
        float range, t, safety;
        Player target;
        Color flash = Color.white;
        float ringT = -1f;
        Vector2 ringPos;

        static readonly Color RopeCol = new Color(0.95f, 0.95f, 1f, 1f);
        static readonly Color BackCol = new Color(0.05f, 0.05f, 0.08f, 0.75f);
        static readonly Color HookCol = new Color(0.75f, 0.8f, 0.9f, 1f);

        void Build()
        {
            if (root != null) return;
            root = new GameObject("WildCards_GrappleRope");
            ropeBack = GrappleNet.MakeLine(root.transform, "ropeBack", 0.24f, BackCol, 49);
            rope = GrappleNet.MakeLine(root.transform, "rope", 0.12f, RopeCol, 50);
            hook = GrappleNet.MakeLine(root.transform, "hook", 0.14f, HookCol, 51);
            hook.positionCount = 5;
            ring = GrappleNet.MakeLine(root.transform, "ring", 0.1f, Color.white, 52);
            ring.positionCount = 25;
            ring.loop = true;
            ring.enabled = false;
        }

        Vector2 Hand
        {
            get
            {
                if (player != null && player.data != null && player.data.hand != null) return player.data.hand.position;
                return player != null ? (Vector2)player.transform.position : Vector2.zero;
            }
        }

        public void OnState(int state, Vector2 a, Vector2 b, Player tgt)
        {
            Build();
            root.SetActive(true);
            switch (state)
            {
                case GrappleNet.Fire:
                    origin = a; range = b.magnitude; dir = range > 0.001f ? b / range : Vector2.up;
                    head = origin; t = 0f; mode = V.Flying; flash = RopeCol; safety = 0f;
                    break;
                case GrappleNet.Anchor:
                    head = a; mode = V.Anchored; flash = RopeCol; safety = 0f;
                    Spark(a, new Color(1f, 0.9f, 0.5f, 1f));
                    break;
                case GrappleNet.Yank:
                    head = a; target = tgt; mode = V.Yank; t = 0f; flash = new Color(1f, 0.85f, 0.2f, 1f);
                    Spark(a, flash);
                    break;
                case GrappleNet.Snap:
                    head = a; StartRetract(); flash = new Color(1f, 0.25f, 0.25f, 1f);
                    Spark(a, flash);
                    break;
                case GrappleNet.Miss:
                case GrappleNet.Release:
                    head = a; StartRetract();
                    break;
            }
        }

        void StartRetract()
        {
            retractFrom = head; t = 0f; mode = V.Retract;
        }

        void Spark(Vector2 p, Color c)
        {
            ringPos = p; ringT = 0f;
            ring.startColor = c; ring.endColor = c;
            ring.enabled = true;
        }

        public void Hide()
        {
            mode = V.Hidden;
            target = null;
            ringT = -1f;
            if (root != null) root.SetActive(false);
        }

        void OnDisable() { Hide(); }

        void OnDestroy()
        {
            if (root != null) Destroy(root);
        }

        void Update()
        {
            if (root == null || mode == V.Hidden && ringT < 0f) return;
            try { Tick(Time.deltaTime); }
            catch (Exception e) { Debug.LogError("[WildCards] Grapple visual " + e); Hide(); }
        }

        void Tick(float dt)
        {
            safety += dt;
            if (mode != V.Hidden && mode != V.Retract && safety > 3.6f) { StartRetract(); }

            Vector2 hand = Hand;
            Color col = RopeCol;
            switch (mode)
            {
                case V.Flying:
                    t += dt;
                    head = origin + dir * Mathf.Min(90f * t, range);
                    break;
                case V.Anchored:
                    break;
                case V.Yank:
                    t += dt;
                    if (target != null && target.data != null && !target.data.dead) head = target.transform.position;
                    col = Color.Lerp(flash, RopeCol, t / 0.3f);
                    if (t >= 0.3f) StartRetract();
                    break;
                case V.Retract:
                    t += dt;
                    float k = Mathf.Clamp01(t / 0.3f);
                    head = Vector2.Lerp(retractFrom, hand, k * k);
                    col = Color.Lerp(flash, RopeCol, k);
                    if (k >= 1f) { mode = V.Hidden; flash = RopeCol; }
                    break;
            }

            bool show = mode != V.Hidden;
            rope.enabled = show; ropeBack.enabled = show; hook.enabled = show;
            if (show)
            {
                // Slight sag while flying/retracting, taut when anchored or yanking.
                bool taut = mode == V.Anchored || mode == V.Yank;
                DrawRope(rope, hand, head, taut);
                DrawRope(ropeBack, hand, head, taut);
                rope.startColor = col; rope.endColor = col;
                DrawHook(hand, head);
            }

            if (ringT >= 0f)
            {
                ringT += dt;
                float rk = ringT / 0.25f;
                if (rk >= 1f) { ringT = -1f; ring.enabled = false; }
                else
                {
                    float r = Mathf.Lerp(0.2f, 1.3f, rk);
                    for (int i = 0; i < ring.positionCount; i++)
                    {
                        float ang = i * Mathf.PI * 2f / ring.positionCount;
                        ring.SetPosition(i, new Vector3(ringPos.x + Mathf.Cos(ang) * r, ringPos.y + Mathf.Sin(ang) * r, 0f));
                    }
                    Color c = ring.startColor; c.a = 1f - rk;
                    ring.startColor = c; ring.endColor = c;
                    ring.widthMultiplier = Mathf.Lerp(1.5f, 0.5f, rk);
                }
            }

            if (mode == V.Hidden && ringT < 0f) root.SetActive(false);
        }

        static void DrawRope(LineRenderer lr, Vector2 a, Vector2 b, bool taut)
        {
            int n = taut ? 2 : 8;
            lr.positionCount = n;
            float len = Vector2.Distance(a, b);
            float sag = taut ? 0f : Mathf.Min(0.6f, len * 0.05f);
            for (int i = 0; i < n; i++)
            {
                float u = n == 1 ? 0f : (float)i / (n - 1);
                Vector2 p = Vector2.Lerp(a, b, u);
                p.y -= sag * 4f * u * (1f - u);
                lr.SetPosition(i, new Vector3(p.x, p.y, 0f));
            }
        }

        void DrawHook(Vector2 from, Vector2 tip)
        {
            Vector2 d = tip - from;
            Vector2 f = d.sqrMagnitude > 0.0001f ? d.normalized : Vector2.up;
            Vector2 s = new Vector2(-f.y, f.x);
            // Arrow-like grappling hook: two barbs curling back from the tip.
            Vector2 back = tip - f * 0.55f;
            hook.SetPosition(0, V3(back + s * 0.35f - f * 0.1f));
            hook.SetPosition(1, V3(tip - f * 0.15f + s * 0.25f));
            hook.SetPosition(2, V3(tip));
            hook.SetPosition(3, V3(tip - f * 0.15f - s * 0.25f));
            hook.SetPosition(4, V3(back - s * 0.35f - f * 0.1f));
        }

        static Vector3 V3(Vector2 v) { return new Vector3(v.x, v.y, 0f); }
    }
}
