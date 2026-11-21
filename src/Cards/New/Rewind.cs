using System;
using System.Collections.Generic;
using UnboundLib;
using UnboundLib.Networking;
using UnityEngine;
using WildCards;

namespace WildCards.Rewind
{
    // ---------------------------------------------------------------- REWIND
    public class RewindCard : WildCard
    {
        static bool inited;

        public static void Init()
        {
            if (inited) return;
            inited = true;
            Hooks.PointStart += RewindEffect.ClearAll;
            Hooks.GameStart += RewindEffect.ClearAll;
        }

        public override void SetupCard(CardInfo cardInfo, Gun gun, ApplyCardStats cardStats, CharacterStatModifiers statModifiers, Block block)
        {
            block.cdAdd = 0.75f;
        }
        public override void OnAddCard(Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health, Gravity gravity, Block block, CharacterStatModifiers characterStats)
        { Fx.Add<RewindEffect>(player, gun, gunAmmo); }
        public override void OnRemoveCard(Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health, Gravity gravity, Block block, CharacterStatModifiers characterStats)
        { Fx.Remove<RewindEffect>(player); }
        protected override string GetTitle() { return "REWIND"; }
        protected override string GetDescription() { return "Blocking rewinds you 2 seconds"; }
        protected override CardInfo.Rarity GetRarity() { return CardInfo.Rarity.Uncommon; }
        protected override CardThemeColor.CardThemeColorType GetTheme() { return CardThemeColor.CardThemeColorType.ColdBlue; }
        protected override CardInfoStat[] GetStats()
        {
            return new CardInfoStat[] {
                Stat(true, "Rewind", "2s"),
                Stat(false, "Block cooldown", "+0.75s")
            };
        }
    }

    struct OwnerSample
    {
        public float t;
        public Vector2 pos;
        public Vector2 vel;
    }

    struct GhostSample
    {
        public float t;
        public Vector2 pos;
    }

    public class RewindEffect : StackEffect
    {
        // Owner-side history (FixedUpdate, 50Hz, 3s+ deep).
        readonly List<OwnerSample> history = new List<OwnerSample>();
        // Cosmetic history on every client (Update, synced transform position).
        readonly List<GhostSample> ghostHist = new List<GhostSample>();

        float cooldownUntil = -100f;      // owner gate + local visual grey-out
        bool pendingRewind;
        Vector2 lastGhostPos;
        bool hasGhostPos;
        // After a rewind RPC, remote clients may still see the pre-teleport synced position for a few frames;
        // skip ghost samples until the player has arrived (or a short timeout) so the ghost does not sit at the departure point.
        Vector2 arriveAt;
        float arriveBy = -100f;

        GameObject ghostObj;
        SpriteRenderer ghostSprite;
        LineRenderer ghostRing;
        TrailRenderer ghostTrail;
        Action reviveHandler;

        public float RewindTime { get { return stacks >= 2 ? 3f : 2f; } }
        public float Cooldown { get { return stacks >= 2 ? 3f : 4f; } }

        public override void Init()
        {
            if (data != null && data.block != null)
                data.block.BlockAction = (Action<BlockTrigger.BlockTriggerType>)Delegate.Combine(data.block.BlockAction, new Action<BlockTrigger.BlockTriggerType>(OnBlock));
            reviveHandler = new Action(ResetHistory);
            if (data != null && data.healthHandler != null)
                data.healthHandler.reviveAction = (Action)Delegate.Combine(data.healthHandler.reviveAction, reviveHandler);
            BuildGhost();
            ResetHistory();
        }

        void OnDestroy()
        {
            if (data != null && data.block != null)
                data.block.BlockAction = (Action<BlockTrigger.BlockTriggerType>)Delegate.Remove(data.block.BlockAction, new Action<BlockTrigger.BlockTriggerType>(OnBlock));
            if (data != null && data.healthHandler != null && reviveHandler != null)
                data.healthHandler.reviveAction = (Action)Delegate.Remove(data.healthHandler.reviveAction, reviveHandler);
            if (ghostObj != null) Destroy(ghostObj);
        }

        void OnDisable()
        {
            if (ghostObj != null) ghostObj.SetActive(false);
            pendingRewind = false;
        }

