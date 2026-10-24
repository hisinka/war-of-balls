// «Война шариков» — a tiny anti-aliased 2D rasterizer used to paint UI textures and icons at startup.
using System.Collections.Generic;
using UnityEngine;

namespace WarOfBalls
{
    public class Canvas2D
    {
        public int w, h; public Color[] px; // y grows downwards like an HTML canvas
        public Canvas2D(int w, int h) { this.w = w; this.h = h; px = new Color[w * h]; for (int i = 0; i < px.Length; i++) px[i] = new Color(0, 0, 0, 0); }
        public void Clear(Color c) { for (int i = 0; i < px.Length; i++) px[i] = c; }
        public void Blend(int x, int y, Color c, float cov)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            float a = c.a * Mathf.Clamp01(cov); if (a <= 0f) return;
            Color d = px[y * w + x]; float oa = a + d.a * (1f - a);
            if (oa <= 1e-5f) { px[y * w + x] = new Color(0, 0, 0, 0); return; }
            Color r = (c * a + d * (d.a * (1f - a))) / oa; r.a = oa; px[y * w + x] = r;
        }
        void Bounds(float x0, float y0, float x1, float y1, out int ax, out int ay, out int bx, out int by)
        {
            ax = Mathf.Max(0, Mathf.FloorToInt(x0) - 1); ay = Mathf.Max(0, Mathf.FloorToInt(y0) - 1);
            bx = Mathf.Min(w - 1, Mathf.CeilToInt(x1) + 1); by = Mathf.Min(h - 1, Mathf.CeilToInt(y1) + 1);
        }
        public void FillCircle(float cx, float cy, float r, Color c)
        {
            int ax, ay, bx, by; Bounds(cx - r, cy - r, cx + r, cy + r, out ax, out ay, out bx, out by);
            for (int y = ay; y <= by; y++) for (int x = ax; x <= bx; x++) { float d = K.Dist(x + 0.5f, y + 0.5f, cx, cy); Blend(x, y, c, r - d + 0.5f); }
        }
        // top half of a circle (canvas arc from PI to 0)
        public void FillHalfTop(float cx, float cy, float r, Color c)
        {
            int ax, ay, bx, by; Bounds(cx - r, cy - r, cx + r, cy, out ax, out ay, out bx, out by);
            for (int y = ay; y <= by; y++) for (int x = ax; x <= bx; x++) { float py = y + 0.5f; if (py > cy + 0.5f) continue; float d = K.Dist(x + 0.5f, py, cx, cy); Blend(x, y, c, Mathf.Min(r - d + 0.5f, cy - py + 0.5f)); }
        }
        public void FillEllipse(float cx, float cy, float rx, float ry, float rot, Color c)
        {
            float R = Mathf.Max(rx, ry); int ax, ay, bx, by; Bounds(cx - R, cy - R, cx + R, cy + R, out ax, out ay, out bx, out by);
            float cs = Mathf.Cos(rot), sn = Mathf.Sin(rot);
            for (int y = ay; y <= by; y++)
                for (int x = ax; x <= bx; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy, u = dx * cs + dy * sn, v = -dx * sn + dy * cs;
                    float k = Mathf.Sqrt(u * u / (rx * rx) + v * v / (ry * ry)); float edge = (1f - k) * Mathf.Min(rx, ry);
                    Blend(x, y, c, edge + 0.5f);
                }
        }
        public void StrokeArc(float cx, float cy, float r, float width, Color c, float a0 = 0f, float sweep = 2f * Mathf.PI)
        {
            float R = r + width; int ax, ay, bx, by; Bounds(cx - R, cy - R, cx + R, cy + R, out ax, out ay, out bx, out by);
            bool full = sweep >= 2f * Mathf.PI - 1e-4f;
            for (int y = ay; y <= by; y++)
                for (int x = ax; x <= bx; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy, d = Mathf.Sqrt(dx * dx + dy * dy);
                    float cov = width / 2f - Mathf.Abs(d - r) + 0.5f; if (cov <= 0) continue;
                    if (!full)
                    {
                        float ang = Mathf.Atan2(dy, dx), rel = ang - a0; rel = rel - Mathf.Floor(rel / (2f * Mathf.PI)) * 2f * Mathf.PI;
                        if (rel > sweep) continue;
                    }
                    Blend(x, y, c, cov);
                }
        }
        public void FillRect(float x0, float y0, float ww, float hh, Color c)
        {
            float x1 = x0 + ww, y1 = y0 + hh; int ax, ay, bx, by; Bounds(x0, y0, x1, y1, out ax, out ay, out bx, out by);
            for (int y = ay; y <= by; y++) for (int x = ax; x <= bx; x++)
                {
                    float cx = Mathf.Clamp01(Mathf.Min(x + 1f, x1) - Mathf.Max(x, x0)), cy = Mathf.Clamp01(Mathf.Min(y + 1f, y1) - Mathf.Max(y, y0));
                    Blend(x, y, c, cx * cy);
                }
        }
        public void FillRoundRect(float x0, float y0, float ww, float hh, float r, Color c)
        {
            int ax, ay, bx, by; Bounds(x0, y0, x0 + ww, y0 + hh, out ax, out ay, out bx, out by);
            float cx = x0 + ww / 2f, cy = y0 + hh / 2f, hx = ww / 2f - r, hy = hh / 2f - r;
            for (int y = ay; y <= by; y++) for (int x = ax; x <= bx; x++)
                {
                    float qx = Mathf.Abs(x + 0.5f - cx) - hx, qy = Mathf.Abs(y + 0.5f - cy) - hy;
                    float outside = K.Hyp(Mathf.Max(qx, 0), Mathf.Max(qy, 0)) + Mathf.Min(Mathf.Max(qx, qy), 0) - r;
                    Blend(x, y, c, 0.5f - outside);
                }
        }
        public void Line(float x0, float y0, float x1, float y1, float width, Color c)
        {
            float hw = width / 2f; int ax, ay, bx, by; Bounds(Mathf.Min(x0, x1) - hw, Mathf.Min(y0, y1) - hw, Mathf.Max(x0, x1) + hw, Mathf.Max(y0, y1) + hw, out ax, out ay, out bx, out by);
            float vx = x1 - x0, vy = y1 - y0, l2 = vx * vx + vy * vy;
            for (int y = ay; y <= by; y++) for (int x = ax; x <= bx; x++)
                {
                    float px0 = x + 0.5f - x0, py0 = y + 0.5f - y0, t = l2 > 0 ? Mathf.Clamp01((px0 * vx + py0 * vy) / l2) : 0f;
                    float d = K.Hyp(px0 - vx * t, py0 - vy * t); Blend(x, y, c, hw - d + 0.5f);
                }
        }
        public void Polyline(Vector2[] p, float width, Color c) { for (int i = 1; i < p.Length; i++) Line(p[i - 1].x, p[i - 1].y, p[i].x, p[i].y, width, c); }
        public void FillPoly(Vector2[] p, Color c)
        {
            float x0 = 1e9f, y0 = 1e9f, x1 = -1e9f, y1 = -1e9f; foreach (Vector2 q in p) { x0 = Mathf.Min(x0, q.x); y0 = Mathf.Min(y0, q.y); x1 = Mathf.Max(x1, q.x); y1 = Mathf.Max(y1, q.y); }
            int ax, ay, bx, by; Bounds(x0, y0, x1, y1, out ax, out ay, out bx, out by);
            for (int y = ay; y <= by; y++) for (int x = ax; x <= bx; x++)
                {
                    int inside = 0;
                    for (int sy = 0; sy < 4; sy++) for (int sx = 0; sx < 4; sx++) if (Inside(p, x + (sx + 0.5f) / 4f, y + (sy + 0.5f) / 4f)) inside++;
                    if (inside > 0) Blend(x, y, c, inside / 16f);
                }
        }
        static bool Inside(Vector2[] p, float x, float y)
        {
            bool c = false;
            for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
                if (((p[i].y > y) != (p[j].y > y)) && (x < (p[j].x - p[i].x) * (y - p[i].y) / (p[j].y - p[i].y) + p[i].x)) c = !c;
            return c;
        }
        public void RadialCircle(float cx, float cy, float r, float gx, float gy, Color inner, Color mid, Color outer)
        {
            int ax, ay, bx, by; Bounds(cx - r, cy - r, cx + r, cy + r, out ax, out ay, out bx, out by);
            for (int y = ay; y <= by; y++) for (int x = ax; x <= bx; x++)
                {
                    float d = K.Dist(x + 0.5f, y + 0.5f, cx, cy); float cov = r - d + 0.5f; if (cov <= 0) continue;
                    float k = Mathf.Clamp01(K.Dist(x + 0.5f, y + 0.5f, gx, gy) / (r * 1.25f));
                    Color col = k < 0.6f ? Color.Lerp(inner, mid, k / 0.6f) : Color.Lerp(mid, outer, (k - 0.6f) / 0.4f);
                    Blend(x, y, col, cov);
                }
        }
        public Texture2D ToTexture(bool mips = true)
        {
            Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, mips); t.wrapMode = TextureWrapMode.Clamp; t.filterMode = FilterMode.Bilinear;
            Color[] flipped = new Color[px.Length];
            for (int y = 0; y < h; y++) System.Array.Copy(px, y * w, flipped, (h - 1 - y) * w, w);
            t.SetPixels(flipped); t.Apply(mips); return t;
        }
    }

    // all the little textures the HUD needs, painted once
    public static class Tex
    {
        public static Texture2D roundSmall;
        public static Texture2D white, circle, ring, round, roundGrad, roundGradRed, card, vignette, dot, star, starOff, compass, sndOn, sndOff, bannerBlue, bannerRed, dashed;
        public static Texture2D[] icon = new Texture2D[9];       // white silhouettes for banners
        public static Texture2D[,] portrait = new Texture2D[9, 2]; // coloured portraits for menus
        public static Texture2D bomb, horn, radio, arrow, home, fit, flagIco, fade;
        static Dictionary<int, Texture2D> arcs = new Dictionary<int, Texture2D>();
        public static void Init()
        {
            if (white != null) return;
            arcs.Clear();
            white = Solid(Color.white);
            Canvas2D cs = new Canvas2D(16, 16); cs.FillRoundRect(0, 0, 16, 16, 5, Color.white); roundSmall = Clamp(cs.ToTexture(false));
            Canvas2D c = new Canvas2D(64, 64); c.FillCircle(32, 32, 30, Color.white); circle = c.ToTexture();
            c = new Canvas2D(64, 64); c.StrokeArc(32, 32, 28, 4, Color.white); ring = c.ToTexture();
            c = new Canvas2D(64, 64); c.FillRoundRect(0, 0, 64, 64, 14, Color.white); round = c.ToTexture(false);
            round = Clamp(round);
            roundGrad = GradRound(K.C(0xffd84a), K.C(0xff9f1c));
            roundGradRed = GradRound(K.C(0xe0533a), K.C(0xa8281a));
            card = GradRound(K.C(0x223018), K.C(0x18220f));
            c = new Canvas2D(32, 32); c.FillCircle(16, 16, 15, Color.white);
            for (int i = 0; i < c.px.Length; i++) { int x = i % 32, y = i / 32; float d = K.Dist(x + 0.5f, y + 0.5f, 16, 16) / 16f; Color p = Color.white; p.a = Mathf.Clamp01(1f - d) * Mathf.Clamp01(1.4f - d); c.px[i] = p; }
            dot = c.ToTexture();
            vignette = new Texture2D(128, 128, TextureFormat.RGBA32, false); vignette.wrapMode = TextureWrapMode.Clamp;
            Color[] vp = new Color[128 * 128];
            for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++) { float dx = (x + 0.5f) / 64f - 1f, dy = (y + 0.5f) / 64f - 1.1f; float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.25f; vp[y * 128 + x] = new Color(0, 0, 0, Mathf.Clamp01((d - 0.55f) / 0.45f) * 0.34f); }
            vignette.SetPixels(vp); vignette.Apply(false);
            star = StarTex(K.C(0xffd84a)); starOff = StarTex(K.C(0x3d4a33));
            c = new Canvas2D(48, 48);
            c.FillPoly(new[] { new Vector2(24, 4), new Vector2(32, 24), new Vector2(24, 21), new Vector2(16, 24) }, K.C(0xff3b30));
            c.FillPoly(new[] { new Vector2(24, 44), new Vector2(16, 24), new Vector2(24, 27), new Vector2(32, 24) }, K.C(0xc9ced6));
            c.FillCircle(24, 24, 3.2f, K.C(0x333333)); compass = c.ToTexture();
            sndOn = Speaker(true); sndOff = Speaker(false);
            bannerBlue = Banner(K.C(0x6fb0ff), Units.FLAGC[0]); bannerRed = Banner(K.C(0xff8a7a), Units.FLAGC[1]);
            c = new Canvas2D(32, 8); c.FillRect(0, 0, 20, 8, Color.white); dashed = c.ToTexture(false); dashed.wrapMode = TextureWrapMode.Repeat;
            for (int i = 0; i < 9; i++) icon[i] = Icon(i);
            for (int i = 0; i < 9; i++) for (int t = 0; t < 2; t++) portrait[i, t] = Portrait(i, t);
            bomb = Sym(0); horn = Sym(1); radio = Sym(2);
            c = new Canvas2D(32, 32); c.FillPoly(new[] { new Vector2(30, 16), new Vector2(4, 4), new Vector2(4, 28) }, Color.white); arrow = c.ToTexture();
            c = new Canvas2D(48, 48); Color hc = K.C(0x222222);
            c.FillPoly(new[] { new Vector2(24, 8), new Vector2(41, 23), new Vector2(7, 23) }, hc); c.FillRect(12, 22, 24, 18, hc); c.FillRect(21, 29, 6, 11, Color.white); home = c.ToTexture();
            c = new Canvas2D(48, 48);
            foreach (Vector2 d in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
            {
                float cx = 24 + d.x * 14, cy = 24 + d.y * 14; c.Line(cx, cy, cx - d.x * 9, cy, 3.5f, hc); c.Line(cx, cy, cx, cy - d.y * 9, 3.5f, hc);
                c.Line(24 + d.x * 4, 24 + d.y * 4, cx - d.x * 2, cy - d.y * 2, 3f, hc);
            }
            fit = c.ToTexture();
            c = new Canvas2D(32, 32); c.FillRect(9, 5, 3, 23, Color.white); c.FillPoly(new[] { new Vector2(12, 5), new Vector2(26, 9), new Vector2(12, 15) }, Color.white); flagIco = c.ToTexture();
            fade = new Texture2D(1, 64, TextureFormat.RGBA32, false); fade.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < 64; y++) fade.SetPixel(0, y, new Color(10 / 255f, 14 / 255f, 8 / 255f, 0.9f * (1f - y / 63f)));
            fade.Apply(false);
        }
        static Texture2D Clamp(Texture2D t) { t.wrapMode = TextureWrapMode.Clamp; return t; }
        public static Texture2D Solid(Color c) { Texture2D t = new Texture2D(2, 2); t.SetPixels(new[] { c, c, c, c }); t.Apply(); return t; }
        static Texture2D GradRound(Color top, Color bot)
        {
            Canvas2D c = new Canvas2D(64, 64);
            Canvas2D m = new Canvas2D(64, 64); m.FillRoundRect(0, 0, 64, 64, 14, Color.white);
            for (int y = 0; y < 64; y++) { Color col = Color.Lerp(top, bot, y / 63f); for (int x = 0; x < 64; x++) { Color p = col; p.a = m.px[y * 64 + x].a; c.px[y * 64 + x] = p; } }
            return Clamp(c.ToTexture(false));
        }
        static Texture2D StarTex(Color col)
        {
            Canvas2D c = new Canvas2D(64, 64); Vector2[] p = new Vector2[10];
            for (int i = 0; i < 10; i++) { float a = -Mathf.PI / 2 + i * Mathf.PI / 5, r = i % 2 == 1 ? 12f : 29f; p[i] = new Vector2(32 + Mathf.Cos(a) * r, 34 + Mathf.Sin(a) * r); }
            c.FillPoly(p, col); return c.ToTexture();
        }
        static Texture2D Speaker(bool on)
        {
            Canvas2D c = new Canvas2D(48, 48); Color w = Color.white;
            c.FillPoly(new[] { new Vector2(8, 18), new Vector2(16, 18), new Vector2(26, 10), new Vector2(26, 38), new Vector2(16, 30), new Vector2(8, 30) }, w);
            if (on) { c.StrokeArc(26, 24, 7, 3, w, -0.9f, 1.8f); c.StrokeArc(26, 24, 13, 3, w, -0.95f, 1.9f); }
            else { c.Line(31, 18, 41, 30, 3.5f, w); c.Line(41, 18, 31, 30, 3.5f, w); }
            return c.ToTexture();
        }
        static Texture2D Banner(Color inner, Color outer)
        {
            Canvas2D c = new Canvas2D(64, 64); c.RadialCircle(32, 32, 30, 32 - 9, 32 - 12, inner, Color.Lerp(inner, outer, 0.55f), outer); return c.ToTexture();
        }
        // a ring segment from 12 o'clock clockwise, quantised to 1/32 steps
        public static Texture2D Arc(Color col, float frac)
        {
            int q = Mathf.Clamp(Mathf.RoundToInt(frac * 32f), 0, 32); Color32 k = col; int key = (k.r << 24) ^ (k.g << 16) ^ (k.b << 8) ^ q;
            Texture2D t; if (arcs.TryGetValue(key, out t)) return t;
            Canvas2D c = new Canvas2D(64, 64); if (q > 0) c.StrokeArc(32, 32, 27, 6, col, -Mathf.PI / 2, 2f * Mathf.PI * q / 32f);
            t = c.ToTexture(); arcs[key] = t; return t;
        }
        // ---------- banner icons (white) — drawn around (32,32) with unit size s ----------
        static Texture2D Icon(int ti)
        {
            Canvas2D c = new Canvas2D(64, 64); const float s = 26f; Color W = Color.white, Dk = new Color(0, 0, 0, 0.45f);
            System.Func<float[], Vector2[]> P = a => { Vector2[] r = new Vector2[a.Length / 2]; for (int i = 0; i < r.Length; i++) r[i] = new Vector2(32 + a[i * 2] * s, 32 + a[i * 2 + 1] * s); return r; };
            float lw = Mathf.Max(1.8f, s * 0.18f);
            switch (ti)
            {
                case 0: c.FillPoly(P(new[] { -0.85f, -0.1f, 0.55f, -0.1f, 0.55f, -0.25f, 0.9f, -0.25f, 0.9f, 0.05f, 0.1f, 0.05f, 0.05f, 0.55f, -0.15f, 0.55f, -0.2f, 0.1f, -0.85f, 0.15f }), W); break;
                case 1: c.StrokeArc(32, 32, s * 0.62f, lw, W); c.Line(32 - s * 0.95f, 32, 32 + s * 0.95f, 32, lw, W); c.Line(32, 32 - s * 0.95f, 32, 32 + s * 0.95f, lw, W); break;
                case 2:
                    c.FillPoly(P(new[] { -0.9f, -0.15f, 0.55f, -0.15f, 0.9f, 0.15f, 0.9f, 0.35f, -0.9f, 0.35f }), W);
                    c.FillPoly(P(new[] { -0.3f, -0.45f, 0.25f, -0.45f, 0.3f, -0.15f, -0.35f, -0.15f }), W);
                    foreach (float x in new[] { -0.6f, -0.2f, 0.2f, 0.6f }) c.FillCircle(32 + x * s, 32 + s * 0.45f, s * 0.16f, Dk); break;
                case 3: c.FillCircle(32, 32 + s * 0.15f, s * 0.55f, W); c.FillRect(32 - s * 0.18f, 32 - s * 0.65f, s * 0.36f, s * 0.3f, W); c.StrokeArc(32 + s * 0.32f, 32 - s * 0.55f, s * 0.2f, Mathf.Max(1.5f, s * 0.12f), W); break;
                case 4:
                    {
                        Vector2[] p = new Vector2[10];
                        for (int i = 0; i < 10; i++) { float a = -Mathf.PI / 2 + i * Mathf.PI / 5, r = i % 2 == 1 ? s * 0.38f : s * 0.9f; p[i] = new Vector2(32 + Mathf.Cos(a) * r, 32 + Mathf.Sin(a) * r); }
                        c.FillPoly(p, W); break;
                    }
                case 5: c.FillRoundRect(32 - s * 0.55f, 32 - s * 0.8f, s * 1.1f, s * 1.6f, s * 0.25f, W); c.FillRect(32 - s * 0.35f, 32 - s * 0.5f, s * 0.7f, s * 0.28f, Dk); break;
                case 6:
                    c.FillPoly(P(new[] { -0.9f, -0.05f, 0.1f, -0.15f, 0.6f, -0.05f, 0.85f, 0.15f, 0.55f, 0.3f, -0.2f, 0.25f, -0.9f, 0.1f }), W);
                    c.Line(32 - s * 0.9f, 32 - s * 0.5f, 32 + s * 0.9f, 32 - s * 0.5f, Mathf.Max(1.6f, s * 0.12f), W); c.Line(32, 32 - s * 0.5f, 32, 32 - s * 0.15f, Mathf.Max(1.6f, s * 0.12f), W); break;
                case 7:
                    c.FillPoly(P(new[] { -0.85f, -0.05f, 0.85f, -0.05f, 0.55f, 0.45f, -0.7f, 0.45f }), W);
                    c.Polyline(P(new[] { -0.1f, -0.05f, -0.1f, -0.85f, 0.5f, -0.25f }), Mathf.Max(1.5f, s * 0.12f), W); break;
                case 8:
                    c.FillPoly(P(new[] { -0.9f, 0.05f, 0.85f, 0.05f, 0.95f, 0.4f, -0.95f, 0.4f }), W);
                    c.FillPoly(P(new[] { -0.45f, -0.35f, 0.25f, -0.35f, 0.35f, 0.05f, -0.5f, 0.05f }), W);
                    c.FillRect(32 + 0.2f * s, 32 - 0.25f * s, 0.8f * s, 0.12f * s, W); break;
            }
            return c.ToTexture();
        }
        // ---------- coloured unit portraits for the menu and the barracks ----------
        static Texture2D Portrait(int ti, int team)
        {
            Canvas2D c = new Canvas2D(64, 64); const float ox = 32, oy = 34;
            System.Action<float, float, float, float, Color> box = (x, y, w, h, f) => c.FillRect(ox + x, oy + y, w, h, f);
            Color css = Units.BODY[team], light = Units.LIGHT[team], dark = Units.DARK[team];
            if (ti == 2 || ti == 8)
            {
                Color body = team == 1 ? K.C(0x8a5a3a) : K.C(0x4a6a8a);
                if (ti == 8)
                {
                    box(-24, -2, 48, 12, body); box(-26, 8, 52, 8, K.C(0x222222)); box(-12, -12, 22, 11, body); box(8, -9, 22, 4, K.C(0x333333));
                    for (float x = -20; x <= 20; x += 8) c.FillCircle(ox + x, oy + 12, 3, K.C(0x555555));
                }
                else
                {
                    box(-24, -4, 44, 13, body); c.FillPoly(new[] { new Vector2(ox + 20, oy - 4), new Vector2(ox + 27, oy + 4), new Vector2(ox + 20, oy + 9) }, body);
                    box(-8, -11, 12, 7, body); box(3, -9, 14, 2.5f, K.C(0x222222));
                    foreach (float x in new[] { -17f, -7f, 4f, 14f }) c.FillCircle(ox + x, oy + 11, 4.5f, K.C(0x1b1c1e));
                }
                return c.ToTexture();
            }
            if (ti == 6)
            {
                Color body = team == 1 ? K.C(0x6a4a3a) : K.C(0x3e5266);
                c.FillEllipse(ox - 2, oy + 4, 17, 7, 0, body); box(-30, 1, 16, 4, body); box(-32, -6, 4, 10, body); c.FillEllipse(ox + 9, oy + 1, 7, 5, 0, K.C(0x8cc2e0));
                box(-26, -12, 52, 2, K.C(0x222222)); box(-1, -12, 2, 9, K.C(0x222222)); box(-7, 10, 16, 2, K.C(0x222222)); return c.ToTexture();
            }
            if (ti == 7)
            {
                c.FillPoly(new[] { new Vector2(ox - 24, oy), new Vector2(ox + 24, oy), new Vector2(ox + 16, oy + 12), new Vector2(ox - 20, oy + 12) }, K.C(0xf0efe8));
                box(-24, -2, 46, 3, css); box(-26, -6, 5, 10, K.C(0x333333)); return c.ToTexture();
            }
            c.RadialCircle(ox, oy, 14, ox - 5, oy - 6, light, css, dark);
            c.FillCircle(ox + 5, oy - 2, 3.4f, Color.white); c.FillCircle(ox + 11, oy - 2, 3.4f, Color.white);
            c.FillCircle(ox + 6.5f, oy - 2, 1.7f, K.C(0x111111)); c.FillCircle(ox + 12.5f, oy - 2, 1.7f, K.C(0x111111));
            System.Action<Color> helm = f => c.FillHalfTop(ox, oy - 4, 14.5f, f);
            if (ti == 0) { helm(K.C(0x56663a)); box(-2, 6, 26, 4, K.C(0x2b2e31)); box(18, 7, 9, 2, K.C(0x111111)); box(6, 9, 3, 6, K.C(0x2b2e31)); }
            if (ti == 1) { box(-18, -12, 36, 3, K.C(0x6b7a45)); c.FillHalfTop(ox, oy - 12, 10, K.C(0x6b7a45)); box(-6, 6, 32, 3.5f, K.C(0x4a5236)); box(4, 3, 9, 3, K.C(0x111111)); }
            if (ti == 3) { helm(K.C(0x56663a)); c.FillCircle(ox + 16, oy + 8, 6, K.C(0x4d5a2c)); box(14, 0, 4, 3, K.C(0x888888)); }
            if (ti == 4) { c.FillEllipse(ox - 2, oy - 12, 13, 5, -0.2f, dark); box(6, 6, 14, 3.5f, K.C(0x222222)); }
            if (ti == 5) { helm(K.C(0x23272c)); box(8, -6, 6, 4, K.C(0x1a2a3a)); c.FillRoundRect(ox + 10, oy - 12, 11, 28, 3, K.C(0x2b3036)); box(12, -6, 7, 4, K.C(0x5b7a98)); }
            return c.ToTexture();
        }
        // symbols for abilities (instead of emoji, which Unity fonts can't draw)
        static Texture2D Sym(int k)
        {
            Canvas2D c = new Canvas2D(64, 64); c.FillCircle(32, 32, 28, K.C(0x4b3a7a));
            if (k == 0) { c.FillCircle(29, 36, 12, K.C(0x1e1e22)); c.FillCircle(25, 32, 3, new Color(1, 1, 1, 0.35f)); c.Line(36, 26, 42, 18, 3, K.C(0xc9a46a)); c.FillCircle(43, 16, 4, K.C(0xffb030)); c.FillCircle(43, 16, 2, K.C(0xfff2a0)); }
            else if (k == 1) { c.FillPoly(new[] { new Vector2(18, 27), new Vector2(26, 27), new Vector2(44, 17), new Vector2(44, 47), new Vector2(26, 37), new Vector2(18, 37) }, K.C(0xffd84a)); c.FillRect(22, 37, 5, 9, K.C(0xffd84a)); c.StrokeArc(44, 32, 8, 2.5f, Color.white, -0.8f, 1.6f); }
            else { c.FillRoundRect(17, 26, 30, 22, 4, K.C(0x5b6b3a)); c.Line(40, 26, 46, 12, 2.5f, K.C(0xdddddd)); c.FillCircle(26, 37, 5, K.C(0x2a2d30)); c.FillRect(34, 32, 9, 3, K.C(0xffd84a)); c.FillRect(34, 38, 9, 3, K.C(0xffd84a)); }
            return c.ToTexture();
        }
    }
}
