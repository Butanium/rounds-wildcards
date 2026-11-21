using System;
using System.Collections.Generic;
using TMPro;
using UnboundLib;
using UnboundLib.Networking;
using UnityEngine;
using WildCards;

namespace WildCards.HotPotato
{
    // ---------------------------------------------------------------- HOT POTATO
    public class HotPotatoCard : WildCard
    {
        static bool inited;

        public static void Init()
        {
            if (inited) return;
            inited = true;
            Hooks.Damaged += HotPotatoEffect.OnDamaged;
            Hooks.PointStart += HotPotatoEffect.OnPointStart;
            Hooks.GameStart += HotPotatoEffect.OnPointStart;
        }

        public override void SetupCard(CardInfo cardInfo, Gun gun, ApplyCardStats cardStats, CharacterStatModifiers statModifiers, Block block)
        {
            gun.attackSpeed = 1.15f;
        }
        public override void OnAddCard(Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health, Gravity gravity, Block block, CharacterStatModifiers characterStats)
        { Fx.Add<HotPotatoEffect>(player, gun, gunAmmo); }
        public override void OnRemoveCard(Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health, Gravity gravity, Block block, CharacterStatModifiers characterStats)
        { Fx.Remove<HotPotatoEffect>(player); }
        protected override string GetTitle() { return "HOT POTATO"; }
        protected override string GetDescription() { return "Hits pass a ticking bomb"; }
        protected override CardInfo.Rarity GetRarity() { return CardInfo.Rarity.Uncommon; }
        protected override CardThemeColor.CardThemeColorType GetTheme() { return CardThemeColor.CardThemeColorType.DestructiveRed; }
        protected override CardInfoStat[] GetStats()
        {
            return new CardInfoStat[] {
                Stat(true, "Bomb", "5s fuse"),
                Stat(false, "Attack speed", "-15%"),
                Stat(false, "Bomb", "Can come back")
            };
        }
    }

    /// Lives on the card owner. The owner's client is the only authority for the bomb state.
    public class HotPotatoEffect : StackEffect
    {
        public const float Fuse = 5f;
        public const float Lockout = 0.6f;
        public const float Rearm = 4f;
        public const float TouchDist = 1.3f;

        static readonly List<HotPotatoEffect> all = new List<HotPotatoEffect>();
        public static bool exploding;

        // Owner-side authoritative state.
        int holderID = -1;
        float fuseEnd;
        float lockoutUntil;
        float rearmAt;

        public override void Init()
        {
            if (!all.Contains(this)) all.Add(this);
        }

        void OnDestroy()
        {
            all.Remove(this);
        }

        void OnDisable()
        {
            // Owner died / removed / round ended: the bomb disappears and re-arms later.
            if (!IsMine) return;
            if (holderID >= 0)
            {
                holderID = -1;
                SendHolder(-1, -1, 0f);
            }
            rearmAt = Time.time + Rearm;
        }

        float DamageFrac { get { return stacks >= 2 ? 0.6f : 0.45f; } }
        float Radius { get { return stacks >= 2 ? 5f : 4f; } }

        public static void OnPointStart()
        {
            for (int i = 0; i < all.Count; i++)
            {
                HotPotatoEffect e = all[i];
                if (e == null) continue;
                e.holderID = -1;
                e.rearmAt = 0f;
                e.lockoutUntil = 0f;
            }
            BombVisual.ClearAll();
        }

        static bool Living(Player p)
        {
            return p != null && p.data != null && !p.data.dead && p.data.isPlaying && p.data.health > 0f && p.gameObject.activeInHierarchy;
        }

        public static Player Find(int id)
        {
            if (id < 0 || PlayerManager.instance == null) return null;
            List<Player> ps = PlayerManager.instance.players;
            for (int i = 0; i < ps.Count; i++)
                if (ps[i] != null && ps[i].playerID == id) return ps[i];
            return null;
        }

        /// Hooks.Damaged: runs on all clients, only the bomb owner's client acts.
        public static void OnDamaged(Player victim, Player attacker, Vector2 dmg)
        {
            if (exploding || victim == null || attacker == null || victim == attacker) return;
            for (int i = 0; i < all.Count; i++)
            {
                HotPotatoEffect e = all[i];
                if (e == null || !e.IsMine) continue;
                try { e.HandleHit(victim, attacker); }
                catch (Exception ex) { Debug.LogError("[WildCards] " + ex); }
            }
        }

