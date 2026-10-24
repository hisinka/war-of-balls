// «Война шариков» — per-frame rendering: camera, soldiers (GPU instancing), vehicles, effects, particles.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WarOfBalls
{
    public class Rig
    {
        public GameObject g; public Transform hull, tur, barrel, rotor, trotor; public List<Transform> wheels; public List<Material> mats = new List<Material>();
        public Unit u; public float bx0; public bool deadInit, boom; public float deadT;
    }

    public partial class WarGame
    {
        // ================= camera: like a map app — pan, zoom, rotate, 2D/3D =================
        public class CamState { public float x, y, dist, yaw, pitch; }
        public const float P3D = 0.92f, P2D = 1.5f, DMIN = 160f, DMAX = 4600f;
        public CamState cam = new CamState { x = K.W * 0.3f, y = K.H / 2, dist = 1200, yaw = 0, pitch = P3D };
        public CamState camT = new CamState { x = K.W * 0.3f, y = K.H / 2, dist = 1200, yaw = 0, pitch = P3D };
        public bool camMode3d = true, camInit;
        public bool hasCamVel; public Vector2 camVel; public Squad camFollow;
        public HashSet<KeyCode> keys = new HashSet<KeyCode>();
        Vector3 lookPt;

        void PlaceCam(CamState c)
        {
            float fx = -Mathf.Sin(c.yaw), fy = -Mathf.Cos(c.yaw), cp = Mathf.Cos(c.pitch), sp = Mathf.Sin(c.pitch);
            Vector3 dir = new Vector3(fx * cp, -sp, -fy * cp).normalized;
            lookPt = K.U(c.x, c.y, Terra.Height(c.x, c.y) * 0.6f);
            cam3.transform.position = lookPt - dir * c.dist; cam3.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }
        public void CamSync() { cam.x = camT.x; cam.y = camT.y; cam.dist = camT.dist; cam.yaw = camT.yaw; cam.pitch = camT.pitch; PlaceCam(cam); }
        void CamFit(float x0, float y0, float x1, float y1, bool inst)
        {
            CamState c = new CamState { x = (x0 + x1) / 2, y = (y0 + y1) / 2, dist = 1000, yaw = camT.yaw, pitch = camT.pitch };
            float yTop = 1 - 2 * TopPad() / VH, yBot = -1 + 2 * BotPad() / VH;
            Vector2[] corners = { new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x0, y1), new Vector2(x1, y1) };
            for (int it = 0; it < 6; it++)
            {
                PlaceCam(c); float mx = 0, ny0 = 1e9f, ny1 = -1e9f;
                foreach (Vector2 p in corners) { Vector3 vp = cam3.WorldToViewportPoint(K.U(p.x, p.y, Terra.Height(p.x, p.y))); float nx = vp.x * 2 - 1, ny = vp.y * 2 - 1; mx = Mathf.Max(mx, Mathf.Abs(nx)); ny0 = Mathf.Min(ny0, ny); ny1 = Mathf.Max(ny1, ny); }
                c.dist *= Mathf.Max(mx / 0.92f, (ny1 - ny0) / (yTop - yBot));
            }
            PlaceCam(cam);
            camT.x = K.Clamp(c.x, K.WX0, K.WX1); camT.y = K.Clamp(c.y, K.WY0, K.WY1); camT.dist = K.Clamp(c.dist, DMIN, DMAX);
            if (inst) CamSync();
        }
        public void CamFitUnits(bool inst)
        {
            float x0 = 1e9f, y0 = 1e9f, x1 = -1e9f, y1 = -1e9f;
            if (units.Count == 0) { x0 = K.FLX; x1 = K.W - K.FLX; y0 = y1 = K.H / 2; }
            foreach (Unit u in units) { x0 = Mathf.Min(x0, u.x); y0 = Mathf.Min(y0, u.y); x1 = Mathf.Max(x1, u.x); y1 = Mathf.Max(y1, u.y); }
            const float m = 90; CamFit(x0 - m, y0 - m, x1 + m, y1 + m, inst);
        }
        public void CamReset(bool jump) { camT.pitch = camMode3d ? P3D : P2D; if (Mathf.Abs(camT.yaw) > 0.01f) camT.yaw = 0; CamFit(K.ZX0 - 80, K.ZY0 - 40, K.ZX1 + 300, K.ZY1 + 40, jump); }
        public void Set3D(bool on) { camMode3d = on; camT.pitch = on ? P3D : P2D; }
        void FitCamera(float dt)
        {
            if (phase == Phase.Menu) { camT.yaw += dt * 0.04f; camT.x = K.W / 2; camT.y = K.H / 2; camT.dist = 2000; camT.pitch = 0.82f; }
            if (hasCamVel)
            {
                camT.x = K.Clamp(camT.x + camVel.x * dt, K.WX0, K.WX1); camT.y = K.Clamp(camT.y + camVel.y * dt, K.WY0, K.WY1); cam.x = camT.x; cam.y = camT.y;
                float d = Mathf.Exp(-dt * 3.2f); camVel *= d; if (camVel.magnitude < 8) hasCamVel = false;
            }
            if (camFollow != null)
            {
                if (!AnyAlive(camFollow) || phase == Phase.Menu) camFollow = null;
                else { Vector2 f = Cen(camFollow); camT.x += (f.x - camT.x) * Mathf.Min(1, dt * 3); camT.y += (f.y - camT.y) * Mathf.Min(1, dt * 3); }
            }
            if (keys.Count > 0 && phase != Phase.Menu)
            {
                float fx = -Mathf.Sin(cam.yaw), fy = -Mathf.Cos(cam.yaw), rx = Mathf.Cos(cam.yaw), ry = -Mathf.Sin(cam.yaw), v = cam.dist * 0.9f * dt, mx = 0, my = 0;
                if (keys.Contains(KeyCode.W) || keys.Contains(KeyCode.UpArrow)) { mx += fx; my += fy; }
                if (keys.Contains(KeyCode.S) || keys.Contains(KeyCode.DownArrow)) { mx -= fx; my -= fy; }
                if (keys.Contains(KeyCode.D) || keys.Contains(KeyCode.RightArrow)) { mx += rx; my += ry; }
                if (keys.Contains(KeyCode.A) || keys.Contains(KeyCode.LeftArrow)) { mx -= rx; my -= ry; }
                if (keys.Contains(KeyCode.Equals) || keys.Contains(KeyCode.KeypadPlus)) camT.dist = K.Clamp(camT.dist * (1 - dt * 1.5f), DMIN, DMAX);
                if (keys.Contains(KeyCode.Minus) || keys.Contains(KeyCode.KeypadMinus)) camT.dist = K.Clamp(camT.dist * (1 + dt * 1.5f), DMIN, DMAX);
                if (mx != 0 || my != 0) { camT.x = K.Clamp(camT.x + mx * v, K.WX0, K.WX1); camT.y = K.Clamp(camT.y + my * v, K.WY0, K.WY1); camFollow = null; }
            }
            float a = Mathf.Min(1, dt * 7); cam.x += (camT.x - cam.x) * a; cam.y += (camT.y - cam.y) * a; cam.dist += (camT.dist - cam.dist) * a; cam.pitch += (camT.pitch - cam.pitch) * a;
            cam.yaw += K.WrapAngle(camT.yaw - cam.yaw) * a;
            if (!camInit) { CamSync(); camInit = true; }
            PlaceCam(cam); skyGO.transform.position = cam3.transform.position;
            if (shake > 0) cam3.transform.position += new Vector3(Rnd(-1, 1), Rnd(-1, 1), 0) * shake * 8;
            RenderSettings.fogStartDistance = cam.dist * 1.4f; RenderSettings.fogEndDistance = Mathf.Min(cam.dist * 4 + 2200, 16000);
            sunL.transform.rotation = Quaternion.LookRotation(-sunDir, Vector3.up);
        }
        // screen (GUI units) -> point on the ground
        public Vector2 Pick(float sx, float sy)
        {
            Ray r = cam3.ScreenPointToRay(new Vector3(sx * us, Screen.height - sy * us, 0));
            if (r.direction.y > -1e-4f) return new Vector2(K.W / 2, K.H / 2);
            float h = 0; Vector3 hit = r.origin;
            for (int it = 0; it < 6; it++)
            {
                float t = (h - r.origin.y) / r.direction.y; hit = r.origin + r.direction * t;
                float gx = K.Clamp(hit.x + K.W / 2, K.WX0 - 2000, K.WX1 + 2000), gy = K.Clamp(K.H / 2 - hit.z, K.WY0 - 2000, K.WY1 + 2000);
                h = Terra.Height(gx, gy);
            }
            return new Vector2(hit.x + K.W / 2, K.H / 2 - hit.z);
        }
        // world -> screen (GUI units); b = behind the camera
        public struct SP { public float x, y; public bool b; }
        public SP ToScr(float x, float y, float h)
        {
            Vector3 p = cam3.WorldToScreenPoint(K.U(x, y, h));
            return new SP { x = p.x / us, y = (Screen.height - p.y) / us, b = p.z < 0 };
        }

        // ================= unit parts =================
        InstPart pBody, pEye, pPupil, pFoot, pHelmet, pVisor, pBoonie, pBeret, pRifle, pSniper, pPistol, pGren, pShield, pCorpse, pShot;
        DynMesh fx, partsMesh;
        Geo gSphere, gBox, gRing, gDisc, gBird;
        const float VS = 1.15f;
        static readonly Color OLIVE = K.C(0x56663a), BLACKG = K.C(0x23272c);
        void InitRender()
        {
            pBody = new InstPart(new ColorModel().Add(Geo.Sphere(1, 18, 14), Color.white), true, true, 0.45f);
            pEye = new InstPart(new ColorModel().Add(Geo.Sphere(1, 8, 6), Color.white), false, false);
            pPupil = new InstPart(new ColorModel().Add(Geo.Sphere(1, 6, 5), K.C(0x111111)), false, false);
            pFoot = new InstPart(new ColorModel().Add(Geo.Sphere(1, 8, 6), Color.white), true, true);
            Geo helm = new Geo(); helm.Append(Geo.Sphere(1, 14, 8, 0, 6.29f, 0, 1.65f), Matrix4x4.identity); helm.Append(Geo.Cyl(1.06f, 1.06f, 0.1f, 16), Geo.M(0, 0.05f, 0));
            pHelmet = new InstPart(new ColorModel().Add(helm, Color.white), true, true, 0.2f);
            pVisor = new InstPart(new ColorModel().Add(Geo.Box(0.2f, 0.32f, 1.1f), K.C(0x1a2a3a), Matrix4x4.identity, 0.85f), false, false);
            Geo boon = new Geo(); boon.Append(Geo.Cyl(1.3f, 1.3f, 0.07f, 16), Matrix4x4.identity); boon.Append(Geo.Cyl(0.72f, 0.86f, 0.5f, 14), Geo.M(0, 0.28f, 0));
            pBoonie = new InstPart(new ColorModel().Add(boon, Color.white), true, true);
            pBeret = new InstPart(new ColorModel().Add(Geo.Sphere(1, 12, 8), Color.white), true, false);
            pRifle = new InstPart(new ColorModel()
                .Add(Geo.Box(1.35f, 0.22f, 0.16f), K.C(0x2b2e31), Geo.M(0.35f, 0, 0), 0.5f).Add(Geo.Box(0.5f, 0.26f, 0.14f), K.C(0x3a3d40), Geo.M(-0.55f, -0.05f, 0), 0.5f)
                .Add(Geo.Cyl(0.05f, 0.05f, 0.75f, 6), K.C(0x18191b), Geo.M(1.35f, 0.03f, 0, 0, 0, Mathf.PI / 2), 0.5f).Add(Geo.Box(0.14f, 0.42f, 0.12f), K.C(0x2b2e31), Geo.M(0.42f, -0.27f, 0, 0, 0, 0.25f), 0.5f), false, true);
            pSniper = new InstPart(new ColorModel()
                .Add(Geo.Box(1.8f, 0.2f, 0.15f), K.C(0x4a5236), Geo.M(0.3f, 0, 0), 0.3f).Add(Geo.Cyl(0.05f, 0.05f, 1.3f, 6), K.C(0x18191b), Geo.M(1.85f, 0.02f, 0, 0, 0, Mathf.PI / 2), 0.3f)
                .Add(Geo.Cyl(0.1f, 0.1f, 0.75f, 8), K.C(0x111111), Geo.M(0.45f, 0.22f, 0, 0, 0, Mathf.PI / 2), 0.6f).Add(Geo.Box(0.55f, 0.3f, 0.14f), K.C(0x4a5236), Geo.M(-0.75f, -0.05f, 0), 0.3f), false, true);
            pPistol = new InstPart(new ColorModel().Add(Geo.Box(0.55f, 0.16f, 0.1f), K.C(0x222428), Geo.M(0.25f, 0, 0), 0.4f).Add(Geo.Box(0.14f, 0.32f, 0.1f), K.C(0x222428), Geo.M(0, -0.16f, 0, 0, 0, 0.2f), 0.4f), false, false);
            pGren = new InstPart(new ColorModel().Add(Geo.Sphere(0.3f, 8, 6), K.C(0x4d5a2c)).Add(Geo.Cyl(0.1f, 0.1f, 0.18f, 6), K.C(0x888888), Geo.M(0, 0.32f, 0)), false, false);
            pShield = new InstPart(new ColorModel().Add(Geo.Box(0.16f, 1.8f, 1.3f), K.C(0x2b3036), Matrix4x4.identity, 0.3f).Add(Geo.Box(0.2f, 0.35f, 0.7f), K.C(0x3d5468), Geo.M(0.04f, 0.5f, 0), 0.8f)
                .Add(Geo.Box(0.2f, 0.12f, 1.34f), K.C(0x4a4d50), Geo.M(0, 0.84f, 0)).Add(Geo.Box(0.2f, 0.12f, 1.34f), K.C(0x4a4d50), Geo.M(0, -0.84f, 0)), false, true);
            pCorpse = new InstPart(new ColorModel().Add(Geo.Sphere(1, 10, 6), Color.white), true, false);
            pShot = new InstPart(new ColorModel().Add(Geo.Sphere(1, 8, 6), Color.white), true, false);
            gSphere = Geo.Sphere(1, 6, 4); gBox = Geo.Box(1, 0.35f, 0.35f); gRing = Geo.Ring(0.85f, 1, 32); gDisc = Geo.Disc(1, 16);
            gBird = new Geo(); { int a = gBird.V(Vector3.zero, Vector3.up), b = gBird.V(new Vector3(-2, 0, 6), Vector3.up), c = gBird.V(new Vector3(2, 0.4f, 0), Vector3.up), d = gBird.V(new Vector3(-2, 0, -6), Vector3.up); gBird.Tri(a, b, c, Vector3.up); gBird.Tri(a, c, d, Vector3.up); }
            fx = new DynMesh(Mats.UnlitNew(Color.white, null, 3010));
            partsMesh = new DynMesh(Mats.UnlitNew(Color.white, Tex.dot, 3020));
        }
        InstPart[] AllParts() { return new[] { pBody, pEye, pPupil, pFoot, pHelmet, pVisor, pBoonie, pBeret, pRifle, pSniper, pPistol, pGren, pShield, pCorpse, pShot }; }
        static Matrix4x4 L(float x, float y, float z, float rx, float ry, float rz, float s, float sy = float.NaN, float sz = float.NaN)
        {
            if (float.IsNaN(sy)) sy = s; if (float.IsNaN(sz)) sz = s;
            return Matrix4x4.TRS(new Vector3(x, y, z), (rx == 0 && ry == 0 && rz == 0) ? Quaternion.identity : Quaternion.Euler(rx * Mathf.Rad2Deg, ry * Mathf.Rad2Deg, rz * Mathf.Rad2Deg), new Vector3(s, sy, sz));
        }
        static Color Q(Color c) { return new Color(Mathf.Round(c.r * 16) / 16, Mathf.Round(c.g * 16) / 16, Mathf.Round(c.b * 16) / 16, 1); }

        void RenderUnits(float t)
        {
            foreach (InstPart p in AllParts()) p.Begin();
            fx.Clear();
            foreach (Unit u in units)
            {
                if (Units.RIG(u.ti)) continue;
                bool pass = u.inBoat != null; float r = u.T.r * VS * (pass ? 0.72f : 1f), h = pass ? K.SEA + 3.5f : Terra.Height(u.x, u.y);
                float bob = pass ? 0 : Mathf.Abs(Mathf.Sin(u.walk)) * (u.spd > 5 ? r * 0.18f : 0) + r * 0.12f;
                Matrix4x4 MU = Matrix4x4.TRS(K.U(u.x, u.y, h), K.Face(u.face), Vector3.one);
                Color bodyC = Units.BODY[u.team]; if (u.flash > 0) bodyC = Color.Lerp(bodyC, Color.white, Mathf.Round(u.flash * 4) / 4 * 0.8f);
                float br = r, by = r + bob, rec = u.draw * 0.25f * br;
                if (!pass)
                {
                    float st = u.spd > 5 ? Mathf.Sin(u.walk * 2) : 0;
                    for (int sd = -1; sd <= 1; sd += 2) { float ph = st * sd; pFoot.Add(MU * L(ph * br * 0.32f, br * 0.16f + Mathf.Max(0, ph) * br * 0.12f, sd * br * 0.42f, 0, 0, 0, br * 0.3f, br * 0.2f, br * 0.26f), Units.DARK[u.team]); }
                }
                pBody.Add(MU * L(0, by, 0, 0, 0, 0, br), bodyC);
                pEye.Add(MU * L(br * 0.78f, by + br * 0.25f, br * 0.36f, 0, 0, 0, br * 0.25f)); pEye.Add(MU * L(br * 0.78f, by + br * 0.25f, -br * 0.36f, 0, 0, 0, br * 0.25f));
                pPupil.Add(MU * L(br * 0.98f, by + br * 0.27f, br * 0.36f, 0, 0, 0, br * 0.12f)); pPupil.Add(MU * L(br * 0.98f, by + br * 0.27f, -br * 0.36f, 0, 0, 0, br * 0.12f));
                float mz = -1;
                if (u.ti == 0) { pHelmet.Add(MU * L(0, by + br * 0.42f, 0, 0, 0, 0, br * 1.02f), OLIVE); pRifle.Add(MU * L(0.25f * br - rec, by - 0.1f * br, 0.85f * br, 0, 0, 0, br)); mz = 1.9f; }
                else if (u.ti == 1) { pBoonie.Add(MU * L(0, by + br * 0.78f, 0, 0, 0, 0.08f, br * 0.95f), K.C(0x6b7a45)); pSniper.Add(MU * L(0.2f * br - rec, by, 0.8f * br, 0, 0, 0, br)); mz = 2.6f; }
                else if (u.ti == 3) { pHelmet.Add(MU * L(0, by + br * 0.42f, 0, 0, 0, 0, br * 1.02f), OLIVE); float up = u.draw > 0.3f ? u.draw : 0; pGren.Add(MU * L(0.3f * br - up * br * 0.4f, by + up * br * 1.1f, 0.95f * br, 0, 0, 0, br)); }
                else if (u.ti == 4) { pBeret.Add(MU * L(-0.1f * br, by + br * 0.82f, 0, 0, 0, 0.25f, br * 0.75f, br * 0.28f, br * 0.75f), Units.DARK[u.team]); pPistol.Add(MU * L(0.55f * br - rec, by, 0.8f * br, 0, 0, 0, br)); mz = 1.2f; }
                else if (u.ti == 5)
                {
                    bool w = u.sq.wall;
                    pHelmet.Add(MU * L(0, by + br * 0.42f, 0, 0, 0, 0, br * 1.05f), BLACKG); pVisor.Add(MU * L(br * 0.88f, by + br * 0.48f, 0, 0, 0, 0, br));
                    pShield.Add(MU * L(1.02f * br, by - (w ? 0 : 0.12f) * br, (w ? 0 : -0.3f) * br, 0, w ? 0 : 0.35f, 0, br)); pPistol.Add(MU * L(0.5f * br - rec, by + 0.1f * br, 0.85f * br, 0, 0, 0, br)); mz = 1.2f;
                }
                if (mz > 0 && u.draw > 0.72f) fx.AddGeo(gSphere, MU * L(mz * br, by, 0.85f * br, 0, 0, 0, br * (u.ti == 1 ? 0.55f : 0.38f) * (0.7f + UnityEngine.Random.value * 0.5f)), new Color(1f, 0.82f, 0.38f, 0.95f));
            }
            foreach (Corpse c in corpses)
            {
                float k = Mathf.Min(1, c.t / 0.4f), sink = Mathf.Max(0, c.t - 14) * 0.6f, h = Terra.Height(c.x, c.y);
                pCorpse.Add(Matrix4x4.TRS(K.U(c.x, c.y, h + c.r * 0.2f - sink), Quaternion.Euler(0, -c.rot * Mathf.Rad2Deg, 0), new Vector3(c.r, c.r * (1 - k * 0.68f), c.r * 0.95f)), Units.DARK[c.team]);
            }
            foreach (Scorch s in scorches)
            {
                float k = Mathf.Max(0, 1 - Mathf.Max(0, s.t - 12) / 8); if (k <= 0) continue;
                fx.AddGeo(gDisc, Matrix4x4.TRS(K.U(s.x, s.y, Terra.Height(s.x, s.y) + 0.35f), Quaternion.identity, Vector3.one * s.r * k), new Color(0.07f, 0.07f, 0.07f, 0.45f));
            }
            foreach (Shot a in shots)
            {
                if (a.delay > 0) continue;
                float k = Mathf.Min(1, a.t / a.T), k2 = Mathf.Min(1, k + 0.03f), x = a.x0 + (a.tx - a.x0) * k, y = a.y0 + (a.ty - a.y0) * k, hh = ProjH(a, k);
                Vector3 P = K.U(x, y, hh);
                if (Units.SMALL(a.kind))
                {
                    Vector3 D = K.U(a.x0 + (a.tx - a.x0) * k2, a.y0 + (a.ty - a.y0) * k2, ProjH(a, k2)) - P; if (D.sqrMagnitude < 1e-6f) D = Vector3.right;
                    fx.AddGeo(gBox, Matrix4x4.TRS(P, Quaternion.FromToRotation(Vector3.right, D.normalized), new Vector3(a.kind == Proj.Snipe ? 34 : 14, a.kind == Proj.Mg ? 1.3f : 1, a.kind == Proj.Mg ? 1.3f : 1)), a.kind == Proj.Snipe ? new Color(1, 1, 1, 0.95f) : new Color(1f, 0.82f, 0.44f, 0.95f));
                }
                else
                {
                    float sz = a.kind == Proj.Shell ? 1.9f : a.kind == Proj.Rocket ? 1.6f : 1.4f;
                    pShot.Add(Matrix4x4.TRS(P, Quaternion.identity, Vector3.one * sz), a.kind == Proj.Grenade ? K.C(0x3e4a2a) : a.kind == Proj.Rocket ? K.C(0x8a8a80) : K.C(0x2a2a2a));
                    if (a.kind == Proj.Rocket) fx.AddGeo(gSphere, Matrix4x4.TRS(K.U(x - (a.tx - a.x0) * 0.02f, y - (a.ty - a.y0) * 0.02f, hh), Quaternion.identity, Vector3.one * 2.2f), new Color(1f, 0.63f, 0.25f, 0.95f));
                }
            }
            if (sel != null && AnyAlive(sel))
                foreach (Unit u in sel.units)
                {
                    if (u.dead || u.inBoat != null) continue; float rr = u.T.r * (Units.RIG(u.ti) ? 1.5f : 1.4f);
                    fx.AddGeo(gRing, Matrix4x4.TRS(K.U(u.x, u.y, (u.T.boat ? K.SEA + 0.9f : Terra.Height(u.x, u.y)) + 0.6f), Quaternion.identity, Vector3.one * rr), new Color(1f, 0.85f, 0.29f, 0.9f));
                }
            foreach (RingFx g in rings)
            {
                float s = 20 + g.t * 160; Color c = g.col; c.a = Mathf.Max(0, 1 - g.t) * 0.9f;
                fx.AddGeo(gRing, Matrix4x4.TRS(K.U(g.x, g.y, Terra.Height(g.x, g.y) + 1), Quaternion.identity, Vector3.one * s), c);
            }
            // seagulls
            float bcx = K.W / 2 + Mathf.Cos(t * 0.05f) * 600, bcy = K.H / 2 + Mathf.Sin(t * 0.07f) * 400;
            for (int i = 0; i < flock.Length; i++)
            {
                Vector4 b = flock[i]; float a = t * 0.35f + b.x, x = bcx + Mathf.Cos(a) * (140 + b.y), y = bcy + Mathf.Sin(a) * (90 + b.y), f = Mathf.Sin(t * 9 + b.w);
                fx.AddGeo(gBird, Matrix4x4.TRS(K.U(x, y, 200 + b.z + f * 1.5f), K.Face(a + Mathf.PI / 2), new Vector3(1.6f, 1 + f * 0.8f, 1.6f)), new Color(0.96f, 0.96f, 0.96f, 1));
            }
            foreach (InstPart p in AllParts()) p.Draw();
            fx.Draw();
        }
        Vector4[] flock;

        // ---------- vehicles, helicopters and boats: one object hierarchy each ----------
        List<Rig> rigs = new List<Rig>();
        static Color Paint(int team, int baseHex, float k = 0.45f) { return Color.Lerp(K.C(baseHex), Units.FLAGC[team], k); }
        Rig BuildRig(Unit u)
        {
            Rig R = new Rig { u = u }; int team = u.team;
            GameObject g = new GameObject(u.T.name); g.transform.SetParent(worldRoot, false); R.g = g;
            Func<Transform, string, ColorModel, Transform> part = (par, name, cm) => cm.Spawn(name, par, true).transform;
            if (u.ti == 2)
            {
                Color body = Paint(team, 0x5b6b3a), dark = K.C(0x2a2d30), wheel = K.C(0x1b1c1e);
                ColorModel hullM = new ColorModel();
                hullM.Add(Geo.Box(32, 8, 17), body, Geo.M(0, 9, 0), 0.3f).Add(Geo.Box(24, 5, 15), body, Geo.M(-3, 15, 0), 0.3f).Add(Geo.Box(9, 7, 16.6f), body, Geo.M(15, 9.5f, 0, 0, 0, 0.65f), 0.3f)
                     .Add(Geo.Box(5, 0.6f, 4), dark, Geo.M(-8, 17.8f, 0));
                part(g.transform, "Hull", hullM);
                R.wheels = new List<Transform>();
                foreach (float x in new[] { -11f, -3.5f, 4f, 11.5f }) foreach (float z in new[] { -8.6f, 8.6f })
                    {
                        Transform w = part(g.transform, "Wheel", new ColorModel().Add(Geo.Cyl(3.7f, 3.7f, 3, 12), wheel, Geo.M(0, 0, 0, Mathf.PI / 2, 0, 0)));
                        w.localPosition = new Vector3(x, 3.7f, z); R.wheels.Add(w);
                    }
                Transform tur = new GameObject("Turret").transform; tur.SetParent(g.transform, false); tur.localPosition = new Vector3(1, 17.5f, 0);
                part(tur, "Cupola", new ColorModel().Add(Geo.Cyl(4.6f, 5, 3.6f, 12), body, Matrix4x4.identity, 0.3f));
                R.barrel = part(tur, "Barrel", new ColorModel().Add(Geo.Cyl(0.55f, 0.55f, 13, 6), dark, Geo.M(0, 0, 0, 0, 0, Mathf.PI / 2)));
                R.barrel.localPosition = new Vector3(7, 0.6f, 0); R.bx0 = 7; R.tur = tur;
            }
            else if (u.ti == 8)
            {
                Color body = Paint(team, 0x5b6b3a), dark = K.C(0x2a2d30), track = K.C(0x1d1e20);
                ColorModel hullM = new ColorModel();
                hullM.Add(Geo.Box(38, 7, 19), body, Geo.M(0, 8, 0), 0.3f).Add(Geo.Box(31, 3, 17), body, Geo.M(-1, 12.5f, 0), 0.3f).Add(Geo.Box(6, 6, 18.6f), body, Geo.M(18, 7.5f, 0, 0, 0, 0.7f), 0.3f);
                foreach (float z in new[] { -11f, 11f })
                {
                    hullM.Add(Geo.Box(40, 7.5f, 5), track, Geo.M(0, 3.8f, z));
                    foreach (float x in new[] { -14f, -7f, 0f, 7f, 14f }) hullM.Add(Geo.Cyl(2.6f, 2.6f, 5.4f, 10), dark, Geo.M(x, 3.4f, z, Mathf.PI / 2, 0, 0));
                }
                part(g.transform, "Hull", hullM);
                Transform tur = new GameObject("Turret").transform; tur.SetParent(g.transform, false); tur.localPosition = new Vector3(-2, 16.5f, 0);
                part(tur, "TurretBody", new ColorModel().Add(Geo.Box(18, 6.5f, 15), body, Matrix4x4.identity, 0.3f).Add(Geo.Cyl(6, 7, 6.4f, 12), body, Geo.M(4, 0, 0), 0.3f).Add(Geo.Cyl(2.4f, 2.4f, 1.2f, 10), dark, Geo.M(-3, 3.8f, 3)));
                R.barrel = part(tur, "Barrel", new ColorModel().Add(Geo.Cyl(1.1f, 1.35f, 28, 10), dark, Geo.M(0, 0, 0, 0, 0, Mathf.PI / 2)).Add(Geo.Cyl(1.8f, 1.8f, 3, 10), dark, Geo.M(13.5f, 0, 0, 0, 0, Mathf.PI / 2)));
                R.barrel.localPosition = new Vector3(21, 0.5f, 0); R.bx0 = 21; R.tur = tur;
                g.transform.localScale = Vector3.one * 1.5f; // the tank is a big boy
            }
            else if (u.ti == 6)
            {
                Color body = Paint(team, 0x4a5534, 0.35f), dark = K.C(0x26292c), glass = K.C(0x8cc2e0);
                Transform hull = new GameObject("Hull").transform; hull.SetParent(g.transform, false); R.hull = hull;
                ColorModel hm = new ColorModel();
                hm.Add(Geo.Sphere(1, 16, 10), body, Geo.M(0, 0, 0, 0, 0, 0, 14, 6, 6.2f), 0.4f).Add(Geo.Sphere(1, 12, 8), glass, Geo.M(9, 2.6f, 0, 0, 0, 0, 6.5f, 4.2f, 4.4f), 0.9f)
                  .Add(Geo.Sphere(1.8f, 8, 6), dark, Geo.M(15, -2.5f, 0)).Add(Geo.Cyl(0.6f, 0.6f, 6, 6), dark, Geo.M(14, -4, 0, 0, 0, Mathf.PI / 2))
                  .Add(Geo.Cyl(1.4f, 3.2f, 26, 8), body, Geo.M(-22, 1.5f, 0, 0, 0, Mathf.PI / 2), 0.4f).Add(Geo.Box(6, 9, 1), body, Geo.M(-34, 5.5f, 0, 0, 0, 0.2f), 0.4f).Add(Geo.Box(5, 0.8f, 9), body, Geo.M(-33, 1.5f, 0), 0.4f)
                  .Add(Geo.Box(4, 0.8f, 19), body, Geo.M(-1, -1.5f, 0), 0.4f);
                foreach (float z in new[] { -8f, 8f }) hm.Add(Geo.Cyl(1.7f, 1.7f, 7, 8), dark, Geo.M(-1, -3.2f, z, 0, 0, Mathf.PI / 2));
                foreach (float z in new[] { -5f, 5f }) { hm.Add(Geo.Box(22, 0.8f, 0.8f), dark, Geo.M(0, -7.5f, z)); foreach (float x in new[] { -5f, 5f }) hm.Add(Geo.Box(0.6f, 3.5f, 0.6f), dark, Geo.M(x, -5.8f, z)); }
                hm.Add(Geo.Cyl(0.9f, 1.2f, 3, 8), dark, Geo.M(0, 6.5f, 0));
                part(hull, "Fuselage", hm);
                Transform rot = part(hull, "Rotor", new ColorModel().Add(Geo.Box(32, 0.35f, 2.4f), dark, Geo.M(0, 0.4f, 0)).Add(Geo.Box(32, 0.35f, 2.4f), dark, Geo.M(0, 0.4f, 0, 0, Mathf.PI / 2, 0)));
                rot.localPosition = new Vector3(0, 7.5f, 0); R.rotor = rot;
                Transform tr = part(hull, "TailRotor", new ColorModel().Add(Geo.Box(0.6f, 10, 0.4f), dark, Matrix4x4.identity).Add(Geo.Box(0.6f, 0.4f, 10), dark, Matrix4x4.identity));
                tr.localPosition = new Vector3(-34, 6, 1.4f); R.trotor = tr;
            }
            else if (u.ti == 7)
            {
                Color hullC = K.C(0xf0efe8), stripe = Units.FLAGC[team], dark = K.C(0x2a2d30), wood = K.C(0x9a7448);
                ColorModel bm = new ColorModel();
                bm.Add(Geo.Box(28, 4.2f, 13), hullC, Geo.M(-1, 1.5f, 0), 0.3f).Add(Geo.Box(9.2f, 4.2f, 9.2f), hullC, Geo.M(13, 1.5f, 0, 0, Mathf.PI / 4, 0), 0.3f)
                  .Add(Geo.Box(29, 1, 13.4f), stripe, Geo.M(-1, 3.8f, 0)).Add(Geo.Box(24, 0.5f, 10.4f), wood, Geo.M(-2, 3.4f, 0));
                foreach (float x in new[] { -8f, 0f, 7f }) bm.Add(Geo.Box(1.5f, 1.2f, 11), wood, Geo.M(x, 4.2f, 0));
                bm.Add(Geo.Box(4, 6, 4), dark, Geo.M(-16, 5, 0)).Add(Geo.Cyl(0.5f, 0.5f, 5, 6), dark, Geo.M(-17.5f, 0.5f, 0));
                part(g.transform, "Boat", bm);
            }
            // own material instances so a wreck can darken without touching other units
            foreach (MeshRenderer mr in g.GetComponentsInChildren<MeshRenderer>())
            {
                Material[] ms = mr.sharedMaterials; Material[] copy = new Material[ms.Length];
                for (int i = 0; i < ms.Length; i++) { copy[i] = new Material(ms[i]); R.mats.Add(copy[i]); }
                mr.sharedMaterials = copy;
            }
            return R;
        }
        void ClearRigs()
        {
            foreach (Rig r in rigs) { foreach (Material m in r.mats) Destroy(m); foreach (MeshFilter mf in r.g.GetComponentsInChildren<MeshFilter>()) Destroy(mf.sharedMesh); Destroy(r.g); }
            rigs.Clear();
        }
        void RenderRigs(float t, float dt)
        {
            foreach (Squad s in squads) foreach (Unit u in s.units) if (Units.RIG(u.ti) && u.rig == null && !u.dead) { u.rig = BuildRig(u); rigs.Add(u.rig); }
            foreach (Rig R in rigs)
            {
                Unit u = R.u; GameObject g = R.g; if (!g.activeSelf) continue;
                float h = u.T.boat ? K.SEA + 0.6f + Mathf.Sin(t * 2 + u.id * 7) * 0.5f : Terra.Height(u.x, u.y);
                if (u.ti == 6) h += 58 + Mathf.Sin(t * 1.7f + u.id * 5) * 2;
                float rotZ = 0, rotX = 0;
                if (u.dead)
                {
                    if (!R.deadInit) { R.deadInit = true; R.deadT = t; foreach (Material m in R.mats) m.color = m.color * 0.28f; }
                    float k = t - R.deadT;
                    if (u.ti == 7) { h = K.SEA - k * 5; rotZ = k * 0.25f; if (k > 3) g.SetActive(false); }
                    else if (u.ti == 6)
                    {
                        float gh = Terra.Height(u.x, u.y); h = Mathf.Max(gh + 2, h - k * k * 45); if (R.hull) R.hull.localRotation = Quaternion.Euler(k * 2 * Mathf.Rad2Deg, 0, 0);
                        if (h <= gh + 2.5f && !R.boom) { R.boom = true; FxExplosion(u.x, u.y, 40, true); }
                        if (k > 25) g.SetActive(false);
                    }
                    else
                    {
                        if (UnityEngine.Random.value < dt * 7) Puff(u.x + Rnd(-6, 6), u.y + Rnd(-6, 6), h + 16, 0x3a3a3a, false);
                        if (UnityEngine.Random.value < dt * 3) Puff(u.x, u.y, h + 12, 0xff8a20, true);
                        if (k > 45) g.SetActive(false);
                    }
                }
                if (u.ti == 7 && !u.dead) rotX = Mathf.Sin(t * 1.6f + u.id * 3) * 0.05f;
                g.transform.position = K.U(u.x, u.y, h);
                g.transform.rotation = K.Face(u.face) * Quaternion.Euler(rotX * Mathf.Rad2Deg, 0, rotZ * Mathf.Rad2Deg);
                if (R.tur != null) { R.tur.localRotation = Quaternion.Euler(0, K.WrapAngle(u.aim - u.face) * Mathf.Rad2Deg, 0); R.barrel.localPosition = new Vector3(R.bx0 - u.draw * 3, R.barrel.localPosition.y, 0); }
                if (R.wheels != null) foreach (Transform w in R.wheels) w.localRotation = Quaternion.Euler(0, 0, -u.walk * 0.6f * Mathf.Rad2Deg);
                if (u.ti == 6 && !u.dead) { R.rotor.localRotation = Quaternion.Euler(0, t * 28 * Mathf.Rad2Deg, 0); R.trotor.localRotation = Quaternion.Euler(0, 0, t * 40 * Mathf.Rad2Deg); R.hull.localRotation = Quaternion.Euler(0, 0, -K.Clamp(u.spd / 260f, 0, 0.22f) * Mathf.Rad2Deg); }
            }
        }

        // ---------- flags, camps, lighthouses, water ----------
        void RenderFlags(float t)
        {
            for (int tm = 0; tm < 2; tm++)
            {
                Flag f = flags[tm]; Flag3 o = flag3[tm]; o.g.SetActive(f.active); if (!f.active) continue;
                for (int i = 0; i < o.bx.Length; i++) { Vector3 b = o.bx[i]; o.buf[i] = new Vector3(b.x, b.y, Mathf.Sin(b.x * 0.16f - t * 5 + b.y * 0.05f) * b.x * 0.09f + Mathf.Sin(b.x * 0.3f - t * 7) * b.x * 0.02f); }
                o.cloth.vertices = o.buf; o.cloth.RecalculateBounds();
                Transform cloth = o.g.transform.Find("Cloth"); if (cloth != null) cloth.localPosition = new Vector3(0, 82 - f.cap * 58, 0);
                o.g.transform.position = K.U(f.x, f.y, Terra.Height(f.x, f.y));
                Color enemy = flag3[1 - tm].col;
                bool capping = f.att > 0 && f.def == 0 && f.cap > 0.005f;
                o.beam.SetActive(capping || f.cap > 0.02f);
                Color bc = enemy; bc.a = (capping ? 0.07f + 0.05f * Mathf.Sin(t * 8) : 0.03f) * (0.4f + f.cap) * 2.2f; o.beamMat.color = bc;
                if (capping && UnityEngine.Random.value < 0.5f) { float a = UnityEngine.Random.value * 6.28f, r = UnityEngine.Random.value * K.FLAG_R; Puff(f.x + Mathf.Cos(a) * r, f.y + Mathf.Sin(a) * r, Terra.Height(f.x, f.y) + 2, tm == 0 ? 0xff6a5a : 0x8ec1ff, true); }
                o.clothMat.color = f.cap >= 1 ? enemy : o.col;
                Color rc = Color.Lerp(o.col, enemy, f.cap); rc.a = f.att > 0 && f.def == 0 ? 0.55f + 0.4f * Mathf.Abs(Mathf.Sin(t * 6)) : 0.7f; o.ringMat.color = rc;
            }
        }
        void RenderDecor(float t, float dt)
        {
            foreach (Transform b in beams) if (b != null) b.localRotation = Quaternion.Euler(0, -t * 0.9f * Mathf.Rad2Deg, 0);
            Vector2 wo = new Vector2(t * 0.004f, t * 0.0025f);
            if (waterTiles) waterMat.mainTextureOffset = wo;
            else { Vector2[] uv = new Vector2[4]; for (int i = 0; i < 4; i++) uv[i] = waterUV0[i] + wo; waterMesh.uv = uv; }
            if (cloudMat != null && cloudTiles) cloudMat.mainTextureOffset = new Vector2(t * 0.006f, t * 0.0035f);
            foreach (Camp c in camps)
            {
                float k = 0.8f + Mathf.Sin(t * 17 + c.fx) * 0.12f + Mathf.Sin(t * 29) * 0.08f; c.flame.localScale = new Vector3(k, 0.9f + k * 0.4f, k);
                if (UnityEngine.Random.value < dt * 10) Puff(c.fx + Rnd(-3, 3), c.fy + Rnd(-3, 3), K.LANDH + 8, UnityEngine.Random.value < 0.5f ? 0xff8a20 : 0xffd060, true);
            }
            if (zoneGO != null) zoneGO.SetActive(zoneVisible);
        }

        // ================= particles: puffs of smoke, sparks, splashes =================
        struct Part { public Vector3 p, v; public float l, ml, gr; public Color c; public bool glow; }
        const int NPART = 1800;
        List<Part> parts = new List<Part>(NPART);
        public void Puff(float x, float y, float h, int col, bool glow)
        {
            if (parts.Count >= NPART) return;
            parts.Add(new Part { p = K.U(x, y, h), v = new Vector3(Rnd(-8, 8), Rnd(6, 18), Rnd(-8, 8)), l = glow ? 0.5f : 0.9f, ml = glow ? 0.5f : 0.9f, c = K.C(col), gr = glow ? -8 : -2, glow = glow });
        }
        void Burst(Unit u, int n, int col, float sp)
        {
            float h = Terra.Height(u.x, u.y) + u.T.r * 1.2f; Vector3 p = K.U(u.x, u.y, h); Color c = K.C(col);
            for (int i = 0; i < n && parts.Count < NPART; i++)
                parts.Add(new Part { p = p, v = new Vector3(Rnd(-1, 1) * sp * 0.5f, Rnd(10, sp * 0.6f), Rnd(-1, 1) * sp * 0.5f), l = 0.45f, ml = 0.45f, c = c, gr = -120 });
        }
        void StepParts(float dt)
        {
            for (int i = parts.Count - 1; i >= 0; i--)
            {
                Part p = parts[i]; p.l -= dt; if (p.l <= 0) { parts.RemoveAt(i); continue; }
                p.v.y += p.gr * dt; p.p += p.v * dt; parts[i] = p;
            }
        }
        void RenderParts()
        {
            partsMesh.Clear(); Vector3 right = cam3.transform.right, up = cam3.transform.up;
            foreach (Part p in parts)
            {
                float k = Mathf.Min(1, p.l / p.ml * 1.5f), s = p.glow ? 3.4f : 4f; Color c = p.c; c.a = k * (p.glow ? 1f : 0.85f);
                partsMesh.AddQuad(p.p, right * s, up * s, c);
            }
            partsMesh.Draw();
        }
    }
}
