using System;
using UnityEngine;

namespace WildCards
{
    // ---------------------------------------------------------------- BOOMERANG
    public class BoomerangCard : WildCard
    {
        public override void SetupCard(CardInfo cardInfo, Gun gun, ApplyCardStats cardStats, CharacterStatModifiers statModifiers, Block block)
        {
            gun.projectileSpeed = 0.85f;
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
                Stat(true, "Catch", "+1 ammo"),
                Stat(false, "Bullet speed", "-15%")
            };
        }
    }

    public class BoomerangEffect : StackEffect { }

    /// Flies out, then steers back to the shooter's current position. Hits anything on the way.
    /// Reaching the shooter = caught: the bullet vanishes (no self-hit) and the shooter gets the ammo back.
    public class BoomerangBullet : MonoBehaviour
    {
        const float OutTime = 0.35f;
        const float CatchRadius = 1.6f;
        const float MaxLife = 3f;

        int stacks;
        float age;
        bool returning, boosted;
        float speed;
        Player owner;
        GunAmmo ammo;
        MoveTransform move;
        ProjectileHit hit;

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
        }

        void Update()
        {
            if (move == null || owner == null || owner.data == null) return;
            age += Time.deltaTime;
            if (age < OutTime || age > MaxLife) return;
            if (owner.data.dead || !owner.gameObject.activeInHierarchy) return;

            if (!returning)
            {
                returning = true;
                speed = Mathf.Max(move.velocity.magnitude, 25f);
            }

            Vector3 to = owner.transform.position - transform.position;
            to.z = 0f;
            float dist = to.magnitude;

            if (dist < CatchRadius)
            {
                Catch();
                return;
            }

            // Steer toward the shooter; turn rate ramps up so the arc stays readable.
            float turn = Mathf.Lerp(3f, 14f, (age - OutTime) / 0.6f) * Time.deltaTime;
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
            gun.damage = 0.85f;
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
                Stat(true, "Float on hit", "0.9s"),
                Stat(false, "Damage", "-15%")
            };
        }
    }

    public class BalloonEffect : StackEffect
    {
        /// Runs on every client; only the victim's owner applies the float (it owns that player's physics).
        public static void OnHit(Player attacker, Player victim)
        {
            int n = Fx.Stacks<BalloonEffect>(attacker);
            if (n <= 0 || !Fx.IsMine(victim) || victim.data.dead) return;
            FloatDebuff f = victim.GetComponent<FloatDebuff>();
            if (f == null) f = victim.gameObject.AddComponent<FloatDebuff>();
            f.Apply(0.9f + 0.4f * (n - 1));
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
                float pop = Fx.JumpVel(data.player) * 0.9f;
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
            v.y += (20f + 6f) * Time.fixedDeltaTime * TimeHandler.timeScale;
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
}