        void HandleHit(Player victim, Player attacker)
        {
            float now = Time.time;
            if (holderID < 0)
            {
                // Plant: owner's first hit on an enemy.
                if (attacker != player || !Alive || now < rearmAt) return;
                if (victim.teamID == player.teamID || !Living(victim)) return;
                holderID = victim.playerID;
                fuseEnd = now + Fuse;
                lockoutUntil = now + Lockout;
                SendHolder(holderID, player.playerID, Fuse);
            }
            else
            {
                // Pass: the holder hurts someone else.
                if (attacker.playerID != holderID || victim.playerID == holderID) return;
                if (now < lockoutUntil || !Living(victim)) return;
                PassTo(victim);
            }
        }

        void PassTo(Player target)
        {
            int prev = holderID;
            holderID = target.playerID;
            lockoutUntil = Time.time + Lockout;
            SendHolder(holderID, prev, Mathf.Max(0f, fuseEnd - Time.time));
        }

        void SendHolder(int holder, int prev, float remaining)
        {
            try
            {
                NetworkingManager.RPC(typeof(HotPotatoNet), "RPCA_SetHolder", new object[] { player.playerID, holder, prev, remaining });
            }
            catch (Exception ex) { Debug.LogError("[WildCards] " + ex); }
        }

        void Update()
        {
            if (!IsMine || holderID < 0) return;
            try
            {
                Player holder = Find(holderID);
                if (!Living(holder))
                {
                    // Holder died or left: the bomb is removed.
                    holderID = -1;
                    rearmAt = Time.time + Rearm;
                    SendHolder(-1, -1, 0f);
                    return;
                }

                if (Time.time >= fuseEnd)
                {
                    Detonate(holder);
                    return;
                }

                if (Time.time >= lockoutUntil)
                {
                    // Touch pass: nearest living player (other team) within reach.
                    Vector2 hp = holder.transform.position;
                    Player best = null;
                    float bestD = TouchDist;
                    List<Player> ps = PlayerManager.instance.players;
                    for (int i = 0; i < ps.Count; i++)
                    {
                        Player o = ps[i];
                        if (o == null || o == holder || !Living(o)) continue;
                        if (o.teamID == holder.teamID) continue;
                        float d = Vector2.Distance(hp, (Vector2)o.transform.position);
                        if (d < bestD) { bestD = d; best = o; }
                    }
                    if (best != null) PassTo(best);
                }
            }
            catch (Exception ex) { Debug.LogError("[WildCards] " + ex); }
        }

        void Detonate(Player holder)
        {
            Vector2 pos = holder.transform.position;
            float frac = DamageFrac;
            float radius = Radius;
            holderID = -1;
            rearmAt = Time.time + Rearm;

            exploding = true;
            try
            {
                List<Player> ps = PlayerManager.instance.players;
                for (int i = 0; i < ps.Count; i++)
                {
                    Player o = ps[i];
                    if (!Living(o)) continue;
                    bool isHolder = o == holder;
                    Vector2 d = (Vector2)o.transform.position - pos;
                    float dist = d.magnitude;
                    if (!isHolder && dist > radius) continue;
                    float factor = isHolder ? 1f : 0.5f;
                    Vector2 dir;
                    if (isHolder || dist < 0.05f) dir = new Vector2(UnityEngine.Random.Range(-0.6f, 0.6f), 1f).normalized;
                    else dir = (d / dist + Vector2.up * 0.5f).normalized;
                    float dmg = o.data.maxHealth * frac * factor;
                    try
                    {
                        o.data.healthHandler.CallTakeDamage(dir * dmg, o.transform.position, null, player, true);
                        float force = 2f * Fx.JumpVel(o) * 100f * factor;
                        o.data.healthHandler.CallTakeForce(dir * force, ForceMode2D.Impulse, true, false, 0.5f);
                    }
                    catch (Exception ex) { Debug.LogError("[WildCards] " + ex); }
                }
            }
            finally { exploding = false; }

            try
            {
                NetworkingManager.RPC(typeof(HotPotatoNet), "RPCA_Boom", new object[] { player.playerID, pos, radius });
            }
            catch (Exception ex) { Debug.LogError("[WildCards] " + ex); }
        }
    }

