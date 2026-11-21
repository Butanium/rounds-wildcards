using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnboundLib;
using UnboundLib.Networking;
using UnityEngine;
using WildCards;

namespace WildCards.Portal
{
    // ---------------------------------------------------------------- PORTAL ROUNDS
    public class PortalCard : WildCard
    {
        static bool inited;

        public static void Init()
        {
            if (inited) return;
            inited = true;
            Hooks.ProjectileHitAction += PortalNet.OnProjectileHit;
            Hooks.BulletInit += PortalNet.OnBulletInit;
            Hooks.PointStart += PortalNet.ClearAll;
            Hooks.GameStart += PortalNet.ClearAll;
        }

        public override void SetupCard(CardInfo cardInfo, Gun gun, ApplyCardStats cardStats, CharacterStatModifiers statModifiers, Block block)
        {
            gun.reloadTimeAdd = 0.5f;
        }
        public override void OnAddCard(Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health, Gravity gravity, Block block, CharacterStatModifiers characterStats)
        {
            Fx.Add<PortalEffect>(player, gun, gunAmmo);
            PortalNet.EnsureRunner();
        }
        public override void OnRemoveCard(Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health, Gravity gravity, Block block, CharacterStatModifiers characterStats)
        { Fx.Remove<PortalEffect>(player); }
        protected override string GetTitle() { return "PORTAL ROUNDS"; }
        protected override string GetDescription() { return "Bullets open linked portals"; }
        protected override CardInfo.Rarity GetRarity() { return CardInfo.Rarity.Rare; }
        protected override CardThemeColor.CardThemeColorType GetTheme() { return CardThemeColor.CardThemeColorType.MagicPink; }
        protected override CardInfoStat[] GetStats()
        {
            return new CardInfoStat[] {
                Stat(true, "Portals", "Shoot walls"),
                Stat(false, "Reload time", "+0.5s")
            };
        }
    }

    public class PortalEffect : StackEffect
    {
        public float nextPlaceTime;

        void OnDestroy()
        {
            // Card removed (or effects stripped): this owner's portals go away.
            if (player != null) PortalNet.ClearOwner(player.playerID);
        }
    }

    /// Marks a bullet that has already placed (or tried to place) a portal.
    public class PortalBulletTag : MonoBehaviour { }

    public class PortalPair
    {
        public int ownerID;
        public Vector2 posA, nA, posB, nB;
        public bool hasA, hasB;
        public int nextSlot;
        public PortalVisual visA, visB;

        public bool Complete { get { return hasA && hasB; } }
    }

    public static class PortalNet
    {
        public static readonly Dictionary<int, PortalPair> pairs = new Dictionary<int, PortalPair>();
        public static readonly Color Orange = new Color(1f, 0.55f, 0.1f, 1f);
        public static readonly Color Blue = new Color(0.15f, 0.6f, 1f, 1f);

        public static readonly FieldInfo lastPosField = AccessTools.Field(typeof(RayCastTrail), "lastPos");
        static readonly FieldInfo groundMaskField = AccessTools.Field(typeof(CharacterData), "groundMask");

        static readonly FieldInfo simSpeedField = AccessTools.Field(typeof(MoveTransform), "simulationSpeed");
        public static float SimSpeed(MoveTransform m)
        {
            try { if (simSpeedField != null && m != null) return (float)simSpeedField.GetValue(m); }
            catch { }
            return 1f;
        }

        public static int GroundMask(CharacterData d)
        {
            try { if (groundMaskField != null && d != null) { int m = ((LayerMask)groundMaskField.GetValue(d)).value; if (m != 0) return m; } }
            catch { }
            return LayerMask.GetMask("Default");
        }

        static PlayerManager diedHookedOn;
        static PortalRunner runner;
        static Material mat;

        public static Material Mat
        {
            get
            {
                if (mat == null) mat = new Material(Shader.Find("Sprites/Default"));
                return mat;
            }
        }

