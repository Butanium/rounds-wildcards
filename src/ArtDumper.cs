using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace WildCards
{
    /// Debug tool (config Debug.DumpVanillaArt): renders every vanilla card's art to PNG
    /// (BepInEx/WildCardsArtDump/<card>_<t>.png, a few animation frames) plus a hierarchy description,
    /// so new card art can match the original style exactly.
    public class ArtDumper : MonoBehaviour
    {
        const int W = 1100, H = 865;  // vanilla card art root size
        const int Layer = 31;
        static readonly float[] Times = { 0.05f, 0.5f, 1.0f };

        public static void Run(string outDir)
        {
            GameObject go = new GameObject("WildCards_ArtDumper");
            DontDestroyOnLoad(go);
            go.AddComponent<ArtDumper>().outDir = outDir;
        }

        string outDir;

        IEnumerator Start()
        {
            // Wait for the card list to exist.
            float waited = 0f;
            while ((CardChoice.instance == null || CardChoice.instance.cards == null || CardChoice.instance.cards.Length == 0) && waited < 60f)
            { waited += 0.5f; yield return new WaitForSeconds(0.5f); }
            yield return new WaitForSeconds(2f);
            if (CardChoice.instance == null) { Debug.LogError("[WildCards] ArtDumper: no CardChoice"); Destroy(gameObject); yield break; }

            Directory.CreateDirectory(outDir);
            RenderTexture rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            GameObject camGo = new GameObject("WildDumpCam");
            camGo.transform.position = new Vector3(5000f, 5000f, -100f);
            Camera cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.cullingMask = 1 << Layer;
            cam.targetTexture = rt;
            cam.enabled = false;

            GameObject canvasGo = new GameObject("WildDumpCanvas", typeof(RectTransform));
            canvasGo.layer = Layer;
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 50f;

            StringBuilder info = new StringBuilder();
            int count = 0;
            foreach (CardInfo ci in CardChoice.instance.cards)
            {
                if (ci == null || ci.cardArt == null || ci.GetComponent<WildCard>() != null) continue;
                string name = Safe(ci.cardName);
                GameObject art = null;
                try
                {
                    art = Instantiate(ci.cardArt, canvasGo.transform, false);
                    RectTransform rtArt = art.GetComponent<RectTransform>();
                    info.AppendLine("=== " + ci.cardName + "  (theme " + ci.colorTheme + ", rarity " + ci.rarity + ")");
                    if (rtArt != null) info.AppendLine("root rect size " + rtArt.rect.size + " sizeDelta " + rtArt.sizeDelta + " anchors " + rtArt.anchorMin + "-" + rtArt.anchorMax + " scale " + rtArt.localScale);
                    Describe(art.transform, 0, info);
                    SetLayer(art.transform);
                    art.transform.localPosition = Vector3.zero;
                }
                catch (Exception e) { Debug.LogError("[WildCards] dump " + name + ": " + e); }
                if (art == null) continue;

                float t0 = 0f;
                for (int k = 0; k < Times.Length; k++)
                {
                    yield return new WaitForSeconds(Times[k] - t0);
                    t0 = Times[k];
                    try
                    {
                        Canvas.ForceUpdateCanvases();
                        cam.Render();
                        RenderTexture prev = RenderTexture.active;
                        RenderTexture.active = rt;
                        Texture2D tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
                        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                        tex.Apply();
                        RenderTexture.active = prev;
                        File.WriteAllBytes(Path.Combine(outDir, name + "_" + k + ".png"), ImageConversion.EncodeToPNG(tex));
                        Destroy(tex);
                    }
                    catch (Exception e) { Debug.LogError("[WildCards] render " + name + ": " + e); }
                }
                Destroy(art);
                count++;
            }
            File.WriteAllText(Path.Combine(outDir, "hierarchy.txt"), info.ToString());
            Debug.Log("[WildCards] ArtDumper wrote " + count + " cards to " + outDir);
            Destroy(canvasGo); Destroy(camGo); rt.Release();
            Destroy(gameObject);
        }

        static void SetLayer(Transform t)
        {
            t.gameObject.layer = Layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i));
        }

        static void Describe(Transform t, int depth, StringBuilder sb)
        {
            string pad = new string(' ', depth * 2);
            StringBuilder line = new StringBuilder(pad + t.name);
            RectTransform r = t as RectTransform;
            if (r != null) line.Append(" rect=" + r.rect.size + " pos=" + r.anchoredPosition);
            line.Append(" rot=" + t.localEulerAngles.z.ToString("0") + " scale=" + t.localScale);
            foreach (Component c in t.GetComponents<Component>())
            {
                if (c == null || c is Transform) continue;
                line.Append(" [" + c.GetType().Name);
                Image img = c as Image;
                if (img != null) line.Append(" sprite=" + (img.sprite != null ? img.sprite.name : "none") + " color=" + img.color + " type=" + img.type);
                SpriteRenderer sr = c as SpriteRenderer;
                if (sr != null) line.Append(" sprite=" + (sr.sprite != null ? sr.sprite.name : "none") + " color=" + sr.color);
                line.Append("]");
            }
            sb.AppendLine(line.ToString());
            for (int i = 0; i < t.childCount; i++) Describe(t.GetChild(i), depth + 1, sb);
        }

        static string Safe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "unnamed";
            StringBuilder b = new StringBuilder();
            foreach (char c in s.ToLowerInvariant()) b.Append(char.IsLetterOrDigit(c) ? c : '_');
            return b.ToString();
        }
    }
}