    public static class HotPotatoNet
    {
        [UnboundRPC]
        static void RPCA_SetHolder(int ownerID, int holderID, int prevID, float remaining)
        {
            try
            {
                if (holderID < 0) { BombVisual.Remove(ownerID); return; }
                BombVisual.Set(ownerID, holderID, remaining);
                if (prevID >= 0 && prevID != holderID)
                {
                    Player a = HotPotatoEffect.Find(prevID);
                    Player b = HotPotatoEffect.Find(holderID);
                    if (a != null && b != null) Whoosh.Spawn(a.transform.position, b.transform.position, BombVisual.OwnerColor(ownerID));
                }
            }
            catch (Exception ex) { Debug.LogError("[WildCards] " + ex); }
        }

        [UnboundRPC]
        static void RPCA_Boom(int ownerID, Vector2 pos, float radius)
        {
            try
            {
                BombVisual.Remove(ownerID);
                Boom.Spawn(pos, radius, BombVisual.OwnerColor(ownerID));
            }
            catch (Exception ex) { Debug.LogError("[WildCards] " + ex); }
        }
    }

    // ================================================================ visuals (local, per client)

    static class Gfx
    {
        static Material mat;
        static Mesh disc;
        static AudioClip beep;
        static TMP_FontAsset font;
        static bool fontSearched;

        static int topLayer;
        static bool topLayerSearched;

        /// Front-most sorting layer, so the bomb is never hidden behind map / player layers.
        public static int TopLayer
        {
            get
            {
                if (!topLayerSearched)
                {
                    topLayerSearched = true;
                    try
                    {
                        SortingLayer[] ls = SortingLayer.layers;
                        int best = int.MinValue;
                        for (int i = 0; i < ls.Length; i++)
                        {
                            int v = SortingLayer.GetLayerValueFromID(ls[i].id);
                            if (v > best) { best = v; topLayer = ls[i].id; }
                        }
                    }
                    catch { topLayer = 0; }
                }
                return topLayer;
            }
        }

        public static Material Mat
        {
            get
            {
                if (mat == null) mat = new Material(Shader.Find("Sprites/Default"));
                return mat;
            }
        }

        public static Mesh Disc
        {
            get
            {
                if (disc != null) return disc;
                const int n = 32;
                Vector3[] v = new Vector3[n + 1];
                int[] t = new int[n * 3];
                v[0] = Vector3.zero;
                for (int i = 0; i < n; i++)
                {
                    float a = i * Mathf.PI * 2f / n;
                    v[i + 1] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                    t[i * 3] = 0;
                    t[i * 3 + 1] = 1 + (i + 1) % n;
                    t[i * 3 + 2] = 1 + i;
                }
                Color[] cols = new Color[n + 1];
                for (int i = 0; i <= n; i++) cols[i] = Color.white;
                disc = new Mesh();
                disc.vertices = v;
                disc.colors = cols;
                disc.triangles = t;
                disc.RecalculateBounds();
                return disc;
            }
        }

        public static MeshRenderer MakeDisc(Transform parent, float radius, Color c, int order)
        {
            GameObject go = new GameObject("disc");
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(radius, radius, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = Disc;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.material = Mat;
            mr.material.color = c;
            mr.sortingLayerID = TopLayer;
            mr.sortingOrder = order;
            return mr;
        }

        public static LineRenderer MakeLine(Transform parent, int points, float width, Color c, int order, bool world)
        {
            GameObject go = new GameObject("line");
            go.transform.SetParent(parent, false);
            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = Mat;
            lr.useWorldSpace = world;
            lr.positionCount = points;
            lr.startWidth = width;
            lr.endWidth = width;
            lr.startColor = c;
            lr.endColor = c;
            lr.sortingLayerID = TopLayer;
            lr.sortingOrder = order;
            lr.numCapVertices = 2;
            return lr;
        }

        public static void Circle(LineRenderer lr, Vector3 center, float r)
        {
            int n = lr.positionCount;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / (n - 1);
                lr.SetPosition(i, center + new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f));
            }
        }

