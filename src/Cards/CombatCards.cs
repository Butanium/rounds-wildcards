using System;
using System.Collections.Generic;
using UnboundLib;
using UnboundLib.Networking;
using UnityEngine;

namespace WildCards
{
    // ---------------------------------------------------------------- SWITCHEROO
    /// Telegraphed swap. Block -> a live link to your last attacker; after WindUp you swap unless they are blocking.

    public class SwitcherooCard : WildCard
    {
        public override void SetupCard(CardInfo cardInfo, Gun gun, ApplyCardStats cardStats, CharacterStatModifiers statModifiers, Block block)
        {
            block.cdAdd = 0.5f;
            cardInfo.allowMultiple = false;   // a second copy does nothing
        }
        public override void OnAddCard(Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health, Gravity gravity, Block block, CharacterStatModifiers characterStats)
        { Fx.Add<SwitcherooEffect>(player, gun, gunAmmo); }
        public override void OnRemoveCard(Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health, Gravity gravity, Block block, CharacterStatModifiers characterStats)
        { Fx.Remove<SwitcherooEffect>(player); }
        protected override string GetTitle() { return "SWITCHEROO"; }
        protected override string GetDescription() { return "Blocking swaps you with your attacker"; }
        protected override CardInfo.Rarity GetRarity() { return CardInfo.Rarity.Rare; }
        protected override CardThemeColor.CardThemeColorType GetTheme() { return CardThemeColor.CardThemeColorType.TechWhite; }
        protected override CardInfoStat[] GetStats()
        {
            return new CardInfoStat[] {
                Stat(true, "Block", "Swap places"),
                Stat(false, "Swap delay", "0.6s"),
                Stat(false, "Target blocks", "Cancels swap"),
                Stat(false, "Block cooldown", "+0.5s")
            };
        }
    }

    public class SwitcherooEffect : StackEffect
    {
        public const float WindUp = 0.6f;

        public float lastHitTime = -100f;
        float cooldownUntil;

        public override void Init()
        {
            if (data != null && data.block != null)
                data.block.BlockAction = (Action<BlockTrigger.BlockTriggerType>)Delegate.Combine(data.block.BlockAction, new Action<BlockTrigger.BlockTriggerType>(OnBlock));
        }

        void OnDestroy()
        {
            if (data != null && data.block != null)
                data.block.BlockAction = (Action<BlockTrigger.BlockTriggerType>)Delegate.Remove(data.block.BlockAction, new Action<BlockTrigger.BlockTriggerType>(OnBlock));
        }

        /// Called on every client for real hits (Patches.cs).
        public static void OnHit(Player attacker, Player victim)
        {
            SwitcherooEffect s = Fx.Get<SwitcherooEffect>(victim);
            if (s != null) s.lastHitTime = Time.time;
        }

        // BlockAction fires on every client; only the holder's owner starts the swap (and broadcasts it).
        void OnBlock(BlockTrigger.BlockTriggerType t)
        {
            if (!IsMine || !Alive || Time.time < cooldownUntil) return;
            if (Time.time - lastHitTime > 5f) return;
            Player target = data.lastSourceOfDamage;
            if (target == null || target == player || target.data == null || target.data.dead || !target.data.isPlaying) return;
            if (!target.gameObject.activeInHierarchy) return;
            cooldownUntil = Time.time + 1.5f;
            NetworkingManager.RPC(typeof(SwitcherooNet), "RPCA_Begin", new object[] {
                player.playerID, target.playerID, (Vector2)player.transform.position, (Vector2)target.transform.position });
        }
    }

    public static class SwitcherooNet
    {
        static Material mat;
        public static Material Mat { get { if (mat == null) mat = new Material(Shader.Find("Sprites/Default")); return mat; } }

        static Player Find(int id)
        {
            if (PlayerManager.instance == null) return null;
            List<Player> ps = PlayerManager.instance.players;
            for (int i = 0; i < ps.Count; i++) if (ps[i] != null && ps[i].playerID == id) return ps[i];
            return null;
        }

        /// All clients: draw the telegraph. The target's owner judges the dodge after WindUp.
        [UnboundRPC]
        static void RPCA_Begin(int holderID, int targetID, Vector2 holderPos, Vector2 targetPos)
        {
            Player holder = Find(holderID), target = Find(targetID);
            if (holder == null || target == null) return;
            GameObject go = new GameObject("WildCards_SwitcherooTelegraph");
            go.AddComponent<SwitcherooTelegraph>().Setup(holder, target, holderPos, targetPos);
        }

        /// All clients: result decided by the target's owner. Each owner moves only its own player.
        [UnboundRPC]
        static void RPCA_Resolve(int holderID, int targetID, Vector2 holderPos, Vector2 targetPos, bool hit)
        {
            Player holder = Find(holderID), target = Find(targetID);
            SwitcherooTelegraph.Finish(holderID, targetID, hit);
            if (!hit || holder == null || target == null) return;
            if (Fx.IsMine(holder) && !holder.data.dead) Fx.Teleport(holder, targetPos);
            if (Fx.IsMine(target) && !target.data.dead) Fx.Teleport(target, holderPos);
        }