        public static void EnsureRunner()
        {
            if (PlayerManager.instance != null && diedHookedOn != PlayerManager.instance)
            {
                diedHookedOn = PlayerManager.instance;
                PlayerManager.instance.AddPlayerDiedAction(new Action<Player, int>(OnPlayerDied));
            }
            if (runner == null)
            {
                GameObject go = new GameObject("WildCards_PortalRunner");
                UnityEngine.Object.DontDestroyOnLoad(go);
                runner = go.AddComponent<PortalRunner>();
            }
        }

        static void OnPlayerDied(Player p, int alive)
        {
            try { if (p != null) ClearOwner(p.playerID); }
            catch (Exception e) { Debug.LogError("[WildCards] " + e); }
        }

        public static Player FindPlayer(int id)
        {
            if (PlayerManager.instance == null) return null;
            foreach (Player p in PlayerManager.instance.players)
                if (p != null && p.playerID == id) return p;
            return null;
        }

        /// Trigger radius for bullets (disc radius) based on the owner's copies.
        public static float DiscRadius(int ownerID)
        {
            return Fx.Stacks<PortalEffect>(FindPlayer(ownerID)) >= 2 ? 1.3f : 1.0f;
        }

        public static float PlayerRadius(int ownerID)
        {
            return Fx.Stacks<PortalEffect>(FindPlayer(ownerID)) >= 2 ? 1.2f : 0.9f;
        }

        public static float ExitOffset(int ownerID)
        {
            return Mathf.Max(1.3f, DiscRadius(ownerID) + 0.35f);
        }

        public static bool AnyComplete()
        {
            foreach (PortalPair pp in pairs.Values) if (pp.Complete) return true;
            return false;
        }

        public static void ClearOwner(int ownerID)
        {
            PortalPair pp;
            if (!pairs.TryGetValue(ownerID, out pp)) return;
            if (pp.visA != null) UnityEngine.Object.Destroy(pp.visA.gameObject);
            if (pp.visB != null) UnityEngine.Object.Destroy(pp.visB.gameObject);
            pairs.Remove(ownerID);
        }

        public static void ClearAll()
        {
            List<int> ids = new List<int>(pairs.Keys);
            foreach (int id in ids) ClearOwner(id);
            pairs.Clear();
        }

        // ---------------- placement (owner-authoritative)

        public static void OnProjectileHit(ProjectileHit hit, Vector2 hitPoint, Vector2 hitNormal, Vector2 vel, int viewID, int colliderID, bool wasBlocked)
        {
            if (hit == null) return;
            Player owner = hit.ownPlayer;
            if (owner == null || !Fx.IsMine(owner)) return;
            if (viewID != -1 || colliderID == -1) return;
            PortalEffect fx = Fx.Get<PortalEffect>(owner);
            if (fx == null || owner.data == null || owner.data.dead) return;
            if (hit.GetComponent<PortalBulletTag>() != null) return;
            hit.gameObject.AddComponent<PortalBulletTag>();
            if (Time.time < fx.nextPlaceTime) return;
            if (hitNormal.sqrMagnitude < 0.01f) return;
            fx.nextPlaceTime = Time.time + 0.75f;

            Vector2 n = hitNormal.normalized;
            Vector2 pos = hitPoint + n * 0.5f;
            PortalPair pp;
            int slot = pairs.TryGetValue(owner.playerID, out pp) ? pp.nextSlot : 0;
            NetworkingManager.RPC(typeof(PortalNet), "RPCA_PortalPlace", new object[] { owner.playerID, slot, pos, n });
        }

        [UnboundRPC]
        public static void RPCA_PortalPlace(int ownerID, int slot, Vector2 pos, Vector2 normal)
        {
            try
            {
                EnsureRunner();
                PortalPair pp;
                if (!pairs.TryGetValue(ownerID, out pp))
                {
                    pp = new PortalPair();
                    pp.ownerID = ownerID;
                    pairs[ownerID] = pp;
                }
                if (slot == 0)
                {
                    pp.posA = pos; pp.nA = normal; pp.hasA = true;
                    if (pp.visA == null) pp.visA = PortalVisual.Create(ownerID, 0);
                    pp.visA.Place(pos, normal);
                }
                else
                {
                    pp.posB = pos; pp.nB = normal; pp.hasB = true;
                    if (pp.visB == null) pp.visB = PortalVisual.Create(ownerID, 1);
                    pp.visB.Place(pos, normal);
                }
                pp.nextSlot = 1 - slot;
                PortalPop.Spawn(pos, slot == 0 ? Orange : Blue, 0.8f);
            }
            catch (Exception e) { Debug.LogError("[WildCards] " + e); }
        }

