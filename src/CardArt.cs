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
                    img.preserveAspect = false;
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

    /// Vanilla card art roots are 1100x865 RectTransforms centered in the slot at scale 1 (see art/ref dumps); match that.
    public class ArtFitter : MonoBehaviour
    {
        void Start()
        {
            RectTransform rt = GetComponent<RectTransform>();
            if (rt == null) return;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(1100f, 865f);
            rt.localPosition = Vector3.zero;
        }
    }
}
