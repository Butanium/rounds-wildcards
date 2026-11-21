using System;
using UnityEngine;

namespace WildCards
{
    // ---------------------------------------------------------------- BOOMERANG
    public class BoomerangCard : WildCard
    {
        public override void SetupCard(CardInfo cardInfo, Gun gun, ApplyCardStats cardStats, CharacterStatModifiers statModifiers, Block block)
        {
        }
        public override void OnAddCard(Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health, Gravity gravity, Block block, CharacterStatModifiers characterStats)
        { Fx.Add<BoomerangEffect>(player, gun, gunAmmo); }
        public override void OnRemoveCard(Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health, Gravity gravity, Block block, CharacterStatModifiers characterStats)
        { Fx.Remove<BoomerangEffect>(player); }
        protected override string GetTitle() { return "BOOMERANG"; }
        protected override string GetDescription() { return "Bullets come back. Catch them for ammo"; }
        protected override CardInfo.Rarity GetRarity() { return CardInfo.Rarity.Uncommon; }
        protected override CardThemeColor.CardThemeColorType GetTheme() { return CardThemeColor.CardThemeColorType.MagicPink; }
        protected override CardInfoStat[] GetStats()
        {
            return new CardInfoStat[] {
                Stat(true, "Returning dmg", "+30%"),
                Stat(true, "Return", "Through walls"),
                Stat(true, "Catch", "+1 ammo")
            };
        }
    }

    public class BoomerangEffect : StackEffect { }

    /// Flies out at full range; turns back when it is about to hit a wall (or after OutTime).
    /// On the way back it phases through terrain (RayCastTrail.mask -> ignoreWallsMask) but still hits players.
    /// Reaching the shooter = caught: the bullet vanishes (no self-hit) and the shooter gets the ammo back.
    public class BoomerangBullet : MonoBehaviour
    {
        const float OutTime = 0.6f;
        const float CatchRadius = 1.6f;
        const float MaxLife = 3.5f;
        static readonly Color ReturnColor = new Color(1f, 0.55f, 0.9f, 0.55f);

        int stacks;
        float age, turnAge, maxDist;
        bool returning, boosted;
        float speed;
        Player owner;
        GunAmmo ammo;
        MoveTransform move;
        ProjectileHit hit;
        RayCastTrail trail;

        public void Setup(int n, Player shooter, Gun gun)
        {
            stacks = n;
            owner = shooter;
            ammo = gun != null ? gun.GetComponentInChildren<GunAmmo>() : null;
        }

        void Start()
        {
            move = GetComponent<MoveTransform>();
            hit = GetComponent<ProjectileHit>();
            trail = GetComponent<RayCastTrail>();
        }

        bool WallAhead()
        {
            if (trail == null || age < 0.08f) return false;
            // Bouncy builds keep bouncing; the boomerang only turns once bounces are spent (or at OutTime).
            RayHitReflect rf = GetComponent<RayHitReflect>();
            if (rf != null && rf.reflects > 0) return false;
            int walls = trail.mask.value & ~trail.playerMask.value;
            if (walls == 0) return false;
            Vector2 v = move.velocity;
            float spd = v.magnitude;
            if (spd < 0.01f) return false;
            float look = spd * Mathf.Max(Time.deltaTime, 0.016f) * 2.5f + 0.4f;
            foreach (RaycastHit2D h in Physics2D.RaycastAll(transform.position, v / spd, look, walls))
            {
                if (h.collider == null) continue;
                if (h.collider.GetComponentInParent<Player>() != null) continue;   // never "walls" made of players (incl. the shooter)
                return true;
            }
            return false;
        }

        void StartReturn()
        {
            returning = true;
            turnAge = 0f;
            speed = Mathf.Max(move.velocity.magnitude, 25f);
            if (trail != null)
            {
                int ghost = trail.ignoreWallsMask.value != 0 ? trail.ignoreWallsMask.value : trail.playerMask.value;
                trail.mask = ghost;
            }
            SpawnedAttack sa = GetComponent<SpawnedAttack>();
            if (sa != null) { try { sa.SetColor(ReturnColor); } catch { } }
        }

