using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using UnboundLib;
using UnboundLib.Networking;
using UnityEngine;
using WildCards;

namespace WildCards.Wraparound
{
    // ---------------------------------------------------------------- WRAPAROUND
    public class WraparoundCard : WildCard
    {
        static bool inited;

        public static void Init()
        {
            if (inited) return;
            inited = true;
            Hooks.BulletInit += WrapNet.OnBulletInit;
        }

        public override void SetupCard(CardInfo cardInfo, Gun gun, ApplyCardStats cardStats, CharacterStatModifiers statModifiers, Block block)
        {
            gun.projectileSpeed = 0.8f;
        }
        public override void OnAddCard(Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health, Gravity gravity, Block block, CharacterStatModifiers characterStats)
        { Fx.Add<WrapEffect>(player, gun, gunAmmo); }
        public override void OnRemoveCard(Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health, Gravity gravity, Block block, CharacterStatModifiers characterStats)
        { Fx.Remove<WrapEffect>(player); }
        protected override string GetTitle() { return "WRAPAROUND"; }
        protected override string GetDescription() { return "Screen edges wrap around"; }
        protected override CardInfo.Rarity GetRarity() { return CardInfo.Rarity.Uncommon; }
        protected override CardThemeColor.CardThemeColorType GetTheme() { return CardThemeColor.CardThemeColorType.EvilPurple; }
        protected override CardInfoStat[] GetStats()
        {
            return new CardInfoStat[] {
                Stat(true, "You and your bullets", "Wrap"),
                Stat(false, "Bullet speed", "-20%")
            };
        }
    }

    /// Arena constants (OutOfBoundsHandler.LateUpdate uses fixed bounds x in [-35.56, 35.56], y in [-20, 20]).
    static class Arena
    {
        public const float MaxX = 35.56f;
        public const float MaxY = 20f;
        public static readonly Color Purple = new Color(0.72f, 0.25f, 1f, 1f);

        // Private game fields, through cached reflection.
        static readonly FieldInfo simulatedField = AccessTools.Field(typeof(PlayerVelocity), "simulated");
        static readonly FieldInfo groundMaskField = AccessTools.Field(typeof(CharacterData), "groundMask");
        static readonly FieldInfo lastPosField = AccessTools.Field(typeof(RayCastTrail), "lastPos");

        public static bool Simulated(PlayerVelocity v)
        {
            if (v == null) return false;
            if (simulatedField == null) return true;
            return (bool)simulatedField.GetValue(v);
        }

        public static int GroundMask(CharacterData d)
        {
            if (d != null && groundMaskField != null)
            {
                int m = ((LayerMask)groundMaskField.GetValue(d)).value;
                if (m != 0) return m;
            }
            return LayerMask.GetMask("Default");
        }

        public static void SetLastPos(RayCastTrail t, Vector3 p)
        {
            if (t != null && lastPosField != null) lastPosField.SetValue(t, p);
        }
    }

    /// Owner-authoritative player wrap. Update runs before OutOfBoundsHandler.LateUpdate, so no OOB damage triggers.
    public class WrapEffect : StackEffect
    {
        const float TriggerX = 34.8f, ArriveX = 34.3f;
        const float TriggerY = 19.3f, ArriveTop = 19.0f, ArriveBottom = -18.8f;
        const float Cooldown = 0.4f;
        const float SafeRadius = 0.7f;

        float cd;
        PlayerCollision col;

        void Update()
        {
            if (cd > 0f) cd -= Time.deltaTime;
            if (data == null || player == null) return;
            if (!IsMine)
            {
                // Remote copy: SyncPlayerMovement snaps it across the map after a wrap, and PlayerCollision's
                // go-through-wall raycast (lastPos -> pos) would catch it on the first wall in between and pin it
                // there. Near a wrap edge keep that check off for this copy (its position comes from the owner).
                Vector3 rp = player.transform.position;
                if (Mathf.Abs(rp.x) > TriggerX - 1.5f || Mathf.Abs(rp.y) > TriggerY - 1.5f)
                {
                    if (col == null) col = player.GetComponent<PlayerCollision>();
                    if (col != null) col.IgnoreWallForFrames(3);
                }
                return;
            }
            if (!Alive) return;
            if (!Arena.Simulated(data.playerVel)) return;
            try { TryWrap(); }
            catch (Exception e) { Debug.LogError("[WildCards] Wraparound: " + e); }
        }