        /// Cosmetic: a player went through ownerID's portal pair entering at slot `from`.
        [UnboundRPC]
        public static void RPCA_PortalPop(int ownerID, int from)
        {
            try { PopPair(ownerID, from); }
            catch (Exception e) { Debug.LogError("[WildCards] " + e); }
        }

        public static void PopPair(int ownerID, int from)
        {
            PortalPair pp;
            if (!pairs.TryGetValue(ownerID, out pp) || !pp.Complete) return;
            PortalPop.Spawn(pp.posA, Orange, 1f);
            PortalPop.Spawn(pp.posB, Blue, 1f);
            if (pp.visA != null) pp.visA.Flash();
            if (pp.visB != null) pp.visB.Flash();
        }

        // ---------------- bullets

        public static void OnBulletInit(Gun gun, GameObject bullet, bool useAmmo)
        {
            if (bullet == null || pairs.Count == 0) return;
            if (bullet.GetComponent<MoveTransform>() == null) return;
            if (bullet.GetComponent<PortalBullet>() == null) bullet.AddComponent<PortalBullet>();
        }

        /// Gets entry/exit data. Returns true if a portal of pp (slot `from`) is entered.
        public static void Exit(PortalPair pp, int from, out Vector2 inN, out Vector2 outPos, out Vector2 outN)
        {
            if (from == 0) { inN = pp.nA; outPos = pp.posB; outN = pp.nB; }
            else { inN = pp.nB; outPos = pp.posA; outN = pp.nA; }
        }

        public static Vector2 Rotate(Vector2 v, Vector2 inN, Vector2 outN)
        {
            Quaternion q = Quaternion.FromToRotation(-(Vector3)inN, (Vector3)outN);
            if (Vector2.Dot(-inN, outN) < -0.999f) q = Quaternion.Euler(0f, 0f, 180f); // anti-parallel: avoid an out-of-plane flip
            return (Vector2)(q * (Vector3)v);
        }

        /// Distance from point c to segment ab.
        public static float SegDist(Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2 ab = b - a;
            float l = ab.sqrMagnitude;
            if (l < 1e-6f) return Vector2.Distance(a, c);
            float t = Mathf.Clamp01(Vector2.Dot(c - a, ab) / l);
            return Vector2.Distance(a + ab * t, c);
        }
    }

    /// Per-bullet portal travel. Runs on every client's copy; the shooter's copy decides hits.
    public class PortalBullet : MonoBehaviour
    {
        MoveTransform move;
        RayCastTrail trail;
        float cooldownUntil;
        int uses;
        Vector2 prev;
        bool hasPrev;

        void Start()
        {
            move = GetComponent<MoveTransform>();
            trail = GetComponent<RayCastTrail>();
            if (trail == null) trail = GetComponentInChildren<RayCastTrail>();
        }

        void Update()
        {
            try { Tick(); }
            catch (Exception e) { Debug.LogError("[WildCards] " + e); enabled = false; }
        }

