using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnboundLib;
using UnboundLib.Networking;

namespace WildCards
{
    // ---------------------------------------------------------------- ROCKET JUMP
    public class RocketJumpCard : WildCard
    {
        public override void SetupCard(CardInfo cardInfo, Gun gun, ApplyCardStats cardStats, CharacterStatModifiers statModifiers, Block block)
        {
            gun.reloadTimeAdd = 0.25f;
        }
        public override void OnAddCard(Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health, Gravity gravity, Block block, CharacterStatModifiers characterStats)
        { Fx.Add<RocketJumpEffect>(player, gun, gunAmmo); }
        public override void OnRemoveCard(Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health, Gravity gravity, Block block, CharacterStatModifiers characterStats)
        { Fx.Remove<RocketJumpEffect>(player); }
        protected override string GetTitle() { return "ROCKET JUMP"; }
        protected override string GetDescription() { return "Shots blast you backwards"; }
        protected override CardInfo.Rarity GetRarity() { return CardInfo.Rarity.Uncommon; }
        protected override CardThemeColor.CardThemeColorType GetTheme() { return CardThemeColor.CardThemeColorType.FirepowerYellow; }
        protected override CardInfoStat[] GetStats()
        {
            return new CardInfoStat[] {
                Stat(true, "Recoil", "Huge"),
                Stat(true, "Air jumps from recoil", "+1"),
                Stat(false, "Reload time", "+0.25s")
            };
        }
    }

    public class RocketJumpEffect : StackEffect
    {
        int lastFrame = -1;
        float pushedThisFrame;
        bool refreshedThisAir;

        void Update()
        {
            if (data != null && data.isGrounded) refreshedThisAir = false;
        }

        /// Called for each bullet the owner's main gun fires (on every client; only the owner acts).
        public void OnShot()
        {
            if (!IsMine || !Alive) return;
            if (data.isGrounded && data.input.aimDirection.y > -0.5f) return; // on the ground only floor-shots launch you
            if (Time.frameCount != lastFrame) { lastFrame = Time.frameCount; pushedThisFrame = 0f; }

            // One volley = a big blast (~1.8 jumps); extra pellets in the same frame add only a little.
            float jv = Fx.JumpVel(player);
            float volley = jv * (1.8f + 0.5f * (stacks - 1));
            float cap = volley * 1.25f;
            float push = pushedThisFrame <= 0f ? volley : Mathf.Min(jv * 0.15f, cap - pushedThisFrame);
            if (push <= 0f) return;
            pushedThisFrame += push;

            Vector2 aim = data.input.aimDirection;
            if (aim.sqrMagnitude < 0.01f) return;
            aim.Normalize();
            Vector2 blast = -aim;
            Vector2 v = Fx.GetVel(data);
            // Remove any velocity fighting the blast (e.g. falling when you shoot down), then add it.
            float against = Vector2.Dot(v, blast);
            if (against < 0f) v -= blast * against;
            v += blast * push;
            Fx.SetVel(data, v);
            data.isGrounded = false;
            // Gravity scales with time since grounded; a real jump resets it, so the blast does too.
            data.sinceGrounded = 0f;

            if (aim.y < -0.7f && !refreshedThisAir)
            {
                refreshedThisAir = true;
                if (data.currentJumps < 1) data.currentJumps = 1;
            }
        }
    }

    // ---------------------------------------------------------------- METEOR
    public class MeteorCard : WildCard
    {
        public override void SetupCard(CardInfo cardInfo, Gun gun, ApplyCardStats cardStats, CharacterStatModifiers statModifiers, Block block)
        {
            statModifiers.jump = 0.85f;
        }
        public override void OnAddCard(Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health, Gravity gravity, Block block, CharacterStatModifiers characterStats)
        { Fx.Add<MeteorEffect>(player, gun, gunAmmo); }
        public override void OnRemoveCard(Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health, Gravity gravity, Block block, CharacterStatModifiers characterStats)
        { Fx.Remove<MeteorEffect>(player); }
        protected override string GetTitle() { return "METEOR"; }
        protected override string GetDescription() { return "Hold down to dive and slam"; }
        protected override CardInfo.Rarity GetRarity() { return CardInfo.Rarity.Uncommon; }
        protected override CardThemeColor.CardThemeColorType GetTheme() { return CardThemeColor.CardThemeColorType.DestructiveRed; }
        protected override CardInfoStat[] GetStats()
        {
            return new CardInfoStat[] {
                Stat(true, "Slam damage", "30"),
                Stat(true, "Slam", "Launches enemies"),
                Stat(false, "Slam cooldown", "2.5s"),
                Stat(false, "Jump height", "-15%")
            };
        }
    }

