using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace WildCards
{
    /// Card illustrations: PNGs embedded in the DLL as "WildCards.Art.<file>.png" (built from art/png/).
    /// The game instantiates CardInfo.cardArt under "Canvas/Front/Background/Art" (a UI canvas) with
    /// localPosition 0 and localScale 1, so the template is a UI Image that stretches to its parent.
    public static class CardArt
    {
        static readonly Dictionary<string, GameObject> cache = new Dictionary<string, GameObject>();

        /// "PORTAL ROUNDS" -> "portal_rounds"
        public static string FileName(string title)
        {
            return title.ToLowerInvariant().Replace(' ', '_');
        }

        public static GameObject Get(string title)
        {
            string key = FileName(title);
            GameObject go;
            if (cache.TryGetValue(key, out go)) return go;
            go = null;
            try
            {
                Sprite sprite = LoadSprite(key);
                if (sprite != null)
                {
                    go = new GameObject("WildArt_" + key, typeof(RectTransform));
                    Object.DontDestroyOnLoad(go);
                    Image img = go.AddComponent<Image>();
                    img.sprite = sprite;
                    img.preserveAspect = true;
                    img.raycastTarget = false;
                    go.AddComponent<ArtFitter>();
                }
            }
            catch (System.Exception e) { Debug.LogError("[WildCards] art " + key + ": " + e); }
            cache[key] = go;
            return go;
        }

        static Sprite LoadSprite(string key)
        {
            Assembly asm = typeof(CardArt).Assembly;
            using (Stream s = asm.GetManifestResourceStream("WildCards.Art." + key + ".png"))
            {
                if (s == null) return null;
                byte[] bytes = new byte[s.Length];
                int read = 0;
                while (read < bytes.Length) { int n = s.Read(bytes, read, bytes.Length - read); if (n <= 0) break; read += n; }
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(tex, bytes)) return null;
                tex.filterMode = FilterMode.Bilinear;
                tex.wrapMode = TextureWrapMode.Clamp;
                Object.DontDestroyOnLoad(tex);
                return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            }
        }
    }

    /// Stretches the art over its parent slot (with a little padding); falls back to a fixed size if the slot has no size.
    public class ArtFitter : MonoBehaviour
    {
        static bool logged;

        void Start()
        {
            RectTransform rt = GetComponent<RectTransform>();
            RectTransform parent = transform.parent as RectTransform;
            if (rt == null) return;
            if (parent != null && parent.rect.width > 10f && parent.rect.height > 10f)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                float pad = Mathf.Min(parent.rect.width, parent.rect.height) * 0.06f;
                rt.offsetMin = new Vector2(pad, pad);
                rt.offsetMax = new Vector2(-pad, -pad);
            }
            else
            {
                rt.sizeDelta = new Vector2(450f, 450f);
            }
            rt.localPosition = Vector3.zero;
            if (!logged)
            {
                logged = true;
                Debug.Log("[WildCards] art slot size: " + (parent != null ? parent.rect.size.ToString() : "none"));
            }
        }
    }
}