        void Tick()
        {
            Vector2 pos = transform.position;
            Vector2 last = hasPrev ? prev : pos;
            prev = pos; hasPrev = true;
            if (move == null || uses >= 3 || Time.time < cooldownUntil) return;
            if (PortalNet.pairs.Count == 0) return;

            Vector2 v = move.velocity;
            // Look ahead one frame so a fast bullet enters the portal before its raycast reaches the wall behind it.
            // MoveTransform steps velocity * min(dt, 0.02) * simulationSpeed * multiplier per frame.
            float step = Mathf.Clamp(Time.deltaTime, 0.01f, 0.02f) * Mathf.Max(1f, PortalNet.SimSpeed(move)) * Mathf.Max(0.1f, move.multiplier);
            Vector2 ahead = pos + v * step * 1.5f;

            foreach (PortalPair pp in PortalNet.pairs.Values)
            {
                if (!pp.Complete) continue;
                float r = PortalNet.DiscRadius(pp.ownerID);
                int from = -1;
                if (PortalNet.SegDist(last, ahead, pp.posA) < r) from = 0;
                else if (PortalNet.SegDist(last, ahead, pp.posB) < r) from = 1;
                if (from < 0) continue;

                Vector2 inN, outPos, outN;
                PortalNet.Exit(pp, from, out inN, out outPos, out outN);
                Vector2 exit = outPos + outN * PortalNet.ExitOffset(pp.ownerID);
                Vector2 nv = PortalNet.Rotate(v, inN, outN);
                // Never shoot back into the exit wall.
                float along = Vector2.Dot(nv, outN);
                if (along < 0.2f * nv.magnitude) nv = nv - outN * along + outN * 0.2f * nv.magnitude;

                Vector3 p3 = new Vector3(exit.x, exit.y, transform.position.z);
                transform.position = p3;
                move.velocity = new Vector3(nv.x, nv.y, move.velocity.z);
                if (trail != null && PortalNet.lastPosField != null) PortalNet.lastPosField.SetValue(trail, p3);
                foreach (TrailRenderer tr in GetComponentsInChildren<TrailRenderer>()) if (tr != null) tr.Clear();
                prev = exit;
                uses++;
                cooldownUntil = Time.time + 0.3f;
                PortalPop.Spawn(from == 0 ? pp.posA : pp.posB, from == 0 ? PortalNet.Orange : PortalNet.Blue, 0.5f);
                PortalPop.Spawn(outPos, from == 0 ? PortalNet.Blue : PortalNet.Orange, 0.5f);
                return;
            }
        }
    }

    /// Global per-client runner: moves the players this client owns through portals.
    public class PortalRunner : MonoBehaviour
    {
        readonly Dictionary<int, float> playerCooldown = new Dictionary<int, float>();

        void Update()
        {
            if (PortalNet.pairs.Count == 0 || PlayerManager.instance == null) return;
            foreach (Player p in PlayerManager.instance.players)
            {
                try { TryPlayer(p); }
                catch (Exception e) { Debug.LogError("[WildCards] " + e); }
            }
        }

        void TryPlayer(Player p)
        {
            if (p == null || p.data == null || !Fx.IsMine(p)) return;
            if (p.data.dead || !p.data.isPlaying || !p.gameObject.activeInHierarchy) return;
            float cd;
            if (playerCooldown.TryGetValue(p.playerID, out cd) && Time.time < cd) return;

            Vector2 c = p.transform.position;
            foreach (PortalPair pp in PortalNet.pairs.Values)
            {
                if (!pp.Complete) continue;
                float r = PortalNet.PlayerRadius(pp.ownerID);
                int from = -1;
                if (Vector2.Distance(c, pp.posA) < r) from = 0;
                else if (Vector2.Distance(c, pp.posB) < r) from = 1;
                if (from < 0) continue;

                Vector2 inN, outPos, outN;
                PortalNet.Exit(pp, from, out inN, out outPos, out outN);
                Vector2 exit = outPos + outN * PortalNet.ExitOffset(pp.ownerID);
                // Exit-inside-wall safety.
                bool ok = false;
                for (float extra = 0f; extra <= 1.5f; extra += 0.25f)
                {
                    Vector2 test = exit + outN * extra;
                    if (Physics2D.OverlapCircle(test, 0.5f, PortalNet.GroundMask(p.data)) == null) { exit = test; ok = true; break; }
                }
                if (!ok) { playerCooldown[p.playerID] = Time.time + 0.3f; continue; }

                Vector2 v = PortalNet.Rotate(Fx.GetVel(p.data), inN, outN);
                float min = 0.6f * Fx.JumpVel(p);
                float along = Vector2.Dot(v, outN);
                if (along < min) v += outN * (min - along);

                Fx.Teleport(p, new Vector3(exit.x, exit.y, p.transform.position.z));
                Fx.SetVel(p.data, v);
                p.data.sinceGrounded = 0f;
                playerCooldown[p.playerID] = Time.time + 0.6f;
                NetworkingManager.RPC(typeof(PortalNet), "RPCA_PortalPop", new object[] { pp.ownerID, from });
                return;
            }
        }
    }

