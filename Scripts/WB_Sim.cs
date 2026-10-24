// «Война шариков» — battle state and simulation (squads, orders, boats, combat, flags).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace WarOfBalls
{
    public partial class WarGame : MonoBehaviour
    {
        public static WarGame Inst;
        public SaveData save;

        // ---------- battle state ----------
        public List<Unit> units = new List<Unit>();
        public List<Squad> squads = new List<Squad>();
        public List<Shot> shots = new List<Shot>();
        public List<Corpse> corpses = new List<Corpse>();
        public List<Scorch> scorches = new List<Scorch>();
        public List<FloatText> floats = new List<FloatText>();
        public List<RingFx> rings = new List<RingFx>();
        public List<Strike> strikes = new List<Strike>();
        public List<Dock> docks = new List<Dock>();
        public Phase phase = Phase.Menu;
        public bool paused;
        public int speed = 1;
        public float battleT, aiT, shake, endT, abFire, abCry, capT, clock = K.TIMER;
        public Squad sel;
        public int kills, losses, flagWin, sqId;
        public bool finishWin, genKilled, redGone, zoneVisible;
        public string endWhy = "";
        public bool roleDef = true; // the player's role this battle: defend our flag / storm theirs
        public Flag[] flags = { new Flag { team = K.BLUE, x = K.FLX, y = K.H / 2f }, new Flag { team = K.RED, x = K.W - K.FLX, y = K.H / 2f } };
        public Flag ActiveFlag { get { return roleDef ? flags[0] : flags[1]; } }
        public Army lastRed;

        // ---------- small helpers ----------
        public static float Rnd(float a, float b) { return K.Rnd(a, b); }
        public List<Unit> Alive(Squad sq) { List<Unit> l = new List<Unit>(sq.units.Count); for (int i = 0; i < sq.units.Count; i++) if (!sq.units[i].dead) l.Add(sq.units[i]); return l; }
        public int NAlive(Squad sq) { int n = 0; for (int i = 0; i < sq.units.Count; i++) if (!sq.units[i].dead) n++; return n; }
        public bool AnyAlive(Squad sq) { if (sq == null) return false; for (int i = 0; i < sq.units.Count; i++) if (!sq.units[i].dead) return true; return false; }
        public Unit BoatOf(Squad sq) { for (int i = 0; i < sq.units.Count; i++) if (!sq.units[i].dead) return sq.units[i]; return null; }
        public Vector2 Cen(Squad sq) { float x = 0, y = 0; int n = 0; foreach (Unit u in sq.units) if (!u.dead) { x += u.x; y += u.y; n++; } return n > 0 ? new Vector2(x / n, y / n) : Vector2.zero; }
        public static Vector2 Centroid(List<Unit> l) { float x = 0, y = 0; foreach (Unit u in l) { x += u.x; y += u.y; } return l.Count > 0 ? new Vector2(x / l.Count, y / l.Count) : Vector2.zero; }
        public int AliveNotInBoat(Squad sq) { int n = 0; foreach (Unit u in sq.units) if (!u.dead && u.inBoat == null) n++; return n; }

        Squad MakeSquad(int team, int ti, int n, Mul mul, float x, float y, float dir)
        {
            UType T = Units.TYPES[ti];
            Squad sq = new Squad { team = team, ti = ti, T = T, mul = mul, ax = x, ay = y, dir = dir, n0 = n, moraleMax = 100f * mul.mor, morale = 100f * mul.mor, bx = x, by = y, id = ++sqId };
            for (int i = 0; i < n; i++)
            {
                Unit u = new Unit { team = team, sq = sq, T = T, ti = ti, face = dir, hp = T.hp * mul.hp, maxhp = T.hp * mul.hp, cool = Rnd(0, T.cd), id = UnityEngine.Random.value, slot = i, walk = Rnd(0, 6), aim = dir };
                sq.units.Add(u); units.Add(u);
            }
            Reslot(sq, true);
            foreach (Unit u in sq.units) { Vector2 p = SlotPos(u); u.x = p.x; u.y = p.y; }
            Vector2 c = Centroid(sq.units); sq.bx = c.x; sq.by = c.y;
            squads.Add(sq); return sq;
        }

        List<Vector2> SlotOffsets(Squad sq, int n)
        {
            int cols = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(n * 2.2f))); float sp = sq.T.space;
            List<Vector2> res = new List<Vector2>(n);
            for (int i = 0; i < n; i++) { int r = i / cols, inRow = Mathf.Min(cols, n - r * cols), c = i - r * cols; res.Add(new Vector2(-r * sp, (c - (inRow - 1) / 2f) * sp)); }
            return res;
        }
        public void Reslot(Squad sq, bool instant = false)
        {
            List<Unit> L = Alive(sq); sq.offs = SlotOffsets(sq, L.Count);
            float cs = Mathf.Cos(sq.dir), sn = Mathf.Sin(sq.dir);
            List<Vector2> pts = new List<Vector2>(sq.offs.Count); List<int> free = new List<int>(sq.offs.Count);
            for (int i = 0; i < sq.offs.Count; i++) { Vector2 o = sq.offs[i]; pts.Add(new Vector2(sq.ax + o.x * cs - o.y * sn, sq.ay + o.x * sn + o.y * cs)); free.Add(i); }
            float ax = sq.ax, ay = sq.ay;
            L.Sort((a, b) => ((b.x - ax) * cs + (b.y - ay) * sn).CompareTo((a.x - ax) * cs + (a.y - ay) * sn));
            foreach (Unit u in L)
            {
                int bi = 0; float bd = 1e9f;
                for (int k = 0; k < free.Count; k++) { Vector2 p = pts[free[k]]; float d = instant ? k : (p.x - u.x) * (p.x - u.x) + (p.y - u.y) * (p.y - u.y); if (d < bd) { bd = d; bi = k; } }
                if (free.Count > 0) { u.slot = free[bi]; free.RemoveAt(bi); }
            }
            sq.nAlive = L.Count;
        }
        public Vector2 SlotPos(Unit u)
        {
            Squad sq = u.sq; Vector2 o = u.slot < sq.offs.Count ? sq.offs[u.slot] : Vector2.zero;
            float cs = Mathf.Cos(sq.dir), sn = Mathf.Sin(sq.dir), lat = o.y;
            if (Mathf.Abs(sq.ay - K.BY) < K.BW + 50 && sq.ax > K.BXA - 60 && sq.ax < K.BXB + 60) lat = K.Clamp(lat, -(K.BW - 10), K.BW - 10); // squeeze into a column on the bridge
            return new Vector2(K.Clamp(sq.ax + o.x * cs - lat * sn, K.WX0 + u.T.r, K.WX1 - u.T.r), K.Clamp(sq.ay + o.x * sn + lat * cs, K.WY0 + u.T.r, K.WY1 - u.T.r));
        }

        // ---------- setting up a battle ----------
        public void SetupBattle()
        {
            units.Clear(); squads.Clear(); shots.Clear(); corpses.Clear(); scorches.Clear(); floats.Clear(); rings.Clear(); strikes.Clear();
            sel = null; kills = 0; losses = 0; battleT = 0; aiT = 1.5f; paused = false; endT = 0; genKilled = false; abFire = 0; abCry = 0; flagWin = 0; finishWin = false; redGone = false; capT = 0; clock = K.TIMER; endWhy = "";
            roleDef = UnityEngine.Random.value < 0.5f;
            foreach (Flag f in flags) { f.cap = 0; f.att = 0; f.def = 0; f.warned = false; f.ann = false; }
            flags[0].active = roleDef; flags[1].active = !roleDef;
            Terra.GenTerrain(); BuildTerrain3D(); ClearRigs(); PickAiStyle(save.level);
            Army B = Armies.Blue(save, roleDef), R = Armies.Red(save, roleDef, save.level); lastRed = R;
            float[,] lay = { { 0, 700, K.H / 2f - 110 }, { 1, 520, K.H / 2f }, { 3, 700, K.H / 2f + 120 }, { 5, 830, K.H / 2f }, { 2, 610, K.H / 2f - 300 }, { 8, 610, K.H / 2f + 300 }, { 6, 430, K.H / 2f + 330 }, { 4, 450, K.H / 2f - 200 } };
            for (int i = 0; i < lay.GetLength(0); i++) { int ti = (int)lay[i, 0]; if (B.counts[ti] > 0) MakeSquad(K.BLUE, ti, B.counts[ti], B.mul[ti], lay[i, 1], lay[i, 2], 0); }
            // big enemy groups come as several squads (so they can flank); several vehicles spread out
            for (int i = 0; i < lay.GetLength(0); i++)
            {
                int ti = (int)lay[i, 0], n = R.counts[ti]; if (n <= 0) continue;
                bool solo = Units.SOLO(ti); int k = solo ? n : Mathf.CeilToInt(n / 14f);
                for (int j = 0; j < k; j++)
                {
                    int m = solo ? 1 : Mathf.RoundToInt(n * (j + 1f) / k) - Mathf.RoundToInt(n * (float)j / k);
                    float oy = (j - (k - 1) / 2f) * (solo ? 80f : 95f);
                    MakeSquad(K.RED, ti, m, R.mul[ti], K.W - lay[i, 1] + (j % 2) * 40f, lay[i, 2] + oy + (solo ? 0f : Rnd(-20, 20)), Mathf.PI);
                }
            }
            // three boats at each island's piers
            docks.Clear();
            float[][] angs = { new float[] { -1.25f, 1.25f, -0.8f }, new float[] { Mathf.PI + 1.25f, Mathf.PI - 1.25f, Mathf.PI + 0.8f } };
            for (int ti = 0; ti < 2; ti++)
            {
                Island I = Terra.ISL[ti];
                foreach (float a in angs[ti])
                {
                    Vector2 s = Terra.ShoreSpot(I, a, 1.09f), l = Terra.ShoreSpot(I, a, 0.97f);
                    MakeSquad(ti, 7, 1, new Mul(), s.x, s.y, Mathf.Atan2(s.y - I.cy, s.x - I.cx));
                    docks.Add(new Dock { x = s.x, y = s.y, lx = l.x, ly = l.y, team = ti });
                }
            }
            BuildDocks();
            phase = Phase.Deploy; speed = 1;
            string goal = roleDef ? "ТЫ ЗАЩИТНИК: не отдай свой флаг 2,5 минуты" : "ТЫ НАПАДАЮЩИЙ: захвати флаг врага за 2,5 минуты";
            Hint(goal + ". Остров врага — только через мост или на лодке. Враг: " + aiStyle.name + (R.counts[4] > 0 ? " · у врага КОМАНДИР" : "") + (R.counts[6] > 0 ? " · у врага АПАЧ!" : ""), 9);
            Announce(roleDef ? "ЗАЩИТА" : "ШТУРМ", roleDef ? "Удержи флаг 2:30" : "Захвати флаг за 2:30", roleDef ? K.C(0x8ec1ff) : K.C(0xffd84a));
            CamReset(true); zoneVisible = true;
        }

        public void StartFight()
        {
            phase = Phase.Battle;
            Announce("В БОЙ!", roleDef ? "Держи флаг 2,5 минуты" : "Захвати флаг за 2,5 минуты", K.C(0xffd84a));
            zoneVisible = false;
            Hint("Тяни знамя — путь. Тяни на врага — атака. Подведи пехоту к лодке — появится кнопка «Сесть в лодку»", 6);
            Snd.Fx("horn");
        }

        // ================= orders =================
        public void OrderPath(Squad sq, List<Vector2> pts, Squad target = null)
        {
            if (sq.order == Ord.Rout || sq.order == Ord.Boat) return;
            if (!AnyAlive(sq) || pts.Count == 0) return;
            Vector2 c = Cen(sq); List<Vector2> outp = new List<Vector2>();
            if (sq.T.boat) { Vector2 cur = c; foreach (Vector2 p in pts) { Vector2 q = Terra.SeaSnap(p.x, p.y); outp.AddRange(Terra.WaterPath(cur.x, cur.y, q.x, q.y)); cur = q; } }
            else if (sq.T.fly) outp.AddRange(pts);
            else { Vector2 cur = c; foreach (Vector2 p in pts) { Vector2 q = Terra.SnapLand(p.x, p.y); outp.AddRange(Terra.Route(cur.x, cur.y, q.x, q.y)); cur = q; } }
            if (outp.Count == 0) return;
            sq.ax = c.x; sq.ay = c.y; sq.path = outp; sq.pi = 0; sq.order = Ord.Path; sq.after = target; sq.stuck = 0;
            sq.dir = Mathf.Atan2(outp[0].y - c.y, outp[0].x - c.x); Reslot(sq);
        }
        public void OrderPath(Squad sq, Vector2 p, Squad target = null) { OrderPath(sq, new List<Vector2> { p }, target); }
        public void OrderAttack(Squad sq, Squad tsq)
        {
            if (sq.order == Ord.Rout || sq.order == Ord.Boat || sq.T.boat) return;
            sq.order = Ord.Attack; sq.target = tsq; sq.path = null; foreach (Unit u in sq.units) u.rt = 0;
        }
        public void OrderHold(Squad sq)
        {
            if (sq.order == Ord.Rout || sq.order == Ord.Boat) return;
            if (!AnyAlive(sq)) return;
            Vector2 c = Cen(sq); sq.ax = c.x; sq.ay = c.y; sq.order = Ord.Hold; sq.path = null; sq.target = null; Reslot(sq);
        }

        // ---------- boats ----------
        public Squad FreeBoatNear(Squad sq, float r = 125f)
        {
            if (sq == null || sq.T.boat || Units.VEH(sq.ti) || sq.T.fly || sq.order == Ord.Boat || !AnyAlive(sq)) return null;
            Vector2 c = Cen(sq); Squad best = null; float bd = r;
            foreach (Squad b in squads)
            {
                if (!b.T.boat || b.team != sq.team || b.cargo != null) continue;
                Unit u = BoatOf(b); if (u == null) continue;
                float d = K.Dist(u.x, u.y, c.x, c.y); if (d < bd) { bd = d; best = b; }
            }
            return best;
        }
        Squad CloneSquad(Squad sq)
        {
            return new Squad
            {
                team = sq.team, ti = sq.ti, T = sq.T, mul = sq.mul, ax = sq.ax, ay = sq.ay, dir = sq.dir, moraleMax = sq.moraleMax, morale = sq.morale,
                lastHit = sq.lastHit, bx = sq.bx, by = sq.by, buffT = sq.buffT, wall = sq.wall, id = ++sqId, order = Ord.Hold
            };
        }
        // move some units of a squad into a new squad of the same kind
        Squad TakeUnits(Squad sq, List<Unit> list)
        {
            Squad q = CloneSquad(sq); q.units = new List<Unit>(list); q.n0 = list.Count;
            sq.units.RemoveAll(u => list.Contains(u)); sq.n0 = Mathf.Max(1, sq.n0 - list.Count);
            foreach (Unit u in list) { u.sq = q; u.target = null; }
            Vector2 c = Centroid(list); q.ax = c.x; q.ay = c.y; q.bx = c.x; q.by = c.y; Reslot(q); Reslot(sq); squads.Add(q); return q;
        }
        public bool BoardBoat(Squad sq, Squad b)
        {
            List<Unit> L = Alive(sq); if (L.Count == 0 || b.cargo != null) return false;
            Unit bu = BoatOf(b); if (bu == null) return false;
            if (L.Count > K.BOATCAP)
            {
                float bx = bu.x, byy = bu.y;
                L.Sort((a, c) => K.Dist(a.x, a.y, bx, byy).CompareTo(K.Dist(c.x, c.y, bx, byy)));
                TakeUnits(sq, L.GetRange(K.BOATCAP, L.Count - K.BOATCAP));
            }
            b.cargo = sq; sq.order = Ord.Boat; sq.path = null; sq.target = null;
            foreach (Unit u in Alive(sq)) { u.inBoat = b; u.vx = u.vy = 0; u.target = null; }
            if (sq.team == K.BLUE) { if (sel == sq) sel = b; Snd.Fx("splash"); AddFloat(bu.x, bu.y, "На борту: " + NAlive(sq), K.C(0xbfe3ff), 1.2f); }
            return true;
        }
        public bool NearShore(Squad b)
        {
            Unit u = BoatOf(b); if (u == null) return false;
            for (int i = 0; i < 16; i++) { float a = i / 16f * 6.28f; if (Terra.IsLand(u.x + Mathf.Cos(a) * 55f, u.y + Mathf.Sin(a) * 55f)) return true; }
            return false;
        }
        public Squad UnloadBoat(Squad b, bool hasGoTo = false, Vector2 goTo = default(Vector2))
        {
            Squad sq = b.cargo; Unit u = BoatOf(b); if (sq == null || u == null) return null;
            bool has = false; Vector2 spot = Vector2.zero; float bd = 1e9f;
            for (int i = 0; i < 24; i++)
            {
                float a = i / 24f * 6.28f;
                for (float r = 24; r < 120; r += 8)
                {
                    float x = u.x + Mathf.Cos(a) * r, y = u.y + Mathf.Sin(a) * r;
                    if (Terra.IsLand(x, y) && Terra.LandQ(x, y) < 0.98f) { float d = r + (hasGoTo ? K.Dist(x, y, goTo.x, goTo.y) * 0.05f : 0f); if (d < bd) { bd = d; spot = new Vector2(x, y); has = true; } break; }
                }
            }
            if (!has) return null;
            b.cargo = null; sq.order = Ord.Hold; List<Unit> L = Alive(sq); int k = 0;
            foreach (Unit v in L)
            {
                v.inBoat = null; float a = k * 2.4f, r = 8f + Mathf.Sqrt(k) * 9f; float px = spot.x + Mathf.Cos(a) * r, py = spot.y + Mathf.Sin(a) * r;
                if (!Terra.IsLand(px, py)) { px = spot.x; py = spot.y; }
                v.x = px; v.y = py; v.vx = v.vy = 0; k++;
            }
            Vector2 c = Centroid(L); sq.ax = c.x; sq.ay = c.y; sq.dir = Mathf.Atan2(c.y - u.y, c.x - u.x); Reslot(sq);
            if (hasGoTo) OrderPath(sq, goTo);
            if (sq.team == K.BLUE) { Snd.Fx("splash"); AddFloat(c.x, c.y, "Высадка!", K.C(0x7dff8a), 1.2f); if (sel == b) sel = sq; }
            return sq;
        }
        public void BoatCommand(Squad b, float wx, float wy)
        {
            Unit u = BoatOf(b); if (u == null) return;
            if (Terra.IsLand(wx, wy)) { Vector2 ls = Terra.LandingSpot(wx, wy, u.x, u.y); b.hasLandAt = b.cargo != null; b.landAt = new Vector2(wx, wy); OrderPath(b, ls); }
            else { b.hasLandAt = false; OrderPath(b, new Vector2(wx, wy)); }
            Snd.Fx("click");
        }

        public bool SplitSquad(Squad sq, int k)
        {
            List<Unit> L = Alive(sq); if (Units.SOLO(sq.ti) || L.Count < k * 2 || sq.order == Ord.Boat) return false;
            float cs = Mathf.Cos(sq.dir), sn = Mathf.Sin(sq.dir), ax = sq.ax, ay = sq.ay;
            L.Sort((a, b) => (-sn * (a.x - ax) + cs * (a.y - ay)).CompareTo(-sn * (b.x - ax) + cs * (b.y - ay)));
            List<List<Unit>> parts = new List<List<Unit>>(); int idx = 0;
            for (int i = 0; i < k; i++) { int n = Mathf.RoundToInt(L.Count * (i + 1f) / k) - idx; parts.Add(L.GetRange(idx, n)); idx += n; }
            int n0left = sq.n0; Squad keepOrder = (phase == Phase.Battle && sq.order == Ord.Attack && sq.target != null) ? sq.target : null;
            for (int i = 0; i < parts.Count; i++)
            {
                List<Unit> part = parts[i];
                int n0 = i == k - 1 ? n0left : Mathf.RoundToInt(sq.n0 * (float)part.Count / L.Count); n0left -= n0;
                Squad q = sq;
                if (i > 0) { q = CloneSquad(sq); q.units = new List<Unit>(part); q.n0 = n0; squads.Add(q); }
                else { sq.units = new List<Unit>(part); sq.n0 = n0; }
                foreach (Unit u in part) { u.sq = q; u.target = null; }
                Vector2 c = Centroid(part); q.ax = c.x; q.ay = c.y; q.bx = c.x; q.by = c.y; q.order = Ord.Hold; q.path = null; Reslot(q);
                if (phase == Phase.Deploy) PlaceSquad(q, c.x, c.y, true); else if (keepOrder != null) OrderAttack(q, keepOrder);
            }
            return true;
        }
        public Squad MergeTarget(Squad sq)
        {
            if (Units.SOLO(sq.ti) || sq.order == Ord.Boat) return null;
            Vector2 c = Cen(sq); Squad best = null; float bd = 170f;
            foreach (Squad o in squads)
            {
                if (o == sq || o.team != sq.team || o.ti != sq.ti || !AnyAlive(o) || o.order == Ord.Boat) continue;
                Vector2 oc = Cen(o); float d = K.Dist(oc.x, oc.y, c.x, c.y); if (d < bd) { bd = d; best = o; }
            }
            return best;
        }
        public void MergeSquads(Squad a, Squad b)
        {
            List<Unit> La = Alive(a), Lb = Alive(b); int n = La.Count + Lb.Count;
            a.morale = (a.morale * La.Count + b.morale * Lb.Count) / Mathf.Max(1, n); a.n0 += b.n0;
            foreach (Unit u in Lb) { u.sq = a; u.target = null; a.units.Add(u); }
            b.units.Clear(); squads.Remove(b); if (sel == b) sel = a;
            foreach (Squad q in squads) if (q.target == b) q.target = a;
            a.wall = a.wall && b.wall; Vector2 c = Cen(a); a.ax = c.x; a.ay = c.y;
            if (phase == Phase.Deploy) PlaceSquad(a, c.x, c.y, true);
            else { if (a.order != Ord.Attack) { a.order = Ord.Hold; a.path = null; } Reslot(a); }
        }
        public void PlaceSquad(Squad sq, float x, float y, bool quiet)
        {
            if (sq.T.boat) return;
            List<Unit> L = Alive(sq); List<Vector2> offs = SlotOffsets(sq, L.Count);
            float depth = 0, half = 0; foreach (Vector2 o in offs) { depth = Mathf.Max(depth, -o.x); half = Mathf.Max(half, Mathf.Abs(o.y)); }
            depth += sq.T.r; half += sq.T.r;
            sq.ax = K.Clamp(x, K.ZX0 + depth + 6, K.ZX1 - sq.T.r - 4); sq.ay = K.Clamp(y, K.ZY0 + half + 6, K.ZY1 - half - 6); sq.dir = 0; Reslot(sq, true);
            foreach (Unit u in L) { Vector2 p = SlotPos(u); u.x = p.x; u.y = p.y; u.face = 0; }
            Vector2 c = Centroid(L); sq.bx = c.x; sq.by = c.y;
            if (!quiet) Snd.Fx("place", 2);
        }

        // ================= combat =================
        float[] sfxT = new float[64]; Dictionary<string, int> sfxIdx = new Dictionary<string, int>();
        public void Sfx(string n, float gapMs = 70f)
        {
            int i; if (!sfxIdx.TryGetValue(n, out i)) { i = sfxIdx.Count % 64; sfxIdx[n] = i; }
            float t = Time.unscaledTime * 1000f; if (sfxT[i] > 0 && t - sfxT[i] < gapMs) return; sfxT[i] = t; Snd.Fx(n);
        }
        bool GenAura(Squad sq)
        {
            Squad g = null; foreach (Squad s in squads) if (s.ti == 4 && s.team == sq.team && AnyAlive(s)) { g = s; break; }
            if (g == null || g == sq || !AnyAlive(sq)) return false;
            Vector2 a = Cen(g), b = Cen(sq); return K.Dist(a.x, a.y, b.x, b.y) < 230f;
        }
        void HurtMorale(Squad sq, float v)
        {
            if (sq != null) sq.lastHit = battleT;
            if (sq == null || Units.SOLO(sq.ti)) return;
            if (sq.team == K.BLUE) v *= 0.6f; sq.morale = Mathf.Max(0, sq.morale - v);
            if (sq.morale <= 0 && battleT - sq.shakenT > 6)
            {
                sq.shakenT = battleT;
                if (AnyAlive(sq)) { Vector2 h = Cen(sq); AddFloat(h.x, h.y, sq.team == K.RED ? "Враг дрогнул!" : "Дрогнули!", sq.team == K.RED ? K.C(0x7dff8a) : K.C(0xff9a6a), 1.1f); }
            }
        }
        void HitUnit(int attTeam, Unit att, Unit tgt, float bas, Proj kind, float srcX, float srcY)
        {
            if (tgt.dead || tgt.inBoat != null) return;
            float vx = srcX - tgt.x, vy = srcY - tgt.y, l = K.Hyp(vx, vy); if (l < 1e-4f) l = 1;
            float c = (vx * Mathf.Cos(tgt.face) + vy * Mathf.Sin(tgt.face)) / l;
            float m = 1f, mor = 0.25f; string tag = null; bool small = Units.SMALL(kind);
            if (small && !Units.SOLO(tgt.ti)) { if (c < -0.4f) { m = 1.5f; tag = "Тыл ×1.5"; mor = 1.2f; } else if (c < 0.4f) { m = 1.25f; mor = 0.6f; } }
            if (Units.VEH(tgt.ti)) m *= kind == Proj.Snipe ? 0.35f : small ? 0.32f : kind == Proj.Grenade ? 1.4f : kind == Proj.Rocket ? 1.3f : 1f;
            if (tgt.ti == 6) m *= kind == Proj.Snipe ? 0.5f : small ? 0.6f : 1f;
            if (tgt.ti == 7) m *= small ? 0.5f : 1.2f;
            if (tgt.ti == 5) { m *= tgt.T.armor; if (c > 0.45f && small) { m *= tgt.sq.wall ? 0.18f : 0.35f; if (UnityEngine.Random.value < 0.12f) AddFloat(tgt.x, tgt.y, "Броня!", K.C(0xcfe3ff), 0.6f); } }
            if (small && Terra.InForest(tgt.x, tgt.y)) m *= 0.65f;
            if (att != null && att.sq != null) { if (att.sq.buffT > 0) m *= 1.25f; if (!Units.SOLO(att.sq.ti)) m *= 0.65f + 0.35f * att.sq.morale / att.sq.moraleMax; }
            tgt.hp -= bas * m; tgt.flash = 1; HurtMorale(tgt.sq, mor * (small ? 0.5f : 2f));
            Burst(tgt, small ? 2 : 6, Units.VEH(tgt.ti) || tgt.ti >= 6 ? 0xffd060 : 0xff4a4a, small ? 50f : 100f);
            if (tag != null && UnityEngine.Random.value < 0.12f) AddFloat(tgt.x, tgt.y, tag, K.C(0xffd84a), 1.1f);
            if (att != null && tgt.sq != null && tgt.sq.order != Ord.Path && (tgt.target == null || tgt.target.dead) && att.team != tgt.team) tgt.target = att;
            if (tgt.hp <= 0) KillUnit(tgt);
        }
        void KillUnit(Unit tgt)
        {
            if (tgt.dead) return; tgt.dead = true; tgt.deadT = battleT;
            if (!Units.SOLO(tgt.ti)) corpses.Add(new Corpse { x = tgt.x, y = tgt.y, team = tgt.team, ti = tgt.ti, t = 0, r = tgt.T.r, rot = Rnd(0, 6) });
            if (Units.VEH(tgt.ti) || tgt.ti == 6)
            {
                FxExplosion(tgt.x, tgt.y, tgt.ti == 6 ? 40 : 30, true);
                AddFloat(tgt.x, tgt.y, tgt.ti == 6 ? "Апач сбит!" : tgt.ti == 8 ? "Танк уничтожен!" : "БТР уничтожен!", tgt.team == K.RED ? K.C(0x7dff8a) : K.C(0xff6a5a), 1.8f);
                shake = Mathf.Max(shake, 0.35f);
                foreach (Squad s in squads) if (s.team == tgt.team && s != tgt.sq) HurtMorale(s, 12);
            }
            if (tgt.ti == 7)
            {
                AddFloat(tgt.x, tgt.y, "Лодка потоплена!", tgt.team == K.RED ? K.C(0x7dff8a) : K.C(0xff6a5a), 1.8f); Sfx("splash");
                for (int i = 0; i < 20; i++) Puff(tgt.x + Rnd(-14, 14), tgt.y + Rnd(-14, 14), K.SEA + 2, 0xffffff, false);
                Squad c = tgt.sq.cargo; if (c != null) { tgt.sq.cargo = null; foreach (Unit v in Alive(c)) KillUnit(v); }
                return;
            }
            if (tgt.team == K.RED) kills++; else losses++;
            if (!Units.SOLO(tgt.ti)) { float lossF = 1f - (float)NAlive(tgt.sq) / Mathf.Max(1, tgt.sq.n0); HurtMorale(tgt.sq, (7f + lossF * 10f) * Mathf.Min(1.5f, 10f / Mathf.Max(1, tgt.sq.n0))); }
            Burst(tgt, 10, tgt.team == K.RED ? 0xff8a7a : 0x8ab8ff, 110f);
            if (tgt.ti == 4)
            {
                genKilled = tgt.team == K.RED;
                foreach (Squad s in squads) if (s.team == tgt.team && s != tgt.sq) HurtMorale(s, 40);
                AddFloat(tgt.x, tgt.y, "Командир пал!", K.C(0xffd84a), 2f); shake = 0.4f; Sfx("crash");
            }
        }
        static bool CanHit(Unit u, Unit e) { return !(e.T.fly && (u.T.proj == Proj.Shell || u.T.proj == Proj.Grenade)); }
        Unit PickTarget(Unit u)
        {
            Squad sq = u.sq; List<Unit> pool = null;
            if (sq.order == Ord.Attack && sq.target != null && AnyAlive(sq.target)) pool = Alive(sq.target);
            Unit best = null; float bd = 1e9f, lim = pool != null ? 1e9f : u.T.aggro * (Terra.OnHill(u.x, u.y) ? 1.12f : 1f);
            List<Unit> src = pool ?? units;
            for (int i = 0; i < src.Count; i++)
            {
                Unit e = src[i];
                if (e.dead || e.team == u.team || e.inBoat != null || !CanHit(u, e)) continue;
                float d = K.Dist(e.x, e.y, u.x, u.y); if (d > lim) continue;
                if (u.ti == 1 && Units.VEH(e.ti)) d *= 1.7f; if ((u.ti == 8 || u.ti == 3) && Units.VEH(e.ti)) d *= 0.7f;
                if (d < bd) { bd = d; best = e; }
            }
            return best;
        }
        public float MuzzleH(Unit u) { return Terra.Height(u.x, u.y) + (u.T.fly ? 58f : u.ti == 8 ? 24f : Units.VEH(u.ti) ? 15f : u.T.boat ? 6f : u.T.r * 1.3f); }
        public float AimH(Unit t) { return Terra.Height(t.x, t.y) + (t.T.fly ? 58f : t.ti == 8 ? 13f : Units.VEH(t.ti) ? 9f : t.T.boat ? 3f : t.T.r); }
        static float PSpeed(Proj p)
        {
            switch (p) { case Proj.Bullet: case Proj.Mg: return 1500; case Proj.Pistol: return 1300; case Proj.Snipe: return 2800; case Proj.Shell: return 1050; case Proj.Grenade: return 230; case Proj.Rocket: return 540; }
            return 1000;
        }
        void Fire(Unit u, Unit t, float acc, Proj kind = Proj.None, float delay = 0f)
        {
            UType T = u.T; Proj P = kind != Proj.None ? kind : T.proj; if (P == Proj.None) return;
            float d = K.Dist(t.x, t.y, u.x, u.y), flight = d / PSpeed(P) + (P == Proj.Grenade ? 0.3f : 0f);
            float lead = (P == Proj.Grenade || P == Proj.Shell || P == Proj.Rocket) ? 0.8f : 0f;
            float sc = (P == Proj.Snipe ? 2f : P == Proj.Shell ? 12f : P == Proj.Grenade ? 14f : P == Proj.Rocket ? 16f : 4f + d * 0.035f) / (acc > 0 ? acc : 1f);
            float dmg = (P == Proj.Rocket ? 55f : T.dmg) * u.sq.mul.dmg * (Terra.OnHill(u.x, u.y) ? 1.15f : 1f);
            shots.Add(new Shot
            {
                kind = P, x0 = u.x, y0 = u.y, h0 = MuzzleH(u), tx = t.x + t.vx * flight * lead + Rnd(-1, 1) * sc, ty = t.y + t.vy * flight * lead + Rnd(-1, 1) * sc, th = AimH(t), t = 0, T = flight,
                src = u, tgt = t, dmg = dmg, splash = P == Proj.Rocket ? 44f : (P == Proj.Shell || P == Proj.Grenade) ? T.splash : 0f, delay = delay
            });
            if (delay <= 0)
            {
                u.cool = T.cd * Rnd(0.85f, 1.15f); u.draw = 1;
                Sfx(P == Proj.Snipe ? "snipe" : P == Proj.Shell ? "cannon" : P == Proj.Grenade ? "throw" : P == Proj.Mg ? "mg" : "shot", P == Proj.Mg || P == Proj.Bullet ? 55f : 90f);
                if (P == Proj.Shell || P == Proj.Snipe) { float a = u.aim, off = u.ti == 8 ? 54f : Units.VEH(u.ti) ? 30f : 12f; Puff(u.x + Mathf.Cos(a) * off, u.y + Mathf.Sin(a) * off, MuzzleH(u), 0xffd070, true); }
            }
        }
        public void FxExplosion(float x, float y, float R, bool big)
        {
            float h = Terra.Height(x, y); bool water = Terra.LandQ(x, y) > 1.02f && !Terra.OnBridge(x, y);
            if (water) { for (int i = 0; i < 16; i++) Puff(x + Rnd(-R, R) * 0.4f, y + Rnd(-R, R) * 0.4f, K.SEA + Rnd(1, 8), 0xeaf6ff, false); Sfx("splash"); return; }
            for (int i = 0; i < (big ? 22 : 12); i++) Puff(x + Rnd(-R, R) * 0.35f, y + Rnd(-R, R) * 0.35f, h + Rnd(2, 10), UnityEngine.Random.value < 0.5f ? 0xff8a20 : 0xffd060, true);
            for (int i = 0; i < (big ? 18 : 9); i++) Puff(x + Rnd(-R, R) * 0.45f, y + Rnd(-R, R) * 0.45f, h + Rnd(4, 16), 0x5a5a5a, false);
            scorches.Add(new Scorch { x = x, y = y, r = R * 0.7f, t = 0 }); Sfx("boom", 120f);
        }
        void Explode(float x, float y, float R, float dmg, int team, Unit src, Proj kind)
        {
            for (int i = 0; i < units.Count; i++)
            {
                Unit e = units[i];
                if (e.dead || e.team == team || e.inBoat != null) continue; if (e.T.fly && kind != Proj.Rocket) continue;
                float d = K.Dist(e.x, e.y, x, y); if (d < R + e.T.r) HitUnit(team, src, e, dmg * (1f - 0.55f * Mathf.Min(1f, d / R)), kind, x, y);
            }
            FxExplosion(x, y, R, kind == Proj.Shell || kind == Proj.Art);
        }

        void UpdateUnit(Unit u, float dt)
        {
            UType T = u.T; Squad sq = u.sq; u.cool -= dt; u.draw = Mathf.Max(0, u.draw - dt * 4f); u.flash = Mathf.Max(0, u.flash - dt * 5f);
            if (u.inBoat != null)
            {
                Unit bu = BoatOf(u.inBoat); if (bu == null) { KillUnit(u); return; }
                int i = 0, idx = 0; foreach (Unit v in sq.units) { if (v.dead) continue; if (v == u) { idx = i; break; } i++; }
                int row = idx / 2, col = idx % 2; float fx = Mathf.Cos(bu.face), fy = Mathf.Sin(bu.face), along = 11f - row * 3.2f, lat = (col == 1 ? -1f : 1f) * 4.3f;
                u.x = bu.x + fx * along - fy * lat; u.y = bu.y + fy * along + fx * lat; u.face = bu.face; u.vx = bu.vx; u.vy = bu.vy; u.spd = 0; return;
            }
            if (u.burn > 0) { u.burn -= dt; u.hp -= 5 * dt; if (u.hp <= 0) { KillUnit(u); return; } }
            bool hasG = false, stop = false, hasFace = false; float gx = 0, gy = 0, faceT = 0, spMul = 1f;
            if (T.boat) { Vector2 p = SlotPos(u); gx = p.x; gy = p.y; hasG = true; }
            else
            {
                u.rt -= dt;
                if (u.target == null || u.target.dead || u.target.inBoat != null || u.rt <= 0)
                {
                    u.rt = 0.35f + UnityEngine.Random.value * 0.2f; Unit nt = PickTarget(u);
                    if (nt != null || sq.order == Ord.Attack) u.target = nt;
                    else if (u.target != null && K.Dist(u.target.x, u.target.y, u.x, u.y) > T.aggro * 1.4f) u.target = null;
                }
                Unit t = (u.target != null && !u.target.dead && u.target.inBoat == null) ? u.target : null;
                float range = T.range * sq.mul.range * (Terra.OnHill(u.x, u.y) ? 1.12f : 1f);
                Vector2 sl = SlotPos(u);
                float d = t != null ? K.Dist(t.x, t.y, u.x, u.y) : 1e9f, ang = t != null ? Mathf.Atan2(t.y - u.y, t.x - u.x) : 0f;
                if (T.fly)
                {
                    if (sq.order == Ord.Path) { gx = sl.x; gy = sl.y; hasG = true; }
                    else if (t != null && (sq.order == Ord.Attack || K.Dist(t.x, t.y, sl.x, sl.y) < T.aggro * 1.3f)) { if (d > range * 0.7f) { gx = t.x; gy = t.y; hasG = true; } else stop = true; faceT = ang; hasFace = true; }
                    else { gx = sl.x; gy = sl.y; hasG = true; }
                    if (t != null && d <= range)
                    {
                        u.aim = ang; if (u.cool <= 0) Fire(u, t, 1);
                        u.rk -= dt; if (u.rk <= 0) { u.rk = 4.5f; for (int i = 0; i < 4; i++) Fire(u, t, 1, Proj.Rocket, i * 0.16f); Sfx("whoosh"); }
                    }
                }
                else if (sq.order == Ord.Path)
                {
                    gx = sl.x; gy = sl.y; hasG = true;
                    if (t != null && d <= range) { u.aim = ang; if (T.proj != Proj.Snipe && T.proj != Proj.Grenade && u.cool <= 0) Fire(u, t, 0.6f); }
                }
                else if (t != null)
                {
                    bool leash = sq.order == Ord.Hold && K.Dist(u.x, u.y, sl.x, sl.y) > 220 && d > range;
                    if (d <= range) { stop = true; faceT = ang; hasFace = true; u.aim = ang; if (u.cool <= 0) Fire(u, t, 1); }
                    else if (!leash) { gx = t.x; gy = t.y; hasG = true; }
                    else { gx = sl.x; gy = sl.y; hasG = true; }
                }
                else { gx = sl.x; gy = sl.y; hasG = true; }
            }
            if (u.dead) return;
            if (hasG && !T.fly && !T.boat && Terra.Side(u.x, u.y) != Terra.Side(gx, gy)) { Vector2 r = Terra.RouteFirst(u.x, u.y, gx, gy); gx = r.x; gy = r.y; }
            float mx = 0, my = 0;
            if (!stop && hasG)
            {
                float dx = gx - u.x, dy = gy - u.y, d = K.Hyp(dx, dy);
                if (d > 1.5f)
                {
                    float sp = T.speed * spMul * Mathf.Min(1f, d / 12f + 0.25f);
                    if (Units.VEH(u.ti) && Terra.InForest(u.x, u.y)) sp *= 0.6f; if (sq.order == Ord.Path && d > 25) sp *= 1.12f; if (sq.wall) sp *= 0.5f;
                    mx = dx / d * sp; my = dy / d * sp;
                }
            }
            bool heavy = Units.VEH(u.ti) || T.boat || T.fly; float acc = Mathf.Min(1f, dt * (heavy ? 2.6f : 6f));
            u.vx += (mx - u.vx) * acc; u.vy += (my - u.vy) * acc;
            float nx = u.x + u.vx * dt, ny = u.y + u.vy * dt;
            if (T.fly) { u.x = nx; u.y = ny; }
            else
            {
                bool bt = T.boat;
                if (Passable(bt, nx, ny)) { u.x = nx; u.y = ny; }
                else if (Passable(bt, nx, u.y)) { u.x = nx; u.vy *= 0.3f; }
                else if (Passable(bt, u.x, ny)) { u.y = ny; u.vx *= 0.3f; }
                else { u.vx *= 0.2f; u.vy *= 0.2f; }
            }
            u.spd = K.Hyp(u.vx, u.vy); u.walk += u.spd * dt * 0.35f;
            if (Units.VEH(u.ti) && u.spd > 25 && UnityEngine.Random.value < dt * 7) Puff(u.x - Mathf.Cos(u.face) * 18, u.y - Mathf.Sin(u.face) * 18, Terra.Height(u.x, u.y) + 2, 0xc9b48a, false);
            if (T.boat && u.spd > 20 && UnityEngine.Random.value < dt * 14) Puff(u.x - Mathf.Cos(u.face) * 16 + Rnd(-4, 4), u.y - Mathf.Sin(u.face) * 16 + Rnd(-4, 4), K.SEA + 1.5f, 0xffffff, false);
            float tf = (hasFace && !Units.VEH(u.ti)) ? faceT : (u.spd > 4 ? Mathf.Atan2(u.vy, u.vx) : (sq.order == Ord.Hold && u.target == null && !T.boat ? sq.dir : u.face));
            float da = K.WrapAngle(tf - u.face); u.face += da * Mathf.Min(1f, dt * (heavy ? 2.5f : 8f));
            if (Units.VEH(u.ti) && !(u.target != null && !u.target.dead)) u.aim += K.WrapAngle(u.face - u.aim) * Mathf.Min(1f, dt * 2f);
            u.x = K.Clamp(u.x, K.WX0 + T.r, K.WX1 - T.r); u.y = K.Clamp(u.y, K.WY0 + T.r, K.WY1 - T.r);
        }
        static bool Passable(bool boat, float x, float y) { return boat ? Terra.IsWater(x, y) : Terra.IsLand(x, y); }
        // spec-ops with their shields up cover whoever stands behind them
        Unit ShieldBlocks(Unit t, float sx, float sy)
        {
            foreach (Unit v in units)
            {
                if (v.ti != 5 || v.team != t.team || v.dead || !v.sq.wall || v == t) continue;
                float d = K.Dist(v.x, v.y, t.x, t.y); if (d > 48) continue;
                if (K.Dist(sx, sy, v.x, v.y) >= K.Dist(sx, sy, t.x, t.y)) continue;
                float l = K.Dist(sx, sy, v.x, v.y); if (l < 1e-4f) l = 1;
                float c = (Mathf.Cos(v.face) * (sx - v.x) + Mathf.Sin(v.face) * (sy - v.y)) / l;
                if (c > 0.35f) return v;
            }
            return null;
        }
        Dictionary<long, List<Unit>> sepMap = new Dictionary<long, List<Unit>>();
        Stack<List<Unit>> sepPool = new Stack<List<Unit>>();
        static long Cell(float x, float y) { return ((long)Mathf.FloorToInt(x / 30f) << 32) ^ (uint)Mathf.FloorToInt(y / 30f); }
        void Separate()
        {
            foreach (var kv in sepMap) { kv.Value.Clear(); sepPool.Push(kv.Value); }
            sepMap.Clear();
            foreach (Unit u in units)
            {
                if (u.dead || u.T.fly || u.T.boat || u.inBoat != null) { u.hasPrev = false; continue; }
                u.px = u.x; u.py = u.y; u.hasPrev = true;
                long k = Cell(u.x, u.y); List<Unit> a;
                if (!sepMap.TryGetValue(k, out a)) { a = sepPool.Count > 0 ? sepPool.Pop() : new List<Unit>(); sepMap[k] = a; }
                a.Add(u);
            }
            foreach (Unit u in units)
            {
                if (!u.hasPrev || u.dead) continue;
                int cx = Mathf.FloorToInt(u.x / 30f), cy = Mathf.FloorToInt(u.y / 30f);
                for (int i = -1; i <= 1; i++)
                    for (int j = -1; j <= 1; j++)
                    {
                        List<Unit> a; if (!sepMap.TryGetValue(((long)(cx + i) << 32) ^ (uint)(cy + j), out a)) continue;
                        foreach (Unit v in a)
                        {
                            if (v == u || v.id < u.id) continue;
                            float dx = v.x - u.x, dy = v.y - u.y, d = K.Hyp(dx, dy), m = u.T.r + v.T.r + 1;
                            if (d < m && d > 0.01f)
                            {
                                float p = m - d, nx = dx / d, ny = dy / d, wu = Units.VEH(u.ti) ? 0.15f : 0.5f, wv = Units.VEH(v.ti) ? 0.15f : 0.5f, s = wu + wv;
                                u.x -= nx * p * wu / s; u.y -= ny * p * wu / s; v.x += nx * p * wv / s; v.y += ny * p * wv / s;
                            }
                        }
                    }
            }
            foreach (Unit u in units) { if (!u.hasPrev || u.dead) continue; if (!Terra.IsLand(u.x, u.y)) { u.x = u.px; u.y = u.py; } }
        }
        void UpdateSquad(Squad sq, float dt)
        {
            if (sq.order == Ord.Boat) return;
            List<Unit> L = Alive(sq); if (L.Count == 0) return;
            if (L.Count != sq.nAlive) Reslot(sq);
            sq.buffT = Mathf.Max(0, sq.buffT - dt);
            Vector2 c = Centroid(L); sq.bx += (c.x - sq.bx) * Mathf.Min(1f, dt * 6f); sq.by += (c.y - sq.by) * Mathf.Min(1f, dt * 6f);
            if (battleT - sq.lastHit > 3) sq.morale = Mathf.Min(sq.moraleMax, sq.morale + dt * (GenAura(sq) ? 8f : 4f));
            if (sq.order == Ord.Path && sq.path != null)
            {
                float lag = 0; foreach (Unit u in L) { Vector2 p = SlotPos(u); lag += K.Dist(u.x, u.y, p.x, p.y); } lag /= L.Count;
                float step = sq.T.speed * (lag > 34 ? 0.25f : lag > 18 ? 0.7f : 1f) * dt;
                while (step > 0 && sq.pi < sq.path.Count)
                {
                    Vector2 pp = sq.path[sq.pi]; float dx = pp.x - sq.ax, dy = pp.y - sq.ay, d = K.Hyp(dx, dy);
                    if (d < 0.01f) { sq.pi++; continue; }
                    float ta = Mathf.Atan2(dy, dx); sq.dir += K.WrapAngle(ta - sq.dir) * Mathf.Min(1f, dt * 5f);
                    if (d <= step) { sq.ax = pp.x; sq.ay = pp.y; step -= d; sq.pi++; } else { sq.ax += dx / d * step; sq.ay += dy / d * step; step = 0; }
                }
                if (sq.after != null && sq.pi >= sq.path.Count - 2)
                {
                    foreach (Unit u in sq.after.units) if (!u.dead && K.Dist(u.x, u.y, c.x, c.y) < sq.T.range + 40) { OrderAttack(sq, sq.after); return; }
                }
                if (sq.pi >= sq.path.Count && lag < (Units.SOLO(sq.ti) ? 30 : 16)) { if (sq.after != null && AnyAlive(sq.after)) OrderAttack(sq, sq.after); else { sq.order = Ord.Hold; sq.path = null; } }
                if (sq.order == Ord.Path && sq.path != null && sq.pi >= sq.path.Count) { sq.stuck += dt; if (sq.stuck > 2) { if (sq.after != null && AnyAlive(sq.after)) OrderAttack(sq, sq.after); else { sq.order = Ord.Hold; sq.path = null; } } }
            }
            else if (sq.order == Ord.Attack) { if (sq.target == null || AliveNotInBoat(sq.target) == 0) OrderHold(sq); }
            if (sq.T.boat && sq.order == Ord.Hold && sq.hasLandAt && sq.cargo != null && NearShore(sq)) { Vector2 g = sq.landAt; sq.hasLandAt = false; UnloadBoat(sq, true, g); }
        }

        // ================= abilities =================
        public void FireVolley()
        {
            if (phase != Phase.Battle || abFire > 0) return;
            Unit best = null; float bn = 0;
            foreach (Unit e in units)
            {
                if (e.team == K.BLUE || e.dead || e.inBoat != null || e.T.fly) continue;
                float n = 0; foreach (Unit o in units) if (o.team == K.RED && !o.dead && o.inBoat == null && K.Dist(o.x, o.y, e.x, e.y) < 80) n += Units.VEH(o.ti) ? 4 : 1;
                if (n > bn) { bn = n; best = e; }
            }
            if (best == null) { Hint("Нет целей для артобстрела", 2); return; }
            for (int i = 0; i < 7; i++) strikes.Add(new Strike { x = best.x + Rnd(-55, 55), y = best.y + Rnd(-55, 55), t = 1.1f + i * 0.22f, team = K.BLUE });
            rings.Add(new RingFx { x = best.x, y = best.y, t = 0, col = K.C(0xff5a3a) }); abFire = 30; Sfx("whistle"); Hint("Артобстрел! Снаряды летят…", 2);
        }
        public void WarCry()
        {
            if (phase != Phase.Battle || abCry > 0) return;
            foreach (Squad s in squads)
                if (s.team == K.BLUE && AnyAlive(s) && !s.T.boat) { s.morale = Mathf.Min(s.moraleMax, s.morale + 35); s.buffT = 8; Vector2 c = Cen(s); rings.Add(new RingFx { x = c.x, y = c.y, t = 0, col = K.C(0xffd84a) }); }
            abCry = 45; Snd.Fx("horn"); Hint("Приказ: в атаку! Дух поднят, урон +25%", 2.5f);
        }

        // ================= main update =================
        void UpdateSim(float dt)
        {
            if (phase != Phase.Battle && phase != Phase.End) return;
            battleT += dt; abFire = Mathf.Max(0, abFire - dt); abCry = Mathf.Max(0, abCry - dt);
            if (phase == Phase.Battle) { clock -= dt; aiT -= dt; if (aiT <= 0) { aiT = aiT0; EnemyAI(); } }
            foreach (Strike s in strikes) { s.t -= dt; if (s.t <= 0 && !s.done) { s.done = true; Explode(s.x, s.y, 50, 70, s.team, null, Proj.Art); } }
            strikes.RemoveAll(s => s.done);
            for (int i = 0; i < squads.Count; i++) UpdateSquad(squads[i], dt);
            for (int i = 0; i < units.Count; i++) if (!units[i].dead) UpdateUnit(units[i], dt);
            Separate();
            for (int i = 0; i < shots.Count; i++)
            {
                Shot a = shots[i];
                if (a.delay > 0) { a.delay -= dt; if (a.delay <= 0) { a.x0 = a.src.x; a.y0 = a.src.y; a.h0 = MuzzleH(a.src); } continue; }
                a.t += dt;
                if (a.kind == Proj.Rocket && UnityEngine.Random.value < dt * 40) { float k = Mathf.Min(1f, a.t / a.T); Puff(a.x0 + (a.tx - a.x0) * k, a.y0 + (a.ty - a.y0) * k, ProjH(a, k), 0xbbbbbb, false); }
                if (a.t >= a.T && !a.done)
                {
                    a.done = true;
                    if (a.splash > 0) Explode(a.tx, a.ty, a.splash, a.dmg, a.src.team, a.src, a.kind);
                    else
                    {
                        Unit t = a.tgt;
                        if (t != null && !t.dead && t.inBoat == null && K.Dist(t.x, t.y, a.tx, a.ty) < t.T.r + 6)
                        {
                            Unit blk = a.kind != Proj.Snipe ? ShieldBlocks(t, a.x0, a.y0) : null;
                            if (blk != null) { Puff(blk.x, blk.y, AimH(blk), 0xffe0a0, true); if (UnityEngine.Random.value < 0.15f) AddFloat(blk.x, blk.y, "Блок!", K.C(0xcfe3ff), 0.6f); }
                            else HitUnit(a.src.team, a.src, t, a.dmg, a.kind, a.x0, a.y0);
                        }
                        else if (UnityEngine.Random.value < 0.4f)
                        {
                            bool w = Terra.LandQ(a.tx, a.ty) > 1.02f && !Terra.OnBridge(a.tx, a.ty);
                            Puff(a.tx, a.ty, w ? K.SEA + 1 : Terra.Height(a.tx, a.ty) + 1, w ? 0xeaf6ff : 0xc9b48a, false);
                        }
                    }
                }
            }
            shots.RemoveAll(a => a.done);
            units.RemoveAll(u => u.dead);
            foreach (Squad s in squads) s.units.RemoveAll(u => u.dead);
            if (phase == Phase.Battle)
            {
                UpdateFlags(dt);
                bool bl = !units.Exists(u => u.team == K.BLUE && !u.T.boat), rl = !units.Exists(u => u.team == K.RED && !u.T.boat);
                Flag F = ActiveFlag;
                if (rl && !redGone && !roleDef) { redGone = true; Announce("Защитники разбиты!", "Захват — 5 секунд", K.C(0xffd84a)); Hint("Врагов не осталось — встань у флага, захват 5 секунд!", 5); }
                if (F.cap >= 1)
                {
                    flagWin = !roleDef ? 1 : -1;
                    End(!roleDef, "flag", !roleDef ? "ПОБЕДА!" : "ПОРАЖЕНИЕ", !roleDef ? "Флаг врага захвачен" : "Враг захватил наш флаг");
                    shake = 0.3f; AddFloat(F.x, F.y, "Флаг захвачен!", !roleDef ? K.C(0x7dff8a) : K.C(0xff6a5a), 2.2f);
                }
                else if (bl) End(false, "dead", "ПОРАЖЕНИЕ", "Армия уничтожена");
                else if (rl && roleDef) End(true, "wiped", "ПОБЕДА!", "Штурм отбит — враги уничтожены");
                else if (clock <= 0) End(roleDef, roleDef ? "held" : "time", roleDef ? "ПОБЕДА!" : "ВРЕМЯ ВЫШЛО", roleDef ? "Флаг удержан 2,5 минуты" : "Флаг не захвачен за 2,5 минуты");
            }
            else { endT -= dt; if (endT <= 0 && phase == Phase.End) { phase = Phase.Done; Finish(finishWin); } }
        }
        void End(bool win, string why, string t, string sub)
        {
            phase = Phase.End; endT = 2.6f; finishWin = win; endWhy = why; capT = battleT;
            Announce(t, sub, win ? K.C(0xffd84a) : K.C(0xff6a5a)); Sfx(win ? "capture" : "alarm");
        }
        void UpdateFlags(float dt)
        {
            Flag f = ActiveFlag; int own = f.team, att = 0, def = 0;
            foreach (Unit u in units)
            {
                if (u.dead || u.inBoat != null || u.T.boat) continue;
                float d = K.Dist(u.x, u.y, f.x, f.y);
                if (u.team != own && d < K.FLAG_R) att += u.ti == 6 ? 3 : 1; else if (u.team == own && d < K.FLAG_R * 1.35f) def++;
            }
            f.att = att; f.def = def;
            bool defAlive = units.Exists(u => u.team == own && !u.T.boat); float T = defAlive ? 40f : 5f; f.secs = Mathf.CeilToInt((1 - f.cap) * T);
            if (att > 0 && def == 0) f.cap = Mathf.Min(1f, f.cap + dt / T); else f.cap = Mathf.Max(0f, f.cap - dt * (def > 0 ? 0.05f : 0.01f));
            if (own == K.RED && att > 0 && def == 0 && f.cap > 0.02f && !f.ann) { f.ann = true; Announce("Захват флага!", "", K.C(0x7dff8a)); }
            if (att > 0 && def == 0)
            {
                if (!f.warned)
                {
                    f.warned = true;
                    if (own == K.BLUE) { Hint("Враг захватывает НАШ флаг! Срочно на защиту", 3.5f); Sfx("alarm"); }
                    else { Hint("Захватываем флаг (" + f.secs + " с)! Не уходи из круга", 2.5f); Sfx("select"); }
                }
            }
            else if (att == 0) f.warned = false;
            foreach (Flag o in flags) if (o != f) { o.att = 0; o.def = 0; o.cap = 0; }
        }
        public float ProjH(Shot a, float k)
        {
            float h = a.h0 + (a.th - a.h0) * k;
            return a.kind == Proj.Grenade ? h + Mathf.Sin(Mathf.PI * k) * Mathf.Max(30f, K.Dist(a.x0, a.y0, a.tx, a.ty) * 0.35f) : h;
        }
        public void AddFloat(float x, float y, string text, Color col, float life = 1.1f) { if (floats.Count < 14) floats.Add(new FloatText { x = x, y = y, text = text, col = col, t = 0, life = life }); }

        // ---------- results ----------
        public int resStars, resGain, resKills, resLosses, resTime; public bool resWin; public string resText = "";
        void Finish(bool win)
        {
            int total = 0; foreach (Squad s in squads) if (s.team == K.BLUE && !s.T.boat) total += s.n0;
            int left = 0; foreach (Unit u in units) if (u.team == K.BLUE && !u.T.boat) left++;
            float f = left / (float)Mathf.Max(1, total);
            int stars = win ? (f >= 0.7f ? 3 : f >= 0.4f ? 2 : 1) : 0, prev = save.Stars(save.level);
            int gain = win ? 10 + save.level * 2 + stars * 3 + (genKilled ? 8 : 0) + (!roleDef ? 5 : 3) + kills / 3 : 3 + kills / 4;
            if (win && stars > prev) save.SetStars(save.level, stars);
            save.pts += gain;
            string why = endWhy == "flag" ? (win ? "Флаг врага захвачен!" : "Враг захватил наш флаг.") : endWhy == "held" ? "Флаг удержан 2,5 минуты!" : endWhy == "time" ? "Время вышло — флаг не захвачен." : endWhy == "wiped" ? "Штурм отбит — враги уничтожены!" : endWhy == "dead" ? "Вся армия погибла." : "";
            resText = why + " " + (win ? (stars < 3 ? "Сбереги больше бойцов для 3 звёзд." : "Идеальная победа!") + (save.level + 1 == K.DRAGON_LVL ? " Теперь у тебя есть АПАЧ!" : "")
                : (!roleDef ? "Попробуй высадить десант на лодках с фланга, пока основная армия давит на мост." : "Держи спецназ и гранатомётчиков у флага, танк — у моста."));
            resWin = win; resStars = stars; resGain = gain; resKills = kills; resLosses = losses; resTime = Mathf.RoundToInt(capT > 0 ? capT : battleT);
            if (win) save.level++;
            save.Persist(); Snd.Fx(win ? "win" : "lose"); screen = Screen_.Result; resShownT = Time.unscaledTime;
        }

        void Tick(float dt)
        {
            int n = speed > 1 ? 2 : 1; for (int i = 0; i < n; i++) UpdateSim(dt / n);
            foreach (Corpse c in corpses) c.t += dt; corpses.RemoveAll(c => c.t >= 28);
            foreach (FloatText f in floats) f.t += dt; floats.RemoveAll(f => f.t >= f.life);
            foreach (RingFx g in rings) g.t += dt; rings.RemoveAll(g => g.t >= 1);
            foreach (Scorch s in scorches) s.t += dt; scorches.RemoveAll(s => s.t >= 20);
            shake = Mathf.Max(0, shake - dt); StepParts(dt);
        }
    }
}