    public class MeteorEffect : StackEffect
    {
        float diveTime;
        float cooldown;
        bool wasGrounded = true;

        const float Radius = 6f;

        void Update()
        {
            if (!IsMine || data == null) return;
            if (cooldown > 0f) cooldown -= Time.deltaTime;
            if (!Alive) { diveTime = 0f; wasGrounded = true; return; }

            bool grounded = data.isGrounded;
            if (!grounded)
            {
                if (data.input.direction.y < -0.5f)
                {
                    diveTime += Time.deltaTime;
                    // Dive: snap straight down at ~1.8 jumps of speed and keep it while held.
                    Vector2 v = Fx.GetVel(data);
                    float diveSpeed = Fx.JumpVel(player) * 1.8f;
                    if (v.y > -diveSpeed) v.y = -diveSpeed;
                    Fx.SetVel(data, v);
                }
            }
            else if (!wasGrounded)
            {
                if (diveTime >= 0.12f && cooldown <= 0f) Slam();
                diveTime = 0f;
            }
            if (grounded) diveTime = 0f;
            wasGrounded = grounded;
        }

        void Slam()
        {
            cooldown = 2.5f;
            float dmg = 30f + 15f * (stacks - 1);
            float launch = Fx.JumpVel(player) * (1.3f + 0.3f * (stacks - 1)); // velocity given to enemies
            Vector2 me = player.transform.position;

            // Visible shockwave ring on every client.
            try { NetworkingManager.RPC(typeof(MeteorNet), "RPCA_Slam", new object[] { (Vector2)me, Radius }); }
            catch (System.Exception e) { Debug.LogError("[WildCards] " + e); }

            List<Player> players = PlayerManager.instance.players;
            for (int i = 0; i < players.Count; i++)
            {
                Player o = players[i];
                if (o == null || o == player || o.data == null || o.data.dead || !o.data.isPlaying) continue;
                if (o.teamID == player.teamID) continue;
                Vector2 d = (Vector2)o.transform.position - me;
                float dist = d.magnitude;
                if (dist > Radius) continue;
                float falloff = Mathf.Lerp(1f, 0.5f, dist / Radius);
                Vector2 dir = dist > 0.01f ? d / dist : Vector2.up;
                dir = (dir + Vector2.up * 0.8f).normalized;
                // Both calls broadcast from this (owner) client only. Both respect the target's block.
                o.data.healthHandler.CallTakeDamage(dir * dmg * falloff, o.transform.position, null, player, true);
                // With forceIgnoreMass the velocity change is force/100 (HealthHandler.TakeForce IL).
                o.data.healthHandler.CallTakeForce(dir * launch * falloff * 100f, ForceMode2D.Impulse, true, false, 0.4f);
            }
        }
    }

    /// Cosmetic shockwave ring, spawned locally on each client.
    public static class MeteorNet
    {
        static Material mat;

        [UnboundRPC]
        static void RPCA_Slam(Vector2 pos, float radius)
        {
            GameObject go = new GameObject("WildCards_MeteorRing");
            go.transform.position = new Vector3(pos.x, pos.y, 0f);
            go.AddComponent<MeteorRing>().Setup(radius, Mat());
        }

        static Material Mat()
        {
            if (mat == null) mat = new Material(Shader.Find("Sprites/Default"));
            return mat;
        }
    }

    public class MeteorRing : MonoBehaviour
    {
        const int Segments = 40;
        const float Life = 0.35f;
        LineRenderer lr;
        float radius, age;

        public void Setup(float r, Material m)
        {
            radius = r;
            lr = gameObject.AddComponent<LineRenderer>();
            lr.material = m;
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.positionCount = Segments;
            lr.sortingLayerName = "MostFront";
            lr.sortingOrder = 50;
            Draw(0.2f);
        }

        void Draw(float r)
        {
            for (int i = 0; i < Segments; i++)
            {
                float a = i * Mathf.PI * 2f / Segments;
                lr.SetPosition(i, new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f));
            }
        }

        void Update()
        {
            age += Time.deltaTime;
            float t = Mathf.Clamp01(age / Life);
            Draw(Mathf.Lerp(0.5f, radius, 1f - (1f - t) * (1f - t)));
            Color c = new Color(1f, 0.55f, 0.15f, 1f - t);
            lr.startColor = c; lr.endColor = c;
            float w = Mathf.Lerp(0.5f, 0.1f, t);
            lr.startWidth = w; lr.endWidth = w;
            if (age >= Life) Destroy(gameObject);
        }
    }
}