    /// Local visual for one portal: owner-coloured outline, glowing slot-coloured ring, swirling arms.
    public class PortalVisual : MonoBehaviour
    {
        int ownerID, slot;
        Color slotColor, ownerColor;
        LineRenderer outline, ring, halo;
        LineRenderer[] arms;
        float flash;
        float spawnT;
        const int Seg = 40;

        static LineRenderer MakeLine(Transform parent, string name, int order)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.material = PortalNet.Mat;
            lr.useWorldSpace = false;
            lr.sortingOrder = order;
            lr.sortingLayerName = "MostFront";
            lr.numCapVertices = 2;
            return lr;
        }

        public static PortalVisual Create(int ownerID, int slot)
        {
            GameObject go = new GameObject("WildCards_Portal_" + ownerID + "_" + slot);
            PortalVisual v = go.AddComponent<PortalVisual>();
            v.ownerID = ownerID;
            v.slot = slot;
            v.slotColor = slot == 0 ? PortalNet.Orange : PortalNet.Blue;
            v.ownerColor = Color.white;
            try
            {
                Player p = PortalNet.FindPlayer(ownerID);
                if (p != null)
                {
                    PlayerSkin skin = p.GetTeamColors();
                    if (skin != null) v.ownerColor = skin.color;
                }
            }
            catch { }
            v.halo = MakeLine(go.transform, "halo", 49);
            v.ring = MakeLine(go.transform, "ring", 51);
            v.outline = MakeLine(go.transform, "outline", 52);
            v.halo.loop = v.ring.loop = v.outline.loop = true;
            v.arms = new LineRenderer[3];
            for (int i = 0; i < 3; i++) v.arms[i] = MakeLine(go.transform, "arm" + i, 50);
            return v;
        }

        public void Place(Vector2 pos, Vector2 normal)
        {
            transform.position = new Vector3(pos.x, pos.y, 0f);
            spawnT = 0f;
            flash = 1f;
        }

        public void Flash() { flash = 1f; }