        void OnEnable()
        {
            ResetHistory();
        }

        public static void ClearAll()
        {
            if (PlayerManager.instance == null) return;
            foreach (Player p in PlayerManager.instance.players)
            {
                if (p == null) continue;
                RewindEffect e = p.GetComponent<RewindEffect>();
                if (e == null) continue;
                try { e.cooldownUntil = -100f; e.ResetHistory(); } catch (Exception ex) { Debug.LogError("[WildCards] Rewind " + ex); }
            }
        }

        public void ResetHistory()
        {
            history.Clear();
            ghostHist.Clear();
            pendingRewind = false;
            hasGhostPos = false;
            arriveBy = -100f;
            if (ghostTrail != null) ghostTrail.Clear();
        }

        // ---------------------------------------------------------- owner logic
        void FixedUpdate()
        {
            if (!IsMine) return;
            if (!Alive) { history.Clear(); return; }
            OwnerSample s;
            s.t = Time.fixedTime;
            s.pos = player.transform.position;
            s.vel = Fx.GetVel(data);
            history.Add(s);
            float keep = 3.2f;
            int drop = 0;
            while (drop < history.Count - 1 && history[drop].t < s.t - keep) drop++;
            if (drop > 0) history.RemoveRange(0, drop);
        }

        // Fires on every client; only the owner acts. Executed in LateUpdate so it resolves after SWITCHEROO.
        void OnBlock(BlockTrigger.BlockTriggerType t)
        {
            if (!IsMine || !Alive) return;
            if (Time.time < cooldownUntil) return;
            if (history.Count < 2) return;
            if (Time.fixedTime - history[0].t < RewindTime - 0.05f) return;
            pendingRewind = true;
        }

        void LateUpdate()
        {
            if (pendingRewind)
            {
                pendingRewind = false;
                try { DoRewind(); } catch (Exception ex) { Debug.LogError("[WildCards] Rewind " + ex); }
            }
            UpdateGhost();
        }

        void DoRewind()
        {
            if (!IsMine || !Alive || history.Count < 2) return;
            float target = Time.fixedTime - RewindTime;
            OwnerSample old = history[0];
            for (int i = 0; i < history.Count; i++)
            {
                if (history[i].t <= target) old = history[i];
                else break;
            }
            Vector2 from = player.transform.position;
            Fx.Teleport(player, old.pos);
            Fx.SetVel(data, old.vel);
            data.sinceGrounded = 0f;
            history.Clear();
            cooldownUntil = Time.time + Cooldown;
            NetworkingManager.RPC(typeof(RewindNet), "RPCA_RewindFx", new object[] { player.playerID, from, old.pos });
        }

        // Called on every client (including owner) by the RPC.
        public void OnRewindFx(Vector2 from, Vector2 to)
        {
            cooldownUntil = Time.time + Cooldown;
            ghostHist.Clear();
            hasGhostPos = false;
            arriveAt = to;
            arriveBy = Time.time + 0.4f;
            if (ghostTrail != null) ghostTrail.Clear();
            RewindVisuals.Streak(from, to);
            RewindVisuals.Burst(from, 1.6f);
            RewindVisuals.Burst(to, 2.2f);
        }

        // ---------------------------------------------------------- ghost (all clients)
        void BuildGhost()
        {
            ghostObj = new GameObject("WildCards_RewindGhost");
            ghostSprite = ghostObj.AddComponent<SpriteRenderer>();
            ghostSprite.sprite = RewindVisuals.Disc;
            ghostSprite.sortingOrder = 40;
            ghostSprite.sortingLayerName = "MostFront";

            GameObject ringGo = new GameObject("Ring");
            ringGo.transform.SetParent(ghostObj.transform, false);
            ghostRing = ringGo.AddComponent<LineRenderer>();
            ghostRing.material = RewindVisuals.Mat;
            ghostRing.useWorldSpace = false;
            ghostRing.loop = true;
            ghostRing.sortingOrder = 41;
            ghostRing.sortingLayerName = "MostFront";
            int n = 32;
            ghostRing.positionCount = n;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                ghostRing.SetPosition(i, new Vector3(Mathf.Cos(a) * 0.5f, Mathf.Sin(a) * 0.5f, 0f));
            }
            ghostRing.widthMultiplier = 0.06f;

