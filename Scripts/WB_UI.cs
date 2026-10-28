// «Война шариков» — HUD, menus, barracks, map overlay and mouse/keyboard control (Unity IMGUI).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace WarOfBalls
{
    public enum Screen_ { None, Menu, Help, Result, Shop }

    public partial class WarGame
    {
        public Screen_ screen = Screen_.Menu; Screen_ shopBack = Screen_.Menu;
        public float us = 1f, VW = 1280, VH = 800;
        string hintText = ""; float hintT, hintA;
        string annTitle = "", annSub = ""; Color annCol = Color.white; float annT = 99f;
        List<Rect> hudRects = new List<Rect>();
        Vector2 hover; bool hasHover;
        class Drag { public Squad sq; public bool was, moved; public List<Vector2> pts = new List<Vector2>(); public float sx, sy, ex, ey, ox, oy; }
        class Pend { public bool rot, moved; public float sx, sy, lx, ly, vx, vy, lt; }
        Drag drag; Pend pend; float lastTapT = -9; Vector2 lastTapP; bool miniDrag; Rect miniRect; Texture2D miniTex;
        Squad boardCand; Squad boardBoat; string boardText = ""; float barT;

        public void Hint(string t, float secs) { hintText = t; hintT = secs; hintA = 1; }
        public void Announce(string t, string sub, Color col) { annTitle = t; annSub = sub ?? ""; annCol = col; annT = 0; }
        float TopPad() { return VH < 520 ? 50 : 58; }
        float BotPad() { return VH < 520 ? 58 : 76; }
        float Now() { return Time.unscaledTime * 1000f; }

        // ================= styles =================
        Dictionary<int, GUIStyle> styles = new Dictionary<int, GUIStyle>();
        Dictionary<GUIStyle, int> styleKey = new Dictionary<GUIStyle, int>();
        // the same style at a different pixel size (text is drawn in real screen pixels so it stays crisp at any UI scale)
        GUIStyle Scaled(GUIStyle st, float k)
        {
            int key; if (!styleKey.TryGetValue(st, out key)) return st;
            int size = key & 255; int ns = Mathf.Clamp(Mathf.RoundToInt(size * k), 1, 255);
            return S(ns, (key & (1 << 8)) != 0, (TextAnchor)((key >> 9) & 31), (key & (1 << 14)) != 0);
        }
        GUIStyle S(int size, bool bold = true, TextAnchor a = TextAnchor.MiddleCenter, bool wrap = false)
        {
            int k = Mathf.Clamp(size, 1, 255) | (bold ? 1 << 8 : 0) | ((int)a << 9) | (wrap ? 1 << 14 : 0);
            GUIStyle st; if (styles.TryGetValue(k, out st)) return st;
            st = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = bold ? FontStyle.Bold : FontStyle.Normal, alignment = a, wordWrap = wrap, richText = true, clipping = TextClipping.Overflow };
            st.padding = new RectOffset(0, 0, 0, 0); st.margin = new RectOffset(0, 0, 0, 0); st.normal.textColor = Color.white;
            styles[k] = st; styleKey[st] = k; return st;
        }
        GUIStyle bgRound, bgCard, bgGold, bgRed, bgSmall;
        void InitStyles()
        {
            if (bgRound != null) return;
            bgRound = new GUIStyle { border = new RectOffset(16, 16, 16, 16) }; bgRound.normal.background = Tex.round;
            bgCard = new GUIStyle { border = new RectOffset(16, 16, 16, 16) }; bgCard.normal.background = Tex.card;
            bgGold = new GUIStyle { border = new RectOffset(16, 16, 16, 16) }; bgGold.normal.background = Tex.roundGrad;
            bgRed = new GUIStyle { border = new RectOffset(16, 16, 16, 16) }; bgRed.normal.background = Tex.roundGradRed;
            bgSmall = new GUIStyle { border = new RectOffset(6, 6, 6, 6) }; bgSmall.normal.background = Tex.roundSmall;
        }
        void Box(Rect r, GUIStyle st, Color c)
        {
            if (Event.current.type != EventType.Repaint) return;
            Color o = GUI.color; GUI.color = c; st.Draw(r, false, false, false, false); GUI.color = o;
        }
        // draws a label in real pixels (GUI.matrix here is only ever scale + offset)
        void RawLabel(Rect r, string text, GUIStyle st)
        {
            Matrix4x4 M = GUI.matrix; float sx = M.m00, sy = M.m11;
            Rect sr = new Rect(M.m03 + r.x * sx, M.m13 + r.y * sy, r.width * sx, r.height * sy);
            GUI.matrix = Matrix4x4.identity; GUI.Label(sr, text, Scaled(st, sy)); GUI.matrix = M;
        }
        void Tx(Rect r, string text, GUIStyle st, Color c)
        {
            if (Event.current.type != EventType.Repaint) return;
            Color o = GUI.color; GUI.color = c; RawLabel(r, text, st); GUI.color = o;
        }
        void TxO(Rect r, string text, GUIStyle st, Color c, Color outline, float th = 2f)
        {
            if (Event.current.type != EventType.Repaint) return;
            Color o = GUI.color; GUI.color = outline;
            for (int i = 0; i < 8; i++) { float a = i * Mathf.PI / 4; RawLabel(new Rect(r.x + Mathf.Cos(a) * th, r.y + Mathf.Sin(a) * th, r.width, r.height), text, st); }
            GUI.color = c; RawLabel(r, text, st); GUI.color = o;
        }
        void Img(Rect r, Texture t, Color c)
        {
            if (Event.current.type != EventType.Repaint || t == null) return;
            Color o = GUI.color; GUI.color = c; GUI.DrawTexture(r, t, ScaleMode.StretchToFill, true); GUI.color = o;
        }
        void ImgRot(Vector2 center, float size, float angDeg, Texture t, Color c)
        {
            if (Event.current.type != EventType.Repaint) return;
            Matrix4x4 old = GUI.matrix; GUI.matrix = old * Matrix4x4.TRS(new Vector3(center.x, center.y, 0), Quaternion.Euler(0, 0, angDeg), Vector3.one);
            Img(new Rect(-size / 2, -size / 2, size, size), t, c); GUI.matrix = old;
        }
        void Line(float x0, float y0, float x1, float y1, float w, Color c)
        {
            if (Event.current.type != EventType.Repaint) return;
            float len = K.Dist(x0, y0, x1, y1); if (len < 0.01f) return;
            Matrix4x4 old = GUI.matrix; GUI.matrix = old * Matrix4x4.TRS(new Vector3(x0, y0, 0), Quaternion.Euler(0, 0, Mathf.Atan2(y1 - y0, x1 - x0) * Mathf.Rad2Deg), Vector3.one);
            Color o = GUI.color; GUI.color = c; GUI.DrawTexture(new Rect(0, -w / 2, len, w), Tex.white); GUI.color = o; GUI.matrix = old;
        }
        void Circle(float cx, float cy, float r, Color c) { Img(new Rect(cx - r * 32 / 30f, cy - r * 32 / 30f, r * 64 / 30f, r * 64 / 30f), Tex.circle, c); }
        void RingC(float cx, float cy, float r, Color c) { float k = 32f / 28f; Img(new Rect(cx - r * k, cy - r * k, 2 * r * k, 2 * r * k), Tex.ring, c); }
        void ArcC(float cx, float cy, float r, float frac, Color c) { float k = 32f / 27f; Img(new Rect(cx - r * k, cy - r * k, 2 * r * k, 2 * r * k), Tex.Arc(c, frac), Color.white); }

        // a button: background + label; returns true when clicked
        enum BK { Gold, Alt, Red, Dark, Ability, Map, Panel, PanelOn, PanelX, Top }
        bool Btn(Rect r, string text, BK kind, string small = null, bool enabled = true, Texture icon = null, float fontSize = 0)
        {
            hudRects.Add(r);
            Color bg = Color.white; GUIStyle st = bgRound; Color fg = Color.white;
            switch (kind)
            {
                case BK.Gold: st = bgGold; fg = K.C(0x2a1600); break;
                case BK.Red: st = bgRed; break;
                case BK.Alt: bg = K.C(0x3d5130); break;
                case BK.Dark: bg = new Color(0.11f, 0.14f, 0.086f, 0.92f); break;
                case BK.Ability: bg = K.C(0x3b2e66); break;
                case BK.Map: bg = new Color(1, 1, 1, 0.94f); fg = K.C(0x222222); break;
                case BK.Panel: bg = K.C(0x2c3b23); break;
                case BK.PanelOn: bg = K.C(0x3d6fd0); break;
                case BK.PanelX: bg = K.C(0x3a2a22); break;
                case BK.Top: bg = new Color(0, 0, 0, 0.5f); break;
            }
            float alpha = enabled ? 1f : 0.5f; bg.a *= alpha; fg.a *= alpha;
            if (kind == BK.Gold || kind == BK.Red || kind == BK.Map || kind == BK.Ability) Box(new Rect(r.x, r.y + 3, r.width, r.height), bgRound, new Color(0, 0, 0, 0.3f * alpha));
            Box(r, st, bg);
            if (kind == BK.Ability) Box(new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2), bgRound, new Color(0.56f, 0.48f, 1f, 0.25f * alpha));
            if (icon != null)
            {
                float s = Mathf.Min(r.height, r.width) * (small != null ? 0.48f : 0.62f);
                Img(new Rect(r.center.x - s / 2, r.y + (small != null ? 5 : (r.height - s) / 2), s, s), icon, new Color(1, 1, 1, alpha));
                if (small != null) Tx(new Rect(r.x, r.yMax - 17, r.width, 14), small, S(10), new Color(1, 1, 1, 0.8f * alpha));
            }
            else if (small != null)
            {
                Tx(new Rect(r.x, r.y + r.height * 0.18f, r.width, r.height * 0.45f), text, S(fontSize > 0 ? (int)fontSize : 14), fg);
                Tx(new Rect(r.x, r.y + r.height * 0.58f, r.width, r.height * 0.3f), small, S(10), new Color(fg.r, fg.g, fg.b, 0.75f * alpha));
            }
            else Tx(r, text, S(fontSize > 0 ? (int)fontSize : 14), fg);
            bool old = GUI.enabled; GUI.enabled = enabled && old;
            bool hit = GUI.Button(r, GUIContent.none, GUIStyle.none);
            GUI.enabled = old;
            if (hit) Snd.Fx("click");
            return hit && enabled;
        }
        void Pill(Rect r, string text, Color bg, int size = 14)
        {
            Box(r, bgRound, bg); Tx(r, text, S(size), Color.white);
        }

        // ================= the big IMGUI pass =================
        void OnGUI()
        {
            if (cam3 == null) return;
            Tex.Init(); InitStyles();
            Event e = Event.current;
            us = Mathf.Clamp(Mathf.Min(Screen.width / 1280f, Screen.height / 760f), 0.7f, 2.5f);
            VW = Screen.width / us; VH = Screen.height / us;
            Vector2 m = e.mousePosition / us;
            GUI.matrix = Matrix4x4.Scale(new Vector3(us, us, 1));
            if (e.type == EventType.Repaint) { hasHover = m.x >= 0 && m.y >= 0 && m.x <= VW && m.y <= VH && !Application.isMobilePlatform; hover = m; }
            hudRects.Clear();
            Img(new Rect(0, 0, VW, VH), Tex.vignette, Color.white);
            if (phase != Phase.Menu && e.type == EventType.Repaint) DrawOverlay(Time.time);
            if (screen == Screen_.None && phase != Phase.Menu) DrawHUD(m);
            DrawAnnounce();
            if (e.type == EventType.KeyUp) keys.Remove(e.keyCode);
            if (screen != Screen_.None) DrawScreens();
            else HandleInput(e, m);
            GUI.matrix = Matrix4x4.identity;
        }

        // ================= HUD =================
        void DrawHUD(Vector2 m)
        {
            float t = Time.time;
            // top bar
            if (Btn(new Rect(10, 8, 40, 40), "", BK.Top, null, true, Snd.on ? Tex.sndOn : Tex.sndOff)) { Snd.SetOn(!Snd.on); save.soundOff = !Snd.on; save.Persist(); }
            float x = 58, y = 8;
            string lvl = save.level + ". " + Units.BattleName(save.level);
            float lw = Mathf.Min(S(14).CalcSize(new GUIContent(lvl)).x + 22, VW * 0.3f); Pill(new Rect(x, y + 4, lw, 32), lvl, new Color(0.055f, 0.07f, 0.04f, 0.8f)); x += lw + 8;
            int s = Mathf.Max(0, Mathf.CeilToInt(clock)); string ct = (roleDef ? "ЗАЩИТА " : "ШТУРМ ") + (s / 60) + ":" + (s % 60).ToString("00");
            bool low = s <= 20 && phase == Phase.Battle; float cw = S(14).CalcSize(new GUIContent(ct)).x + 22;
            Color cbg = low ? new Color(0.63f, 0.08f, 0.08f, 0.6f + 0.25f * Mathf.Abs(Mathf.Sin(t * 4))) : new Color(0.055f, 0.07f, 0.04f, 0.8f);
            Pill(new Rect(x, y + 4, cw, 32), ct, cbg); x += cw + 8;
            float right = VW - 10;
            if (phase == Phase.Battle)
            {
                if (Btn(new Rect(right - 40, y, 40, 40), paused ? ">" : "II", BK.Top, null, true, null, 15)) { paused = !paused; }
                right -= 48;
                if (Btn(new Rect(right - 44, y, 44, 40), "×" + speed, BK.Top, null, true, null, 15)) { speed = speed == 1 ? 2 : 1; }
                right -= 52;
            }
            int nb = 0, nr = 0, tb = 0, tr = 0;
            foreach (Unit u in units) { if (u.T.boat) continue; if (u.team == K.BLUE) nb++; else if (u.ti != 4) nr++; }
            foreach (Squad q in squads) { if (q.T.boat) continue; if (q.team == K.BLUE) tb += q.n0; else if (q.ti != 4) tr += q.n0; }
            float fw = right - x; if (fw > 80)
            {
                float bw = (fw - 60) / 2;
                Tx(new Rect(x, y + 4, 26, 32), nb.ToString(), S(14), Color.white);
                Box(new Rect(x + 28, y + 14, bw, 12), bgSmall, new Color(0, 0, 0, 0.45f));
                Img(new Rect(x + 28, y + 14, bw * (tb > 0 ? nb / (float)tb : 0), 12), Tex.white, K.C(0x3d8bff));
                Box(new Rect(x + 32 + bw, y + 14, bw, 12), bgSmall, new Color(0, 0, 0, 0.45f));
                float rf = tr > 0 ? nr / (float)tr : 0; Img(new Rect(x + 32 + bw + bw * (1 - rf), y + 14, bw * rf, 12), Tex.white, K.C(0xff5a4a));
                Tx(new Rect(x + 36 + 2 * bw, y + 4, 26, 32), nr.ToString(), S(14), Color.white);
            }
            // hint
            if (hintA > 0.01f && hintText.Length > 0)
            {
                GUIStyle hs = S(13, true, TextAnchor.MiddleCenter, true); float hw = Mathf.Min(VW * 0.94f, hs.CalcSize(new GUIContent(hintText)).x + 26);
                float hh = hs.CalcHeight(new GUIContent(hintText), hw - 24) + 12; Rect hr = new Rect(VW / 2 - hw / 2, TopPad() - 2, hw, hh);
                Box(hr, bgRound, new Color(0.055f, 0.07f, 0.04f, 0.82f * hintA)); Tx(new Rect(hr.x + 12, hr.y + 6, hw - 24, hh - 12), hintText, hs, new Color(1, 1, 1, hintA));
            }
            // pause caption
            if (paused && phase == Phase.Battle) TxO(new Rect(0, TopPad() + 52, VW, 30), "ПАУЗА — рисуй пути отрядам", S(20), Color.white, new Color(0, 0, 0, 0.6f));
            // bottom bar
            Img(new Rect(0, VH - 78, VW, 78), Tex.fade, Color.white);
            float by = VH - 60, bh = 52;
            if (phase == Phase.Deploy)
            {
                float w = Mathf.Min(240, VW - 40);
                if (Btn(new Rect(VW / 2 - w / 2, by, w, bh), "В бой!", BK.Gold, null, true, null, 17)) StartFight();
            }
            else if (phase == Phase.Battle || phase == Phase.End)
            {
                List<int> btns = new List<int> { 1 }; if (save.fire > 0) btns.Add(2); if (save.cry > 0) btns.Add(3);
                float total = 0; foreach (int b in btns) total += (b < 2 ? Mathf.Min(170, (VW - 40) / btns.Count) : 96) + 8; total -= 8;
                float bx = VW / 2 - total / 2;
                foreach (int b in btns)
                {
                    float w = b < 2 ? Mathf.Min(170, (VW - 40) / btns.Count) : 96;
                    Rect r = new Rect(bx, by, w, bh);
                    if (b == 1 && Btn(r, "Стоять", BK.Dark, "все отряды")) { foreach (Squad q in squads) if (q.team == K.BLUE) OrderHold(q); }
                    if (b == 2) { if (Btn(r, "", BK.Ability, "Обстрел (Q)", abFire <= 0, Tex.bomb)) FireVolley(); if (abFire > 0) Img(new Rect(r.x + 2, r.yMax - 2 - (r.height - 4) * abFire / 30f, r.width - 4, (r.height - 4) * abFire / 30f), Tex.white, new Color(0, 0, 0, 0.55f)); }
                    if (b == 3) { if (Btn(r, "", BK.Ability, "Приказ (E)", abCry <= 0, Tex.horn)) WarCry(); if (abCry > 0) Img(new Rect(r.x + 2, r.yMax - 2 - (r.height - 4) * abCry / 45f, r.width - 4, (r.height - 4) * abCry / 45f), Tex.white, new Color(0, 0, 0, 0.55f)); }
                    bx += w + 8;
                }
            }
            // squad panel
            if (sel != null && !AnyAlive(sel)) sel = null;
            if (sel != null && sel.team == K.BLUE && (phase == Phase.Deploy || phase == Phase.Battle)) DrawSquadPanel();
            // board / land button
            if (boardCand != null && phase == Phase.Battle)
            {
                GUIStyle bs = S(17); float w = bs.CalcSize(new GUIContent(boardText)).x + 44, k = 1 + 0.04f * Mathf.Sin(t * 5.2f);
                Rect r = new Rect(VW / 2 - w * k / 2, VH - (VH < 520 ? 100 : 132) - 50, w * k, 48 * k);
                if (Btn(r, boardText, BK.Gold, null, true, null, 17)) { if (boardBoat != null) BoardBoat(boardCand, boardBoat); else UnloadBoat(boardCand); boardCand = null; }
            }
            // map controls
            float mh = VH < 520 ? 36 : 46, gap = VH < 520 ? 4 : 8, mxp = VW - 10 - mh, myp = VH / 2 - (6 * mh + 5 * gap) / 2;
            if (Btn(new Rect(mxp, myp, mh, mh), camMode3d ? "2D" : "3D", BK.Map, null, true, null, VH < 520 ? 13 : 16)) Set3D(!camMode3d); myp += mh + gap;
            if (Btn(new Rect(mxp, myp, mh, mh), "+", BK.Map, null, true, null, 22)) camT.dist = K.Clamp(camT.dist * 0.68f, DMIN, DMAX); myp += mh + gap;
            if (Btn(new Rect(mxp, myp, mh, mh), "–", BK.Map, null, true, null, 22)) camT.dist = K.Clamp(camT.dist * 1.47f, DMIN, DMAX); myp += mh + gap;
            Rect cr = new Rect(mxp, myp, mh, mh); if (Btn(cr, "", BK.Map)) camT.yaw = Mathf.Round(camT.yaw / (2 * Mathf.PI)) * 2 * Mathf.PI;
            ImgRot(cr.center, mh * 0.62f, cam.yaw * Mathf.Rad2Deg, Tex.compass, Color.white); myp += mh + gap;
            if (Btn(new Rect(mxp, myp, mh, mh), "", BK.Map, null, true, Tex.home)) { camFollow = null; camT.x = K.FLX + 200; camT.y = K.H / 2; camT.dist = K.Clamp(Mathf.Min(camT.dist, 1000), DMIN, DMAX); }
            myp += mh + gap;
            if (Btn(new Rect(mxp, myp, mh, mh), "", BK.Map, null, true, Tex.fit)) { camFollow = null; CamFitUnits(false); }
            DrawMini(t);
        }
        void AllAttack()
        {
            List<Squad> reds = squads.FindAll(q => q.team == K.RED && AnyAlive(q) && !q.T.boat && q.order != Ord.Boat); if (reds.Count == 0) return;
            foreach (Squad q in squads)
            {
                if (q.team != K.BLUE || !AnyAlive(q) || q.T.boat || q.order == Ord.Boat) continue;
                Vector2 c = Cen(q); Squad best = null; float bd = 1e9f;
                foreach (Squad r in reds) { Vector2 rc = Cen(r); float d = K.Dist(rc.x, rc.y, c.x, c.y); if (d < bd) { bd = d; best = r; } }
                OrderAttack(q, best);
            }
            Snd.Fx("horn");
        }
        void DrawSquadPanel()
        {
            bool solo = Units.SOLO(sel.ti); int n = NAlive(sel);
            string name = sel.T.boat ? ("Лодка" + (sel.cargo != null ? " · десант " + NAlive(sel.cargo) : " (пустая)")) : sel.ti == 6 ? "Апач ур." + (save.dragon + 1) : sel.T.name + (solo ? "" : " · " + n);
            List<string> ids = new List<string>();
            if (!solo) { ids.Add("s2"); ids.Add("s3"); }
            Squad mt = MergeTarget(sel); if (!solo && mt != null) ids.Add("mg");
            if (sel.ti == 5) ids.Add("wall");
            if (phase == Phase.Battle && FreeBoatNear(sel) != null) ids.Add("board");
            if (sel.T.boat && sel.cargo != null && NearShore(sel)) ids.Add("unload");
            if (phase == Phase.Battle && !sel.T.boat) ids.Add("flag");
            ids.Add("cam");
            if (phase == Phase.Battle && !sel.T.boat) ids.Add("hold");
            ids.Add("x");
            bool narrow = VH < 520 || VW < 700;
            Func<string, string> label = id =>
            {
                switch (id)
                {
                    case "s2": return narrow ? "÷2" : "На 2"; case "s3": return narrow ? "÷3" : "На 3"; case "mg": return "Объединить";
                    case "wall": return sel.wall ? "Стена: ВКЛ" : "Стена"; case "board": return "В лодку"; case "unload": return "Высадить";
                    case "flag": return "К флагу"; case "cam": return camFollow == sel ? "Следим" : "Следить"; case "hold": return "Стоять"; case "x": return "X";
                }
                return id;
            };
            GUIStyle bs = S(13); float h = narrow ? 30 : 34, pad = 8, gap = 6, w = 0;
            List<float> ws = new List<float>(); foreach (string id in ids) { float bw = bs.CalcSize(new GUIContent(label(id))).x + 20; ws.Add(bw); w += bw + gap; }
            w = w - gap + pad * 2; float ph = h + pad * 2 + (narrow ? 0 : 18); w = Mathf.Max(w, 160);
            Rect pr = new Rect(VW / 2 - w / 2, VH - (narrow ? 56 : 70) - ph, w, ph);
            hudRects.Add(pr); Box(pr, bgRound, new Color(0.055f, 0.07f, 0.04f, 0.88f));
            if (!narrow) Tx(new Rect(pr.x, pr.y + 5, pr.width, 16), name, S(12), K.C(0xffd84a));
            float bx = pr.x + pad, byy = pr.yMax - pad - h;
            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i]; bool en = !((id == "s2" && n < 4) || (id == "s3" && n < 6));
                BK kind = id == "x" ? BK.PanelX : ((id == "wall" && sel.wall) || (id == "cam" && camFollow == sel)) ? BK.PanelOn : BK.Panel;
                if (Btn(new Rect(bx, byy, ws[i], h), label(id), kind, null, en, null, 13)) PanelAction(id);
                bx += ws[i] + gap;
                if (sel == null) break;
            }
        }
        void PanelAction(string a)
        {
            if (sel == null) return;
            if (a == "s2" || a == "s3")
            {
                if (squads.FindAll(q => q.team == K.BLUE).Count >= 14) { Hint("Слишком много отрядов", 2); return; }
                if (SplitSquad(sel, a == "s2" ? 2 : 3)) { Snd.Fx("select"); AddFloat(sel.bx, sel.by, "Разделено!", K.C(0xffd84a)); }
            }
            else if (a == "mg") { Squad t = MergeTarget(sel); if (t != null) { MergeSquads(sel, t); Snd.Fx("place", 4); AddFloat(sel.bx, sel.by, "Объединено!", K.C(0xffd84a)); } }
            else if (a == "wall") { sel.wall = !sel.wall; Snd.Fx("chop"); AddFloat(sel.bx, sel.by, sel.wall ? "Стена щитов!" : "Строй разомкнут", K.C(0xcfe3ff)); }
            else if (a == "flag") { Flag f = ActiveFlag; OrderPath(sel, new Vector2(f.x + Rnd(-14, 14), f.y + Rnd(-18, 18))); Snd.Fx("select"); AddFloat(sel.bx, sel.by, "К флагу!", K.C(0x7dff8a)); }
            else if (a == "board") { Squad b = FreeBoatNear(sel); if (b != null) BoardBoat(sel, b); }
            else if (a == "unload") UnloadBoat(sel);
            else if (a == "cam") { camFollow = camFollow == sel ? null : sel; hasCamVel = false; if (camFollow != null) camT.dist = Mathf.Min(camT.dist, 700); }
            else if (a == "hold") { OrderHold(sel); AddFloat(sel.bx, sel.by, "Стоять!", K.C(0xffd84a)); }
            else if (a == "x") sel = null;
        }
        void UpdateBoardBtn()
        {
            boardCand = null; boardBoat = null;
            if (phase != Phase.Battle) return;
            List<Squad> cands = new List<Squad>();
            if (sel != null && sel.team == K.BLUE) cands.Add(sel);
            foreach (Squad q in squads) if (q.team == K.BLUE && q != sel && q.order == Ord.Hold) cands.Add(q);
            foreach (Squad q in cands)
            {
                if (!AnyAlive(q)) continue;
                if (q.T.boat) { if (q.cargo != null && NearShore(q) && q.order == Ord.Hold) { boardCand = q; boardText = "Высадиться"; break; } continue; }
                Squad bt = FreeBoatNear(q, 110); if (bt != null) { boardCand = q; boardBoat = bt; boardText = "Сесть в лодку (" + q.T.name + ")"; break; }
            }
        }

        // ================= minimap =================
        float miniW, miniH;
        void BuildMiniTex()
        {
            int tw = 400, th = Mathf.RoundToInt(tw * (K.WY1 - K.WY0) / (K.WX1 - K.WX0)); Color32[] px = new Color32[tw * th];
            Color water = K.C(0x235f86), sand = K.C(0xe2cf96), grass = K.C(0x5c9a42), bridge = K.C(0xb9b4a8), forest = K.C(0x2c5220), hill = K.C(0x86b866);
            for (int j = 0; j < th; j++)
                for (int i = 0; i < tw; i++)
                {
                    float x = K.WX0 + (i + 0.5f) / tw * (K.WX1 - K.WX0), y = K.WY0 + (1f - (j + 0.5f) / th) * (K.WY1 - K.WY0);
                    Color c = water; float q = Terra.LandQ(x, y);
                    if (q < 1f) c = sand; if (q < 0.92f) c = grass;
                    if (q < 0.92f) { foreach (Hill h in Terra.hills) if (K.Dist(x, y, h.x, h.y) < h.r) c = Color.Lerp(c, hill, 0.35f); foreach (Forest f in Terra.forests) if (K.Dist(x, y, f.x, f.y) < f.r) c = forest; }
                    if (Terra.OnBridge(x, y)) c = bridge;
                    px[j * tw + i] = c;
                }
            if (miniTex != null) Destroy(miniTex);
            miniTex = new Texture2D(tw, th, TextureFormat.RGBA32, false); miniTex.wrapMode = TextureWrapMode.Clamp; miniTex.SetPixels32(px); miniTex.Apply(false);
        }
        Vector2 M2S(float x, float y) { return new Vector2(miniRect.x + (x - K.WX0) / (K.WX1 - K.WX0) * miniW, miniRect.y + (y - K.WY0) / (K.WY1 - K.WY0) * miniH); }
        Vector2 S2M(Vector2 p) { return new Vector2(K.WX0 + (p.x - miniRect.x) / miniW * (K.WX1 - K.WX0), K.WY0 + (p.y - miniRect.y) / miniH * (K.WY1 - K.WY0)); }
        void DrawMini(float t)
        {
            bool big = VW > 900 && VH > 600; float L = big ? 200 : VH < 500 ? 124 : Mathf.Min(160, VW * 0.3f);
            miniW = L; miniH = L * (K.WY1 - K.WY0) / (K.WX1 - K.WX0);
            miniRect = new Rect(10, VH - (VH < 520 ? 56 : 68) - miniH, miniW, miniH); hudRects.Add(miniRect);
            if (Event.current.type != EventType.Repaint) return;
            Box(new Rect(miniRect.x - 2, miniRect.y - 2, miniW + 4, miniH + 4), bgRound, new Color(1, 1, 1, 0.25f));
            Img(miniRect, miniTex, Color.white);
            if (phase == Phase.Deploy) { Vector2 a = M2S(K.ZX0, K.ZY0), b = M2S(K.ZX1, K.ZY1); Img(new Rect(a.x, a.y, b.x - a.x, b.y - a.y), Tex.white, new Color(0.29f, 0.6f, 1f, 0.25f)); }
            foreach (Unit u in units)
            {
                if (u.inBoat != null) continue; Vector2 p = M2S(u.x, u.y);
                Color c = u.T.boat ? (u.team == K.BLUE ? Color.white : K.C(0xffd0c8)) : u.team == K.BLUE ? K.C(0x8ec1ff) : K.C(0xff7a6a);
                float r = u.T.boat ? 2.6f : Units.SOLO(u.ti) ? 2.8f : 1.5f; Img(new Rect(p.x - r, p.y - r, r * 2, r * 2), Tex.white, c);
            }
            foreach (Flag f in flags)
            {
                if (!f.active) continue; Vector2 p = M2S(f.x, f.y);
                Circle(p.x, p.y, 6.5f, Color.white); Circle(p.x, p.y, 5, Units.FLAGC[f.team]);
                if (f.cap > 0.01f) ArcC(p.x, p.y, 8, f.cap, f.team == K.BLUE ? K.C(0xff4a4a) : K.C(0x7dff8a));
                if (f.att > 0 && f.def == 0 && Mathf.Sin(t * 10) > 0) RingC(p.x, p.y, 11, new Color(1, 1, 1, 0.8f));
            }
            float yT = TopPad() + 4, yB = VH - BotPad() - 4;
            Vector2[] cs = { M2S(Pick(0, yT)), M2S(Pick(VW, yT)), M2S(Pick(VW, yB)), M2S(Pick(0, yB)) };
            for (int i = 0; i < 4; i++)
            {
                Vector2 a = Clip(cs[i]), b = Clip(cs[(i + 1) % 4]);
                Line(a.x, a.y, b.x, b.y, 1.5f, new Color(1, 1, 1, 0.9f));
            }
        }
        Vector2 M2S(Vector2 w) { return M2S(w.x, w.y); }
        Vector2 Clip(Vector2 p) { return new Vector2(Mathf.Clamp(p.x, miniRect.x, miniRect.xMax), Mathf.Clamp(p.y, miniRect.y, miniRect.yMax)); }
        void MiniGo(Vector2 m) { Vector2 w = S2M(m); camT.x = K.Clamp(w.x, K.WX0, K.WX1); camT.y = K.Clamp(w.y, K.WY0, K.WY1); camFollow = null; hasCamVel = false; }

        // ================= announce (big centre text) =================
        void DrawAnnounce()
        {
            if (annT > 2f || annTitle.Length == 0) return;
            float k = annT / 2f, a, sc, dy = 0;
            if (k < 0.12f) { a = k / 0.12f; sc = 0.5f + (1.12f - 0.5f) * k / 0.12f; }
            else if (k < 0.22f) { a = 1; sc = 1.12f - 0.12f * (k - 0.12f) / 0.1f; }
            else if (k < 0.78f) { a = 1; sc = 1; }
            else { float q = (k - 0.78f) / 0.22f; a = 1 - q; sc = 1 + 0.05f * q; dy = -14 * q; }
            Matrix4x4 old = GUI.matrix; Vector2 c = new Vector2(VW / 2, VH * 0.34f + 40);
            GUI.matrix = old * Matrix4x4.TRS(new Vector3(c.x, c.y + dy, 0), Quaternion.identity, new Vector3(sc, sc, 1)) * Matrix4x4.TRS(new Vector3(-c.x, -c.y, 0), Quaternion.identity, Vector3.one);
            Color col = annCol; col.a = a;
            TxO(new Rect(0, VH * 0.34f, VW, 64), annTitle, S(VW < 600 ? 40 : 56), col, new Color(0.16f, 0.08f, 0, 0.7f * a), 3);
            if (annSub.Length > 0) TxO(new Rect(0, VH * 0.34f + 64, VW, 28), annSub, S(20), new Color(1, 1, 1, a), new Color(0, 0, 0, 0.7f * a), 2);
            GUI.matrix = old;
        }

        // ================= map overlay: banners, paths, bars, floating text =================
        float BannerR() { return VW < 500 ? 15 : 17; }
        SP BannerScreen(Squad sq)
        {
            float top = sq.ti == 6 ? 96 : Units.VEH(sq.ti) ? 30 : sq.T.boat ? 14 : sq.T.r * 2.8f;
            return ToScr(sq.bx, sq.by, (sq.T.boat ? K.SEA : Terra.Height(sq.bx, sq.by)) + top + 30);
        }
        bool OnScr(SP p, float m) { return !p.b && p.x > -m && p.x < VW + m && p.y > -m && p.y < VH + m; }
        void DrawBanner(Squad sq, float t)
        {
            int n = NAlive(sq); if (n == 0) return;
            SP bs = BannerScreen(sq); if (!OnScr(bs, 120)) return;
            float sx = bs.x, y = bs.y, R = BannerR() * (sq.ti == 4 ? 1.15f : sq.ti == 6 || sq.ti == 8 ? 1.25f : sq.T.boat ? 0.9f : 1f);
            SP f = ToScr(sq.bx, sq.by, Terra.Height(sq.bx, sq.by) + (sq.ti == 6 ? 62 : sq.T.r * 2));
            float al = sq.T.boat && sq.cargo == null ? 0.7f : 1f;
            if (!f.b && K.Dist(f.x, f.y, sx, y) < 150 && f.x > -20 && f.x < VW + 20 && f.y > -20 && f.y < VH + 20) Line(f.x, f.y, sx, y + R, 2, new Color(0, 0, 0, 0.4f * al));
            if (sel == sq) Circle(sx, y, R + 8, new Color(1f, 0.85f, 0.29f, 0.35f));
            if (sq.buffT > 0) RingC(sx, y, R + 10 + Mathf.Sin(t * 12) * 2, new Color(1f, 0.82f, 0.31f, 0.8f));
            bool solo = Units.SOLO(sq.ti);
            Circle(sx, y, R + (solo ? 2.5f : 1.5f), solo ? new Color(1f, 0.82f, 0.25f, al) : new Color(1, 1, 1, 0.9f * al));
            Img(new Rect(sx - R * 32 / 30f, y - R * 32 / 30f, R * 64 / 30f, R * 64 / 30f), sq.team == K.BLUE ? Tex.bannerBlue : Tex.bannerRed, new Color(1, 1, 1, al));
            if (sq.wall)
            {
                float a = R + 7; Color wc = K.C(0xcfe3ff);
                Line(sx - a, y - a, sx + a, y - a, 3, wc); Line(sx + a, y - a, sx + a, y + a, 3, wc); Line(sx + a, y + a, sx - a, y + a, 3, wc); Line(sx - a, y + a, sx - a, y - a, 3, wc);
            }
            if (!solo)
            {
                float fr = Mathf.Max(0, sq.morale / sq.moraleMax);
                RingC(sx, y, R + 4, new Color(0, 0, 0, 0.45f));
                ArcC(sx, y, R + 4, fr, fr > 0.5f ? K.C(0x7dff8a) : fr > 0.25f ? K.C(0xffd84a) : K.C(0xff5a5a));
            }
            else
            {
                Unit u = BoatOf(sq); float hp = u != null ? u.hp / u.maxhp : 0;
                Img(new Rect(sx - R * 1.4f, y + R + 5, R * 2.8f, 6), Tex.white, new Color(0, 0, 0, 0.6f * al));
                Img(new Rect(sx - R * 1.4f, y + R + 5, R * 2.8f * hp, 6), Tex.white, Units.VEH(sq.ti) || sq.ti == 6 ? K.C(0xff9a3a) : sq.T.boat ? K.C(0x8ec1ff) : K.C(0xffd23f));
                if (sq.T.boat && sq.cargo != null) CountBox(sx, y, R, NAlive(sq.cargo).ToString());
            }
            float isz = 64 * (R * 0.62f / 26f); Img(new Rect(sx - isz / 2, y - isz / 2, isz, isz), Tex.icon[sq.ti], new Color(1, 1, 1, al));
            if (!solo) CountBox(sx, y, R, n.ToString());
        }
        void CountBox(float sx, float y, float R, string txt)
        {
            GUIStyle st = S(Mathf.RoundToInt(R * 0.62f)); float bw = Mathf.Max(R * 0.95f, st.CalcSize(new GUIContent(txt)).x + 8);
            Rect r = new Rect(sx + R * 0.45f, y + R * 0.25f, bw, R * 0.8f);
            Box(r, bgSmall, new Color(0.04f, 0.047f, 0.03f, 0.88f)); Tx(r, txt, st, Color.white);
        }
        void DrawPath2D(List<Vector2> pts, Color col, Vector2 from, bool head)
        {
            if (pts.Count == 0) return;
            List<SP> sp = new List<SP>(pts.Count + 1);
            sp.Add(ToScr(from.x, from.y, Terra.Height(from.x, from.y) + 2));
            foreach (Vector2 p in pts) sp.Add(ToScr(p.x, p.y, Terra.Height(p.x, p.y) + 2));
            foreach (SP p in sp) if (p.b) return;
            for (int pass = 0; pass < 2; pass++)
            {
                float w = pass == 0 ? 6 : 3.5f; Color c = pass == 0 ? new Color(0, 0, 0, 0.35f * col.a) : col; float phase0 = 0;
                for (int i = 1; i < sp.Count; i++)
                {
                    float x0 = sp[i - 1].x, y0 = sp[i - 1].y, x1 = sp[i].x, y1 = sp[i].y;
                    if (!ClipSeg(ref x0, ref y0, ref x1, ref y1, -60, -60, VW + 60, VH + 60)) { phase0 = 0; continue; }
                    float len = K.Dist(x0, y0, x1, y1); if (len < 0.5f) continue;
                    float dx = (x1 - x0) / len, dy = (y1 - y0) / len, d = -phase0;
                    while (d < len) { float a = Mathf.Max(0, d), b = Mathf.Min(len, d + 12); if (b > a) Line(x0 + dx * a, y0 + dy * a, x0 + dx * b, y0 + dy * b, w, c); d += 21; }
                    phase0 = d - len;
                }
            }
            if (head && sp.Count > 1)
            {
                SP a = sp[sp.Count - 1], b = sp[sp.Count - 2]; float ang = Mathf.Atan2(a.y - b.y, a.x - b.x) * Mathf.Rad2Deg;
                ImgRot(new Vector2(a.x, a.y), 24, ang, Tex.arrow, col);
            }
        }
        // Liang–Barsky: clip a segment to a rectangle; false when it is completely outside
        static bool ClipSeg(ref float x0, ref float y0, ref float x1, ref float y1, float xmin, float ymin, float xmax, float ymax)
        {
            float dx = x1 - x0, dy = y1 - y0, t0 = 0, t1 = 1;
            float[] p = { -dx, dx, -dy, dy }, q = { x0 - xmin, xmax - x0, y0 - ymin, ymax - y0 };
            for (int i = 0; i < 4; i++)
            {
                if (Mathf.Abs(p[i]) < 1e-6f) { if (q[i] < 0) return false; continue; }
                float r = q[i] / p[i];
                if (p[i] < 0) { if (r > t1) return false; if (r > t0) t0 = r; } else { if (r < t0) return false; if (r < t1) t1 = r; }
            }
            float ox = x0, oy = y0; x0 = ox + t0 * dx; y0 = oy + t0 * dy; x1 = ox + t1 * dx; y1 = oy + t1 * dy; return true;
        }
        void DrawReticle(Squad tsq, float t, float al)
        {
            if (!AnyAlive(tsq)) return; Vector2 tc = Cen(tsq); float rr = 20;
            foreach (Unit u in tsq.units) if (!u.dead) rr = Mathf.Max(rr, K.Dist(u.x, u.y, tc.x, tc.y) + u.T.r + 6);
            float hh = Terra.Height(tc.x, tc.y); SP c = ToScr(tc.x, tc.y, hh), ex = ToScr(tc.x + rr, tc.y, hh), ez = ToScr(tc.x, tc.y + rr, hh);
            if (c.b || ex.b || ez.b) return;
            float rx = Mathf.Max(18, K.Dist(ex.x, ex.y, c.x, c.y)), ry = Mathf.Max(10, K.Dist(ez.x, ez.y, c.x, c.y)), k = 1 + Mathf.Sin(t * 8) * 0.06f;
            int N = 24;
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < N; i++)
                {
                    if (pass == 1 && ((i + Mathf.FloorToInt(t * 6)) % 3 == 2)) continue;
                    float a0 = i * 2 * Mathf.PI / N, a1 = (i + 1) * 2 * Mathf.PI / N;
                    Line(c.x + Mathf.Cos(a0) * rx * k, c.y + Mathf.Sin(a0) * ry * k, c.x + Mathf.Cos(a1) * rx * k, c.y + Mathf.Sin(a1) * ry * k, pass == 0 ? 6 : 3, pass == 0 ? new Color(0, 0, 0, 0.4f * al) : new Color(1f, 0.35f, 0.27f, al));
                }
            for (int i = 0; i < 4; i++)
            {
                float an = i * Mathf.PI / 2 + t * 0.8f, px = c.x + Mathf.Cos(an) * rx * k * 1.12f, py = c.y + Mathf.Sin(an) * ry * k * 1.12f;
                ImgRot(new Vector2(px + Mathf.Cos(an) * 5, py + Mathf.Sin(an) * 5), 14, an * Mathf.Rad2Deg + 180, Tex.arrow, new Color(1f, 0.35f, 0.27f, al));
            }
        }
        void DrawFlagMarker(Flag f, float t)
        {
            SP p = ToScr(f.x, f.y, Terra.Height(f.x, f.y) + 112); float sx = p.x, sy = p.y; if (p.b) { sx = VW - sx; sy = VH * 2; }
            float m = 18, y0 = TopPad() + m, y1 = VH - BotPad() - m;
            bool off = sx < m || sx > VW - m || sy < y0 || sy > y1; sx = K.Clamp(sx, m, VW - m); sy = K.Clamp(sy, y0, y1);
            float r = f.cap > 0.01f || off ? 15 : 12; bool own = f.team == K.BLUE; Color col = Units.FLAGC[f.team], enemy = own ? K.C(0xff4a4a) : K.C(0x4a9aff);
            float al = f.att > 0 && f.def == 0 ? 0.7f + 0.3f * Mathf.Sin(t * 10) : 1f;
            Circle(sx, sy, r + 3, new Color(0, 0, 0, 0.55f * al)); Circle(sx, sy, r, new Color(col.r, col.g, col.b, al));
            if (f.cap > 0.01f) ArcC(sx, sy, r + 2, f.cap, enemy);
            if (f.cap > 0.01f) Tx(new Rect(sx - 20, sy - 10, 40, 20), f.att > 0 && f.def == 0 ? f.secs + "с" : Mathf.RoundToInt(f.cap * 100) + "%", S(r > 12 ? 12 : 11), Color.white);
            else Img(new Rect(sx - r * 0.7f, sy - r * 0.7f, r * 1.4f, r * 1.4f), Tex.flagIco, Color.white);
            if (off)
            {
                SP q = ToScr(f.x, f.y, 0); float a = Mathf.Atan2(q.y - sy, q.x - sx);
                ImgRot(new Vector2(sx + Mathf.Cos(a) * (r + 8), sy + Mathf.Sin(a) * (r + 8)), 14, a * Mathf.Rad2Deg, Tex.arrow, col);
            }
            if (f.att > 0 && f.def == 0 && own) TxO(new Rect(K.Clamp(sx - 60, 4, VW - 124), sy - r - 24, 120, 16), "НАШ ФЛАГ!", S(12), K.C(0xff8a7a), new Color(0, 0, 0, 0.7f), 1.5f);
        }
        void DrawOverlay(float t)
        {
            foreach (Squad s in squads)
                if (s.team == K.BLUE && s.order == Ord.Path && s.path != null && AnyAlive(s) && s.pi < s.path.Count)
                    DrawPath2D(s.path.GetRange(s.pi, s.path.Count - s.pi), s.after != null ? new Color(1f, 0.39f, 0.31f, 0.9f) : new Color(1f, 0.85f, 0.29f, 0.9f), new Vector2(s.ax, s.ay), true);
            foreach (Squad s in squads)
                if (s.team == K.BLUE && s.order == Ord.Attack && s.target != null && AnyAlive(s) && AnyAlive(s.target))
                {
                    Vector2 tc = Cen(s.target); float al = sel == s ? 1f : 0.55f;
                    DrawPath2D(new List<Vector2> { tc }, new Color(1f, 0.35f, 0.27f, 0.9f * al), new Vector2(s.bx, s.by), true); DrawReticle(s.target, t, 0.8f * al);
                }
            if (drag != null && drag.pts.Count > 1 && phase == Phase.Battle)
            {
                Vector2 e = drag.pts[drag.pts.Count - 1]; Squad tgt = EnemyAt(new Vector2(drag.ex, drag.ey)) ?? EnemySquadAt(e.x, e.y);
                DrawPath2D(drag.pts.GetRange(1, drag.pts.Count - 1), tgt != null ? K.C(0xff5a46) : K.C(0xffd84a), drag.pts[0], true);
                if (tgt != null) DrawReticle(tgt, t, 1);
            }
            else if (hasHover && drag == null && pend == null && (phase == Phase.Battle || phase == Phase.Deploy) && BannerAt(hover, K.BLUE) == null)
            {
                if (phase == Phase.Battle && sel != null && AnyAlive(sel) && sel.order != Ord.Rout && !OverHud(hover))
                {
                    Squad tgt = sel.T.boat ? null : EnemyAt(hover);
                    if (tgt != null)
                    {
                        DrawReticle(tgt, t, 1); Vector2 tc = Cen(tgt);
                        DrawPath2D(new List<Vector2> { tc }, new Color(1f, 0.35f, 0.27f, 0.55f), new Vector2(sel.bx, sel.by), true);
                        SP l = BannerScreen(tgt); TxO(new Rect(l.x - 60, l.y - BannerR() - 30, 120, 18), "Атаковать", S(14), K.C(0xff8a7a), new Color(0, 0, 0, 0.75f));
                    }
                    else
                    {
                        Vector2 w = Pick(hover.x, hover.y);
                        if (w.x > K.WX0 && w.y > K.WY0 && w.x < K.WX1 && w.y < K.WY1)
                        {
                            SP mp = ToScr(w.x, w.y, Terra.Height(w.x, w.y) + 1);
                            for (int i = 0; i < 24; i++) { float a0 = i * Mathf.PI / 12, a1 = (i + 1) * Mathf.PI / 12; Line(mp.x + Mathf.Cos(a0) * 16, mp.y + Mathf.Sin(a0) * 9, mp.x + Mathf.Cos(a1) * 16, mp.y + Mathf.Sin(a1) * 9, 2.5f, new Color(1f, 0.85f, 0.29f, 0.9f)); }
                            DrawPath2D(new List<Vector2> { w }, new Color(1f, 0.85f, 0.29f, 0.35f), new Vector2(sel.bx, sel.by), false);
                        }
                    }
                }
            }
            foreach (Unit u in units)
            {
                if (u.hp >= u.maxhp || Units.SOLO(u.ti) || u.inBoat != null) continue;
                SP p = ToScr(u.x, u.y, Terra.Height(u.x, u.y) + u.T.r * 2.3f); if (p.b || p.x < -40 || p.x > VW + 40) continue;
                float w = Mathf.Max(12, u.T.r * 1.8f), f = u.hp / u.maxhp;
                Img(new Rect(p.x - w / 2 - 1, p.y - 1, w + 2, 5), Tex.white, new Color(0, 0, 0, 0.6f));
                Img(new Rect(p.x - w / 2, p.y, w * f, 3), Tex.white, f > 0.5f ? K.C(0x6dff7a) : f > 0.25f ? K.C(0xffd84a) : K.C(0xff5a5a));
            }
            List<Squad> order = new List<Squad>(squads); order.Sort((a, b) => a.bx.CompareTo(b.bx));
            foreach (Squad s in order) DrawBanner(s, t);
            foreach (Flag f in flags) if (f.active) DrawFlagMarker(f, t);
            GUIStyle fs = S(VW < 500 ? 13 : 15);
            foreach (FloatText f in floats)
            {
                SP p = ToScr(f.x, f.y, Terra.Height(f.x, f.y) + 30); if (p.b) continue;
                float a = Mathf.Min(1, (f.life - f.t) * 3); Color c = f.col; c.a = a;
                TxO(new Rect(p.x - 100, p.y - f.t * 30 - 12, 200, 24), f.text, fs, c, new Color(0, 0, 0, 0.75f * a), 1.8f);
            }
        }

        // ================= input =================
        bool OverHud(Vector2 m) { foreach (Rect r in hudRects) if (r.Contains(m)) return true; return false; }
        Squad BannerAt(Vector2 s, int team)
        {
            Squad best = null; float bd = 1e9f, R = BannerR();
            foreach (Squad q in squads)
            {
                if (q.team != team || !AnyAlive(q)) continue; SP b = BannerScreen(q); if (b.b) continue;
                float d = K.Dist(s.x, s.y, b.x, b.y); if (d < R + 18 && d < bd) { bd = d; best = q; }
            }
            if (best == null)
                foreach (Unit u in units)
                {
                    if (u.team != team) continue; SP p = ToScr(u.x, u.y, Terra.Height(u.x, u.y) + u.T.r); if (p.b) continue;
                    float d = K.Dist(p.x, p.y, s.x, s.y); if (d < 22 && d < bd) { bd = d; best = u.sq; }
                }
            return best;
        }
        Squad EnemySquadAt(float wx, float wy)
        {
            Squad best = null; float bd = 1e9f;
            foreach (Squad s in squads)
            {
                if (s.team != K.RED || !AnyAlive(s)) continue;
                foreach (Unit u in s.units) { if (u.dead) continue; float d = K.Dist(u.x, u.y, wx, wy); if (d < 42 && d < bd) { bd = d; best = s; } }
                Vector2 c = Cen(s); float dc = K.Dist(c.x, c.y, wx, wy); if (dc < 55 && dc < bd) { bd = dc; best = s; }
            }
            return best;
        }
        Squad EnemyAt(Vector2 s) { Squad b = BannerAt(s, K.RED); if (b != null) return b; Vector2 w = Pick(s.x, s.y); return EnemySquadAt(w.x, w.y); }
        bool CommandAt(Squad sqd, Vector2 s)
        {
            Vector2 w = Pick(s.x, s.y);
            if (sqd.T.boat) { BoatCommand(sqd, w.x, w.y); return true; }
            Squad tgt = EnemyAt(s);
            if (tgt != null) { OrderAttack(sqd, tgt); Vector2 tc = Cen(tgt); AddFloat(tc.x, tc.y, "В атаку!", K.C(0xff6a5a), 0.9f); Snd.Fx("select"); return true; }
            if (w.x > K.WX0 - 200 && w.y > K.WY0 - 200 && w.x < K.WX1 + 200 && w.y < K.WY1 + 200) { OrderPath(sqd, new Vector2(K.Clamp(w.x, K.WX0 + 15, K.WX1 - 15), K.Clamp(w.y, K.WY0 + 15, K.WY1 - 15))); Snd.Fx("click"); return true; }
            return false;
        }
        void ZoomAt(Vector2 s, float k)
        {
            Vector2 w0 = Pick(s.x, s.y); camT.dist = K.Clamp(camT.dist * k, DMIN, DMAX); CamSync();
            Vector2 w1 = Pick(s.x, s.y); camT.x = K.Clamp(camT.x + w0.x - w1.x, K.WX0, K.WX1); camT.y = K.Clamp(camT.y + w0.y - w1.y, K.WY0, K.WY1); CamSync();
        }
        void PanBy(float ax, float ay, float bx, float by)
        {
            Vector2 a = Pick(ax, ay), b = Pick(bx, by);
            camT.x = K.Clamp(camT.x + a.x - b.x, K.WX0, K.WX1); camT.y = K.Clamp(camT.y + a.y - b.y, K.WY0, K.WY1); cam.x = camT.x; cam.y = camT.y; PlaceCam(cam);
        }
        static readonly KeyCode[] CamKeys = { KeyCode.W, KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow, KeyCode.Equals, KeyCode.Minus, KeyCode.KeypadPlus, KeyCode.KeypadMinus };
        void HandleInput(Event e, Vector2 m)
        {
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (phase == Phase.Menu) break;
                    if (miniRect.Contains(m)) { miniDrag = true; MiniGo(m); e.Use(); break; }
                    if (OverHud(m)) break;
                    if (phase != Phase.Deploy && phase != Phase.Battle && phase != Phase.End) break;
                    PointerDown(m, e.button, e.shift); e.Use(); break;
                case EventType.MouseDrag:
                    if (miniDrag) { MiniGo(m); e.Use(); break; }
                    if (drag != null || pend != null) { PointerMove(m); e.Use(); }
                    break;
                case EventType.MouseUp:
                    if (miniDrag) { miniDrag = false; e.Use(); break; }
                    if (drag != null || pend != null) { PointerUp(m); e.Use(); }
                    break;
                case EventType.ScrollWheel:
                    if (phase == Phase.Menu || OverHud(m)) break;
                    ZoomAt(m, Mathf.Exp(e.delta.y * 0.04f)); e.Use(); break;
                case EventType.KeyDown:
                    if (Array.IndexOf(CamKeys, e.keyCode) >= 0 && phase != Phase.Menu) keys.Add(e.keyCode);
                    if (phase == Phase.Battle)
                    {
                        if (e.keyCode == KeyCode.Space) { paused = !paused; e.Use(); }
                        if (e.keyCode == KeyCode.Q && save.fire > 0) FireVolley();
                        if (e.keyCode == KeyCode.E && save.cry > 0) WarCry();
                    }
                    break;
                case EventType.KeyUp: keys.Remove(e.keyCode); break;
            }
        }
        void PointerDown(Vector2 m, int button, bool shift)
        {
            if (button == 2 || (button == 0 && shift)) { pend = new Pend { rot = true, lx = m.x, ly = m.y }; return; }
            if (button == 1) { if (phase == Phase.Battle && sel != null && AnyAlive(sel) && sel.order != Ord.Rout) CommandAt(sel, m); return; }
            Squad sq = phase != Phase.End ? BannerAt(m, K.BLUE) : null; Vector2 w = Pick(m.x, m.y);
            if (sq != null && sq.order != Ord.Rout && !(phase == Phase.Deploy && sq.T.boat))
            {
                bool was = sel == sq; sel = sq; Vector2 c = Cen(sq);
                drag = new Drag { sq = sq, was = was, sx = m.x, sy = m.y, ex = m.x, ey = m.y, ox = sq.ax - w.x, oy = sq.ay - w.y }; drag.pts.Add(c);
                Snd.Fx("select"); return;
            }
            hasCamVel = false; pend = new Pend { sx = m.x, sy = m.y, lx = m.x, ly = m.y };
        }
        void PointerMove(Vector2 m)
        {
            if (pend != null && pend.rot)
            {
                camT.yaw += (m.x - pend.lx) * 0.006f; if (camMode3d) camT.pitch = K.Clamp(camT.pitch + (m.y - pend.ly) * 0.004f, PMIN, PMAX);
                pend.lx = m.x; pend.ly = m.y; CamSync(); return;
            }
            if (pend != null)
            {
                if (!pend.moved && K.Dist(m.x, m.y, pend.sx, pend.sy) > 10) pend.moved = true;
                if (pend.moved)
                {
                    float ox = camT.x, oy = camT.y, now = Now(); PanBy(pend.lx, pend.ly, m.x, m.y);
                    float dtm = Mathf.Max(8, now - (pend.lt > 0 ? pend.lt : now - 16)) / 1000f;
                    pend.vx = pend.vx * 0.6f + (camT.x - ox) / dtm * 0.4f; pend.vy = pend.vy * 0.6f + (camT.y - oy) / dtm * 0.4f; pend.lt = now; camFollow = null; pend.lx = m.x; pend.ly = m.y;
                }
                return;
            }
            if (drag == null) return;
            drag.ex = m.x; drag.ey = m.y; Vector2 w = Pick(m.x, m.y);
            if (K.Dist(m.x, m.y, drag.sx, drag.sy) > 8) drag.moved = true;
            if (phase == Phase.Deploy) { if (drag.moved) PlaceSquad(drag.sq, w.x + drag.ox, w.y + drag.oy, true); return; }
            Vector2 last = drag.pts[drag.pts.Count - 1];
            if (K.Dist(w.x, w.y, last.x, last.y) > 10) drag.pts.Add(new Vector2(K.Clamp(w.x, K.WX0 + 12, K.WX1 - 12), K.Clamp(w.y, K.WY0 + 12, K.WY1 - 12)));
        }
        void PointerUp(Vector2 m)
        {
            if (pend != null)
            {
                Pend p = pend; pend = null; if (p.rot) return;
                if (p.moved) { if (Now() - p.lt < 90 && K.Hyp(p.vx, p.vy) > 60) { camVel = new Vector2(K.Clamp(p.vx, -4000, 4000), K.Clamp(p.vy, -4000, 4000)); hasCamVel = true; } return; }
                float now = Now();
                if (!(phase == Phase.Battle && sel != null && AnyAlive(sel)) && now - lastTapT < 320 && K.Dist(m.x, m.y, lastTapP.x, lastTapP.y) < 30)
                {
                    Vector2 w = Pick(m.x, m.y); camT.x = K.Clamp(w.x, K.WX0, K.WX1); camT.y = K.Clamp(w.y, K.WY0, K.WY1); camT.dist = K.Clamp(camT.dist * 0.55f, DMIN, DMAX); lastTapT = -9; return;
                }
                lastTapT = now; lastTapP = m;
                if (phase == Phase.Battle && sel != null && AnyAlive(sel) && sel.order != Ord.Rout) CommandAt(sel, m);
                else if (phase == Phase.Deploy) sel = null;
                return;
            }
            EndDrag();
        }
        void EndDrag()
        {
            if (drag == null) return; Drag d = drag; drag = null;
            if (phase == Phase.Deploy) { if (d.moved) Snd.Fx("place", 2); return; }
            if (phase != Phase.Battle) return;
            if (d.sq.T.boat && d.moved && d.pts.Count > 1) { Vector2 e = d.pts[d.pts.Count - 1]; BoatCommand(d.sq, e.x, e.y); return; }
            if (!d.moved || d.pts.Count < 2)
            {
                if (d.was && !d.sq.T.boat) { OrderHold(d.sq); AddFloat(d.sq.bx, d.sq.by, "Стоять!", K.C(0xffd84a)); }
                else Hint("Отряд выбран: нажми на землю — бежит туда, на врага — гонится за ним", 3);
                return;
            }
            Vector2 end = d.pts[d.pts.Count - 1]; Squad tgt = EnemyAt(new Vector2(d.ex, d.ey)) ?? EnemySquadAt(end.x, end.y);
            List<Vector2> pts = d.pts.GetRange(1, d.pts.Count - 1);
            if (tgt != null)
            {
                float len = 0; for (int i = 1; i < d.pts.Count; i++) len += Vector2.Distance(d.pts[i], d.pts[i - 1]);
                float direct = Vector2.Distance(end, d.pts[0]); Vector2 tc = Cen(tgt); AddFloat(tc.x, tc.y, "В атаку!", K.C(0xff6a5a), 0.9f); Snd.Fx("select");
                if (len < direct * 1.3f + 30) { OrderAttack(d.sq, tgt); return; } // straight drag onto enemy = chase it directly
                while (pts.Count > 1)
                {
                    Vector2 p = pts[pts.Count - 1]; bool near = false;
                    foreach (Unit u in tgt.units) if (!u.dead && K.Dist(u.x, u.y, p.x, p.y) < 70) { near = true; break; }
                    if (near) pts.RemoveAt(pts.Count - 1); else break;
                }
                OrderPath(d.sq, pts, tgt);
            }
            else { OrderPath(d.sq, pts, null); Snd.Fx("click"); }
        }

        // ================= menus =================
        class ShopItem { public string id, name, need; public int ti = -1, sym = -1, max; public Func<int, string> desc; public Func<int, int> cost; public Func<bool> locked; }
        List<ShopItem> SHOP;
        void InitShop()
        {
            SHOP = new List<ShopItem> {
                new ShopItem{ id="dragon", ti=6, name="Апач", desc=n=>Armies.DragonOwned(save)?"Уровень "+(n+1)+" · броня +15%, ракеты и пулемёт +12% за уровень":"Ударный вертолёт — бесплатно с "+K.DRAGON_LVL+"-й битвы", cost=n=>6+3*n, max=10, locked=()=>!Armies.DragonOwned(save) },
                new ShopItem{ id="sword_n", ti=0, name="+2 автоматчика", desc=n=>"В отряде: "+(10+2*n)+" / 200", cost=n=>3+Mathf.FloorToInt(n*0.25f), max=95 },
                new ShopItem{ id="arch_n", ti=1, name="+2 снайпера", desc=n=>"В отряде: "+(6+2*n)+" / 200", cost=n=>4+Mathf.FloorToInt(n*0.25f), max=97 },
                new ShopItem{ id="boat", ti=7, name="Лодки", desc=n=>"Ур. "+n+"/10 · мест: "+WarGame.BoatCapFor(n)+(n>=5?" · пушка: урон "+WarGame.BoatGunDmg(n):" · с 5 ур. — пушка"), cost=n=>4+2*n, max=10 },
                new ShopItem{ id="spear", ti=3, name="Гранатомётчики", desc=n=>n>0?"Нанято! 4 бойца с гранатами":"4 бойца: гранаты по площади, бьют технику", cost=n=>8, max=1 },
                new ShopItem{ id="spear_n", ti=3, name="+2 гранатомётчика", desc=n=>save.spear>0?"В отряде: "+(4+2*n)+" / 200":"Сначала найми гранатомётчиков", cost=n=>3+Mathf.FloorToInt(n*0.25f), max=98, need="spear" },
                new ShopItem{ id="shield", ti=5, name="Спецназ", desc=n=>n>0?"Нанят! Только когда ты защитник":"Щиты, броня, пистолеты — выдаётся только защитнику", cost=n=>8, max=1 },
                new ShopItem{ id="shield_n", ti=5, name="+2 спецназовца", desc=n=>save.shield>0?"В отряде: "+(8+2*n)+" / 200":"Сначала найми спецназ", cost=n=>3+Mathf.FloorToInt(n*0.25f), max=96, need="shield" },
                new ShopItem{ id="fire", sym=0, name="Артобстрел", desc=n=>n>0?"Изучено — кнопка в бою (Q)":"7 снарядов по самому большому скоплению врагов", cost=n=>6, max=1 },
                new ShopItem{ id="cry", sym=1, name="Приказ", desc=n=>n>0?"Изучено — кнопка в бою (E)":"+35 духа и +25% урона всем на 8 секунд", cost=n=>6, max=1 },
                new ShopItem{ id="sword_p", ti=0, name="Бронебойные патроны", desc=n=>"Автоматчики: урон +12%, здоровье +8% · ур. "+n+"/5", cost=n=>4+2*n, max=5 },
                new ShopItem{ id="arch_p", ti=1, name="Оптика", desc=n=>"Снайперы: урон +10%, дальность +6% · ур. "+n+"/5", cost=n=>4+2*n, max=5 },
                new ShopItem{ id="cav_p", ti=8, name="Броня техники", desc=n=>"Танк и БТР: броня +12%, огонь +5% · ур. "+n+"/5", cost=n=>4+2*n, max=5 },
                new ShopItem{ id="spear_p", ti=3, name="Мощные гранаты", desc=n=>"Гранатомётчики: урон +12%, здоровье +10% · ур. "+n+"/5", cost=n=>4+2*n, max=5, need="spear" },
                new ShopItem{ id="shield_p", ti=5, name="Тяжёлые бронежилеты", desc=n=>"Спецназ: здоровье +10%, урон +8% · ур. "+n+"/5", cost=n=>4+2*n, max=5, need="shield" },
                new ShopItem{ id="morale", sym=2, name="Рации", desc=n=>"Боевой дух всех отрядов +15% · ур. "+n+"/5", cost=n=>5+2*n, max=5 },
            };
        }
        Vector2 shopScroll;
        void Dim() { Img(new Rect(0, 0, VW, VH), Tex.white, new Color(0.03f, 0.047f, 0.024f, 0.62f)); }
        Rect CardRect(float h) { float w = Mathf.Min(440, VW - 32); h = Mathf.Min(h, VH - 32); return new Rect(VW / 2 - w / 2, VH / 2 - h / 2, w, h); }
        void Card(Rect r) { Box(new Rect(r.x, r.y + 8, r.width, r.height), bgRound, new Color(0, 0, 0, 0.35f)); Box(r, bgCard, Color.white); Box(new Rect(r.x, r.y, r.width, r.height), bgRound, new Color(0.29f, 0.38f, 0.21f, 0.0f)); }
        void DrawScreens()
        {
            Dim();
            bool small = VH < 560;
            if (screen == Screen_.Menu)
            {
                Blue_ForMenu(out Army a);
                string intro = "Два острова, два моста и океан. Каждый бой — жребий: ты <b>защищаешь</b> флаг 2,5 минуты или <b>штурмуешь</b> вражеский. Автоматчики, снайперы, танк, БТР, лодки и Апач.";
                float w = Mathf.Min(440, VW - 32) - 40; GUIStyle ps = S(14, false, TextAnchor.UpperCenter, true);
                float ih = small ? 0 : ps.CalcHeight(new GUIContent(intro), w);
                string next = "Следующая битва: <b>" + save.level + ". " + Units.BattleName(save.level) + "</b>" + (Units.HasGeneral(save.level) ? " · командир" : "") + (Units.RedDragon(save.level) ? " · вражеский Апач" : "") + "\nРоль решит жребий: защита или штурм" + (!Armies.DragonOwned(save) ? " · свой Апач — с " + K.DRAGON_LVL + "-й битвы" : "");
                GUIStyle ns = S(13, false, TextAnchor.MiddleCenter, true); float nh = ns.CalcHeight(new GUIContent(next), w - 16) + 16;
                float h = 22 + (small ? 34 : 50) + ih + (small ? 4 : 14) + (small ? 52 : 76) + 8 + nh + 8 + 30 + 8 + 52 + 8 + 50 + 18;
                Rect c = CardRect(h); Card(c); float y = c.y + 18;
                TxO(new Rect(c.x, y, c.width, small ? 34 : 48), "Война шариков", S(small ? 28 : 40), K.C(0xffc94a), new Color(0.35f, 0.15f, 0, 0.5f), 2); y += small ? 34 : 50;
                if (!small) { Tx(new Rect(c.x + 20, y, w, ih), intro, ps, K.C(0xc9d4bc)); y += ih + 14; } else y += 4;
                int[] show = { 0, 1, 3, 5, 2, 8, 6 }; List<int> have = new List<int>(); foreach (int i in show) if (a.counts[i] > 0) have.Add(i);
                float cw = (w - (have.Count - 1) * 8) / Mathf.Max(1, have.Count), ch = small ? 52 : 76, x = c.x + 20;
                foreach (int i in have)
                {
                    Rect r = new Rect(x, y, cw, ch); Box(r, bgRound, new Color(1, 1, 1, 0.05f));
                    float isz = small ? 24 : 40; Img(new Rect(r.center.x - isz / 2, r.y + 3, isz, isz), Tex.portrait[i, 0], Color.white);
                    string num = i == 6 ? "ур." + (save.dragon + 1) : a.counts[i].ToString(); string nm = i == 6 ? "Апач" : i == 5 ? "Спецназ" : Units.TYPES[i].name;
                    Tx(new Rect(r.x, r.y + isz + 2, r.width, small ? 14 : 20), num, S(small ? 13 : 17), Color.white);
                    if (!small) Tx(new Rect(r.x - 4, r.y + isz + 20, r.width + 8, 14), nm, S(10, false), K.C(0xa9b89a));
                    x += cw + 8;
                }
                y += ch + 8;
                Rect nr = new Rect(c.x + 20, y, w, nh); Box(nr, bgRound, new Color(0, 0, 0, 0.25f)); Tx(new Rect(nr.x + 8, nr.y + 8, nr.width - 16, nr.height - 16), next, ns, K.C(0xc9d4bc)); y += nh + 8;
                string pts = "Очки: " + save.pts; float pw = S(15).CalcSize(new GUIContent(pts)).x + 28; Box(new Rect(c.center.x - pw / 2, y, pw, 30), bgRound, new Color(1f, 0.85f, 0.29f, 0.15f)); Tx(new Rect(c.center.x - pw / 2, y, pw, 30), pts, S(15), K.C(0xffd84a)); y += 38;
                if (Btn(new Rect(c.x + 20, y, w, 50), "В бой", BK.Gold, null, true, null, 17)) { screen = Screen_.None; Snd.Music(true); SetupBattle(); BuildMiniTex(); }
                y += 58;
                if (Btn(new Rect(c.x + 20, y, (w - 8) / 2, 46), "Казарма", BK.Alt, null, true, null, 16)) { shopBack = Screen_.Menu; screen = Screen_.Shop; shopScroll = Vector2.zero; }
                if (Btn(new Rect(c.x + 20 + (w + 8) / 2, y, (w - 8) / 2, 46), "Как играть", BK.Alt, null, true, null, 16)) screen = Screen_.Help;
                if (Btn(new Rect(10, 8, 40, 40), "", BK.Top, null, true, Snd.on ? Tex.sndOn : Tex.sndOff)) { Snd.SetOn(!Snd.on); save.soundOff = !Snd.on; save.Persist(); }
            }
            else if (screen == Screen_.Help)
            {
                string[] items = {
                    "<b>Жребий:</b> в начале боя ты становишься <b>защитником</b> (не отдай свой флаг 2,5 минуты) или <b>нападающим</b> (захвати флаг врага за 2,5 минуты). Таймер — вверху.",
                    "<b>Острова:</b> на остров врага можно попасть только <b>по одному из двух мостов</b> или <b>на лодке</b>. У каждой стороны по 3 лодки у причалов.",
                    "<b>Лодки:</b> подведи пехоту к лодке — появится кнопка <b>«Сесть в лодку»</b>. В казарме лодки качаются до 10 уровня: больше мест, а с 5-го — своя пушка. Потом выбери лодку и нажми, куда плыть: на воду — поплывёт туда, на берег — причалит и высадит десант. Техника в лодку не помещается. Лодку можно потопить вместе с десантом!",
                    "<b>Захват:</b> встань в круг у флага. Пока у защитников есть живые бойцы — 40 секунд, если все погибли — 5 секунд. Погибла вся твоя армия — поражение. Штурм отбит до последнего бойца — победа защитника.",
                    "<b>Войска:</b> автоматчики (стреляют на ходу), снайперы (далеко и больно), гранатомётчики (урон по площади, хороши против техники), <b>БТР</b> (быстрый, пулемёт), <b>танк</b> (пушка, броня), <b>спецназ</b> со щитами и бронёй — <b>только у защитника</b>, и <b>Апач</b> — с 5-й битвы.",
                    "<b>Управление:</b> тяни знамя отряда — рисуй путь; тяни на врага — атака. Выбери отряд и нажми правой кнопкой — бежит туда / атакует. Нажми знамя — меню: разделить, объединить, в лодку, «Стена» у спецназа, «К флагу», «Следить».",
                    "<b>Артобстрел (Q)</b> — 7 снарядов по скоплению врагов. <b>Приказ (E)</b> — дух и урон +25%. Открываются в казарме.",
                    "<b>Камера:</b> тяни поле мышью, колёсико — зум, средняя кнопка или Shift+тяни — поворот и наклон, WASD — движение, кнопки справа: 2D/3D, компас, дом, «показать всех». Пробел — пауза.",
                };
                float w = Mathf.Min(440, VW - 32) - 40; GUIStyle hs = S(13, false, TextAnchor.UpperLeft, true); float th = 0; foreach (string s in items) th += hs.CalcHeight(new GUIContent(s), w - 14) + 6;
                Rect c = CardRect(18 + 36 + th + 12 + 50 + 18); Card(c); float y = c.y + 18;
                Tx(new Rect(c.x, y, c.width, 32), "Как играть", S(26), Color.white); y += 40;
                foreach (string s in items) { float hh = hs.CalcHeight(new GUIContent(s), w - 14); Circle(c.x + 24, y + 9, 2.5f, K.C(0xffd84a)); Tx(new Rect(c.x + 34, y, w - 14, hh), s, hs, K.C(0xc9d4bc)); y += hh + 6; }
                if (Btn(new Rect(c.x + 20, c.yMax - 68, w, 50), "Понятно", BK.Gold, null, true, null, 17)) screen = Screen_.Menu;
            }
            else if (screen == Screen_.Result)
            {
                float w = Mathf.Min(440, VW - 32) - 40; GUIStyle ps = S(14, false, TextAnchor.UpperCenter, true); float ph = ps.CalcHeight(new GUIContent(resText), w);
                Rect c = CardRect(18 + 50 + (resWin ? 48 : 0) + ph + 10 + 60 + 3 * 54 + 16); Card(c); float y = c.y + 18;
                TxO(new Rect(c.x, y, c.width, 48), resWin ? "Победа!" : "Поражение", S(40), resWin ? K.C(0xffc94a) : K.C(0xff7a5a), new Color(0, 0, 0, 0.35f), 2); y += 50;
                if (resWin) { for (int i = 0; i < 3; i++) { float k = Mathf.Clamp01((Time.unscaledTime - resShownT - i * 0.25f) / 0.5f); float sz = 42 * (k < 1 ? Mathf.Sin(k * Mathf.PI * 0.75f) / 0.7071f : 1f); Img(new Rect(c.center.x - 66 + i * 44 + 22 - sz / 2, y + 22 - sz / 2, sz, sz), i < resStars ? Tex.star : Tex.starOff, Color.white); } y += 48; }
                Tx(new Rect(c.x + 20, y, w, ph), resText, ps, K.C(0xc9d4bc)); y += ph + 10;
                string[] lab = { "Разбито", "Потери", "Время", "Очки" }; string[] val = { resKills.ToString(), resLosses.ToString(), (resTime / 60) + ":" + (resTime % 60).ToString("00"), "+" + resGain };
                for (int i = 0; i < 4; i++) { float cx = c.x + 20 + w * (i + 0.5f) / 4; Tx(new Rect(cx - 50, y, 100, 16), lab[i], S(12, false), K.C(0xa9b89a)); Tx(new Rect(cx - 50, y + 16, 100, 30), val[i], S(24), Color.white); }
                y += 60;
                if (Btn(new Rect(c.x + 20, y, w, 46), resWin ? "Следующая битва" : "Ещё раз", BK.Gold, null, true, null, 17)) { screen = Screen_.None; SetupBattle(); BuildMiniTex(); }
                y += 54;
                if (Btn(new Rect(c.x + 20, y, w, 46), "Казарма", BK.Alt, null, true, null, 16)) { shopBack = Screen_.Result; screen = Screen_.Shop; shopScroll = Vector2.zero; }
                y += 54;
                if (Btn(new Rect(c.x + 20, y, w, 46), "В меню", BK.Alt, null, true, null, 16)) { phase = Phase.Menu; sel = null; screen = Screen_.Menu; Snd.Music(false); }
            }
            else if (screen == Screen_.Shop)
            {
                float w = Mathf.Min(440, VW - 32) - 40, rowH = 58; Rect c = CardRect(18 + 36 + 38 + SHOP.Count * (rowH + 6) + 12 + 50 + 18); Card(c); float y = c.y + 18;
                Tx(new Rect(c.x, y, c.width, 32), "Казарма", S(26), Color.white); y += 38;
                string pts = "Очки: " + save.pts; float pw = S(15).CalcSize(new GUIContent(pts)).x + 28; Box(new Rect(c.center.x - pw / 2, y, pw, 30), bgRound, new Color(1f, 0.85f, 0.29f, 0.15f)); Tx(new Rect(c.center.x - pw / 2, y, pw, 30), pts, S(15), K.C(0xffd84a)); y += 38;
                Rect view = new Rect(c.x + 14, y, c.width - 28, c.yMax - 76 - y);
                float contentH = SHOP.Count * (rowH + 6), maxScroll = Mathf.Max(0, contentH - view.height);
                Event ev = Event.current;
                if (ev.type == EventType.ScrollWheel && view.Contains(ev.mousePosition / us)) { shopScroll.y = Mathf.Clamp(shopScroll.y + ev.delta.y * 22f, 0, maxScroll); ev.Use(); }
                if (ev.type == EventType.MouseDrag && view.Contains(ev.mousePosition / us)) { shopScroll.y = Mathf.Clamp(shopScroll.y - ev.delta.y / us, 0, maxScroll); }
                shopScroll.y = Mathf.Clamp(shopScroll.y, 0, maxScroll);
                Rect content = new Rect(view.x, view.y, view.width - (maxScroll > 0 ? 12 : 0), contentH);
                if (maxScroll > 0)
                {
                    float bh = view.height * view.height / contentH, by0 = view.y + (view.height - bh) * shopScroll.y / maxScroll;
                    Box(new Rect(view.xMax - 6, view.y, 6, view.height), bgSmall, new Color(1, 1, 1, 0.06f)); Box(new Rect(view.xMax - 6, by0, 6, bh), bgSmall, new Color(1, 1, 1, 0.3f));
                }
                float ry = view.y - shopScroll.y;
                foreach (ShopItem it in SHOP)
                {
                    if (ry < view.y - 1 || ry + rowH > view.yMax + 1) { ry += rowH + 6; continue; }
                    int n = save.Up(it.id); bool maxed = n >= it.max, locked = (it.need != null && save.Up(it.need) == 0) || (it.locked != null && it.locked());
                    Rect r = new Rect(content.x, ry, content.width, rowH); Box(r, bgRound, new Color(1, 1, 1, it.ti == 6 ? 0.1f : 0.05f));
                    Texture ic = it.sym >= 0 ? (it.sym == 0 ? Tex.bomb : it.sym == 1 ? Tex.horn : Tex.radio) : Tex.portrait[it.ti, 0];
                    Img(new Rect(r.x + 8, r.y + 11, 36, 36), ic, Color.white);
                    Tx(new Rect(r.x + 52, r.y + 6, r.width - 52 - 90, 20), it.name, S(14, true, TextAnchor.MiddleLeft), Color.white);
                    Tx(new Rect(r.x + 52, r.y + 26, r.width - 52 - 90, 30), it.desc(n), S(11, false, TextAnchor.UpperLeft, true), K.C(0xa9b89a));
                    int cost = it.cost(n); string bt = maxed ? (it.max == 1 ? "Есть" : "Макс.") : locked ? "Закрыто" : cost + " очк.";
                    bool can = !maxed && !locked && save.pts >= cost;
                    if (Btn(new Rect(r.xMax - 84, r.y + 9, 76, 40), bt, can ? BK.Gold : BK.Dark, null, can, null, 13)) { save.pts -= cost; save.AddUp(it.id); save.Persist(); Snd.Fx("coin", save.Up(it.id)); }
                    ry += rowH + 6;
                }
                if (Btn(new Rect(c.x + 20, c.yMax - 68, w, 50), "Готово", BK.Gold, null, true, null, 17))
                {
                    if (shopBack == Screen_.Result) { screen = Screen_.None; SetupBattle(); BuildMiniTex(); } else screen = Screen_.Menu;
                }
            }
        }
        float resShownT;
        void Blue_ForMenu(out Army a) { a = Armies.Blue(save, true); }
    }
}
