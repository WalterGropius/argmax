using System;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace ArgmaxClient
{
    /// <summary>
    /// Type that the build can't ship: Futura (headlines) and Alte Haas Grotesk (reading) are used when the
    /// player's OS has them; otherwise the bundled OFL faces in the stylesheet stand in (Michroma, Jost).
    /// </summary>
    public static class TypeKit
    {
        static bool probed;
        static FontDefinition display, body;
        static bool hasDisplay, hasBody;

        static void Probe()
        {
            if (probed) return;
            probed = true;
#if !UNITY_WEBGL
            try
            {
                var names = Font.GetOSInstalledFontNames();
                string Find(params string[] wanted)
                {
                    foreach (var w in wanted) foreach (var n in names) if (string.Equals(n, w, StringComparison.OrdinalIgnoreCase)) return n;
                    return null;
                }
                var f = Find("Futura", "Futura PT", "Futura Std");
                if (f != null) { var fa = FontAsset.CreateFontAsset(Font.CreateDynamicFontFromOSFont(f, 32)); if (fa != null) { display = FontDefinition.FromSDFFont(fa); hasDisplay = true; } }
                var b = Find("Alte Haas Grotesk", "AlteHaasGrotesk", "Alte Haas Grotesk Regular");
                if (b != null) { var fb = FontAsset.CreateFontAsset(Font.CreateDynamicFontFromOSFont(b, 32)); if (fb != null) { body = FontDefinition.FromSDFFont(fb); hasBody = true; } }
            }
            catch (Exception e) { Debug.Log("ARGMAX: OS fonts unavailable (" + e.Message + ")"); }
#endif
        }

        /// <summary>Apply OS faces to everything under root marked .display (headlines) or .body (reading).</summary>
        public static void Apply(VisualElement root)
        {
            Probe();
            if (hasDisplay) root.Query(className: "display").ForEach(e => e.style.unityFontDefinition = display);
            if (hasBody) root.Query(className: "body").ForEach(e => e.style.unityFontDefinition = body);
        }
    }

    public enum Glyph { Circle, Square, Triangle }

    /// <summary>A Bauhaus primary shape: circle, square or triangle, filled.</summary>
    public sealed class ShapeGlyph : VisualElement
    {
        public Glyph Shape;
        public Color Color;
        public ShapeGlyph(Glyph shape, Color color, float size)
        {
            Shape = shape; Color = color;
            style.width = size; style.height = size; style.flexShrink = 0;
            generateVisualContent += ctx => Design.Draw(ctx.painter2D, Shape, contentRect, Color, 0f);
        }
    }

    public static class Design
    {
        public static readonly Color Ink = new Color(0.055f, 0.051f, 0.043f);
        public static readonly Color Bone = new Color(0.925f, 0.894f, 0.827f);
        public static readonly Color Cobalt = new Color(0.184f, 0.380f, 1.0f);
        public static readonly Color Vermilion = new Color(1.0f, 0.275f, 0.149f);
        public static readonly Color Chrome = new Color(0.957f, 0.761f, 0.051f);

        public static Glyph ChapterGlyph(int chapter) => (Glyph)((chapter - 1) % 3);
        public static Color ChapterColor(int chapter) => chapter % 3 == 1 ? Cobalt : chapter % 3 == 2 ? Chrome : Vermilion;

        public static void Draw(Painter2D p, Glyph g, Rect r, Color c, float rotDeg)
        {
            var center = r.center;
            float s = Mathf.Min(r.width, r.height) * 0.5f;
            p.fillColor = c;
            p.BeginPath();
            switch (g)
            {
                case Glyph.Circle:
                    p.Arc(center, s, 0f, 360f);
                    break;
                case Glyph.Square:
                    Poly(p, center, s * 1.41421f, 4, 45f + rotDeg);
                    break;
                default:
                    Poly(p, center + new Vector2(0, s * 0.18f), s * 1.18f, 3, -90f + rotDeg);
                    break;
            }
            p.ClosePath();
            p.Fill();
        }

        static void Poly(Painter2D p, Vector2 c, float radius, int n, float startDeg)
        {
            for (int i = 0; i < n; i++)
            {
                float a = (startDeg + 360f * i / n) * Mathf.Deg2Rad;
                var v = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                if (i == 0) p.MoveTo(v); else p.LineTo(v);
            }
        }
    }

    /// <summary>
    /// "argmax", lowercase and constructed, after Herbert Bayer's universal alphabet: every letter is a circle,
    /// an arc or a bar on one x-height. No font involved. The first bowl holds a vermilion disc.
    /// </summary>
    public sealed class Logotype : VisualElement
    {
        public Color Ink = Design.Bone;
        public float Weight = 0.17f;

        public Logotype()
        {
            generateVisualContent += Draw;
        }

        /// <summary>Total width in units of x-height, for layout.</summary>
        public const float Width = 0.8f + 1f + 0.18f + 0.8f + 0.18f + 1f + 0.18f + 1.5f + 0.18f + 1f + 0.18f + 0.86f;

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            float h = Mathf.Min(r.height * 0.62f, r.width / (Width - 0.8f + 0.2f));
            float w = h * Weight;
            float baseY = r.yMin + r.height * 0.72f;
            float top = baseY - h;
            float x = r.xMin + w * 0.5f;
            float gap = h * 0.18f;
            var p = ctx.painter2D;
            p.strokeColor = Ink;
            p.lineWidth = w;
            p.lineCap = LineCap.Butt;
            p.lineJoin = LineJoin.Miter;
            float rad = (h - w) * 0.5f;

            void Stem(float sx, float y0, float y1) { p.BeginPath(); p.MoveTo(new Vector2(sx, y0)); p.LineTo(new Vector2(sx, y1)); p.Stroke(); }
            void Ring(Vector2 c, float rr) { p.BeginPath(); p.Arc(c, rr, 0f, 360f); p.Stroke(); }
            void ArcStroke(Vector2 c, float rr, float a0, float a1) { p.BeginPath(); p.Arc(c, rr, a0, a1); p.Stroke(); }

            // a, with the vermilion disc
            var ca = new Vector2(x + h * 0.5f - w * 0.5f, baseY - h * 0.5f);
            p.fillColor = Design.Vermilion;
            p.BeginPath(); p.Arc(ca, rad - w * 0.5f, 0f, 360f); p.Fill();
            Ring(ca, rad);
            Stem(ca.x + rad, top - w * 0.5f, baseY);
            x = ca.x + rad + w * 0.5f + gap;
            // r
            float rx = x + w * 0.5f;
            Stem(rx, top - w * 0.5f, baseY);
            ArcStroke(new Vector2(rx + rad, top + rad), rad, 180f, 300f);
            x = rx + rad * 1.55f + gap;
            // g
            var cg = new Vector2(x + rad + w * 0.5f, baseY - h * 0.5f);
            Ring(cg, rad);
            Stem(cg.x + rad, top - w * 0.5f, baseY + h * 0.22f);
            ArcStroke(new Vector2(cg.x, baseY + h * 0.22f), rad, 0f, 160f);
            x = cg.x + rad + w * 0.5f + gap;
            // m
            float m0 = x + w * 0.5f, m2 = m0 + h * 1.5f - w, m1 = (m0 + m2) * 0.5f;
            float ar = (m1 - m0) * 0.5f;
            Stem(m0, top - w * 0.5f, baseY);
            ArcStroke(new Vector2(m0 + ar, top + ar), ar, 180f, 360f);
            ArcStroke(new Vector2(m1 + ar, top + ar), ar, 180f, 360f);
            Stem(m1, top + ar, baseY);
            Stem(m2, top + ar, baseY);
            x = m2 + w * 0.5f + gap;
            // a (the second one is plain)
            var ca2 = new Vector2(x + rad + w * 0.5f, baseY - h * 0.5f);
            Ring(ca2, rad);
            Stem(ca2.x + rad, top - w * 0.5f, baseY);
            x = ca2.x + rad + w * 0.5f + gap;
            // x, with a chrome triangle at the crossing
            float xw = h * 0.86f;
            p.BeginPath(); p.MoveTo(new Vector2(x, top)); p.LineTo(new Vector2(x + xw, baseY)); p.Stroke();
            p.BeginPath(); p.MoveTo(new Vector2(x + xw, top)); p.LineTo(new Vector2(x, baseY)); p.Stroke();
            var cx = new Vector2(x + xw * 0.5f, baseY - h * 0.5f);
            p.fillColor = Design.Chrome;
            p.BeginPath(); p.MoveTo(cx + new Vector2(0, -w * 1.2f)); p.LineTo(cx + new Vector2(w * 1.05f, w * 0.65f)); p.LineTo(cx + new Vector2(-w * 1.05f, w * 0.65f)); p.ClosePath(); p.Fill();
        }
    }

    /// <summary>
    /// The menu's hero: a slowly turning Bauhaus composition (a cobalt sun, a vermilion square, a chrome
    /// triangle, bone rules and an orbit) set behind the logotype. Kandinsky by way of a screensaver.
    /// </summary>
    public sealed class Composition : VisualElement
    {
        float t;
        public Composition()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            schedule.Execute(() => { t += 1f / 30f; MarkDirtyRepaint(); }).Every(33);
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (r.width < 10) return;
            var p = ctx.painter2D;
            float H = r.height;
            // cobalt sun, partly off the right edge
            var sun = new Vector2(r.xMax - H * 0.42f, r.yMin + H * 0.46f);
            p.fillColor = Design.Cobalt;
            p.BeginPath(); p.Arc(sun, H * 0.42f + Mathf.Sin(t * 0.6f) * 2f, 0f, 360f); p.Fill();
            // orbit line and a bone moon
            p.strokeColor = new Color(Design.Bone.r, Design.Bone.g, Design.Bone.b, 0.35f);
            p.lineWidth = 1.2f;
            p.BeginPath(); p.Arc(sun, H * 0.62f, 0f, 360f); p.Stroke();
            float a = t * 0.25f;
            var moon = sun + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * H * 0.62f;
            p.fillColor = Design.Bone;
            p.BeginPath(); p.Arc(moon, H * 0.045f, 0f, 360f); p.Fill();
            // vermilion square, slowly rotating, overlapping the sun
            Design.Draw(p, Glyph.Square, new Rect(sun.x - H * 0.78f, sun.y - H * 0.02f, H * 0.36f, H * 0.36f), Design.Vermilion, t * 6f);
            // chrome triangle
            Design.Draw(p, Glyph.Triangle, new Rect(sun.x + H * 0.08f, sun.y + H * 0.08f, H * 0.3f, H * 0.3f), Design.Chrome, Mathf.Sin(t * 0.4f) * 8f);
            // bone rules: one long horizon, one short vertical
            p.strokeColor = Design.Bone;
            p.lineWidth = 2f;
            p.BeginPath(); p.MoveTo(new Vector2(r.xMin, r.yMax - 6)); p.LineTo(new Vector2(r.xMax, r.yMax - 6)); p.Stroke();
            p.lineWidth = 6f;
            p.BeginPath(); p.MoveTo(new Vector2(sun.x - H * 0.9f, r.yMin + H * 0.1f)); p.LineTo(new Vector2(sun.x - H * 0.9f, r.yMin + H * 0.5f)); p.Stroke();
        }
    }
}