            ghostTrail = ghostObj.AddComponent<TrailRenderer>();
            ghostTrail.material = RewindVisuals.Mat;
            ghostTrail.time = 0.5f;
            ghostTrail.minVertexDistance = 0.1f;
            ghostTrail.sortingOrder = 39;
            ghostTrail.sortingLayerName = "MostFront";
            ghostTrail.widthMultiplier = 0.5f;
            ghostObj.SetActive(false);
        }

        Color PlayerColor()
        {
            try
            {
                PlayerSkin skin = player.GetTeamColors();
                if (skin != null) return skin.color;
            }
            catch { }
            return new Color(0.4f, 0.9f, 1f);
        }

        void UpdateGhost()
        {
            if (ghostObj == null) return;
            if (player == null || !Alive || !player.gameObject.activeInHierarchy)
            {
                if (ghostObj.activeSelf) ghostObj.SetActive(false);
                // Drop stale samples so the ghost does not lerp from a pre-death / previous-round position.
                ghostHist.Clear();
                hasGhostPos = false;
                return;
            }
            float now = Time.time;
            Vector2 cur = player.transform.position;
            if (now < arriveBy)
            {
                if ((cur - arriveAt).sqrMagnitude > 2.25f && ghostHist.Count == 0)
                {
                    if (ghostObj.activeSelf) ghostObj.SetActive(false);
                    return;
                }
                arriveBy = -100f;
            }
            if (ghostHist.Count == 0 || now - ghostHist[ghostHist.Count - 1].t >= 0.05f)
            {
                GhostSample g;
                g.t = now;
                g.pos = cur;
                ghostHist.Add(g);
            }
            float T = RewindTime;
            int drop = 0;
            while (drop < ghostHist.Count - 1 && ghostHist[drop + 1].t <= now - T) drop++;
            if (drop > 0) ghostHist.RemoveRange(0, drop);

            // Interpolate position at now - T (clamped to oldest sample while history re-grows).
            float target = now - T;
            Vector2 gp = ghostHist[0].pos;
            if (ghostHist.Count > 1 && ghostHist[0].t < target)
            {
                GhostSample a = ghostHist[0];
                GhostSample b = ghostHist[1];
                float k = b.t > a.t ? Mathf.Clamp01((target - a.t) / (b.t - a.t)) : 1f;
                gp = Vector2.Lerp(a.pos, b.pos, k);
            }
            float fill = Mathf.Clamp01((now - ghostHist[0].t) / T);
            if (ghostHist[0].t <= target) fill = 1f;

            // A big jump (teleport, portal, switcheroo) should not smear the trail across the map.
            if (hasGhostPos && (gp - lastGhostPos).sqrMagnitude > 25f && ghostTrail != null) ghostTrail.Clear();
            lastGhostPos = gp;
            hasGhostPos = true;

            if (!ghostObj.activeSelf) { ghostObj.SetActive(true); if (ghostTrail != null) ghostTrail.Clear(); }
            ghostObj.transform.position = new Vector3(gp.x, gp.y, player.transform.position.z + 0.1f);
            float d = Mathf.Clamp(player.transform.localScale.x * 1.15f, 0.6f, 6f);
            ghostObj.transform.localScale = new Vector3(d, d, 1f);

            bool ready = now >= cooldownUntil;
            Color c = PlayerColor();
            if (!ready)
            {
                float gray = (c.r + c.g + c.b) / 3f * 0.6f + 0.2f;
                c = new Color(gray, gray, gray);
            }
            float baseAlpha = (ready ? 0.4f : 0.22f) * Mathf.Lerp(0.25f, 1f, fill);
            if (ready && fill >= 1f) baseAlpha *= 1f + 0.15f * Mathf.Sin(now * 6f);
            ghostSprite.color = new Color(c.r, c.g, c.b, baseAlpha);
            Color ringC = ready && fill >= 1f ? new Color(0.45f, 0.95f, 1f, 0.75f) : new Color(c.r, c.g, c.b, baseAlpha);
            ghostRing.startColor = ringC;
            ghostRing.endColor = ringC;
            ghostTrail.startColor = new Color(c.r, c.g, c.b, baseAlpha * 0.6f);
            ghostTrail.endColor = new Color(c.r, c.g, c.b, 0f);
            ghostTrail.widthMultiplier = d * 0.8f;
        }
    }

    public static class RewindNet
    {
        [UnboundRPC]
        public static void RPCA_RewindFx(int playerID, Vector2 from, Vector2 to)
        {
            try
            {
                if (PlayerManager.instance == null) return;
                foreach (Player p in PlayerManager.instance.players)
                {
                    if (p == null || p.playerID != playerID) continue;
                    RewindEffect e = p.GetComponent<RewindEffect>();
                    if (e != null) e.OnRewindFx(from, to);
                    else { RewindVisuals.Streak(from, to); RewindVisuals.Burst(to, 2f); }
                    return;
                }
            }
            catch (Exception ex) { Debug.LogError("[WildCards] Rewind RPC " + ex); }
        }
    }

    // ---------------------------------------------------------------- visuals (local, cosmetic)
    public static class RewindVisuals
    {
        static Material mat;
        static Sprite disc;

        public static Material Mat
        {
            get
            {
                if (mat == null) mat = new Material(Shader.Find("Sprites/Default"));
                return mat;
            }
        }

        public static Sprite Disc
        {
            get
            {
                if (disc == null)
                {
                    int s = 64;
                    Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
                    tex.filterMode = FilterMode.Bilinear;
                    float r = s * 0.5f;
                    for (int y = 0; y < s; y++)
                        for (int x = 0; x < s; x++)
                        {
                            float dx = x + 0.5f - r, dy = y + 0.5f - r;
                            float dist = Mathf.Sqrt(dx * dx + dy * dy);
                            float a = Mathf.Clamp01(r - dist);
                            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                        }
                    tex.Apply();
                    // pixelsPerUnit = size => sprite is 1 world unit wide.
                    disc = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), s);
                }
                return disc;
            }
        }

        static LineRenderer NewLine(string name, int order)
        {
            GameObject go = new GameObject(name);
            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.material = Mat;
            lr.useWorldSpace = true;
            lr.sortingOrder = order;
            lr.sortingLayerName = "MostFront";
            return lr;
        }

        /// VHS-style streak: cyan core with offset magenta/white ghosts and scanline ticks.
        public static void Streak(Vector2 from, Vector2 to)
        {
            Vector2 dir = to - from;
            Vector2 perp = dir.sqrMagnitude > 0.0001f ? new Vector2(-dir.y, dir.x).normalized : Vector2.up;
            AddStreakLine(from, to, Vector2.zero, new Color(0.3f, 1f, 1f, 1f), 0.55f, 0.5f, 55);
            AddStreakLine(from, to, perp * 0.22f, new Color(1f, 0.25f, 0.85f, 0.7f), 0.25f, 0.4f, 54);
            AddStreakLine(from, to, -perp * 0.22f, new Color(0.9f, 1f, 1f, 0.6f), 0.15f, 0.35f, 54);
            // scanline ticks along the path
            int ticks = Mathf.Clamp((int)(dir.magnitude / 1.2f), 2, 30);
            for (int i = 0; i <= ticks; i++)
            {
                Vector2 p = Vector2.Lerp(from, to, i / (float)ticks);
                float len = i % 2 == 0 ? 0.6f : 0.35f;
                AddStreakLine(p - perp * len, p + perp * len, Vector2.zero, new Color(0.4f, 1f, 1f, 0.55f), 0.07f, 0.3f + 0.01f * i, 53);
            }
        }

        static void AddStreakLine(Vector2 a, Vector2 b, Vector2 offset, Color c, float width, float life, int order)
        {
            LineRenderer lr = NewLine("WildCards_RewindStreak", order);
            lr.positionCount = 2;
            lr.SetPosition(0, new Vector3(a.x + offset.x, a.y + offset.y, 0f));
            lr.SetPosition(1, new Vector3(b.x + offset.x, b.y + offset.y, 0f));
            RewindFade f = lr.gameObject.AddComponent<RewindFade>();
            f.lr = lr; f.color = c; f.width = width; f.life = life; f.jitter = offset.sqrMagnitude > 0f ? 0.12f : 0f;
            f.Apply(0f);
        }

        /// "Zzzip" burst: two expanding rings and radial sparks.
        public static void Burst(Vector2 at, float size)
        {
            for (int k = 0; k < 2; k++)
            {
                LineRenderer ring = NewLine("WildCards_RewindRing", 56);
                ring.loop = true;
                int n = 32;
                ring.positionCount = n;
                RewindFade f = ring.gameObject.AddComponent<RewindFade>();
                f.lr = ring; f.center = at; f.ringFrom = 0.2f + 0.3f * k; f.ringTo = size * (k == 0 ? 1f : 0.6f);
                f.color = k == 0 ? new Color(0.3f, 1f, 1f, 1f) : new Color(1f, 0.3f, 0.85f, 0.8f);
                f.width = 0.15f; f.life = 0.45f + 0.1f * k; f.isRing = true;
                f.Apply(0f);
            }
            int sparks = 10;
            float rot = UnityEngine.Random.value * Mathf.PI * 2f;
            for (int i = 0; i < sparks; i++)
            {
                float a = rot + i * Mathf.PI * 2f / sparks;
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                LineRenderer lr = NewLine("WildCards_RewindSpark", 57);
                lr.positionCount = 2;
                RewindFade f = lr.gameObject.AddComponent<RewindFade>();
                f.lr = lr; f.center = at; f.sparkDir = d; f.ringFrom = 0.3f; f.ringTo = size * 1.2f;
                f.color = new Color(0.6f, 1f, 1f, 1f); f.width = 0.08f; f.life = 0.35f; f.isSpark = true;
                f.Apply(0f);
            }
        }
    }

    public class RewindFade : MonoBehaviour
    {
        public LineRenderer lr;
        public Color color;
        public float width = 0.2f;
        public float life = 0.4f;
        public float jitter;
        public bool isRing, isSpark;
        public Vector2 center, sparkDir;
        public float ringFrom, ringTo;
        float age;

        void Start()
        {
            // Self-clean even if a round ends mid-effect.
            Destroy(gameObject, life + 0.1f);
        }

        void Update()
        {
            age += Time.deltaTime;
            float k = Mathf.Clamp01(age / life);
            Apply(k);
            if (k >= 1f) Destroy(gameObject);
        }

        public void Apply(float k)
        {
            if (lr == null) return;
            float ease = 1f - (1f - k) * (1f - k);
            Color c = new Color(color.r, color.g, color.b, color.a * (1f - k));
            lr.startColor = c;
            lr.endColor = c;
            if (isRing)
            {
                float r = Mathf.Lerp(ringFrom, ringTo, ease);
                int n = lr.positionCount;
                for (int i = 0; i < n; i++)
                {
                    float a = i * Mathf.PI * 2f / n;
                    lr.SetPosition(i, new Vector3(center.x + Mathf.Cos(a) * r, center.y + Mathf.Sin(a) * r, 0f));
                }
                lr.widthMultiplier = width * (1f - 0.5f * k);
            }
            else if (isSpark)
            {
                float r0 = Mathf.Lerp(ringFrom, ringTo * 0.7f, ease);
                float r1 = Mathf.Lerp(ringFrom + 0.4f, ringTo, ease);
                lr.SetPosition(0, new Vector3(center.x + sparkDir.x * r0, center.y + sparkDir.y * r0, 0f));
                lr.SetPosition(1, new Vector3(center.x + sparkDir.x * r1, center.y + sparkDir.y * r1, 0f));
                lr.widthMultiplier = width;
            }
            else
            {
                lr.widthMultiplier = width * (1f - 0.6f * k);
                if (jitter > 0f && lr.positionCount == 2)
                {
                    Vector3 p0 = lr.GetPosition(0), p1 = lr.GetPosition(1);
                    float j = (UnityEngine.Random.value - 0.5f) * jitter;
                    lr.SetPosition(0, p0 + new Vector3(0f, j, 0f));
                    lr.SetPosition(1, p1 + new Vector3(0f, j, 0f));
                }
            }
        }
    }
}