        void Update()
        {
            if (move == null || owner == null || owner.data == null) return;
            age += Time.deltaTime;
            if (age > MaxLife) return;
            if (owner.data.dead || !owner.gameObject.activeInHierarchy) return;

            if (!returning)
            {
                if (age >= OutTime || WallAhead()) StartReturn();
                else return;
            }
            turnAge += Time.deltaTime;

            Vector3 to = owner.transform.position - transform.position;
            to.z = 0f;
            float dist = to.magnitude;

            if (dist > maxDist) maxDist = dist;
            // Only catchable once it actually flew away (it spawns inside the catch radius).
            if (dist < CatchRadius && maxDist <= 3f && turnAge > 0.05f)
            {
                Destroy(gameObject);   // point-blank wall shot came straight back: no self-hit, no free ammo
                return;
            }
            if (dist < CatchRadius && maxDist > 3f)
            {
                Catch();
                return;
            }

            // Steer toward the shooter; turn rate ramps up so the arc stays readable.
            float turn = Mathf.Lerp(4f, 16f, turnAge / 0.5f) * Time.deltaTime;
            Vector3 desired = to / dist * speed;
            move.velocity = Vector3.RotateTowards(move.velocity, desired, turn, speed * 4f * Time.deltaTime);
            if (move.velocity.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(move.velocity, Vector3.forward); // hit direction uses forward

            if (!boosted && Vector3.Dot(move.velocity.normalized, to / dist) > 0.7f)
            {
                boosted = true;
                if (hit != null) hit.damage *= 1.3f + 0.2f * (stacks - 1);
            }
        }

        void Catch()
        {
            // Ammo is simulated by the owner's client only.
            if (Fx.IsMine(owner) && ammo != null)
            {
                int cur = Fx.GetAmmo(ammo);
                if (cur < ammo.maxAmmo) { Fx.SetAmmo(ammo, cur + 1); Fx.RefreshAmmoIcons(ammo); }
            }
            Destroy(gameObject);
        }
    }

    // ---------------------------------------------------------------- BALLOON ROUNDS
    public class BalloonCard : WildCard
    {
        public override void SetupCard(CardInfo cardInfo, Gun gun, ApplyCardStats cardStats, CharacterStatModifiers statModifiers, Block block)
        {
        }
        public override void OnAddCard(Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health, Gravity gravity, Block block, CharacterStatModifiers characterStats)
        { Fx.Add<BalloonEffect>(player, gun, gunAmmo); }
        public override void OnRemoveCard(Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health, Gravity gravity, Block block, CharacterStatModifiers characterStats)
        { Fx.Remove<BalloonEffect>(player); }
        protected override string GetTitle() { return "BALLOON ROUNDS"; }
        protected override string GetDescription() { return "Hits make enemies float"; }
        protected override CardInfo.Rarity GetRarity() { return CardInfo.Rarity.Uncommon; }
        protected override CardThemeColor.CardThemeColorType GetTheme() { return CardThemeColor.CardThemeColorType.ColdBlue; }
        protected override CardInfoStat[] GetStats()
        {
            return new CardInfoStat[] {
                Stat(true, "Pop on hit", "Big"),
                Stat(true, "Float on hit", "1.3s")
            };
        }
    }

    public class BalloonEffect : StackEffect
    {
        /// Runs on every client; only the victim's owner applies the float (it owns that player's physics).
        public static void OnHit(Player attacker, Player victim)
        {
            int n = Fx.Stacks<BalloonEffect>(attacker);
            if (n <= 0 || victim.data.dead) return;
            float dur = 1.3f + 0.4f * (n - 1);
            // Visible to everyone: a balloon tied to the victim while they float.
            BalloonVisual vis = victim.GetComponent<BalloonVisual>();
            if (vis == null) vis = victim.gameObject.AddComponent<BalloonVisual>();
            vis.Apply(dur);
            if (!Fx.IsMine(victim)) return;

            FloatDebuff f = victim.GetComponent<FloatDebuff>();
            if (f == null) f = victim.gameObject.AddComponent<FloatDebuff>();
            f.Apply(dur);
        }
    }

    /// Pops the owner's own player upward and cancels gravity briefly. Immunity afterwards (no juggling forever).
    /// Gravity in ROUNDS scales with time since grounded, so we zero that timer and cancel the base pull.
    public class FloatDebuff : MonoBehaviour
    {
        const float Immunity = 2.0f;
        CharacterData data;
        float timeLeft;
        float immuneLeft;
        float elapsed;
        bool active;

        void Awake() { data = GetComponent<CharacterData>(); }

        public void Apply(float duration)
        {
            if (data == null || immuneLeft > 0f) return;
            if (!active)
            {
                active = true;
                elapsed = 0f;
                timeLeft = 0f;
                // Pop: like a jump, but a bit stronger.
                Vector2 v = Fx.GetVel(data);
                float pop = Fx.JumpVel(data.player) * 1.3f;
                if (v.y < pop) v.y = pop;
                Fx.SetVel(data, v);
                data.isGrounded = false;
                data.sinceGrounded = 0f;
            }
            // Re-hits extend the float, never past 1.6x one float in total.
            timeLeft = Mathf.Min(Mathf.Max(timeLeft, duration), duration * 1.6f - elapsed);
        }

        void FixedUpdate()
        {
            if (!active || data == null) return;
            data.sinceGrounded = 0f;
            Vector2 v = Fx.GetVel(data);
            // PlayerVelocity pulls down 20/s^2; cancel it and drift up gently.
            v.y += (20f + 12f) * Time.fixedDeltaTime * TimeHandler.timeScale;
            Fx.SetVel(data, v);
        }

