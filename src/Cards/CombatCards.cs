using System;
using UnityEngine;

namespace WildCards
{
    // ---------------------------------------------------------------- SWITCHEROO
    public class SwitcherooCard : WildCard
    {
        public override void SetupCard(CardInfo cardInfo, Gun gun, ApplyCardStats cardStats, CharacterStatModifiers statModifiers, Block block)
        {
            block.cdAdd = 0.5f;
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
                Stat(false, "Block cooldown", "+0.5s")
            };
        }
    }

    public class SwitcherooEffect : StackEffect
    {
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

        public static void OnHit(Player attacker, Player victim)
        {
            SwitcherooEffect s = Fx.Get<SwitcherooEffect>(victim);
            if (s != null) s.lastHitTime = Time.time;
        }

        // Fires on every client; each client moves only the player it owns.
        void OnBlock(BlockTrigger.BlockTriggerType t)
        {
            if (!Alive || Time.time < cooldownUntil) return;
            if (Time.time - lastHitTime > 5f) return;
            Player target = data.lastSourceOfDamage;
            if (target == null || target == player || target.data == null || target.data.dead || !target.data.isPlaying) return;
            if (!target.gameObject.activeInHierarchy) return;
            cooldownUntil = Time.time + 1.5f;

            Vector3 myPos = player.transform.position;
            Vector3 theirPos = target.transform.position;
            if (IsMine) Fx.Teleport(player, theirPos);
            if (Fx.IsMine(target)) Fx.Teleport(target, myPos);
        }
    }

}
