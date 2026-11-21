using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WildCards
{
    /// Shared hook points. Card files subscribe in their static Init() (called by CardRegistry).
    /// All of these fire on EVERY client; gate owner-only work with Fx.IsMine(...).
    public static class Hooks
    {
        /// Gun.BulletInit postfix: (gun, bullet, useAmmo). Runs for every bullet on every client,
        /// after ApplyProjectileStats (RayHitReflect added if reflects>0) and after GunAmmo.Shoot.
        public static Action<Gun, GameObject, bool> BulletInit;
        /// HealthHandler.DoDamage postfix for real hits only (victim HP went down): (victim, attacker or null, damage).
        public static Action<Player, Player, Vector2> Damaged;
        /// ProjectileHit.RPCA_DoHit postfix: (projectile, hitPoint, hitNormal, vel, viewID, colliderID, wasBlocked).
        /// viewID != -1 => hit a PhotonView (player/prop); viewID == -1 && colliderID != -1 => static map collider.
        public static Action<ProjectileHit, Vector2, Vector2, Vector2, int, int, bool> ProjectileHitAction;
        /// Point start (every client, via UnboundLib GameModeHooks.HookPointStart).
        public static Action PointStart;
        /// Game start.
        public static Action GameStart;

        public static void Fire(Action a)
        {
            if (a == null) return;
            foreach (Delegate d in a.GetInvocationList())
            {
                try { ((Action)d)(); } catch (Exception e) { Debug.LogError("[WildCards] " + e); }
            }
        }
    }

    [HarmonyPatch(typeof(Gun), "BulletInit")]
    static class BulletInitPatch
    {
        static void Postfix(Gun __instance, GameObject __0, bool __4)
        {
            try
            {
                Player owner = __instance.player;
                GameObject bullet = __0;
                if (bullet == null) return;

                if (owner != null)
                {
                    int n = Fx.Stacks<BoomerangEffect>(owner);
                    if (n > 0) bullet.AddComponent<BoomerangBullet>().Setup(n, owner, __instance);

                    RocketJumpEffect rj = Fx.Get<RocketJumpEffect>(owner);
                    if (rj != null && owner.data.weaponHandler != null && owner.data.weaponHandler.gun == __instance)
                        rj.OnShot();
                }

                if (Hooks.BulletInit != null)
                    foreach (Delegate d in Hooks.BulletInit.GetInvocationList())
                    {
                        try { ((Action<Gun, GameObject, bool>)d)(__instance, bullet, __4); }
                        catch (Exception e) { Debug.LogError("[WildCards] " + e); }
                    }
            }
            catch (Exception e) { Debug.LogError("[WildCards] " + e); }
        }
    }

    /// Every damage instance funnels through DoDamage (runs on all clients).
    [HarmonyPatch(typeof(HealthHandler), "DoDamage")]
    static class DoDamagePatch
    {
        static Player VictimOf(HealthHandler h)
        {
            Player p = h.GetComponent<Player>();
            if (p == null) p = h.GetComponentInParent<Player>();
            return p;
        }

        static void Prefix(HealthHandler __instance, out float __state)
        {
            CharacterData cd = __instance.GetComponent<CharacterData>();
            __state = cd != null ? cd.health : 0f;
        }

        static void Postfix(HealthHandler __instance, Vector2 __0, Player __4, float __state)
        {
            try
            {
                Player victim = VictimOf(__instance);
                Player attacker = __4;
                if (victim == null || victim.data == null) return;
                // DoDamage returns early when dead / not playing / respawning / blocking: only react to real hits.
                if (victim.data.health >= __state) return;

                if (attacker != null && attacker != victim)
                {
                    BalloonEffect.OnHit(attacker, victim);
                    SwitcherooEffect.OnHit(attacker, victim);
                }

                if (Hooks.Damaged != null)
                    foreach (Delegate d in Hooks.Damaged.GetInvocationList())
                    {
                        try { ((Action<Player, Player, Vector2>)d)(victim, attacker, __0); }
                        catch (Exception e) { Debug.LogError("[WildCards] " + e); }
                    }
            }
            catch (Exception e) { Debug.LogError("[WildCards] " + e); }
        }
    }

    [HarmonyPatch(typeof(ProjectileHit), "RPCA_DoHit")]
    static class ProjectileHitPatch
    {
        static void Postfix(ProjectileHit __instance, Vector2 __0, Vector2 __1, Vector2 __2, int __3, int __4, bool __5)
        {
            if (Hooks.ProjectileHitAction == null) return;
            foreach (Delegate d in Hooks.ProjectileHitAction.GetInvocationList())
            {
                try { ((Action<ProjectileHit, Vector2, Vector2, Vector2, int, int, bool>)d)(__instance, __0, __1, __2, __3, __4, __5); }
                catch (Exception e) { Debug.LogError("[WildCards] " + e); }
            }
        }
    }

    /// Testing aid: boost how often WildCards cards are offered (config WildCardChance).
    /// Only enabled cards (CardChoice.cards respects the Toggle Cards menu) are considered.
    [HarmonyPatch(typeof(CardChoice), "GetRanomCard")]
    static class OfferBoostPatch
    {
        static void Postfix(CardChoice __instance, ref GameObject __result)
        {
            try
            {
                float p = WildCardsPlugin.WildChance != null ? WildCardsPlugin.WildChance.Value : 0f;
                if (p <= 0f || UnityEngine.Random.value >= p || __instance.cards == null) return;
                List<CardInfo> ours = new List<CardInfo>();
                foreach (CardInfo c in __instance.cards)
                    if (c != null && c.GetComponent<WildCard>() != null) ours.Add(c);
                if (ours.Count == 0) return;
                __result = ours[UnityEngine.Random.Range(0, ours.Count)].gameObject;
            }
            catch (Exception e) { Debug.LogError("[WildCards] " + e); }
        }
    }
}