        void TryWrap()
        {
            Vector3 pos = player.transform.position;
            Vector2 v = Fx.GetVel(data);
            Vector2 dest = pos;
            bool wrap = false;

            if (pos.x > TriggerX && v.x >= 0f) { dest.x = -ArriveX; wrap = true; }
            else if (pos.x < -TriggerX && v.x <= 0f) { dest.x = ArriveX; wrap = true; }

            if (pos.y < -TriggerY && v.y <= 0f) { dest.y = ArriveTop; wrap = true; }
            else if (pos.y > TriggerY && v.y >= 0f) { dest.y = ArriveBottom; wrap = true; }

            if (!wrap) return;

            // During the cooldown only wrap when vanilla out-of-bounds is about to fire: fast fallers can cross
            // the whole arena within 0.4s and would otherwise take OOB damage instead of wrapping.
            if (cd > 0f && Mathf.Abs(pos.x) < Arena.MaxX - 0.3f && Mathf.Abs(pos.y) < Arena.MaxY - 0.3f) return;

            // Safety: never drop the player inside terrain; vanilla out-of-bounds handles it instead.
            if (Physics2D.OverlapCircle(dest, SafeRadius, Arena.GroundMask(data)) != null) { cd = 0.1f; return; }

            // Gravity grows with sinceGrounded, so a bottomless column would loop ever faster.
            // Keep the fall (and sinceGrounded) but cap the carried vertical speed on vertical wraps.
            if (dest.y != pos.y)
            {
                float maxV = Mathf.Max(60f, 3f * Fx.JumpVel(player));
                v.y = Mathf.Clamp(v.y, -maxV, maxV);
            }

            Vector2 from = pos;
            Fx.Teleport(player, dest);
            Fx.SetVel(data, v); // keep momentum; sinceGrounded deliberately untouched
            cd = Cooldown;

            NetworkingManager.RPC(typeof(WrapNet), "RPCA_WrapFx", new object[] { player.playerID, from, dest });
        }
    }

    public static class WrapNet
    {
        static readonly FieldInfo syncPackagesField = AccessTools.Field(typeof(SyncPlayerMovement), "syncPackages");

        [UnboundRPC]
        public static void RPCA_WrapFx(int playerID, Vector2 from, Vector2 to)
        {
            try
            {
                Player p = null;
                if (PlayerManager.instance != null)
                    foreach (Player q in PlayerManager.instance.players)
                        if (q != null && q.playerID == playerID) { p = q; break; }

                // Remote copies interpolate toward synced positions; snap them so they don't slide across the map.
                if (p != null && !Fx.IsMine(p))
                {
                    SyncPlayerMovement sync = p.GetComponent<SyncPlayerMovement>();
                    if (sync != null && syncPackagesField != null)
                    {
                        IList list = syncPackagesField.GetValue(sync) as IList;
                        if (list != null) list.Clear();
                    }
                    PlayerCollision pc = p.GetComponent<PlayerCollision>();
                    if (pc != null) pc.IgnoreWallForFrames(3); // don't let the cross-map snap stop on a wall
                    Vector3 np = p.transform.position;
                    np.x = to.x; np.y = to.y;
                    p.transform.position = np;
                }

                WrapRing.Spawn(from, 1.6f, 0.45f, true);
                WrapRing.Spawn(to, 1.6f, 0.6f, false);
            }
            catch (Exception e) { Debug.LogError("[WildCards] Wraparound RPC: " + e); }
        }

