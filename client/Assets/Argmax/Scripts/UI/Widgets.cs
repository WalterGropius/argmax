using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ArgmaxClient
{
    /// <summary>A five-pointed star drawn with Painter2D, so it renders in any font.</summary>
    public sealed class StarIcon : VisualElement
    {
        bool on;
        Color onColor = Design.Chrome;
        float pop = 1f;

        public StarIcon(float size = 28f)
        {
            style.width = size; style.height = size;
            generateVisualContent += Draw;
        }

        public bool On
        {
            get => on;
            set { if (on == value) return; on = value; MarkDirtyRepaint(); }
        }

        public void Pop(float k) { pop = k; MarkDirtyRepaint(); }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            var c = r.center;
            float outer = Mathf.Min(r.width, r.height) * 0.5f * pop, inner = outer * 0.45f;
            var p = ctx.painter2D;
            p.BeginPath();
            for (int i = 0; i < 10; i++)
            {
                float a = -Mathf.PI / 2 + i * Mathf.PI / 5;
                float rad = i % 2 == 0 ? outer : inner;
                var v = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad;
                if (i == 0) p.MoveTo(v); else p.LineTo(v);
            }
            p.ClosePath();
            p.fillColor = on ? onColor : new Color(1, 1, 1, 0.04f);
            p.Fill();
            p.strokeColor = on ? onColor : new Color(0.925f, 0.894f, 0.827f, 0.3f);
            p.lineWidth = 1.2f;
            p.Stroke();
        }
    }

    /// <summary>A small line/bar chart: evolution fitness per generation, the learning curve, ...</summary>
    public sealed class Chart : VisualElement
    {
        public readonly List<float> A = new List<float>(), B = new List<float>();
        public Color ColorA = Palette.Me, ColorB = new Color(0.925f, 0.894f, 0.827f, 0.45f);
        public bool BarsA;
        public float Min = 0f, Max = 1f;
        public bool AutoMax;

        public Chart()
        {
            style.flexGrow = 1;
            generateVisualContent += Draw;
        }

        public void Refresh() => MarkDirtyRepaint();

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (r.width < 4 || r.height < 4) return;
            var p = ctx.painter2D;
            p.strokeColor = new Color(0.925f, 0.894f, 0.827f, 0.08f);
            p.lineWidth = 1f;
            for (int g = 0; g <= 4; g++)
            {
                float y = r.yMin + r.height * g / 4f;
                p.BeginPath(); p.MoveTo(new Vector2(r.xMin, y)); p.LineTo(new Vector2(r.xMax, y)); p.Stroke();
            }
            float max = Max;
            if (AutoMax) { max = Min + 1e-3f; foreach (var v in A) max = Mathf.Max(max, v); foreach (var v in B) max = Mathf.Max(max, v); max *= 1.1f; }
            Vector2 At(int i, int n, float v)
            {
                float x = n <= 1 ? r.center.x : r.xMin + r.width * i / (n - 1f);
                float y = r.yMax - r.height * Mathf.Clamp01((v - Min) / Mathf.Max(1e-5f, max - Min));
                return new Vector2(x, y);
            }
            if (BarsA && A.Count > 0)
            {
                float bw = r.width / A.Count;
                for (int i = 0; i < A.Count; i++)
                {
                    float h = r.height * Mathf.Clamp01((A[i] - Min) / Mathf.Max(1e-5f, max - Min));
                    p.fillColor = new Color(ColorA.r, ColorA.g, ColorA.b, 0.55f);
                    p.BeginPath();
                    float x0 = r.xMin + i * bw + bw * 0.15f, x1 = r.xMin + (i + 1) * bw - bw * 0.15f;
                    p.MoveTo(new Vector2(x0, r.yMax)); p.LineTo(new Vector2(x0, r.yMax - h)); p.LineTo(new Vector2(x1, r.yMax - h)); p.LineTo(new Vector2(x1, r.yMax));
                    p.ClosePath(); p.Fill();
                }
            }
            void Line(List<float> s, Color c, float w)
            {
                if (s.Count == 0) return;
                p.strokeColor = c; p.lineWidth = w; p.lineJoin = LineJoin.Round;
                p.BeginPath();
                for (int i = 0; i < s.Count; i++) { var v = At(i, s.Count, s[i]); if (i == 0) p.MoveTo(v); else p.LineTo(v); }
                p.Stroke();
                var last = At(s.Count - 1, s.Count, s[s.Count - 1]);
                p.fillColor = c; p.BeginPath(); p.Arc(last, w * 1.6f, 0, 360); p.Fill();
            }
            Line(B, ColorB, 1.5f);
            if (!BarsA) Line(A, ColorA, 2.5f);
        }
    }

    /// <summary>PROCTOR speaks one character at a time. Click to finish the line.</summary>
    public sealed class Typewriter
    {
        public readonly Label Target;
        string full = "";
        float shown, cps = 55f;
        public Action OnChar;
        public bool Done => shown >= full.Length;

        public Typewriter(Label target) { Target = target; }

        public void Say(string text, float charsPerSecond = 55f)
        {
            full = text ?? "";
            shown = 0;
            cps = charsPerSecond;
            Target.text = "";
        }

        public void Finish() { shown = full.Length; Target.text = full; }

        public void Update(float dt)
        {
            if (Done) return;
            int before = (int)shown;
            shown = Mathf.Min(full.Length, shown + dt * cps);
            int now = (int)shown;
            if (now != before)
            {
                Target.text = full.Substring(0, now);
                if (now < full.Length && full[now - 1] != ' ') OnChar?.Invoke();
            }
        }
    }
}
