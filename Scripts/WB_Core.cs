// «Война шариков» — Unity port of the HTML game.
// Core: constants, unit types, save data, army composition, terrain math, navigation.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace WarOfBalls
{
    public static class K
    {
        public const float W = 3500f, H = 1700f, MG = 1500f;
        public const float WX0 = -MG + 80f, WX1 = W + MG - 80f, WY0 = -MG + 80f, WY1 = H + MG - 80f;
        public const float FLAG_R = 66f, FLX = 330f, ZX0 = 300f, ZX1 = 980f, ZY0 = H / 2f - 420f, ZY1 = H / 2f + 420f, TIMER = 150f;
        public const int DRAGON_LVL = 5, BOATCAP = 16;
        public const float BY = H / 2f, BW = 62f, BXA = 1140f, BXB = W - 1140f, LANDH = 4f, DECK = 8f, SEA = 1.2f;
        public const int BLUE = 0, RED = 1;
        public const float PI = Mathf.PI;

        public static float Rnd(float a, float b) { return a + UnityEngine.Random.value * (b - a); }
        public static float Clamp(float v, float a, float b) { return v < a ? a : (v > b ? b : v); }
        public static float Hyp(float x, float y) { return Mathf.Sqrt(x * x + y * y); }
        public static float Dist(float x0, float y0, float x1, float y1) { float dx = x1 - x0, dy = y1 - y0; return Mathf.Sqrt(dx * dx + dy * dy); }
        public static float WrapAngle(float a) { return Mathf.Atan2(Mathf.Sin(a), Mathf.Cos(a)); }
        public static Color C(int hex) { return new Color(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f, 1f); }
        public static Color C(int hex, float a) { Color c = C(hex); c.a = a; return c; }
        public static Color HSL(float h, float s, float l)
        {
            h = h - Mathf.Floor(h);
            float q = l < 0.5f ? l * (1f + s) : l + s - l * s, p = 2f * l - q;
            return new Color(Hue(p, q, h + 1f / 3f), Hue(p, q, h), Hue(p, q, h - 1f / 3f), 1f);
        }
        static float Hue(float p, float q, float t)
        {
            if (t < 0f) t += 1f; if (t > 1f) t -= 1f;
            if (t < 1f / 6f) return p + (q - p) * 6f * t;
            if (t < 0.5f) return q;
            if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6f;
            return p;
        }
        // game plane (x, y) + height -> Unity world. y grows "south", which is -Z in Unity (keeps the blue island on the left)
        public static Vector3 U(float x, float y, float h) { return new Vector3(x - W / 2f, h, H / 2f - y); }
        // a heading in the game plane -> rotation about the vertical axis (local +X points along the heading)
        public static Quaternion Face(float f) { return Quaternion.Euler(0f, f * Mathf.Rad2Deg, 0f); }
    }

    public enum Proj { None, Bullet, Mg, Pistol, Snipe, Shell, Grenade, Rocket, Art }
    public enum Ord { Hold, Path, Attack, Boat, Rout }
    public enum Phase { Menu, Deploy, Battle, End, Done }

    public class UType
    {
        public string key, name;
        public float hp, dmg, cd, speed, r, aggro, range, splash, space, armor = 1f;
        public Proj proj = Proj.None;
        public bool veh, fly, boat;
    }

    public class Mul
    {
        public float hp = 1, dmg = 1, range = 1, mor = 1;
        public Mul() { }
        public Mul(float hp, float dmg, float range, float mor) { this.hp = hp; this.dmg = dmg; this.range = range; this.mor = mor; }
    }

    public class Unit
    {
        public int team, ti, slot;
        public Squad sq;
        public UType T;
        public float x, y, face, hp, maxhp, cool, rt, vx, vy, spd, draw, id, flash, burn, walk, aim, rk, deadT, px, py;
        public bool dead, hasPrev;
        public Unit target;
        public Squad inBoat;
        public Rig rig;
    }

    public class BoatPlan { public Squad boat; public string stage; public Vector2 land, goal; }

    public class Squad
    {
        public int team, ti, id, n0, nAlive, pi;
        public UType T;
        public Mul mul;
        public float ax, ay, dir, moraleMax, morale, lastHit = -9f, bx, by, buffT, stuck, aiIdle, shakenT = -99f;
        public Ord order = Ord.Hold;
        public Squad target, after, cargo;
        public List<Vector2> path;
        public List<Unit> units = new List<Unit>();
        public List<Vector2> offs = new List<Vector2>();
        public bool wall, run, hasLandAt, hasPost;
        public Vector2 landAt, post;
        public BoatPlan boatPlan;
    }

    public class Island { public float cx, cy, rx, ry, s; }
    public class Hill { public float x, y, r, h; }
    public class Forest { public float x, y, r; }
    public class Rock { public float x, y, s, r; }
    public class Palm { public float x, y, s, r; }
    public class Dock { public float x, y, lx, ly; public int team; }

    public class Flag
    {
        public int team; public float x, y, cap; public int att, def, secs; public bool active, warned, ann;
    }

    public class Shot
    {
        public Proj kind; public float x0, y0, h0, tx, ty, th, t, T, dmg, splash, delay; public Unit src, tgt; public bool done;
    }
    public class Strike { public float x, y, t; public int team; public bool done; }
    public class FloatText { public float x, y, t, life; public string text; public Color col; }
    public class RingFx { public float x, y, t; public Color col; }
    public class Corpse { public float x, y, t, r, rot; public int team, ti; }
    public class Scorch { public float x, y, r, t; }

    public class Army { public int[] counts = new int[9]; public Mul[] mul = new Mul[9]; public float power; }

    // ================= unit types: 0 rifles, 1 snipers, 2 APC, 3 grenadiers, 4 commander, 5 spec-ops, 6 Apache, 7 boat, 8 tank =================
    public static class Units
    {
        public static readonly UType[] TYPES = {
            new UType{ key="rifle",  name="Автоматчики",    hp=120,  dmg=7,   cd=0.42f, speed=52, r=9,    aggro=200, range=165, proj=Proj.Bullet, space=20 },
            new UType{ key="sniper", name="Снайперы",       hp=85,   dmg=48,  cd=2.6f,  speed=48, r=8.5f, aggro=370, range=340, proj=Proj.Snipe,  space=22 },
            new UType{ key="apc",    name="БТР",            hp=950,  dmg=3.6f,cd=0.18f, speed=88, r=17,   aggro=230, range=178, proj=Proj.Mg,     space=40, veh=true },
            new UType{ key="gren",   name="Гранатомётчики", hp=115,  dmg=46,  cd=3.4f,  speed=50, r=9,    aggro=210, range=180, proj=Proj.Grenade,splash=40, space=22 },
            new UType{ key="gen",    name="Командир",       hp=700,  dmg=12,  cd=0.5f,  speed=58, r=13,   aggro=200, range=150, proj=Proj.Pistol, space=30 },
            new UType{ key="spec",   name="Спецназ",        hp=230,  dmg=10,  cd=0.6f,  speed=46, r=9.5f, aggro=160, range=125, proj=Proj.Pistol, space=21, armor=0.65f },
            new UType{ key="heli",   name="Апач",           hp=1400, dmg=7,   cd=0.12f, speed=95, r=20,   aggro=320, range=240, proj=Proj.Mg,     space=40, fly=true },
            new UType{ key="boat",   name="Лодка",          hp=420,  dmg=0,   cd=9,     speed=88, r=15,   aggro=0,   range=0,   proj=Proj.None,   space=40, boat=true },
            new UType{ key="tank",   name="Танк",           hp=2100, dmg=62,  cd=3.0f,  speed=54, r=27,   aggro=300, range=290, proj=Proj.Shell,  splash=38, space=60, veh=true },
        };
        public static bool SOLO(int ti) { return ti == 2 || ti == 4 || ti == 6 || ti == 7 || ti == 8; }
        public static bool VEH(int ti) { return ti == 2 || ti == 8; }
        public static bool RIG(int ti) { return ti == 2 || ti == 6 || ti == 7 || ti == 8; }
        public static bool SMALL(Proj k) { return k == Proj.Bullet || k == Proj.Mg || k == Proj.Pistol || k == Proj.Snipe; }
        public static readonly float[] UPOW = { 1f, 1.3f, 8f, 1.3f, 6f, 1.8f, 14f, 0f, 14f }; // rough combat value of one unit
        public static readonly string[] BATTLES = { "Два острова", "Ржавый мост", "Пальмовый берег", "Маяк", "Скалистая бухта", "Песчаная коса", "Остров Чаек", "Туманный пролив", "Коралловый риф", "Бухта Акул", "Северный мыс", "Лагуна", "Штормовой пролив", "Порт Надежды", "Последний рубеж" };
        public static string BattleName(int l)
        {
            int n = BATTLES.Length;
            return BATTLES[(l - 1) % n] + (l > n ? " " + ((l - 1) / n + 1) : "");
        }
        public static bool HasGeneral(int l) { return l >= 5 && (l - 5) % 3 == 0; }
        public static bool RedDragon(int l) { return l >= 8 && l % 4 == 0; }
        public static readonly Color[] BODY = { K.C(0x3d8bff), K.C(0xff4a4a) };
        public static readonly Color[] DARK = { K.C(0x1f4fa8), K.C(0xa82020) };
        public static readonly Color[] FLAGC = { K.C(0x2f6fe0), K.C(0xd23030) };
        public static readonly Color[] LIGHT = { K.C(0xa6ceff), K.C(0xffb0a6) };
    }

    // ================= progress =================
    [Serializable]
    public class SaveData
    {
        public int level = 1, pts = 0;
        public int[] stars = new int[400];
        public int sword_n, arch_n, cav_n, spear_n, sword_p, arch_p, cav_p, spear_p, morale, spear, fire, cry, shield, shield_n, shield_p, dragon;
        public bool soundOff;

        public int Up(string id)
        {
            switch (id)
            {
                case "sword_n": return sword_n; case "arch_n": return arch_n; case "cav_n": return cav_n; case "spear_n": return spear_n;
                case "sword_p": return sword_p; case "arch_p": return arch_p; case "cav_p": return cav_p; case "spear_p": return spear_p;
                case "morale": return morale; case "spear": return spear; case "fire": return fire; case "cry": return cry;
                case "shield": return shield; case "shield_n": return shield_n; case "shield_p": return shield_p; case "dragon": return dragon;
            }
            return 0;
        }
        public void AddUp(string id)
        {
            switch (id)
            {
                case "sword_n": sword_n++; break; case "arch_n": arch_n++; break; case "cav_n": cav_n++; break; case "spear_n": spear_n++; break;
                case "sword_p": sword_p++; break; case "arch_p": arch_p++; break; case "cav_p": cav_p++; break; case "spear_p": spear_p++; break;
                case "morale": morale++; break; case "spear": spear++; break; case "fire": fire++; break; case "cry": cry++; break;
                case "shield": shield++; break; case "shield_n": shield_n++; break; case "shield_p": shield_p++; break; case "dragon": dragon++; break;
            }
        }
        public int Stars(int lvl) { return lvl >= 0 && lvl < stars.Length ? stars[lvl] : 0; }
        public void SetStars(int lvl, int s) { if (lvl >= 0 && lvl < stars.Length) stars[lvl] = s; }

        const string KEY = "war_of_balls_save_v1";
        public static SaveData Load()
        {
            try
            {
                string j = PlayerPrefs.GetString(KEY, "");
                if (!string.IsNullOrEmpty(j))
                {
                    SaveData s = JsonUtility.FromJson<SaveData>(j);
                    if (s != null) { if (s.stars == null || s.stars.Length < 400) { int[] st = new int[400]; if (s.stars != null) Array.Copy(s.stars, st, Mathf.Min(400, s.stars.Length)); s.stars = st; } if (s.level < 1) s.level = 1; return s; }
                }
            }
            catch (Exception) { }
            return new SaveData();
        }
        public void Persist()
        {
            try { PlayerPrefs.SetString(KEY, JsonUtility.ToJson(this)); PlayerPrefs.Save(); } catch (Exception) { }
        }
    }

    public static class Armies
    {
        public static bool DragonOwned(SaveData s) { return s.level >= K.DRAGON_LVL; }

        // the player's army; spec-ops only when the player defends
        public static Army Blue(SaveData s, bool roleDef)
        {
            Army a = new Army();
            float mor = 1f + 0.15f * s.morale;
            int[] c = { 10 + 2 * s.sword_n, 6 + 2 * s.arch_n, 1, s.spear > 0 ? 4 + s.spear_n : 0, 0, (roleDef && s.shield > 0) ? 8 + 2 * s.shield_n : 0, DragonOwned(s) ? 1 : 0, 0, 1 };
            a.counts = c;
            a.mul = new Mul[] {
                new Mul(1 + 0.08f * s.sword_p, 1 + 0.12f * s.sword_p, 1, mor),
                new Mul(1, 1 + 0.1f * s.arch_p, 1 + 0.06f * s.arch_p, mor),
                new Mul(1 + 0.12f * s.cav_p, 1 + 0.05f * s.cav_p, 1, mor),
                new Mul(1 + 0.1f * s.spear_p, 1 + 0.12f * s.spear_p, 1, mor),
                new Mul(1, 1, 1, mor),
                new Mul(1 + 0.1f * s.shield_p, 1 + 0.08f * s.shield_p, 1, mor),
                new Mul(1 + 0.15f * s.dragon, 1 + 0.12f * s.dragon, 1, mor),
                new Mul(),
                new Mul(1 + 0.12f * s.cav_p, 1 + 0.06f * s.cav_p, 1, mor),
            };
            return a;
        }

        // the enemy army grows with the level AND with how strong the player's own army has become (plus a random roll)
        public static Army Red(SaveData s, bool roleDef, int level)
        {
            bool redDef = !roleDef;
            float bas = redDef ? 12f + 2f * (level - 1) : 15f + 2.3f * (level - 1);
            Army B = Blue(s, roleDef);
            float bp = 0f; for (int i = 0; i < 9; i++) bp += B.counts[i] * Units.UPOW[i] * Mathf.Sqrt(B.mul[i].hp * B.mul[i].dmg);
            int bVeh = B.counts[2] + B.counts[8] + B.counts[6];
            float lvlP = bas * 1.1f + (level >= 2 ? 8f : 0f) + (level >= (redDef ? 6 : 4) ? 14f : 0f), roll = 0.85f + UnityEngine.Random.value * 0.33f;
            float sc = bp * (redDef ? 0.62f : 0.8f) * Mathf.Min(1f, 0.55f + level * 0.045f);
            float P = (sc > lvlP ? lvlP + (sc - lvlP) * 0.8f : lvlP) * roll;
            int[] c = new int[9]; float rem = P;
            c[8] = Mathf.Min(2, (level >= (redDef ? 6 : 4) ? 1 : 0) + (P > 150 ? 1 : 0)); rem -= c[8] * Units.UPOW[8];
            c[2] = level >= 2 ? Mathf.Min(3, 1 + Mathf.FloorToInt(Mathf.Max(0f, P - 90f) / 70f)) : 0; rem -= c[2] * Units.UPOW[2];
            c[6] = (Units.RedDragon(level) || (P > 180 && level >= 6)) ? 1 : 0; rem -= c[6] * Units.UPOW[6];
            c[4] = Units.HasGeneral(level) ? 1 : 0; rem = Mathf.Max(rem, P * 0.45f);
            float[] w = { 1f, 0.35f, 0f, (level >= 3 || bVeh >= 3) ? 0.22f + 0.04f * bVeh : 0f, 0f, (redDef && level >= 2) ? 0.45f : 0f };
            float ws = 0f; foreach (float x in w) ws += x;
            for (int i = 0; i < w.Length; i++) if (w[i] > 0f) c[i] = Mathf.Max(i == 1 || i == 3 ? 3 : 6, Mathf.RoundToInt(rem * w[i] / ws / Units.UPOW[i]));
            float avgU = (s.sword_p + s.arch_p + s.cav_p + s.spear_p + s.shield_p) / 5f;
            float m = (level <= 3 ? 0.92f + 0.04f * (level - 1) : 1f + 0.05f * (level - 1)) * (1f + 0.03f * avgU);
            Army a = new Army(); a.counts = c; a.power = P;
            for (int i = 0; i < 9; i++)
            {
                float hp = i == 4 ? 1f + 0.12f * (level - 1) : i == 6 ? Mathf.Max(0.8f, 0.8f + 0.06f * (level - 8)) : m;
                float dmg = i == 6 ? Mathf.Max(0.8f, 0.8f + 0.05f * (level - 8)) : m;
                a.mul[i] = new Mul(hp, dmg, 1f, 1f + 0.03f * (level - 1));
            }
            return a;
        }
    }

    // ================= terrain: two islands, a bridge, the ocean =================
    public static class Terra
    {
        public static readonly Island[] ISL = {
            new Island{ cx=640f, cy=K.H/2f, rx=560f, ry=680f, s=0f },
            new Island{ cx=K.W-640f, cy=K.H/2f, rx=560f, ry=680f, s=0f },
        };
        public static List<Forest> forests = new List<Forest>();
        public static List<Hill> hills = new List<Hill>();
        public static List<Rock> rocks = new List<Rock>();
        public static List<Palm> palms = new List<Palm>();
        public static int tod = 0;

        public static float IslandQ(Island I, float x, float y)
        {
            float dx = (x - I.cx) / I.rx, dy = (y - I.cy) / I.ry, d = Mathf.Sqrt(dx * dx + dy * dy), th = Mathf.Atan2(dy, dx);
            float n = 1f + 0.075f * Mathf.Sin(3f * th + I.s) + 0.05f * Mathf.Sin(5f * th + 2.1f * I.s) + 0.025f * Mathf.Sin(11f * th + 0.7f * I.s);
            if (Mathf.Abs(y - K.BY) < 150f && (I.cx < K.W / 2f ? x > I.cx : x < I.cx)) n = Mathf.Max(n, 1.04f); // solid shore where the bridge lands
            if (Mathf.Abs(y - K.BY) < 300f && (I.cx < K.W / 2f ? x < I.cx : x > I.cx)) n = Mathf.Max(n, 1.0f);
            return d / n;
        }
        // the outline radius factor along an angle (for drawing island shapes)
        public static float OutlineN(Island I, float th, float q)
        {
            float n = 1f + 0.075f * Mathf.Sin(3f * th + I.s) + 0.05f * Mathf.Sin(5f * th + 2.1f * I.s) + 0.025f * Mathf.Sin(11f * th + 0.7f * I.s);
            float x = I.cx + Mathf.Cos(th) * I.rx * q * n, y = I.cy + Mathf.Sin(th) * I.ry * q * n;
            if (Mathf.Abs(y - K.BY) < 150f && (I.cx < K.W / 2f ? x > I.cx : x < I.cx)) n = Mathf.Max(n, 1.04f);
            if (Mathf.Abs(y - K.BY) < 300f && (I.cx < K.W / 2f ? x < I.cx : x > I.cx)) n = Mathf.Max(n, 1.0f);
            return n;
        }
        public static float LandQ(float x, float y) { return Mathf.Min(IslandQ(ISL[0], x, y), IslandQ(ISL[1], x, y)); }
        public static bool OnBridge(float x, float y) { return x >= K.BXA && x <= K.BXB && Mathf.Abs(y - K.BY) <= K.BW; }
        public static bool IsLand(float x, float y) { return OnBridge(x, y) || LandQ(x, y) < 1f; }
        public static bool IsWater(float x, float y) { return LandQ(x, y) > 1.03f && x > K.WX0 && x < K.WX1 && y > K.WY0 && y < K.WY1; }
        public static float DeckH(float x) { return K.Clamp(2f + Mathf.Min(x - K.BXA, K.BXB - x) * 0.12f, 2f, K.DECK); }
        public static float GroundH(float x, float y)
        {
            float q = LandQ(x, y);
            if (q < 1f)
            {
                float h = K.SEA + 0.8f + (K.LANDH - K.SEA - 0.8f) * K.Clamp((1f - q) / 0.1f, 0f, 1f);
                for (int i = 0; i < hills.Count; i++) { Hill k = hills[i]; float d = K.Dist(x, y, k.x, k.y); if (d < k.r) { float t = d / k.r; h += k.h * (Mathf.Cos(t * Mathf.PI) * 0.5f + 0.5f); } }
                return h;
            }
            return Mathf.Max(-16f, K.SEA + 0.8f - (q - 1f) * 70f);
        }
        public static float Height(float x, float y) { float g = GroundH(x, y); return OnBridge(x, y) ? Mathf.Max(g, DeckH(x)) : g; }
        public static bool InForest(float x, float y) { for (int i = 0; i < forests.Count; i++) { Forest f = forests[i]; if (K.Dist(x, y, f.x, f.y) < f.r) return true; } return false; }
        public static bool OnHill(float x, float y) { return Height(x, y) > K.LANDH + 7f; }

        public static bool IslandSpot(char side, float qmax, float pad, int tries, out Vector2 p)
        {
            Island I = ISL[side == 'L' ? 0 : 1];
            for (int k = 0; k < tries; k++)
            {
                float a = K.Rnd(0, 6.28f), r = Mathf.Sqrt(UnityEngine.Random.value) * qmax, x = I.cx + Mathf.Cos(a) * I.rx * r, y = I.cy + Mathf.Sin(a) * I.ry * r;
                if (IslandQ(I, x, y) > qmax) continue;
                if (K.Dist(x, y, K.FLX, K.H / 2f) < pad || K.Dist(x, y, K.W - K.FLX, K.H / 2f) < pad) continue;
                if (Mathf.Abs(y - K.BY) < 110f && Mathf.Abs(x - K.W / 2f) < K.W / 2f - K.FLX - 40f) continue;
                if ((x > K.ZX0 - 30 && x < K.ZX1 + 30 && y > K.ZY0 - 30 && y < K.ZY1 + 30) || (x > K.W - K.ZX1 - 30 && x < K.W - K.ZX0 + 30 && y > K.ZY0 - 30 && y < K.ZY1 + 30)) continue;
                p = new Vector2(x, y); return true;
            }
            p = Vector2.zero; return false;
        }

        public static void GenTerrain()
        {
            foreach (Island I in ISL) { I.s = K.Rnd(0, 6.28f); I.rx = K.Rnd(540, 590); I.ry = K.Rnd(640, 720); }
            forests.Clear(); hills.Clear(); rocks.Clear(); palms.Clear(); WNODES = null;
            foreach (char side in new[] { 'L', 'R' })
            {
                int nh = 3 + (UnityEngine.Random.value < 0.5f ? 0 : 1);
                for (int i = 0; i < nh; i++)
                {
                    Vector2 p; if (!IslandSpot(side, 0.72f, 280, 60, out p)) continue;
                    Hill h = new Hill { x = p.x, y = p.y, r = K.Rnd(80, 130), h = K.Rnd(14, 26) };
                    bool ok = true; foreach (Hill o in hills) if (K.Dist(o.x, o.y, h.x, h.y) <= o.r + h.r * 0.7f) ok = false;
                    if (ok) hills.Add(h);
                }
                int nf = 6 + Mathf.FloorToInt(UnityEngine.Random.value * 3f);
                for (int i = 0; i < nf; i++)
                {
                    Vector2 p; if (!IslandSpot(side, 0.8f, 300, 60, out p)) continue;
                    Forest f = new Forest { x = p.x, y = p.y, r = K.Rnd(45, 85) };
                    bool ok = true;
                    foreach (Hill h in hills) if (K.Dist(h.x, h.y, f.x, f.y) <= h.r * 0.8f + f.r) ok = false;
                    foreach (Forest o in forests) if (K.Dist(o.x, o.y, f.x, f.y) <= o.r + f.r + 50) ok = false;
                    if (ok) forests.Add(f);
                }
                Island I = ISL[side == 'L' ? 0 : 1];
                for (int i = 0; i < 46; i++)
                {
                    float a = K.Rnd(0, 6.28f), r = 0.5f;
                    for (int k = 0; k < 40; k++) { r += 0.015f; if (IslandQ(I, I.cx + Mathf.Cos(a) * I.rx * r, I.cy + Mathf.Sin(a) * I.ry * r) > 0.96f) break; }
                    float x = I.cx + Mathf.Cos(a) * I.rx * (r - 0.02f), y = I.cy + Mathf.Sin(a) * I.ry * (r - 0.02f);
                    if (Mathf.Abs(y - K.BY) < 90f && (side == 'L' ? x > I.cx : x < I.cx)) continue;
                    palms.Add(new Palm { x = x, y = y, s = K.Rnd(0.8f, 1.3f), r = K.Rnd(0, 6.28f) });
                }
                for (int i = 0; i < 40; i++)
                {
                    Vector2 p; if (IslandSpot(side, 0.97f, 220, 10, out p) && !InForest(p.x, p.y)) rocks.Add(new Rock { x = p.x, y = p.y, s = K.Rnd(3, 8), r = K.Rnd(0, 6) });
                }
            }
            tod = Mathf.FloorToInt(UnityEngine.Random.value * 4f) % 4;
        }

        // shore spots for boats: walk from the island centre outwards until the water starts
        public static Vector2 ShoreSpot(Island I, float ang, float outQ)
        {
            float r = 0.4f;
            for (int k = 0; k < 120; k++)
            {
                r += 0.01f; float x = I.cx + Mathf.Cos(ang) * I.rx * r, y = I.cy + Mathf.Sin(ang) * I.ry * r;
                if (IslandQ(I, x, y) >= outQ) return new Vector2(x, y);
            }
            return new Vector2(I.cx + Mathf.Cos(ang) * I.rx * 1.2f, I.cy + Mathf.Sin(ang) * I.ry * 1.2f);
        }

        // ---------- navigation ----------
        public static char Side(float x, float y) { return OnBridge(x, y) ? 'B' : (x < K.W / 2f ? 'L' : 'R'); }
        static Vector2 BEND(char s) { return s == 'L' ? new Vector2(K.BXA - 30f, K.BY) : new Vector2(K.BXB + 30f, K.BY); }
        public static List<Vector2> Route(float x0, float y0, float x1, float y1)
        {
            char a = Side(x0, y0), b = Side(x1, y1);
            List<Vector2> r = new List<Vector2>();
            if (a == b) { r.Add(new Vector2(x1, y1)); return r; }
            if (a == 'B') { r.Add(BEND(b)); r.Add(new Vector2(x1, y1)); return r; }
            if (b == 'B') { r.Add(BEND(a)); r.Add(new Vector2(x1, y1)); }
            else { r.Add(BEND(a)); r.Add(BEND(b)); r.Add(new Vector2(x1, y1)); }
            Vector2 e = BEND(a);
            if (K.Dist(x0, y0, e.x, e.y) < 34f) r.RemoveAt(0); // already at the bridge end: step straight onto the deck
            return r;
        }
        public static Vector2 RouteFirst(float x0, float y0, float x1, float y1) { return Route(x0, y0, x1, y1)[0]; }
        public static float RouteLen(float x, float y, float tx, float ty)
        {
            float d = 0, cx = x, cy = y;
            foreach (Vector2 p in Route(x, y, tx, ty)) { d += K.Dist(cx, cy, p.x, p.y); cx = p.x; cy = p.y; }
            return d;
        }
        public static Vector2 SnapLand(float x, float y)
        {
            if (IsLand(x, y)) return new Vector2(x, y);
            bool has = false; float bx = 0, byy = 0, bd = 0;
            if (x > K.BXA - 40 && x < K.BXB + 40 && Mathf.Abs(y - K.BY) < K.BW + 160)
            {
                bx = K.Clamp(x, K.BXA, K.BXB); byy = K.Clamp(y, K.BY - K.BW + 8, K.BY + K.BW - 8); bd = K.Dist(bx, byy, x, y); has = true;
            }
            foreach (Island I in ISL)
            {
                float px = x, py = y;
                for (int k = 0; k < 60 && IslandQ(I, px, py) >= 0.97f; k++) { px += (I.cx - px) * 0.05f; py += (I.cy - py) * 0.05f; }
                float d = K.Dist(px, py, x, y);
                if (!has || d < bd) { bx = px; byy = py; bd = d; has = true; }
            }
            return new Vector2(bx, byy);
        }
        public static Vector2 SnapLand(Vector2 p) { return SnapLand(p.x, p.y); }

        // water routing for boats: visibility graph over points around both islands
        public static bool WaterClear(float x0, float y0, float x1, float y1)
        {
            int n = Mathf.CeilToInt(K.Dist(x0, y0, x1, y1) / 18f);
            for (int i = 1; i < n; i++) { float k = (float)i / n; if (LandQ(x0 + (x1 - x0) * k, y0 + (y1 - y0) * k) < 1.02f) return false; }
            return true;
        }
        public static List<Vector2> WNODES = null;
        public static List<Vector2> WaterNodes()
        {
            if (WNODES != null) return WNODES;
            WNODES = new List<Vector2>();
            foreach (Island I in ISL) for (int i = 0; i < 12; i++) { float a = i / 12f * 6.28f; WNODES.Add(ShoreSpot(I, a, 1.22f)); }
            return WNODES;
        }
        public static List<Vector2> WaterPath(float x0, float y0, float x1, float y1)
        {
            List<Vector2> res = new List<Vector2>();
            if (WaterClear(x0, y0, x1, y1)) { res.Add(new Vector2(x1, y1)); return res; }
            List<Vector2> N = new List<Vector2>(); N.Add(new Vector2(x0, y0)); N.AddRange(WaterNodes()); N.Add(new Vector2(x1, y1));
            int n = N.Count; float[] dist = new float[n]; int[] prev = new int[n]; bool[] done = new bool[n];
            for (int i = 0; i < n; i++) { dist[i] = 1e18f; prev[i] = -1; }
            dist[0] = 0;
            for (int it = 0; it < n; it++)
            {
                int u = -1; for (int i = 0; i < n; i++) if (!done[i] && (u < 0 || dist[i] < dist[u])) u = i;
                if (u < 0 || dist[u] >= 1e18f) break; done[u] = true; if (u == n - 1) break;
                for (int v = 0; v < n; v++)
                {
                    if (done[v]) continue;
                    float d = Vector2.Distance(N[u], N[v]);
                    if (dist[u] + d < dist[v] && WaterClear(N[u].x, N[u].y, N[v].x, N[v].y)) { dist[v] = dist[u] + d; prev[v] = u; }
                }
            }
            if (prev[n - 1] < 0) { res.Add(new Vector2(x1, y1)); return res; }
            for (int v = n - 1; v > 0; v = prev[v]) res.Insert(0, N[v]);
            return res;
        }
        public static Vector2 SeaSnap(float x, float y)
        {
            if (IsWater(x, y)) return new Vector2(x, y);
            for (float r = 20; r < 400; r += 20)
                for (int i = 0; i < 16; i++) { float a = i / 16f * 6.28f, px = x + Mathf.Cos(a) * r, py = y + Mathf.Sin(a) * r; if (IsWater(px, py)) return new Vector2(px, py); }
            return new Vector2(x, y);
        }
        // the water spot just off the shore closest to a land point (where a boat lands to unload)
        public static Vector2 LandingSpot(float x, float y, float fromX, float fromY)
        {
            bool has = false; Vector2 best = new Vector2(x, y); float bd = 1e9f;
            for (int i = 0; i < 48; i++)
            {
                float a = i / 48f * 6.28f;
                for (float r = 20; r < 500; r += 12)
                {
                    float px = x + Mathf.Cos(a) * r, py = y + Mathf.Sin(a) * r;
                    if (LandQ(px, py) > 1.05f) { float d = r + K.Dist(px, py, fromX, fromY) * 0.15f; if (d < bd) { bd = d; best = new Vector2(px, py); has = true; } break; }
                }
            }
            return has ? best : new Vector2(x, y);
        }
    }
}