        public static void OnBulletInit(Gun gun, GameObject bullet, bool useAmmo)
        {
            if (gun == null || bullet == null) return;
            Player owner = gun.player;
            int n = Fx.Stacks<WrapEffect>(owner);
            if (n <= 0) return;
            if (bullet.GetComponent<MoveTransform>() == null) return;
            WrapBullet wb = bullet.AddComponent<WrapBullet>();
            wb.wrapsLeft = Mathf.Min(n, 2);
        }
    }

    /// Deterministic on every client: mirrors the bullet across the arena when it leaves the bounds.
    public class WrapBullet : MonoBehaviour
    {
        const float Margin = 0.3f;
        public int wrapsLeft = 1;
        RayCastTrail trail;

        void Start() { trail = GetComponent<RayCastTrail>(); }

        // LateUpdate: after MoveTransform and RayCastTrail have run this frame.
        void LateUpdate()
        {
            if (wrapsLeft <= 0) return;
            Vector3 pos = transform.position;
            Vector3 dest = pos;
            bool wrap = false;

            if (pos.x > Arena.MaxX) { dest.x = -Arena.MaxX + Margin; wrap = true; }
            else if (pos.x < -Arena.MaxX) { dest.x = Arena.MaxX - Margin; wrap = true; }
            if (pos.y > Arena.MaxY) { dest.y = -Arena.MaxY + Margin; wrap = true; }
            else if (pos.y < -Arena.MaxY) { dest.y = Arena.MaxY - Margin; wrap = true; }
            if (!wrap) return;

            wrapsLeft--;
            transform.position = dest;
            if (trail == null) trail = GetComponent<RayCastTrail>();
            Arena.SetLastPos(trail, dest); // otherwise the next raycast spans the whole map
            foreach (TrailRenderer tr in GetComponentsInChildren<TrailRenderer>(true))
                if (tr != null) tr.Clear();

            WrapRing.Spawn(pos, 0.8f, 0.3f, true);
            WrapRing.Spawn(dest, 0.8f, 0.35f, false);
        }
    }

    /// Local-only purple ring flash. Exit rings collapse inward, arrival rings burst outward.
    public class WrapRing : MonoBehaviour
    {
        const int Segments = 36;
        static Material mat;

        LineRenderer lr;
        float life, age, radius;
        bool exit;

        public static void Spawn(Vector2 at, float radius, float life, bool exit)
        {
            GameObject go = new GameObject("WrapRing");
            go.transform.position = new Vector3(at.x, at.y, 0f);
            WrapRing r = go.AddComponent<WrapRing>();
            r.radius = radius;
            r.life = life;
            r.exit = exit;
            r.Build();
        }

        void Build()
        {
            if (mat == null) mat = new Material(Shader.Find("Sprites/Default"));
            lr = gameObject.AddComponent<LineRenderer>();
            lr.material = mat;
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.positionCount = Segments;
            lr.sortingOrder = 50;
            lr.sortingLayerName = "MostFront";
            lr.numCapVertices = 2;
            Apply(0f);
        }

        void Apply(float t)
        {
            float r = exit ? Mathf.Lerp(radius * 1.3f, radius * 0.2f, t) : Mathf.Lerp(radius * 0.3f, radius * 1.4f, t);
            for (int i = 0; i < Segments; i++)
            {
                float a = i * Mathf.PI * 2f / Segments;
                lr.SetPosition(i, new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f));
            }
            Color c = Arena.Purple;
            c.a = 1f - t * t;
            lr.startColor = c;
            lr.endColor = c;
            float w = Mathf.Lerp(0.35f, 0.08f, t) * Mathf.Max(0.5f, radius / 1.6f);
            lr.startWidth = w;
            lr.endWidth = w;
        }

        void Update()
        {
            age += Time.deltaTime;
            float t = life > 0f ? age / life : 1f;
            if (t >= 1f) { Destroy(gameObject); return; }
            Apply(t);
        }
    }
}