        static void Circle(LineRenderer lr, float r, float squash)
        {
            lr.positionCount = Seg;
            for (int i = 0; i < Seg; i++)
            {
                float a = i * Mathf.PI * 2f / Seg;
                lr.SetPosition(i, new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r * squash, 0f));
            }
        }

        static Color A(Color c, float a) { c.a = a; return c; }

        void Update()
        {
            spawnT += Time.deltaTime;
            flash = Mathf.Max(0f, flash - Time.deltaTime * 3f);
            PortalPair pp;
            bool complete = PortalNet.pairs.TryGetValue(ownerID, out pp) && pp.Complete;
            float alpha = complete ? 1f : 0.4f;
            float r = PortalNet.DiscRadius(ownerID);
            float grow = Mathf.Clamp01(spawnT / 0.2f);
            float pulse = 1f + 0.06f * Mathf.Sin(Time.time * 6f + slot * 2f);
            float rr = r * grow * pulse * (1f + 0.25f * flash);

            Circle(halo, rr * 0.8f, 1f);
            halo.startWidth = halo.endWidth = rr * 0.9f;
            halo.startColor = halo.endColor = A(slotColor, 0.18f * alpha + 0.3f * flash);

            Circle(ring, rr, 1f);
            ring.startWidth = ring.endWidth = 0.22f + 0.15f * flash;
            Color rc = Color.Lerp(slotColor, Color.white, flash * 0.6f);
            ring.startColor = ring.endColor = A(rc, 0.95f * alpha);

            Circle(outline, rr + 0.16f, 1f);
            outline.startWidth = outline.endWidth = 0.07f;
            outline.startColor = outline.endColor = A(ownerColor, 0.9f * alpha);

            // Swirling spiral arms.
            float spin = Time.time * (slot == 0 ? 4f : -4f);
            for (int i = 0; i < arms.Length; i++)
            {
                LineRenderer lr = arms[i];
                int n = 14;
                lr.positionCount = n;
                for (int k = 0; k < n; k++)
                {
                    float t = k / (float)(n - 1);
                    float ang = spin + i * Mathf.PI * 2f / arms.Length + t * 2.4f * (slot == 0 ? 1f : -1f);
                    float rad = rr * (0.1f + 0.8f * t);
                    lr.SetPosition(k, new Vector3(Mathf.Cos(ang) * rad, Mathf.Sin(ang) * rad, 0f));
                }
                lr.startWidth = 0.05f;
                lr.endWidth = 0.14f;
                lr.startColor = A(Color.white, 0.8f * alpha);
                lr.endColor = A(slotColor, 0.7f * alpha);
            }
        }
    }

    /// Short expanding ring + sparks, local only.
    public class PortalPop : MonoBehaviour
    {
        LineRenderer ring;
        LineRenderer[] sparks;
        Vector2[] dirs;
        Color color;
        float t, scale;
        const float Life = 0.4f;

        public static void Spawn(Vector2 pos, Color c, float scale)
        {
            try
            {
                GameObject go = new GameObject("WildCards_PortalPop");
                go.transform.position = new Vector3(pos.x, pos.y, 0f);
                PortalPop pp = go.AddComponent<PortalPop>();
                pp.color = c;
                pp.scale = scale;
                pp.ring = Line(go.transform);
                pp.ring.loop = true;
                pp.ring.positionCount = 28;
                pp.sparks = new LineRenderer[8];
                pp.dirs = new Vector2[8];
                for (int i = 0; i < 8; i++)
                {
                    pp.sparks[i] = Line(go.transform);
                    pp.sparks[i].positionCount = 2;
                    float a = (i + UnityEngine.Random.value * 0.6f) * Mathf.PI * 2f / 8f;
                    pp.dirs[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                }
                pp.Tick();
            }
            catch (Exception e) { Debug.LogError("[WildCards] " + e); }
        }

        static LineRenderer Line(Transform parent)
        {
            GameObject g = new GameObject("l");
            g.transform.SetParent(parent, false);
            LineRenderer lr = g.AddComponent<LineRenderer>();
            lr.material = PortalNet.Mat;
            lr.useWorldSpace = false;
            lr.sortingOrder = 53;
            lr.sortingLayerName = "MostFront";
            return lr;
        }

        void Update()
        {
            t += Time.deltaTime;
            if (t >= Life) { Destroy(gameObject); return; }
            Tick();
        }

        void Tick()
        {
            float k = t / Life;
            float a = 1f - k;
            Color c = color; c.a = a;
            float r = scale * (0.6f + 1.4f * k);
            for (int i = 0; i < 28; i++)
            {
                float ang = i * Mathf.PI * 2f / 28;
                ring.SetPosition(i, new Vector3(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r, 0f));
            }
            ring.startWidth = ring.endWidth = 0.18f * a + 0.02f;
            ring.startColor = ring.endColor = c;
            for (int i = 0; i < sparks.Length; i++)
            {
                float d0 = scale * (0.4f + 2.0f * k);
                float d1 = d0 + scale * 0.5f * a;
                sparks[i].SetPosition(0, (Vector3)(dirs[i] * d0));
                sparks[i].SetPosition(1, (Vector3)(dirs[i] * d1));
                sparks[i].startWidth = 0.1f * a;
                sparks[i].endWidth = 0.02f;
                sparks[i].startColor = Color.Lerp(Color.white, color, k);
                sparks[i].endColor = c;
            }
        }
    }
}
