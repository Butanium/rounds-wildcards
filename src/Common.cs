using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnboundLib.Cards;
using UnboundLib.GameModes;
using UnityEngine;

namespace WildCards
{
    /// Base for all WildCards cards: no art, shared mod name.
    public abstract class WildCard : CustomCard
    {
        protected override GameObject GetCardArt() { return CardArt.Get(GetTitle()); }
        public override string GetModName() { return WildCardsPlugin.ModInitials; }

        protected static CardInfoStat Stat(bool positive, string stat, string amount)
        {
            CardInfoStat s = new CardInfoStat();
            s.positive = positive;
            s.stat = stat;
            s.amount = amount;
            s.simepleAmount = CardInfoStat.SimpleAmount.notAssigned;
            return s;
        }
    }

    /// A per-player effect component; one instance per player per card type, counting copies.
    public abstract class StackEffect : MonoBehaviour
    {
        public int stacks;
        public Player player;
        public CharacterData data;
        public Gun gun;
        public GunAmmo gunAmmo;

        public bool IsMine
        {
            get { return data != null && data.view != null && data.view.IsMine; }
        }

        public bool Alive
        {
            get { return data != null && !data.dead && data.isPlaying; }
        }

        /// Called once when the component is created.
        public virtual void Init() { }
        /// Called whenever the number of copies changes.
        public virtual void OnStacksChanged() { }
    }

    public static class Fx
    {
        public static T Add<T>(Player p, Gun gun, GunAmmo ammo) where T : StackEffect
        {
            T e = p.gameObject.GetComponent<T>();
            if (e == null)
            {
                e = p.gameObject.AddComponent<T>();
                e.player = p;
                e.data = p.data;
                e.gun = gun;
                e.gunAmmo = ammo;
                e.stacks = 1;
                e.Init();
            }
            else
            {
                e.stacks++;
            }
            e.OnStacksChanged();
            return e;
        }

        public static void Remove<T>(Player p) where T : StackEffect
        {
            if (p == null) return;
            T e = p.gameObject.GetComponent<T>();
            if (e == null) return;
            e.stacks--;
            if (e.stacks <= 0) UnityEngine.Object.DestroyImmediate(e);
            else e.OnStacksChanged();
        }

        public static int Stacks<T>(Player p) where T : StackEffect
        {
            if (p == null) return 0;
            T e = p.GetComponent<T>();
            return e == null ? 0 : e.stacks;
        }

        public static T Get<T>(Player p) where T : Component
        {
            if (p == null) return null;
            return p.GetComponent<T>();
        }

        public static bool IsMine(Player p)
        {
            return p != null && p.data != null && p.data.view != null && p.data.view.IsMine;
        }

        /// Velocity change of one normal jump. From PlayerJump.Jump: dv = upForce * 0.01 * stats.jump (mass cancels).
        public static float JumpVel(Player p)
        {
            PlayerJump j = p.GetComponent<PlayerJump>();
            float v = j != null ? j.upForce * 0.01f : 0f;
            if (v < 5f || v > 200f) v = 25f;
            return v;
        }

        /// Move a player that this client owns to a world position.
        public static void Teleport(Player p, Vector3 pos)
        {
            PlayerCollision col = p.GetComponent<PlayerCollision>();
            if (col != null) col.IgnoreWallForFrames(3);
            pos.z = p.transform.position.z;
            p.transform.position = pos;
            if (p.data.playerVel != null) SetVel(p.data, Vector2.zero);
        }

        // Private game fields, accessed through cached reflection.
        static readonly FieldInfo velField = AccessTools.Field(typeof(PlayerVelocity), "velocity");
        static readonly FieldInfo ammoField = AccessTools.Field(typeof(GunAmmo), "currentAmmo");

        public static Vector2 GetVel(CharacterData d)
        {
            if (d == null || d.playerVel == null || velField == null) return Vector2.zero;
            return (Vector2)velField.GetValue(d.playerVel);
        }

        public static void SetVel(CharacterData d, Vector2 v)
        {
            if (d == null || d.playerVel == null || velField == null) return;
            velField.SetValue(d.playerVel, v);
        }

        public static int GetAmmo(GunAmmo a)
        {
            if (a == null || ammoField == null) return 0;
            return (int)ammoField.GetValue(a);
        }

        public static void SetAmmo(GunAmmo a, int n)
        {
            if (a == null || ammoField == null) return;
            ammoField.SetValue(a, n);
        }

        // ReDrawTotalBullets refills the gun; SetActiveBullets(false) only redraws the icons.
        static readonly MethodInfo setActiveBullets = AccessTools.Method(typeof(GunAmmo), "SetActiveBullets");

        public static void RefreshAmmoIcons(GunAmmo a)
        {
            if (a == null || setActiveBullets == null) return;
            try { setActiveBullets.Invoke(a, new object[] { false }); } catch { }
        }
    }

    /// Game-wide state shared by cards (identical on every client).
    public static class WildState
    {
        public static int pointCounter = 0;

        public static void Register()
        {
            GameModeManager.AddHook(GameModeHooks.HookGameStart, OnGameStart);
            GameModeManager.AddHook(GameModeHooks.HookPointStart, OnPointStart);
        }

        static IEnumerator OnGameStart(IGameModeHandler gm)
        {
            pointCounter = 0;
            Hooks.Fire(Hooks.GameStart);
            // Fresh game: no cards are held yet, so strip any leftover effects.
            if (PlayerManager.instance != null)
            {
                foreach (Player p in PlayerManager.instance.players)
                {
                    if (p == null) continue;
                    foreach (StackEffect e in p.GetComponents<StackEffect>())
                        UnityEngine.Object.DestroyImmediate(e);
                }
            }
            yield break;
        }

        static IEnumerator OnPointStart(IGameModeHandler gm)
        {
            pointCounter++;
            Hooks.Fire(Hooks.PointStart);
            yield break;
        }
    }
}