        void Update()
        {
            if (active)
            {
                timeLeft -= Time.deltaTime;
                elapsed += Time.deltaTime;
                if (timeLeft <= 0f || data.dead) End();
            }
            else if (immuneLeft > 0f) immuneLeft -= Time.deltaTime;
        }

        void End()
        {
            if (!active) return;
            active = false;
            immuneLeft = Immunity;
        }

        void OnDisable() { active = false; immuneLeft = 0f; }
    }

    /// Cosmetic balloon tied above a floating player (all clients). Mirrors FloatDebuff's timing rules
    /// (extend up to 1.6x, then 2s immunity) so it shows exactly while the victim floats; pops when it ends.
    public class BalloonVisual : MonoBehaviour
    {
        const float Immunity = 2.0f;
        static Sprite circle;
        static Material mat;

        CharacterData data;
        GameObject root;
        SpriteRenderer balloon, shine;
        LineRenderer str;
        float timeLeft, elapsed, immuneLeft, popT = -1f;
        bool active;

        static Sprite Circle()
        {
            if (circle != null) return circle;
            const int S = 64;
            Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01((1f - d) * S * 0.5f)));
                }
            tex.Apply();
            circle = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
            return circle;
        }

        void Awake() { data = GetComponent<CharacterData>(); }

        public void Apply(float duration)
        {
            if (immuneLeft > 0f) return;
            if (!active)
            {
                active = true;
                elapsed = 0f;
                timeLeft = 0f;
                popT = -1f;
                Build();
            }
            timeLeft = Mathf.Min(Mathf.Max(timeLeft, duration), duration * 1.6f - elapsed);
        }

        void Build()
        {
            if (root != null) return;
            if (mat == null) mat = new Material(Shader.Find("Sprites/Default"));
            root = new GameObject("WildCards_Balloon");
            GameObject b = new GameObject("balloon");
            b.transform.SetParent(root.transform, false);
            balloon = b.AddComponent<SpriteRenderer>();
            balloon.sprite = Circle();
            balloon.color = new Color(0.9f, 0.22f, 0.27f, 0.95f);
            balloon.sortingLayerName = "MostFront";
            balloon.sortingOrder = 50;
            b.transform.localScale = new Vector3(1.1f, 1.3f, 1f);
            GameObject s = new GameObject("shine");
            s.transform.SetParent(b.transform, false);
            s.transform.localPosition = new Vector3(-0.18f, 0.2f, 0f);
            s.transform.localScale = new Vector3(0.25f, 0.32f, 1f);
            shine = s.AddComponent<SpriteRenderer>();
            shine.sprite = Circle();
            shine.color = new Color(1f, 1f, 1f, 0.55f);
            shine.sortingLayerName = "MostFront";
            shine.sortingOrder = 51;
            GameObject l = new GameObject("string");
            l.transform.SetParent(root.transform, false);
            str = l.AddComponent<LineRenderer>();
            str.material = mat;
            str.useWorldSpace = true;
            str.positionCount = 3;
            str.startWidth = 0.06f; str.endWidth = 0.06f;
            str.startColor = new Color(1f, 1f, 1f, 0.85f); str.endColor = str.startColor;
            str.sortingLayerName = "MostFront";
            str.sortingOrder = 49;
        }

        void Update()
        {
            if (active)
            {
                timeLeft -= Time.deltaTime;
                elapsed += Time.deltaTime;
                if (timeLeft <= 0f || (data != null && data.dead)) { active = false; immuneLeft = Immunity; popT = 0f; }
            }
            else if (immuneLeft > 0f) immuneLeft -= Time.deltaTime;

            if (root == null) return;
            float s = Mathf.Max(transform.localScale.y, 0.5f);
            Vector3 head = transform.position + new Vector3(0f, 0.9f * s, 0f);
            float bob = Mathf.Sin(Time.time * 6f) * 0.12f;
            Vector3 bp = transform.position + new Vector3(0.25f + bob * 0.5f, 2.6f * s + bob, 0f);
            head.z = 0f; bp.z = 0f;

            if (popT >= 0f)
            {
                // Pop: quick swell + fade, then remove.
                popT += Time.deltaTime;
                float k = Mathf.Clamp01(popT / 0.15f);
                balloon.transform.localScale = new Vector3(1.1f, 1.3f, 1f) * (1f + k * 0.6f);
                Color c = balloon.color; c.a = 0.95f * (1f - k); balloon.color = c;
                shine.enabled = false; str.enabled = false;
                if (k >= 1f) { Destroy(root); root = null; }
                return;
            }

            balloon.transform.position = bp;
            Vector3 mid = Vector3.Lerp(head, bp, 0.5f) + new Vector3(-bob, 0f, 0f);
            str.SetPosition(0, head);
            str.SetPosition(1, mid);
            str.SetPosition(2, bp - new Vector3(0f, 0.6f, 0f));
        }

        void OnDisable() { active = false; immuneLeft = 0f; if (root != null) { Destroy(root); root = null; } }
        void OnDestroy() { if (root != null) Destroy(root); }
    }
}