        public static void SendResolve(int holderID, int targetID, Vector2 holderPos, Vector2 targetPos, bool hit)
        {
            NetworkingManager.RPC(typeof(SwitcherooNet), "RPCA_Resolve", new object[] { holderID, targetID, holderPos, targetPos, hit });
        }
    }

    /// Line between the two locked spots + a shrinking ring on the target's mark (the dodge zone).
    public class SwitcherooTelegraph : MonoBehaviour
    {
        static readonly List<SwitcherooTelegraph> live = new List<SwitcherooTelegraph>();
        const int Segs = 32;
        Player holder, target;
        Vector2 a, b;
        LineRenderer line, ring, ringA;
        float age, endAge = -1f;
        bool judged, hitResult;

        public void Setup(Player h, Player t, Vector2 holderPos, Vector2 targetPos)
        {
            holder = h; target = t; a = holderPos; b = targetPos;
            line = Make("line", 0.12f, 50);
            line.positionCount = 2;
            line.SetPosition(0, new Vector3(a.x, a.y, 0f));
            line.SetPosition(1, new Vector3(b.x, b.y, 0f));
            ring = Make("ring", 0.12f, 51);
            ring.loop = true;
            ring.positionCount = Segs;
            ringA = Make("ringA", 0.08f, 51);
            ringA.loop = true;
            ringA.positionCount = Segs;
            Circle(ringA, a, 0.7f);
            live.Add(this);
        }

        LineRenderer Make(string n, float w, int order)
        {
            GameObject go = new GameObject(n);
            go.transform.SetParent(transform, false);
            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.material = SwitcherooNet.Mat;
            lr.useWorldSpace = true;
            lr.startWidth = w; lr.endWidth = w;
            lr.sortingLayerName = "MostFront";
            lr.sortingOrder = order;
            return lr;
        }

        static void Circle(LineRenderer lr, Vector2 c, float r)
        {
            for (int i = 0; i < Segs; i++)
            {
                float t = i * Mathf.PI * 2f / Segs;
                lr.SetPosition(i, new Vector3(c.x + Mathf.Cos(t) * r, c.y + Mathf.Sin(t) * r, 0f));
            }
        }

        public static void Finish(int holderID, int targetID, bool hit)
        {
            for (int i = live.Count - 1; i >= 0; i--)
            {
                SwitcherooTelegraph tg = live[i];
                if (tg == null) { live.RemoveAt(i); continue; }
                if (tg.holder != null && tg.target != null && tg.holder.playerID == holderID && tg.target.playerID == targetID && tg.endAge < 0f)
                {
                    tg.endAge = tg.age;
                    tg.hitResult = hit;
                }
            }
        }

        const float BlockWindow = 0.3f;   // a block started this recently still counts at the swap moment

        void Update()
        {
            age += Time.deltaTime;

            // Live link: follows both players until the swap resolves.
            if (endAge < 0f)
            {
                if (holder != null && holder.gameObject.activeInHierarchy) a = holder.transform.position;
                if (target != null && target.gameObject.activeInHierarchy) b = target.transform.position;
                line.SetPosition(0, new Vector3(a.x, a.y, 0f));
                line.SetPosition(1, new Vector3(b.x, b.y, 0f));
                Circle(ringA, a, 0.7f);
            }

            // At the swap moment the target's owner decides: blocking right now cancels it.
            if (!judged && age >= SwitcherooEffect.WindUp)
            {
                judged = true;
                if (Fx.IsMine(target))
                {
                    Block bl = target.data.block;
                    bool blocking = bl != null && (bl.IsBlocking() || bl.sinceBlock < BlockWindow);
                    bool hit = holder != null && !holder.data.dead && !target.data.dead && !blocking;
                    SwitcherooNet.SendResolve(holder != null ? holder.playerID : -1, target.playerID, a, b, hit);
                }
            }

            if (endAge >= 0f)
            {
                // Short result flash: white burst on swap, grey fizzle when blocked.
                float k = Mathf.Clamp01((age - endAge) / 0.25f);
                Color c = hitResult ? new Color(1f, 1f, 1f, 1f - k) : new Color(0.5f, 0.5f, 0.55f, 0.6f * (1f - k));
                Paint(c);
                Circle(ring, b, 1.1f * (hitResult ? 1f + k : 1f));
                if (k >= 1f) Destroy(gameObject);
                return;
            }

            // Wind-up: countdown ring around the target closes in on them; line pulses.
            float p = Mathf.Clamp01(age / SwitcherooEffect.WindUp);
            Circle(ring, b, Mathf.Lerp(3f, 1.1f, p));
            float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(age * 18f));
            Paint(new Color(0.75f, 0.95f, 1f, pulse));

            if (age > SwitcherooEffect.WindUp + 2f) Destroy(gameObject); // safety if the result never arrives
        }

        void Paint(Color c)
        {
            line.startColor = c; line.endColor = c;
            ring.startColor = c; ring.endColor = c;
            ringA.startColor = c; ringA.endColor = c;
        }

        void OnDestroy() { live.Remove(this); }
    }
}