        public static TMP_FontAsset Font
        {
            get
            {
                if (font == null && !fontSearched)
                {
                    fontSearched = true;
                    try
                    {
                        font = TMP_Settings.defaultFontAsset;
                        if (font == null)
                        {
                            TMP_FontAsset[] fs = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                            if (fs != null && fs.Length > 0) font = fs[0];
                        }
                    }
                    catch { }
                }
                return font;
            }
        }

        public static AudioClip Beep
        {
            get
            {
                if (beep != null) return beep;
                const int rate = 44100;
                int len = (int)(rate * 0.07f);
                float[] s = new float[len];
                for (int i = 0; i < len; i++)
                {
                    float tt = (float)i / rate;
                    float env = Mathf.Clamp01(1f - (float)i / len) * Mathf.Clamp01(i / 200f);
                    s[i] = Mathf.Sin(2f * Mathf.PI * 1250f * tt) * env * 0.6f;
                }
                beep = AudioClip.Create("hotpotato_beep", len, 1, rate, false);
                beep.SetData(s, 0);
                return beep;
            }
        }
    }

    /// A bomb floating over the holder's head, with a fuse spark and a big countdown number.
    public class BombVisual : MonoBehaviour
    {
        static readonly Dictionary<int, BombVisual> byOwner = new Dictionary<int, BombVisual>();

        int ownerID;
        int holderID;
        float fuseEnd;
        Player holder;
        Color ownerColor;

        GameObject body;
        MeshRenderer bodyR;
        LineRenderer rim;
        LineRenderer fuse;
        MeshRenderer spark;
        MeshRenderer glow;
        TextMeshPro text;
        AudioSource audio;
        float nextBeep;

        public static Color OwnerColor(int ownerID)
        {
            try
            {
                if (PlayerManager.instance != null)
                {
                    Player o = HotPotatoEffect.Find(ownerID);
                    PlayerSkin s = PlayerManager.instance.GetColorFromPlayer(o != null ? o.teamID : ownerID);
                    if (s != null) return s.color;
                }
            }
            catch { }
            return new Color(1f, 0.5f, 0.1f);
        }

        public static void Set(int ownerID, int holderID, float remaining)
        {
            BombVisual v;
            if (!byOwner.TryGetValue(ownerID, out v) || v == null)
            {
                GameObject go = new GameObject("HotPotatoBomb_" + ownerID);
                v = go.AddComponent<BombVisual>();
                v.ownerID = ownerID;
                v.Build();
                byOwner[ownerID] = v;
            }
            v.holderID = holderID;
            v.holder = HotPotatoEffect.Find(holderID);
            v.fuseEnd = Time.time + remaining;
            v.nextBeep = 0f;
        }

        public static void Remove(int ownerID)
        {
            BombVisual v;
            if (byOwner.TryGetValue(ownerID, out v))
            {
                byOwner.Remove(ownerID);
                if (v != null) Destroy(v.gameObject);
            }
        }

        public static void ClearAll()
        {
            foreach (BombVisual v in byOwner.Values)
                if (v != null) Destroy(v.gameObject);
            byOwner.Clear();
            Whoosh.ClearAll();
        }

        void Build()
        {
            ownerColor = OwnerColor(ownerID);
            body = new GameObject("body");
            body.transform.SetParent(transform, false);
            Transform b = body.transform;

            glow = Gfx.MakeDisc(b, 0.95f, new Color(1f, 0.2f, 0.1f, 0f), 49);
            bodyR = Gfx.MakeDisc(b, 0.6f, new Color(0.08f, 0.08f, 0.08f, 1f), 50);
            rim = Gfx.MakeLine(b, 33, 0.12f, ownerColor, 51, false);
            Gfx.Circle(rim, Vector3.zero, 0.6f);
            // Small highlight so it reads as a round bomb.
            Gfx.MakeDisc(b, 0.13f, new Color(1f, 1f, 1f, 0.35f), 51).transform.localPosition = new Vector3(-0.25f, 0.25f, 0f);

            fuse = Gfx.MakeLine(b, 4, 0.09f, new Color(0.85f, 0.75f, 0.55f), 51, false);
            fuse.SetPosition(0, new Vector3(0.25f, 0.5f, 0f));
            fuse.SetPosition(1, new Vector3(0.4f, 0.75f, 0f));
            fuse.SetPosition(2, new Vector3(0.6f, 0.85f, 0f));
            fuse.SetPosition(3, new Vector3(0.75f, 0.8f, 0f));
            spark = Gfx.MakeDisc(b, 0.16f, new Color(1f, 0.85f, 0.2f), 52);
            spark.transform.localPosition = new Vector3(0.78f, 0.8f, 0f);

            try
            {
                GameObject tg = new GameObject("count", typeof(RectTransform));
                tg.transform.SetParent(b, false);
                tg.transform.localPosition = new Vector3(0f, -0.03f, 0f);
                text = tg.AddComponent<TextMeshPro>();
                TMP_FontAsset f = Gfx.Font;
                if (f != null) text.font = f;
                text.text = "5";
                text.fontSize = 8f;
                text.fontStyle = FontStyles.Bold;
                text.alignment = TextAlignmentOptions.Center;
                text.enableWordWrapping = false;
                text.color = Color.white;
                text.rectTransform.sizeDelta = new Vector2(2f, 2f);
                text.renderer.sortingLayerID = Gfx.TopLayer;
                text.sortingOrder = 53;
            }
            catch (Exception ex) { Debug.LogError("[WildCards] HotPotato text: " + ex); text = null; }

            try
            {
                audio = gameObject.AddComponent<AudioSource>();
                audio.playOnAwake = false;
                audio.spatialBlend = 0f;
                audio.volume = 0.35f;
            }
            catch { audio = null; }
        }

        int IndexOnHolder()
        {
            int idx = 0;
            foreach (KeyValuePair<int, BombVisual> kv in byOwner)
                if (kv.Value != null && kv.Value != this && kv.Value.holderID == holderID && kv.Key < ownerID) idx++;
            return idx;
        }

        void LateUpdate()
        {
            try
            {
                if (HotPotatoEffect.Find(ownerID) == null) { Destroy(gameObject); return; } // owner left the game
                if (holder == null) holder = HotPotatoEffect.Find(holderID);
                bool show = holder != null && holder.gameObject.activeInHierarchy && holder.data != null && !holder.data.dead;
                if (body.activeSelf != show) body.SetActive(show);
                if (!show) return;

                float remaining = Mathf.Max(0f, fuseEnd - Time.time);
                float s = Mathf.Max(0.6f, holder.transform.localScale.y);
                Vector3 hp = holder.transform.position;
                transform.position = new Vector3(hp.x, hp.y + 1.1f * s + 0.9f + IndexOnHolder() * 1.5f, hp.z - 1f);

                bool critical = remaining <= 1.5f;
                // Flash speed ramps up near the end.
                float freq = critical ? Mathf.Lerp(14f, 5f, remaining / 1.5f) : 1.5f;
                float wave = 0.5f + 0.5f * Mathf.Sin(Time.time * freq * Mathf.PI * 2f);
                float pulse = critical ? 1.15f + 0.2f * wave : 1f + 0.05f * wave;
                body.transform.localScale = new Vector3(pulse, pulse, 1f);
                body.transform.localRotation = Quaternion.Euler(0f, 0f, critical ? Mathf.Sin(Time.time * 40f) * 8f : 0f);

                bodyR.material.color = critical ? Color.Lerp(new Color(0.08f, 0.08f, 0.08f), new Color(0.9f, 0.1f, 0.05f), wave) : new Color(0.08f, 0.08f, 0.08f);
                glow.material.color = new Color(1f, critical ? 0.15f : 0.45f, 0.05f, critical ? 0.25f + 0.35f * wave : 0.12f + 0.1f * wave);

                // Fuse burns down: spark slides along the fuse toward the bomb.
                float burn = Mathf.Clamp01(remaining / HotPotatoEffect.Fuse);
                Vector3 tip = Vector3.Lerp(new Vector3(0.25f, 0.5f, 0f), new Vector3(0.78f, 0.8f, 0f), burn);
                fuse.SetPosition(3, tip);
                fuse.SetPosition(2, Vector3.Lerp(new Vector3(0.25f, 0.5f, 0f), new Vector3(0.6f, 0.85f, 0f), burn));
                fuse.SetPosition(1, Vector3.Lerp(new Vector3(0.25f, 0.5f, 0f), new Vector3(0.4f, 0.75f, 0f), burn));
                spark.transform.localPosition = tip;
                float flick = 0.12f + UnityEngine.Random.value * 0.12f;
                spark.transform.localScale = new Vector3(flick, flick, 1f);
                spark.material.color = UnityEngine.Random.value < 0.5f ? new Color(1f, 0.9f, 0.3f) : new Color(1f, 0.45f, 0.1f);

                if (text != null)
                {
                    text.text = critical ? remaining.ToString("0.0") : Mathf.CeilToInt(remaining).ToString();
                    text.fontSize = critical ? 6.5f : 8f;
                    text.color = critical && wave > 0.5f ? new Color(1f, 0.95f, 0.3f) : Color.white;
                }

                if (audio != null && Time.time >= nextBeep && remaining > 0.05f)
                {
                    audio.pitch = critical ? 1.4f : 1f;
                    audio.PlayOneShot(Gfx.Beep, critical ? 1f : 0.6f);
                    nextBeep = Time.time + (critical ? Mathf.Lerp(0.08f, 0.25f, remaining / 1.5f) : 1f);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[WildCards] " + ex);
                enabled = false;
            }
        }

        void OnDestroy()
        {
            BombVisual cur;
            if (byOwner.TryGetValue(ownerID, out cur) && cur == this) byOwner.Remove(ownerID);
            foreach (MeshRenderer mr in GetComponentsInChildren<MeshRenderer>(true))
                if (mr != null && mr.GetComponent<TextMeshPro>() == null && mr.sharedMaterial != null && mr.sharedMaterial != Gfx.Mat) Destroy(mr.sharedMaterial);
        }
    }

    /// Arc from the old holder to the new one.
    public class Whoosh : MonoBehaviour
    {
        static readonly List<Whoosh> live = new List<Whoosh>();
        LineRenderer lr;
        Vector3 a, b, c;
        Color col;
        float t;
        const int N = 20;
        const float Grow = 0.15f, Life = 0.55f;

        public static void Spawn(Vector3 from, Vector3 to, Color color)
        {
            GameObject go = new GameObject("HotPotatoWhoosh");
            Whoosh w = go.AddComponent<Whoosh>();
            w.a = from; w.c = to;
            w.a.z = w.c.z = -1f;
            float lift = 1.5f + 0.25f * Vector2.Distance(from, to);
            w.b = (w.a + w.c) * 0.5f + Vector3.up * Mathf.Min(lift, 5f);
            w.col = Color.Lerp(color, new Color(1f, 0.6f, 0.1f), 0.5f);
            w.lr = Gfx.MakeLine(go.transform, N, 0.3f, w.col, 48, true);
            w.lr.endWidth = 0.08f;
            live.Add(w);
            w.Draw(0f);
        }

        public static void ClearAll()
        {
            for (int i = 0; i < live.Count; i++) if (live[i] != null) Destroy(live[i].gameObject);
            live.Clear();
        }

        Vector3 Bez(float u)
        {
            float m = 1f - u;
            return m * m * a + 2f * m * u * b + u * u * c;
        }

        void Draw(float grow)
        {
            float end = Mathf.Clamp01(grow);
            for (int i = 0; i < N; i++) lr.SetPosition(i, Bez(end * i / (N - 1f)));
        }

        void Update()
        {
            t += Time.deltaTime;
            Draw(t / Grow);
            float alpha = t < Grow ? 1f : Mathf.Clamp01(1f - (t - Grow) / (Life - Grow));
            Color c0 = col; c0.a = alpha * 0.3f;
            Color c1 = col; c1.a = alpha;
            lr.startColor = c0;
            lr.endColor = c1;
            if (t >= Life) Destroy(gameObject);
        }

        void OnDestroy() { live.Remove(this); }
    }

    /// Explosion: vanilla Explosive bullet effect (damage stripped) plus a blast ring the size of the radius.
    public class Boom : MonoBehaviour
    {
        static GameObject vanillaFx;
        static bool vanillaSearched;

        LineRenderer ring;
        MeshRenderer flash;
        float radius;
        float t;
        const float Life = 0.45f;

        static GameObject VanillaFx
        {
            get
            {
                if (vanillaFx == null && !vanillaSearched)
                {
                    vanillaSearched = true;
                    try
                    {
                        GameObject card = Resources.Load<GameObject>("0 cards/Explosive bullet");
                        Gun g = card != null ? card.GetComponent<Gun>() : null;
                        if (g != null && g.objectsToSpawn != null && g.objectsToSpawn.Length > 0) vanillaFx = g.objectsToSpawn[0].effect;
                    }
                    catch (Exception ex) { Debug.LogError("[WildCards] HotPotato fx: " + ex); }
                }
                return vanillaFx;
            }
        }

        public static void Spawn(Vector2 pos, float radius, Color color)
        {
            SpawnVanilla(pos, radius);

            GameObject go = new GameObject("HotPotatoBoom");
            go.transform.position = new Vector3(pos.x, pos.y, -1f);
            Boom bm = go.AddComponent<Boom>();
            bm.radius = radius;
            bm.flash = Gfx.MakeDisc(go.transform, radius, new Color(1f, 0.6f, 0.15f, 0.55f), 47);
            bm.ring = Gfx.MakeLine(go.transform, 49, 0.35f, Color.Lerp(color, Color.white, 0.3f), 48, false);
            Gfx.Circle(bm.ring, Vector3.zero, 0.3f);
        }

        static void SpawnVanilla(Vector2 pos, float radius)
        {
            GameObject prefab = VanillaFx;
            if (prefab == null) return;
            GameObject inst = null;
            bool wasActive = prefab.activeSelf;
            try
            {
                // Instantiate inactive so Explosion.Start never runs, then strip anything that deals damage.
                prefab.SetActive(false);
                inst = Instantiate(prefab, new Vector3(pos.x, pos.y, 0f), Quaternion.identity);
            }
            catch (Exception ex) { Debug.LogError("[WildCards] " + ex); }
            finally { prefab.SetActive(wasActive); }
            if (inst == null) return;
            try
            {
                // Explosion.Start only calls Explode() (overlap + damage/force) when auto is set; the visuals
                // are separate particle/line effects, so keep the component (modifiers look it up) but never fire it.
                foreach (Explosion e in inst.GetComponentsInChildren<Explosion>(true))
                {
                    e.auto = false;
                    e.damage = 0f;
                    e.force = 0f;
                    e.stun = 0f;
                    e.slow = 0f;
                    e.silence = 0f;
                    e.range = 0f;
                }
                foreach (DamageBox d in inst.GetComponentsInChildren<DamageBox>(true)) DestroyImmediate(d);
                foreach (DealDamageToPlayer d in inst.GetComponentsInChildren<DealDamageToPlayer>(true)) DestroyImmediate(d);
                float k = radius / 3f;
                inst.transform.localScale = inst.transform.localScale * Mathf.Clamp(k, 1f, 2.5f);
                inst.SetActive(true);
                Destroy(inst, 4f);
            }
            catch (Exception ex)
            {
                Debug.LogError("[WildCards] HotPotato vanilla fx: " + ex);
                if (inst != null) Destroy(inst);
            }
        }

        void Update()
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / Life);
            float r = Mathf.Lerp(0.3f, radius, 1f - (1f - u) * (1f - u));
            Gfx.Circle(ring, Vector3.zero, r);
            Color rc = ring.startColor; rc.a = 1f - u;
            ring.startColor = rc; ring.endColor = rc;
            ring.startWidth = ring.endWidth = Mathf.Lerp(0.45f, 0.1f, u);
            flash.material.color = new Color(1f, 0.6f, 0.15f, 0.55f * (1f - u) * (1f - u));
            if (t >= Life) Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (flash != null && flash.sharedMaterial != null && flash.sharedMaterial != Gfx.Mat) Destroy(flash.sharedMaterial);
        }
    }
}
