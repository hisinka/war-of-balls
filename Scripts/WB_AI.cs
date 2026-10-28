// «Война шариков» — enemy AI: squads coordinate, back each other up, never stand around.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace WarOfBalls
{
    public class AIStyle { public string id, name; public float greed, def, patience, boats, art; }

    public partial class WarGame
    {
        static readonly AIStyle[] AI_STYLES = {
            new AIStyle{ id="bal", name="Опытный командир",   greed=1f,    def=1f,    patience=1f,   boats=1f,   art=1f },
            new AIStyle{ id="agg", name="Бешеный штурмовик",  greed=1.5f,  def=0.75f, patience=0.6f, boats=1.3f, art=1.2f },
            new AIStyle{ id="def", name="Осторожный стратег", greed=0.8f,  def=1.35f, patience=1.4f, boats=0.7f, art=1f },
            new AIStyle{ id="fox", name="Хитрый лис",         greed=1.15f, def=1f,    patience=1f,   boats=2f,   art=1.4f },
        };
        public AIStyle aiStyle = AI_STYLES[0];
        class AIState { public string phase = "gather"; public float art; public bool counter; }
        AIState AIS = new AIState();
        public const float aiT0 = 0.45f;
        static float Jit(Squad s, float a, int k = 0) { return (((s.id * 9301 + k * 7919 + 49297) % 233280) / 233280f - 0.5f) * 2f * a; }

        void PickAiStyle(int l)
        {
            int[] pool = l <= 1 ? new[] { 0 } : l <= 3 ? new[] { 0, 2 } : new[] { 0, 1, 2, 3 };
            aiStyle = AI_STYLES[pool[Mathf.Min(pool.Length - 1, Mathf.FloorToInt(UnityEngine.Random.value * pool.Length))]];
            AIS = new AIState { phase = "gather", art = 22f / aiStyle.art + Rnd(0, 6), counter = false };
        }
        float SqPow(Squad q)
        {
            float p = 0; foreach (Unit u in q.units) if (!u.dead) p += Units.UPOW[u.ti] * Mathf.Max(0.2f, u.hp / u.maxhp);
            return p * Mathf.Sqrt(q.mul.hp * q.mul.dmg);
        }
        bool Engaged(Squad q)
        {
            if (battleT - q.lastHit < 2.5f) return true;
            foreach (Unit u in q.units) if (!u.dead && u.target != null && !u.target.dead && u.target.inBoat == null && K.Dist(u.target.x, u.target.y, u.x, u.y) < u.T.range * 1.05f) return true;
            return false;
        }
        // how well squad s is suited to fight squad t
        float Fit(Squad s, Squad t)
        {
            int a = s.ti, b = t.ti, n = NAlive(t);
            if ((a == 3 || a == 8) && b == 6) return 0f;                     // shells and grenades can't hit the helicopter
            if (a == 3 || a == 8) return Units.VEH(b) ? 1.7f : n >= 6 ? 1.35f : 1f; // splash: armour and crowds
            if (a == 1) return Units.VEH(b) ? 0.35f : b == 6 ? 0.6f : b == 5 ? 0.8f : 1.25f; // snipers pick off infantry
            if (a == 0 || a == 5) return Units.VEH(b) ? 0.5f : b == 6 ? 0.8f : 1.1f;  // rifles waste time on armour
            if (a == 2) return Units.VEH(b) ? 0.45f : 1.25f;
            if (a == 6) return b == 3 || b == 8 ? 1.35f : 1.1f;                   // the Apache loves things that can't shoot back
            return 1f;
        }

        class Threat { public Squad t, boat; public float x, y, p, d; }

        void EnemyAI()
        {
            List<Squad> mine = new List<Squad>(), theirs = new List<Squad>();
            foreach (Squad s in squads)
            {
                if (!AnyAlive(s) || s.T.boat || s.order == Ord.Boat) continue;
                if (s.team == K.RED) mine.Add(s); else theirs.Add(s);
            }
            if (mine.Count == 0) return;
            int lvl = save.level; AIStyle st = aiStyle; Flag F = ActiveFlag; bool redAtt = roleDef;
            HashSet<Squad> busy = new HashSet<Squad>();
            Dictionary<Squad, Vector2> C = new Dictionary<Squad, Vector2>();
            Func<Squad, Vector2> cen = q => { Vector2 v; if (!C.TryGetValue(q, out v)) { v = Cen(q); C[q] = v; } return v; };
            Func<Squad, float, float, float> dist = (q, x, y) => { Vector2 c = cen(q); return K.Dist(c.x, c.y, x, y); };
            Func<Squad, Squad, float> dQ = (a, b) => { Vector2 c = cen(b); return dist(a, c.x, c.y); };
            Func<Squad, float, float, float, bool> heading = (q, fx, fy, r) => q.order == Ord.Path && q.path != null && q.path.Count > 0 && K.Dist(q.path[q.path.Count - 1].x, q.path[q.path.Count - 1].y, fx, fy) < r;
            Action<Squad, Squad> attack = (s, t) => { if (t != null && (s.order != Ord.Attack || s.target != t)) OrderAttack(s, t); busy.Add(s); };
            Action<Squad, float, float, float> goTo = (s, x, y, r) =>
            {
                if (dist(s, x, y) > r) { if (!heading(s, x, y, r)) OrderPath(s, new Vector2(x, y)); }
                else if (s.order == Ord.Attack || s.order == Ord.Path) OrderHold(s);
                busy.Add(s);
            };
            // best target for s among the list, judged by matchup, distance, weakness and danger to the objective
            Func<Squad, List<Squad>, float, Func<Squad, float>, Squad> best = (s, list, maxD, dangerOf) =>
            {
                Squad b = null; float bs = 0;
                foreach (Squad t in list)
                {
                    float d = dQ(s, t); if (d > maxD) continue;
                    float f = Fit(s, t); if (f <= 0) continue;
                    float hpF = 0; int n = 0; foreach (Unit u in t.units) if (!u.dead) { hpF += u.hp / u.maxhp; n++; }
                    if (n == 0) continue; hpF /= n;
                    float sc = f * (1.25f - hpF * 0.35f) * (1f + (dangerOf != null ? dangerOf(t) : 0f)) / (1f + d / 350f);
                    if (sc > bs) { bs = sc; b = t; }
                }
                return b;
            };
            Func<Squad, char> sideOf = q => { Vector2 c = cen(q); return Terra.Side(c.x, c.y); };
            float powR = 0, powB = 0;
            foreach (Squad q in mine) powR += SqPow(q);
            foreach (Squad q in theirs) powB += SqPow(q);
            foreach (Squad b in squads) if (b.team == K.BLUE && b.T.boat && b.cargo != null) powB += SqPow(b.cargo);

            // ---- artillery: the enemy calls in strikes on clumps of our troops (warned in advance)
            AIS.art -= aiT0;
            if (lvl >= 4 && AIS.art <= 0 && battleT > 12)
            {
                Unit bt = null; float bn = 0;
                foreach (Unit e in units)
                {
                    if (e.team != K.BLUE || e.dead || e.inBoat != null || e.T.fly || e.T.boat) continue;
                    float n = 0; int own = 0;
                    foreach (Unit o in units)
                    {
                        if (o.dead || o.inBoat != null) continue; float d = K.Dist(o.x, o.y, e.x, e.y);
                        if (o.team == K.BLUE && d < 80) n += Units.VEH(o.ti) ? 4 : 1; else if (o.team == K.RED && d < 120) own++;
                    }
                    if (Terra.OnBridge(e.x, e.y)) n *= 1.4f;
                    if (own == 0 && n > bn) { bn = n; bt = e; }
                }
                if (bt != null && bn >= 5)
                {
                    int k = lvl >= 8 ? 6 : lvl >= 6 ? 5 : 4;
                    for (int i = 0; i < k; i++) strikes.Add(new Strike { x = bt.x + Rnd(-60, 60), y = bt.y + Rnd(-60, 60), t = 2.4f + i * 0.24f, team = K.RED });
                    rings.Add(new RingFx { x = bt.x, y = bt.y, t = 0, col = K.C(0xff3a2a) }); Sfx("whistle");
                    Hint("Враг вызвал АРТОБСТРЕЛ — уводи отряд из красного круга!", 2.5f);
                    AIS.art = (46f - Mathf.Min(16f, lvl * 1.5f)) / st.art + Rnd(0, 8);
                }
                else AIS.art = 3;
            }

            // ---- boat raids: carry an infantry squad across the water and land it where we are weakest
            List<Squad> boats = new List<Squad>();
            foreach (Squad b in squads) if (b.team == K.RED && b.T.boat && AnyAlive(b)) boats.Add(b);
            foreach (Squad s in mine)
            {
                BoatPlan bp = s.boatPlan; if (bp == null) continue;
                busy.Add(s); Squad b = bp.boat;
                if (bp.stage == "sail") { if (s.order != Ord.Boat) { s.boatPlan = null; s.run = true; busy.Remove(s); } continue; }
                if (!AnyAlive(b) || (b.cargo != null && b.cargo != s) || (Engaged(s) && battleT - s.lastHit < 2)) { s.boatPlan = null; busy.Remove(s); continue; }
                if (bp.stage == "walk")
                {
                    Unit bu = BoatOf(b);
                    if (dist(s, bu.x, bu.y) < 120)
                    {
                        BoardBoat(s, b); bp.stage = "sail"; b.landAt = bp.goal; b.hasLandAt = true;
                        OrderPath(b, Terra.LandingSpot(bp.land.x, bp.land.y, bu.x, bu.y));
                        if (redAtt) { Hint("Вражеская лодка с десантом плывёт к нашему острову!", 3.5f); Sfx("alarm"); }
                    }
                    else if (s.order != Ord.Path)
                    {
                        Dock dk = docks.Find(d => K.Dist(d.x, d.y, bu.x, bu.y) < 60);
                        OrderPath(s, dk != null ? new Vector2(dk.lx, dk.ly) : new Vector2(bu.x, bu.y));
                    }
                }
            }
            foreach (Squad b in boats)
                if (b.cargo != null && b.order == Ord.Hold && b.hasLandAt && !NearShore(b)) { Unit bu = BoatOf(b); OrderPath(b, Terra.LandingSpot(b.landAt.x, b.landAt.y, bu.x, bu.y)); }
            if ((redAtt && lvl >= 2 && battleT > 3 && AIS.phase != "gather") || (redAtt && lvl >= 2 && battleT > 9))
            {
                List<Squad> freeBoats = boats.FindAll(b => b.cargo == null && !mine.Exists(s => s.boatPlan != null && s.boatPlan.boat == b));
                int planned = mine.FindAll(s => s.boatPlan != null).Count, inf = mine.FindAll(s => s.ti == 0 || s.ti == 3 || s.ti == 5).Count;
                int maxPlans = Mathf.Min(inf - 1, lvl >= 6 ? 2 : 1) + (st.boats > 1.5f ? 1 : 0);
                if (freeBoats.Count > 0 && planned < maxPlans && clock > 45 && UnityEngine.Random.value < 0.5f * st.boats)
                {
                    Squad cand = null; float cd = -1;
                    foreach (Squad s in mine)
                    {
                        if (!((s.ti == 0 || s.ti == 3) && !busy.Contains(s) && !s.run && !Engaged(s) && NAlive(s) <= K.BOATCAP && sideOf(s) == 'R')) continue;
                        float d = dist(s, K.BXB, K.BY); if (d > cd) { cd = d; cand = s; } // the rear-most squad goes raiding
                    }
                    if (cand != null)
                    {
                        Squad b = null; float bd = 1e9f;
                        foreach (Squad fb in freeBoats) { Unit fu = BoatOf(fb); float d = dist(cand, fu.x, fu.y); if (d < bd) { bd = d; b = fb; } }
                        // land on whichever side of the island the defenders are thinner
                        Island I = Terra.ISL[0]; float up = 0, dn = 0;
                        foreach (Squad t in theirs) { Vector2 c = cen(t); if (Terra.Side(c.x, c.y) != 'L') continue; float p = SqPow(t); if (c.y < I.cy) up += p; else dn += p; }
                        float sY = up < dn ? -1f : up > dn ? 1f : (UnityEngine.Random.value < 0.5f ? -1f : 1f);
                        cand.boatPlan = new BoatPlan { boat = b, stage = "walk", land = new Vector2(I.cx - 0.05f * I.rx, I.cy + sY * I.ry * 0.72f), goal = new Vector2(F.x, F.y) };
                        busy.Add(cand);
                    }
                }
            }
            // ---- squads on a run for the flag (landed raiders): take the flag, fight only what blocks the way
            foreach (Squad s in mine)
            {
                if (!s.run || busy.Contains(s)) continue; busy.Add(s);
                Squad t = best(s, theirs, 170, null);
                if (t != null) attack(s, t); else goTo(s, F.x + Jit(s, 15), F.y + Jit(s, 20, 1), 30);
            }
            // ---- support: an idle squad helps the nearest friend in a fight
            Func<Squad, Func<Squad, bool>, Squad> helpTarget = (s, ok) =>
            {
                Squad r = null; float rd = 1e9f;
                foreach (Squad f in mine)
                {
                    if (f == s || !Engaged(f)) continue; float d = dQ(s, f); if (d > 650 || d >= rd) continue;
                    Squad t = best(f, theirs, 420, null);
                    if (t != null && Fit(s, t) > 0 && (ok == null || ok(t))) { rd = d; r = t; }
                }
                return r;
            };

            if (redAtt)
            {
                // ===================== RED ATTACKS =====================
                Vector2 stage = new Vector2(K.BXB + 170, K.BY);
                List<Squad> main = mine.FindAll(s => !busy.Contains(s) && !s.run);
                if (AIS.phase == "gather")
                {
                    float near = 0, tot = 0;
                    foreach (Squad s in main) { float p = SqPow(s); tot += p; if (dist(s, stage.x, stage.y) < 300) near += p; }
                    if (tot <= 0) tot = 1;
                    if (battleT > 26 * st.patience || (near / tot > 0.75f && battleT > 9 * st.patience)) { AIS.phase = "push"; Announce("Враг идёт в атаку!", "", K.C(0xff6a5a)); }
                }
                bool endgame = clock < 40;
                // regroup: who is ahead of the main body along the road to our flag
                Dictionary<Squad, float> rl = new Dictionary<Squad, float>(); List<float> sorted = new List<float>();
                foreach (Squad s in main) { Vector2 c = cen(s); float v = Terra.RouteLen(c.x, c.y, F.x, F.y); rl[s] = v; sorted.Add(v); }
                sorted.Sort(); float med = sorted.Count > 0 ? sorted[sorted.Count >> 1] : 0f;
                int k = 0;
                foreach (Squad s in main)
                {
                    Vector2 c = cen(s); int i = k++;
                    if (s.ti == 6) { Squad th = best(s, theirs, 1e9f, t => dist(t, F.x, F.y) < 400 ? 0.6f : 0f); if (th != null) attack(s, th); continue; }
                    float eng = s.ti == 1 ? 380 : s.ti == 8 ? 340 : s.ti == 2 ? 280 : 250;
                    // snipers keep their distance
                    if (s.ti == 1)
                    {
                        Squad n = null; float nd = 1e9f; foreach (Squad t in theirs) { float d = dQ(s, t); if (d < nd) { nd = d; n = t; } }
                        if (n != null && nd < 140 && !Units.VEH(n.ti))
                        {
                            Vector2 nc = cen(n); float ax = c.x - nc.x, ay = c.y - nc.y, l = K.Hyp(ax, ay); if (l < 1e-3f) l = 1;
                            OrderPath(s, Terra.SnapLand(c.x + ax / l * 130, c.y + ay / l * 130)); busy.Add(s); continue;
                        }
                    }
                    if (endgame && dist(s, F.x, F.y) > K.FLAG_R * 0.7f)
                    {
                        Squad t0 = best(s, theirs, 120, null); if (t0 != null) { attack(s, t0); continue; }
                        goTo(s, F.x + Jit(s, 20), F.y + ((i % 5) - 2) * 18, 25); continue;
                    }
                    Squad t1 = best(s, theirs, eng, t => dist(t, F.x, F.y) < 300 ? 0.4f : 0f); if (t1 != null) { attack(s, t1); continue; }
                    Squad h = helpTarget(s, null); if (h != null) { attack(s, h); continue; }
                    if (AIS.phase == "gather")
                    {
                        Vector2 sp = s.ti == 1 ? new Vector2(stage.x + 80, stage.y + (s.id % 2 == 1 ? -110 : 110)) : Units.VEH(s.ti) ? new Vector2(stage.x - 40, stage.y + (s.id % 2 == 1 ? -30 : 30)) : new Vector2(stage.x + 40 + Jit(s, 20), stage.y + ((i % 4) - 1.5f) * 55);
                        Vector2 sl = Terra.SnapLand(sp); goTo(s, sl.x, sl.y, 60); continue;
                    }
                    // push: vehicles lead, the rest keep together; snipers follow and overwatch
                    float ahead = med - rl[s];
                    if (!Units.VEH(s.ti) && ahead > 220 && !Engaged(s)) { if (s.order != Ord.Hold) OrderHold(s); busy.Add(s); continue; }
                    if (s.ti == 1)
                    {
                        Vector2 p = Terra.Side(F.x, F.y) == Terra.Side(c.x, c.y) ? new Vector2(F.x + 170, F.y + (s.id % 2 == 1 ? -90 : 90)) : new Vector2(K.BXA - 90, K.BYS[s.id % 2]);
                        Vector2 sl = Terra.SnapLand(p); goTo(s, sl.x, sl.y, 60); continue;
                    }
                    goTo(s, F.x + Jit(s, 15), F.y + ((i % 5) - 2) * 24, 40);
                }
            }
            else
            {
                // ===================== RED DEFENDS =====================
                // threats: anyone on our island or on the bridge, and loaded boats on their way to us
                List<Threat> threats = new List<Threat>();
                foreach (Squad t in theirs)
                {
                    Vector2 c = cen(t); char sd = Terra.Side(c.x, c.y);
                    if (sd == 'L' && dist(t, F.x, F.y) > 900) continue;
                    threats.Add(new Threat { t = t, x = c.x, y = c.y, p = SqPow(t) * 1.3f, d = K.Dist(c.x, c.y, F.x, F.y) });
                }
                foreach (Squad b in squads)
                {
                    if (b.team != K.BLUE || !b.T.boat || b.cargo == null || !AnyAlive(b)) continue;
                    Unit bu = BoatOf(b); Vector2 end = b.path != null && b.path.Count > 0 ? b.path[b.path.Count - 1] : new Vector2(bu.x, bu.y);
                    if (end.x < K.W / 2 && bu.x < K.W / 2 - 150) continue;
                    Vector2 land = Terra.SnapLand(end.x, end.y);
                    threats.Add(new Threat { boat = b, t = b.cargo, x = land.x, y = land.y, p = SqPow(b.cargo) * 1.2f, d = K.Dist(land.x, land.y, F.x, F.y) });
                }
                threats.Sort((a, b) => a.d.CompareTo(b.d));
                // a strong defender goes on the counter-attack
                if (!AIS.counter && battleT > 25 && powR > 1.8f * powB && powB > 0) { AIS.counter = true; Announce("Враг контратакует!", "", K.C(0xff6a5a)); }
                if (AIS.counter && powR < 1.2f * powB) AIS.counter = false;
                List<Squad> free = mine.FindAll(s => !busy.Contains(s));
                // reserve: the closest infantry squad to the flag stays home unless the flag itself is attacked
                Squad reserve = null; float rdist = 1e9f;
                foreach (Squad s in free) if (s.ti == 0 || s.ti == 5 || s.ti == 4) { float d = dist(s, F.x, F.y); if (d < rdist) { rdist = d; reserve = s; } }
                // assign defenders to each threat until we clearly outgun it
                foreach (Threat th in threats)
                {
                    float got = 0;
                    List<Squad> cand = free.FindAll(s => s != reserve || th.d < 330);
                    cand.Sort((a, b) => (dist(a, th.x, th.y) / Mathf.Max(1e-3f, Fit(a, th.t))).CompareTo(dist(b, th.x, th.y) / Mathf.Max(1e-3f, Fit(b, th.t))));
                    foreach (Squad s in cand)
                    {
                        if (got >= th.p) break;
                        if (Fit(s, th.t) <= 0) continue;
                        if (s.ti == 1 && th.boat == null && dist(s, th.x, th.y) > 500) continue;
                        free.Remove(s); got += SqPow(s) * Fit(s, th.t);
                        if (th.boat != null)
                        {
                            if (dist(s, th.x, th.y) > 90) goTo(s, th.x, th.y, 60);
                            else { Squad t = best(s, theirs, 260, null); if (t != null) attack(s, t); else busy.Add(s); }
                        }
                        else attack(s, th.t);
                    }
                }
                Vector2[] posts = { new Vector2(K.BXB + 150, K.BYS[0]), new Vector2(K.BXB + 150, K.BYS[1]), new Vector2(K.BXB + 300, K.BY), new Vector2(F.x - 90, F.y - 60), new Vector2(F.x - 90, F.y + 60), new Vector2(F.x - 160, F.y) };
                int pi = 0;
                foreach (Squad s in free)
                {
                    if (s.ti == 6) { Squad th = best(s, theirs, 1e9f, t => sideOf(t) != 'L' ? 0.8f : 0f); if (th != null) attack(s, th); continue; }
                    float aw = (s.ti == 1 ? 440 : s.ti == 8 ? 380 : 300) * st.def;
                    Squad t1 = best(s, theirs, aw, t => sideOf(t) != 'L' ? 0.5f : 0f);
                    if (t1 != null && (sideOf(t1) != 'L' || AIS.counter || dQ(s, t1) < s.T.range + 40)) { attack(s, t1); continue; }
                    if (AIS.counter && s != reserve) { Squad t2 = best(s, theirs, 1e9f, null); if (t2 != null) { attack(s, t2); continue; } }
                    Squad h = s != reserve ? helpTarget(s, t => AIS.counter || sideOf(t) != 'L') : null; if (h != null) { attack(s, h); continue; }
                    // nothing to do: hold a post — snipers and armour cover the bridge, the reserve sits on the flag
                    Vector2 post;
                    if (s == reserve) post = new Vector2(F.x - 50, F.y);
                    else if (s.ti == 1) post = new Vector2(K.BXB + 200, K.BYS[s.id % 2] + (s.id % 4 < 2 ? -70 : 70));
                    else if (Units.VEH(s.ti)) post = new Vector2(K.BXB + 130, K.BYS[s.id % 2]);
                    else { if (!s.hasPost) { Vector2 pp = posts[(pi++) % posts.Length]; s.post = new Vector2(pp.x + Rnd(-20, 20), pp.y + Rnd(-20, 20)); s.hasPost = true; } post = s.post; }
                    // low armour falls back to the flag
                    if (Units.VEH(s.ti)) { Unit u = BoatOf(s); if (u != null && u.hp < u.maxhp * 0.25f) post = new Vector2(F.x - 120, F.y + (s.id % 2 == 1 ? -70 : 70)); }
                    Vector2 sl = Terra.SnapLand(post); goTo(s, sl.x, sl.y, 55);
                }
            }
            // ---- nobody stands around: an idle squad with enemies in sight goes for them
            foreach (Squad s in mine)
            {
                if (s.order != Ord.Hold || s.run || Engaged(s)) { s.aiIdle = 0; continue; }
                float sp = 0; int n = 0; foreach (Unit u in s.units) if (!u.dead) { sp += u.spd; n++; }
                if (n == 0 || sp / n > 4) { s.aiIdle = 0; continue; }
                s.aiIdle += aiT0; if (s.aiIdle < (redAtt ? 3f : 6f)) continue;
                List<Squad> pool = (redAtt || AIS.counter) ? theirs : theirs.FindAll(t => sideOf(t) != 'L');
                Squad tt = best(s, pool, redAtt ? 700f : 450f, null);
                if (tt != null) { OrderAttack(s, tt); s.aiIdle = 0; }
            }
        }
    }
}
