// «Война шариков» — the 3D world: camera, light, sky, ocean, islands, bridge, piers, camps, flags.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WarOfBalls
{
    public partial class WarGame
    {
        public Camera cam3; Light sunL;
        Transform worldRoot, terrainRoot;
        Mesh waterMesh, cloudMesh; Material waterMat, cloudMat; Texture2D waveTex, cloudTex;
        bool waterTiles, cloudTiles; Vector2[] waterUV0, cloudUV0;
        Mesh skyMesh; GameObject skyGO;
        List<Transform> beams = new List<Transform>();
        Vector3 sunDir = new Vector3(-0.45f, 0.8f, 0.35f).normalized;

        class Tod { public string name; public int top, bot, fog, sun; public float si, hemi; public Vector3 dir; }
        static readonly Tod[] TOD = {
            new Tod{ name="День",     top=0x5e9eea, bot=0xcfe6ff, fog=0xc4ddf2, sun=0xfff1d8, si=0.95f, hemi=0.62f, dir=new Vector3(-0.45f,0.8f,-0.35f) },
            new Tod{ name="Утро",     top=0x7aa6e0, bot=0xffd6b8, fog=0xf0d8c8, sun=0xffd9b0, si=0.85f, hemi=0.6f,  dir=new Vector3(-0.8f,0.45f,0.35f) },
            new Tod{ name="Закат",    top=0x4a4e8a, bot=0xffa060, fog=0xe8a070, sun=0xff9a5a, si=0.95f, hemi=0.45f, dir=new Vector3(0.85f,0.35f,-0.4f) },
            new Tod{ name="Пасмурно", top=0x8a96a2, bot=0xc8d0d4, fog=0xbcc4c8, sun=0xffffff, si=0.45f, hemi=0.85f, dir=new Vector3(-0.3f,0.9f,0.2f) },
        };

        void InitWorld()
        {
            Mats.Init();
            worldRoot = new GameObject("WarOfBalls_World").transform;
            terrainRoot = new GameObject("Terrain").transform; terrainRoot.SetParent(worldRoot, false);
            // our own camera and sun; anything the template scene had is switched off
            foreach (Camera c in Camera.allCameras) c.gameObject.SetActive(false);
            foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) l.gameObject.SetActive(false);
            GameObject cg = new GameObject("WarCamera"); cg.tag = "MainCamera"; cg.transform.SetParent(worldRoot, false);
            cam3 = cg.AddComponent<Camera>(); cam3.fieldOfView = 42; cam3.nearClipPlane = 8; cam3.farClipPlane = 40000; cam3.clearFlags = CameraClearFlags.SolidColor; cam3.backgroundColor = K.C(0xc4ddf2);
            cg.AddComponent<AudioListener>();
            GameObject lg = new GameObject("Sun"); lg.transform.SetParent(worldRoot, false);
            sunL = lg.AddComponent<Light>(); sunL.type = LightType.Directional; sunL.shadows = LightShadows.Soft; sunL.shadowStrength = 0.6f; sunL.intensity = 1.1f;
            sunL.shadowBias = 0.05f; sunL.shadowNormalBias = 0.4f;
            RenderSettings.sun = sunL;
            // shadows far enough for our scale (the map is a few thousand units wide)
            QualitySettings.shadowDistance = 1600; QualitySettings.shadowCascades = 4;
            try
            {
                RenderPipelineAsset rp = GraphicsSettings.currentRenderPipeline;
                if (rp != null)
                {
                    var p = rp.GetType().GetProperty("shadowDistance"); if (p != null && p.CanWrite) p.SetValue(rp, 1600f, null);
                    var cc = rp.GetType().GetProperty("shadowCascadeCount"); if (cc != null && cc.CanWrite) cc.SetValue(rp, 4, null);
                }
            }
            catch (Exception) { }
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            BuildSky(); BuildOcean(); BuildBridge(); BuildCamps(); BuildFlags();
        }

        // ---------- sky: a big vertex-coloured dome that follows the camera ----------
        void BuildSky()
        {
            Geo g = Geo.Sphere(30000, 32, 16); skyMesh = g.ToMesh();
            skyGO = new GameObject("Sky"); skyGO.transform.SetParent(worldRoot, false);
            skyGO.AddComponent<MeshFilter>().sharedMesh = skyMesh;
            MeshRenderer mr = skyGO.AddComponent<MeshRenderer>(); mr.sharedMaterial = Mats.UnlitNew(Color.white, null, 1000); mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
        }
        void ApplyTOD()
        {
            Tod p = TOD[Terra.tod]; Color ct = K.C(p.top), cb = K.C(p.bot);
            Vector3[] v = skyMesh.vertices; Color[] cols = new Color[v.Length];
            for (int i = 0; i < v.Length; i++) { float k = Mathf.Clamp01(v[i].y / 30000f * 1.8f); cols[i] = Color.Lerp(cb, ct, Mathf.Pow(k, 0.6f)); }
            skyMesh.colors = cols;
            RenderSettings.fogColor = K.C(p.fog); cam3.backgroundColor = K.C(p.fog);
            sunL.color = K.C(p.sun); sunL.intensity = p.si * 1.15f;
            Color amb = Color.Lerp(K.C(0x4a5a3a), K.C(0xeaf4ff), 0.62f) * (0.55f + p.hemi * 0.55f);
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = amb;
            SphericalHarmonicsL2 sh = new SphericalHarmonicsL2(); sh.AddAmbientLight(amb); RenderSettings.ambientProbe = sh;
            Vector3 d = p.dir.normalized; sunDir = new Vector3(d.x, d.y, -d.z);
        }

        // ---------- the ocean: a glossy translucent plane with drifting ripples over a dark sea floor ----------
        void BuildOcean()
        {
            Canvas2D c = new Canvas2D(256, 256); c.Clear(K.C(0x2b86c2));
            System.Random R = new System.Random(7);
            for (int i = 0; i < 500; i++)
            {
                float x = (float)R.NextDouble() * 256, y = (float)R.NextDouble() * 256, w = 6 + (float)R.NextDouble() * 22;
                Color col = R.NextDouble() < 0.6 ? new Color(1, 1, 1, 0.18f) : new Color(10 / 255f, 50 / 255f, 90 / 255f, 0.18f);
                c.Line(x, y, x + w / 2, y - 2, 1.2f, col); c.Line(x + w / 2, y - 2, x + w, y, 1.2f, col);
            }
            Color tint = K.C(0x9fd2f0);
            for (int i = 0; i < c.px.Length; i++) { Color p = c.px[i] * tint; p.a = 0.8f; c.px[i] = p; }
            waveTex = c.ToTexture(); waveTex.wrapMode = TextureWrapMode.Repeat; waveTex.anisoLevel = 4;
            Shader ut = Mats.UnlitTransparent();
            waterTiles = ut != null;
            waterMat = waterTiles ? new Material(ut) : Mats.UnlitNew(Color.white, waveTex, 3000);
            if (waterTiles) { waterMat.mainTexture = waveTex; waterMat.mainTextureScale = new Vector2(220, 220); waterMat.renderQueue = 3000; }
            waterMesh = new Mesh(); float S = 35000, uvs = waterTiles ? 1f : 220f;
            waterMesh.vertices = new[] { new Vector3(-S, 0, -S), new Vector3(-S, 0, S), new Vector3(S, 0, S), new Vector3(S, 0, -S) };
            waterUV0 = new[] { new Vector2(0, 0), new Vector2(0, uvs), new Vector2(uvs, uvs), new Vector2(uvs, 0) };
            waterMesh.uv = waterUV0; waterMesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            waterMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 }; waterMesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up }; waterMesh.RecalculateBounds();
            GameObject wg = new GameObject("Water"); wg.transform.SetParent(worldRoot, false); wg.transform.position = new Vector3(0, K.SEA + 0.3f, 0);
            wg.AddComponent<MeshFilter>().sharedMesh = waterMesh; MeshRenderer mr = wg.AddComponent<MeshRenderer>(); mr.sharedMaterial = waterMat; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            GameObject sb = new GameObject("Seabed"); sb.transform.SetParent(worldRoot, false); sb.transform.position = new Vector3(0, -17, 0);
            sb.AddComponent<MeshFilter>().sharedMesh = Geo.Box(70000, 0.5f, 70000).ToMesh(); MeshRenderer sm = sb.AddComponent<MeshRenderer>(); sm.sharedMaterial = Mats.Lit(K.C(0x1b5a80)); sm.shadowCastingMode = ShadowCastingMode.Off;
            // drifting cloud shadows
            c = new Canvas2D(256, 256); System.Random R2 = new System.Random(3);
            for (int i = 0; i < 26; i++)
            {
                float x = (float)R2.NextDouble() * 256, y = (float)R2.NextDouble() * 256, r = 20 + (float)R2.NextDouble() * 50;
                for (int k = 0; k < 9; k++) { float ox = (k % 3 - 1) * 256f, oy = (k / 3 - 1) * 256f; SoftBlob(c, x + ox, y + oy, r, new Color(0, 10 / 255f, 0, 0.16f)); }
            }
            cloudTex = c.ToTexture(); cloudTex.wrapMode = TextureWrapMode.Repeat;
        }
        static void SoftBlob(Canvas2D c, float cx, float cy, float r, Color col)
        {
            int ax = Mathf.Max(0, (int)(cx - r)), bx = Mathf.Min(c.w - 1, (int)(cx + r)), ay = Mathf.Max(0, (int)(cy - r)), by = Mathf.Min(c.h - 1, (int)(cy + r));
            for (int y = ay; y <= by; y++) for (int x = ax; x <= bx; x++) { float d = K.Dist(x + 0.5f, y + 0.5f, cx, cy) / r; if (d < 1) c.Blend(x, y, col, 1f - d); }
        }

        // ---------- islands ----------
        public void BuildTerrain3D()
        {
            for (int i = terrainRoot.childCount - 1; i >= 0; i--)
            {
                Transform ch = terrainRoot.GetChild(i);
                MeshFilter mf = ch.GetComponent<MeshFilter>(); if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
                Destroy(ch.gameObject);
            }
            beams.Clear();
            BuildGround(); BuildZone(); BuildVegetation(); BuildDecor(); ApplyTOD();
        }
        const int GX = 230, GY = 200; Material groundMat;
        void BuildGround()
        {
            float TW = K.W + 2 * K.MG, TH = K.H + 2 * K.MG;
            List<Vector3> V = new List<Vector3>((GX + 1) * (GY + 1)); List<Vector3> N = new List<Vector3>(V.Capacity); List<Vector2> UV = new List<Vector2>(V.Capacity); List<int> T = new List<int>(GX * GY * 6);
            for (int j = 0; j <= GY; j++)
                for (int i = 0; i <= GX; i++)
                {
                    float x = -K.MG + TW * i / GX, y = -K.MG + TH * j / GY, h = Terra.GroundH(x, y);
                    V.Add(K.U(x, y, h)); UV.Add(new Vector2((x + K.MG) / TW, (y + K.MG) / TH));
                    float e = 6f, hx = Terra.GroundH(x + e, y) - Terra.GroundH(x - e, y), hy = Terra.GroundH(x, y + e) - Terra.GroundH(x, y - e);
                    N.Add(new Vector3(-hx / (2 * e), 1f, hy / (2 * e)).normalized); // Unity z = -y
                }
            for (int j = 0; j < GY; j++)
                for (int i = 0; i < GX; i++)
                {
                    int a = j * (GX + 1) + i, b = a + 1, c = a + GX + 1, d = c + 1;
                    // the plane faces up: wind so that the cross product points to +Y
                    AddTriUp(T, V, a, b, d); AddTriUp(T, V, a, d, c);
                }
            Mesh m = new Mesh(); m.indexFormat = IndexFormat.UInt32; m.SetVertices(V); m.SetNormals(N); m.SetUVs(0, UV); m.SetTriangles(T, 0); m.RecalculateBounds();
            GameObject go = new GameObject("Ground"); go.transform.SetParent(terrainRoot, false);
            go.AddComponent<MeshFilter>().sharedMesh = m; MeshRenderer mr = go.AddComponent<MeshRenderer>();
            if (groundMat != null) { if (groundMat.mainTexture != null) Destroy(groundMat.mainTexture); Destroy(groundMat); }
            groundMat = Mats.LitTex(FieldTexture(Application.isMobilePlatform ? 1400 : 2048));
            mr.sharedMaterial = groundMat; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = true;
            // cloud shadows drift over a coarse copy of the ground
            int cx = 90, cy = 70; List<Vector3> CV = new List<Vector3>(); List<Vector2> CU = new List<Vector2>(); List<int> CT = new List<int>(); List<Color> CC = new List<Color>();
            for (int j = 0; j <= cy; j++) for (int i = 0; i <= cx; i++)
                {
                    float x = -K.MG + TW * i / cx, y = -K.MG + TH * j / cy;
                    CV.Add(K.U(x, y, Mathf.Max(Terra.GroundH(x, y), K.SEA + 0.4f) + 0.8f)); CU.Add(new Vector2(3f * i / cx, 2.6f * j / cy)); CC.Add(Color.white);
                }
            for (int j = 0; j < cy; j++) for (int i = 0; i < cx; i++) { int a = j * (cx + 1) + i, b = a + 1, c = a + cx + 1, d = c + 1; AddTriUp(CT, CV, a, b, d); AddTriUp(CT, CV, a, d, c); }
            cloudMesh = new Mesh(); cloudMesh.SetVertices(CV); cloudMesh.SetUVs(0, CU); cloudMesh.SetColors(CC); cloudMesh.SetTriangles(CT, 0); cloudMesh.RecalculateBounds(); cloudUV0 = CU.ToArray();
            Shader ut = Mats.UnlitTransparent(); cloudTiles = ut != null;
            if (cloudMat == null) { cloudMat = cloudTiles ? new Material(ut) : Mats.UnlitNew(Color.white, cloudTex, 2990); cloudMat.mainTexture = cloudTex; cloudMat.renderQueue = 2990; }
            cloudMat.color = new Color(1, 1, 1, TOD[Terra.tod].name == "Пасмурно" ? 0.65f : 1f);
            GameObject cgo = new GameObject("CloudShadows"); cgo.transform.SetParent(terrainRoot, false);
            cgo.AddComponent<MeshFilter>().sharedMesh = cloudMesh; MeshRenderer cmr = cgo.AddComponent<MeshRenderer>(); cmr.sharedMaterial = cloudMat; cmr.shadowCastingMode = ShadowCastingMode.Off; cmr.receiveShadows = false;
        }
        static void AddTriUp(List<int> T, List<Vector3> V, int a, int b, int c)
        {
            Vector3 cr = Vector3.Cross(V[b] - V[a], V[c] - V[a]);
            if (cr.y < 0) { T.Add(a); T.Add(c); T.Add(b); } else { T.Add(a); T.Add(b); T.Add(c); }
        }

        // ---------- the painted ground: sea bands, beaches, grass, roads, forests ----------
        static float Hash(int x, int y) { unchecked { int h = x * 374761393 + y * 668265263; h = (h ^ (h >> 13)) * 1274126177; h ^= h >> 16; return (h & 0xffffff) / (float)0xffffff; } }
        static float VNoise(float x, float y)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y); float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            float a = Hash(xi, yi), b = Hash(xi + 1, yi), c = Hash(xi, yi + 1), d = Hash(xi + 1, yi + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }
        Texture2D FieldTexture(int tw)
        {
            float TW = K.W + 2 * K.MG, TH = K.H + 2 * K.MG; int th = Mathf.RoundToInt(tw * TH / TW);
            Color32[] px = new Color32[tw * th];
            // per-island outline radius lookup (fast islandQ)
            int NT = 720; float[][] ntab = new float[2][];
            for (int k = 0; k < 2; k++) { ntab[k] = new float[NT + 1]; Island I = Terra.ISL[k]; for (int i = 0; i <= NT; i++) { float tt = -Mathf.PI + 2 * Mathf.PI * i / NT; ntab[k][i] = 1f + 0.075f * Mathf.Sin(3f * tt + I.s) + 0.05f * Mathf.Sin(5f * tt + 2.1f * I.s) + 0.025f * Mathf.Sin(11f * tt + 0.7f * I.s); } }
            Color deep = K.C(0x1d5d84), b1 = K.C(0x2671a0), b2 = K.C(0x3b98c2), b3 = K.C(0x7cc7d4), beach = K.C(0xead8a0), sand2 = K.C(0xd9c48a), grass = K.C(0x62a446);
            Color gLight = K.C(0x8cc35a), gDark = K.C(0x2f5a22), road = new Color(176 / 255f, 146 / 255f, 96 / 255f), forestC = new Color(40 / 255f, 60 / 255f, 25 / 255f), hillC = new Color(60 / 255f, 90 / 255f, 40 / 255f);
            Color[] fl = { K.C(0xfff6c8), K.C(0xffd1e8), K.C(0xffe066), K.C(0xcfe8ff), K.C(0xff8a8a) };
            float seed = UnityEngine.Random.value * 1000f;
            for (int j = 0; j < th; j++)
            {
                float y = -K.MG + (j + 0.5f) * TH / th;
                for (int i = 0; i < tw; i++)
                {
                    float x = -K.MG + (i + 0.5f) * TW / tw;
                    float q = 9f;
                    for (int k = 0; k < 2; k++)
                    {
                        Island I = Terra.ISL[k]; float dx = (x - I.cx) / I.rx, dy = (y - I.cy) / I.ry;
                        if (Mathf.Abs(dx) > 1.7f || Mathf.Abs(dy) > 1.7f) continue;
                        float d = Mathf.Sqrt(dx * dx + dy * dy), ta = Mathf.Atan2(dy, dx), fi = (ta + Mathf.PI) / (2 * Mathf.PI) * NT; int ii = Mathf.Clamp((int)fi, 0, NT - 1);
                        float n = Mathf.Lerp(ntab[k][ii], ntab[k][ii + 1], fi - ii);
                        if (Terra.BridgeGap(y) < 150f && (I.cx < K.W / 2f ? x > I.cx : x < I.cx)) n = Mathf.Max(n, 1.07f);
                        if (Mathf.Abs(y - K.BY) < 300f && (I.cx < K.W / 2f ? x < I.cx : x > I.cx)) n = Mathf.Max(n, 1.0f);
                        q = Mathf.Min(q, d / n);
                    }
                    Color col;
                    if (q >= 1.5f)
                    {
                        float nb = VNoise(x / 320f + seed, y / 320f);
                        col = Color.Lerp(deep, nb < 0.5f ? K.C(0x0a3250) : K.C(0x286e96), Mathf.Abs(nb - 0.5f) * 0.6f);
                    }
                    else
                    {
                        float e = 0.012f;
                        col = deep;
                        col = Color.Lerp(col, b1, Mathf.Clamp01((1.5f - q) / e));
                        col = Color.Lerp(col, b2, Mathf.Clamp01((1.22f - q) / e));
                        col = Color.Lerp(col, b3, Mathf.Clamp01((1.08f - q) / e));
                        col = Color.Lerp(col, beach, Mathf.Clamp01((1.0f - q) / e));
                        col = Color.Lerp(col, sand2, Mathf.Clamp01((0.94f - q) / e));
                        col = Color.Lerp(col, grass, Mathf.Clamp01((0.9f - q) / e));
                        if (q > 1.0f)
                        {
                            float nb = VNoise(x / 320f + seed, y / 320f); col = Color.Lerp(col, nb < 0.5f ? K.C(0x0a3250) : K.C(0x8fd0e0), Mathf.Abs(nb - 0.5f) * 0.25f);
                            col = Color.Lerp(col, Color.white, 0.55f * Mathf.Clamp01(1f - Mathf.Abs(q - 1.012f) / 0.012f));
                            col = Color.Lerp(col, Color.white, 0.22f * Mathf.Clamp01(1f - Mathf.Abs(q - 1.045f) / 0.009f));
                        }
                        if (q < 0.9f)
                        {
                            float n1 = VNoise(x / 110f, y / 110f + seed) * 0.6f + VNoise(x / 45f + seed, y / 45f) * 0.4f;
                            col = Color.Lerp(col, n1 > 0.5f ? gLight : gDark, Mathf.Abs(n1 - 0.5f) * 0.9f);
                            float g = Hash(i * 3 + 11, j * 7 + 5);
                            col *= 0.93f + g * 0.14f;
                            if (g > 0.996f) col = Color.Lerp(col, fl[(i + j) % 5], 0.8f);
                            // roads from each flag to the bridge
                            float rd = Terra.RoadDist(x, y);
                            if (rd < 19f) col = Color.Lerp(col, road * (0.92f + g * 0.1f), 0.7f * Mathf.Clamp01((19f - rd) / 3f));
                            foreach (Forest f in Terra.forests) { float d = K.Dist(x, y, f.x, f.y); if (d < f.r + 10) { float k = d / (f.r + 10); col = Color.Lerp(col, forestC, k < 0.85f ? Mathf.Lerp(0.7f, 0.5f, k / 0.85f) : 0.5f * (1f - (k - 0.85f) / 0.15f)); } }
                            foreach (Hill h in Terra.hills)
                            {
                                float d = K.Dist(x, y, h.x, h.y); if (d > h.r) continue;
                                for (int r = 1; r <= 4; r++) { float rr = h.r * r / 4.6f; float a = Mathf.Clamp01(1f - Mathf.Abs(d - rr) / 2.5f); if (a > 0) col = Color.Lerp(col, hillC, 0.18f * a); }
                            }
                        }
                    }
                    col.a = 1f; px[j * tw + i] = col;
                }
            }
            Texture2D t = new Texture2D(tw, th, TextureFormat.RGBA32, true); t.wrapMode = TextureWrapMode.Clamp; t.anisoLevel = 4;
            t.SetPixels32(px); t.Apply(true); return t;
        }

        void BuildZone()
        {
            Geo g = new Geo(); int nx = 12, ny = 24; int[,] idx = new int[nx + 1, ny + 1];
            for (int j = 0; j <= ny; j++) for (int i = 0; i <= nx; i++)
                {
                    float x = K.ZX0 + (K.ZX1 - K.ZX0) * i / nx, y = K.ZY0 + (K.ZY1 - K.ZY0) * j / ny;
                    idx[i, j] = g.V(K.U(x, y, Mathf.Max(Terra.Height(x, y), K.SEA + 0.4f) + 0.8f), Vector3.up);
                }
            for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++) g.Quad(idx[i, j], idx[i + 1, j], idx[i + 1, j + 1], idx[i, j + 1], Vector3.up);
            zoneGO = new GameObject("DeployZone"); zoneGO.transform.SetParent(terrainRoot, false);
            Mesh m = g.ToMesh(); Color[] cc = new Color[m.vertexCount]; for (int i = 0; i < cc.Length; i++) cc[i] = Color.white; m.colors = cc;
            zoneGO.AddComponent<MeshFilter>().sharedMesh = m; MeshRenderer mr = zoneGO.AddComponent<MeshRenderer>(); mr.sharedMaterial = Mats.Unlit(new Color(0.29f, 0.6f, 1f, 0.22f), null, 2995); mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
        }
        GameObject zoneGO;

        static Color Quant(Color c, int steps) { return new Color(Mathf.Round(c.r * steps) / steps, Mathf.Round(c.g * steps) / steps, Mathf.Round(c.b * steps) / steps, 1f); }
        void BuildVegetation()
        {
            SceneBuilder sb = new SceneBuilder();
            bool mob = Application.isMobilePlatform;
            // trees in the forests and scattered around
            Geo trunk = Geo.Cyl(1.4f, 2f, 9, 6).Transformed(Matrix4x4.Translate(new Vector3(0, 4.5f, 0)));
            Geo crown = Geo.Ico(9).Transformed(Matrix4x4.Translate(new Vector3(0, 15, 0)));
            Geo pine = Geo.Cone(8, 22, 7).Transformed(Matrix4x4.Translate(new Vector3(0, 17, 0)));
            List<Vector4> trees = new List<Vector4>();
            foreach (Forest f in Terra.forests)
            {
                int n = Mathf.RoundToInt(f.r * f.r / 75f);
                for (int i = 0; i < n; i++) { float a = UnityEngine.Random.value * 6.28f, d = Mathf.Sqrt(UnityEngine.Random.value) * (f.r - 6); trees.Add(new Vector4(f.x + Mathf.Cos(a) * d, f.y + Mathf.Sin(a) * d, Rnd(0.8f, 1.25f), UnityEngine.Random.value < 0.35f ? 1 : 0)); }
            }
            for (int i = 0; i < 120; i++) { Vector2 p; if (Terra.IslandSpot(UnityEngine.Random.value < 0.5f ? 'L' : 'R', 0.86f, 200, 6, out p)) trees.Add(new Vector4(p.x, p.y, Rnd(0.8f, 1.3f), UnityEngine.Random.value < 0.4f ? 1 : 0)); }
            foreach (Vector4 tr in trees)
            {
                float h = Terra.Height(tr.x, tr.y), s = tr.z;
                Matrix4x4 m = Matrix4x4.TRS(K.U(tr.x, tr.y, h), Quaternion.Euler(0, UnityEngine.Random.value * 360f, 0), new Vector3(s, s * Rnd(0.9f, 1.2f), s));
                sb.Add(trunk, K.C(0x6b4628), m);
                if (tr.w > 0) sb.Add(pine, Quant(K.HSL(Rnd(0.29f, 0.36f), 0.5f, Rnd(0.22f, 0.3f)), 12), m);
                else sb.Add(crown, Quant(K.HSL(Rnd(0.24f, 0.33f), 0.55f, Rnd(0.28f, 0.4f)), 12), m);
            }
            sb.Build(terrainRoot, "Trees", true);
            // palms along the beaches
            ColorModel palmT = new ColorModel();
            for (int i = 0; i < 6; i++) palmT.Add(Geo.Cyl(0.9f - i * 0.08f, 1.0f - i * 0.08f, 5, 6), K.C(0x8a6a44), Geo.M(i * 0.9f, 2.5f + i * 4.6f, 0, 0, 0, -0.12f - i * 0.03f));
            Geo leaf = Geo.Cone(2.2f, 14, 4);
            foreach (Palm p in Terra.palms)
            {
                Matrix4x4 m = Matrix4x4.TRS(K.U(p.x, p.y, Terra.Height(p.x, p.y)), Quaternion.Euler(0, -p.r * Mathf.Rad2Deg, 0), Vector3.one * p.s);
                sb.Add(palmT, m);
                Color lc = Quant(K.HSL(Rnd(0.25f, 0.32f), 0.6f, Rnd(0.28f, 0.38f)), 10);
                for (int i = 0; i < 7; i++) { float a = i / 7f * 6.28f; sb.Add(leaf, lc, m * Geo.M(5.4f + Mathf.Cos(a) * 6, 28 + Mathf.Sin(i * 1.7f) * 0.6f, Mathf.Sin(a) * 6, Mathf.Sin(a) * 1.25f, 0, -Mathf.Cos(a) * 1.25f, 1, 1, 0.35f)); }
            }
            sb.Build(terrainRoot, "Palms", true);
            Geo rock = Geo.Ico(1);
            foreach (Rock r in Terra.rocks) sb.Add(rock, K.C(0x8e938a), Matrix4x4.TRS(K.U(r.x, r.y, Terra.Height(r.x, r.y) + r.s * 0.3f), Quaternion.Euler(r.r * Mathf.Rad2Deg, r.r * 2 * Mathf.Rad2Deg, 0), new Vector3(r.s * 1.3f, r.s * 0.8f, r.s)));
            sb.Build(terrainRoot, "Rocks", true);
            // grass tufts + flowers
            Geo blade = new Geo();
            blade.Append(Geo.Cone(0.5f, 4.2f, 3, true), Geo.M(0, 2.1f, 0, 0, 0, 0.25f));
            blade.Append(Geo.Cone(0.45f, 3.4f, 3, true), Geo.M(0.7f, 1.7f, 0.4f, 0.3f, 0, -0.3f));
            blade.Append(Geo.Cone(0.45f, 3.0f, 3, true), Geo.M(-0.6f, 1.5f, -0.3f, -0.3f, 0, 0.35f));
            int NG = mob ? 3000 : 6500;
            for (int i = 0; i < NG; i++)
            {
                Island I = Terra.ISL[i % 2]; float a = Rnd(0, 6.28f), r = Mathf.Sqrt(UnityEngine.Random.value) * 0.9f, x = I.cx + Mathf.Cos(a) * I.rx * r, y = I.cy + Mathf.Sin(a) * I.ry * r;
                if (Terra.LandQ(x, y) > 0.9f || Terra.InForest(x, y)) continue;
                sb.Add(blade, Quant(K.HSL(Rnd(0.22f, 0.3f), Rnd(0.45f, 0.6f), Rnd(0.3f, 0.45f)), 8), Matrix4x4.TRS(K.U(x, y, Terra.Height(x, y) - 0.2f), Quaternion.Euler(0, UnityEngine.Random.value * 360f, 0), Vector3.one * Rnd(0.8f, 1.6f)));
            }
            sb.Build(terrainRoot, "Grass", false);
            Geo stem = Geo.Cyl(0.12f, 0.12f, 2.4f, 3, true).Transformed(Matrix4x4.Translate(new Vector3(0, 1.2f, 0)));
            Geo head = Geo.Sphere(0.9f, 6, 4).Transformed(Matrix4x4.Translate(new Vector3(0, 2.6f, 0)));
            int[] FC = { 0xfff3a0, 0xffb0d8, 0xff6a6a, 0xbfd8ff, 0xffffff, 0xffd040 };
            int NF = mob ? 600 : 1300;
            for (int i = 0; i < NF; i++)
            {
                Island I = Terra.ISL[i % 2]; float a = Rnd(0, 6.28f), r = Mathf.Sqrt(UnityEngine.Random.value) * 0.88f, x = I.cx + Mathf.Cos(a) * I.rx * r, y = I.cy + Mathf.Sin(a) * I.ry * r;
                if (Terra.LandQ(x, y) > 0.9f || Terra.InForest(x, y)) continue;
                Matrix4x4 m = Matrix4x4.TRS(K.U(x, y, Terra.Height(x, y)), Quaternion.identity, Vector3.one * Rnd(0.8f, 1.3f));
                sb.Add(stem, K.C(0x3d7a2a), m); sb.Add(head, K.C(FC[i % FC.Length]), m);
            }
            sb.Build(terrainRoot, "Flowers", false);
        }

        // ---------- decorations: bushes, sandbags, crates, lighthouses ----------
        void BuildDecor()
        {
            SceneBuilder sb = new SceneBuilder(); bool mob = Application.isMobilePlatform;
            Geo bush = new Geo(); bush.Append(Geo.Ico(5), Geo.M(0, 4, 0)); bush.Append(Geo.Ico(4), Geo.M(4.5f, 3, 1.5f)); bush.Append(Geo.Ico(3.6f), Geo.M(-3.8f, 2.8f, -1.8f));
            int nb = mob ? 100 : 160;
            for (int i = 0; i < nb; i++)
            {
                Vector2 p; if (!Terra.IslandSpot(i % 2 == 1 ? 'L' : 'R', 0.9f, 180, 8, out p)) continue; float k = Rnd(0.7f, 1.4f);
                sb.Add(bush, Quant(K.HSL(Rnd(0.24f, 0.33f), 0.5f, Rnd(0.22f, 0.33f)), 10), Matrix4x4.TRS(K.U(p.x, p.y, Terra.Height(p.x, p.y) - 0.5f), Quaternion.Euler(0, Rnd(0, 360), 0), new Vector3(k, k * Rnd(0.8f, 1.1f), k)));
            }
            Geo bag = Geo.Box(5, 2.4f, 3.2f), nest = new Geo();
            for (int i = 0; i < 5; i++) { float a = i * 0.32f - 0.64f; nest.Append(bag, Geo.M(Mathf.Cos(a) * 14, 1.2f, Mathf.Sin(a) * 14, 0, -a, 0)); }
            for (int i = 0; i < 4; i++) { float a = i * 0.32f - 0.48f; nest.Append(bag, Geo.M(Mathf.Cos(a) * 14, 3.5f, Mathf.Sin(a) * 14, 0, -a, 0)); }
            Geo crate = Geo.Box(7, 7, 7).Transformed(Matrix4x4.Translate(new Vector3(0, 3.5f, 0))), barrel = Geo.Cyl(3, 3.3f, 7, 10).Transformed(Matrix4x4.Translate(new Vector3(0, 3.5f, 0)));
            int nn = mob ? 16 : 26;
            for (int i = 0; i < nn; i++)
            {
                Vector2 p; if (!Terra.IslandSpot(i % 2 == 1 ? 'L' : 'R', 0.85f, 200, 10, out p)) continue;
                sb.Add(nest, K.C(0xcbb98a), Matrix4x4.TRS(K.U(p.x, p.y, Terra.Height(p.x, p.y)), Quaternion.Euler(0, Rnd(0, 360), 0), Vector3.one));
                for (int j = 0; j < 3; j++)
                {
                    float x = p.x + Rnd(-22, 22), y = p.y + Rnd(-22, 22);
                    Matrix4x4 m = Matrix4x4.TRS(K.U(x, y, Terra.Height(x, y)), Quaternion.Euler(0, Rnd(0, 360), 0), Vector3.one * Rnd(0.8f, 1.2f));
                    if (j % 2 == 1) sb.Add(crate, K.C(0x8c7a4a), m); else sb.Add(barrel, K.C(0x4d6b3a), m);
                }
            }
            sb.Build(terrainRoot, "Decor", true);
            // a lighthouse on each island
            for (int k = 0; k < 2; k++)
            {
                Island I = Terra.ISL[k]; Vector2 l = Terra.ShoreSpot(I, k == 1 ? 0.64f : 2.5f, 0.9f);
                GameObject g = new GameObject("Lighthouse"); g.transform.SetParent(terrainRoot, false); g.transform.position = K.U(l.x, l.y, Terra.Height(l.x, l.y));
                ColorModel cm = new ColorModel();
                for (int i = 0; i < 6; i++) cm.Add(Geo.Cyl(7.5f - i * 0.6f, 8 - i * 0.6f, 10, 14), i % 2 == 1 ? K.C(0xd83a2a) : K.C(0xf4f1ea), Geo.M(0, 5 + i * 10, 0));
                cm.Add(Geo.Cone(6, 6, 10), K.C(0x2a2a2a), Geo.M(0, 70.5f, 0));
                cm.Spawn("Tower", g.transform, true);
                Spawn(g.transform, "Lamp", Geo.Cyl(4.5f, 4.5f, 7, 10), new Vector3(0, 64, 0), Mats.Unlit(K.C(0xfff2a0)));
                GameObject pivot = new GameObject("BeamPivot"); pivot.transform.SetParent(g.transform, false); pivot.transform.localPosition = new Vector3(0, 64, 0);
                Geo bg = Geo.Cyl(0, 14, 160, 16, true).Transformed(Matrix4x4.TRS(new Vector3(80, 0, 0), Quaternion.Euler(0, 0, 90), Vector3.one));
                GameObject beam = new GameObject("Beam"); beam.transform.SetParent(pivot.transform, false);
                Mesh bm = bg.ToMesh(); Color[] bc = new Color[bm.vertexCount]; for (int i = 0; i < bc.Length; i++) bc[i] = Color.white; bm.colors = bc;
                beam.AddComponent<MeshFilter>().sharedMesh = bm; MeshRenderer br = beam.AddComponent<MeshRenderer>(); br.sharedMaterial = Mats.Unlit(new Color(1f, 0.965f, 0.75f, 0.16f)); br.shadowCastingMode = ShadowCastingMode.Off; br.receiveShadows = false;
                beams.Add(pivot.transform);
            }
        }

        public void BuildDocks()
        {
            SceneBuilder sb = new SceneBuilder(); Geo post = Geo.Cyl(0.8f, 0.8f, 14, 5);
            foreach (Dock d in docks)
            {
                float len = K.Dist(d.x, d.y, d.lx, d.ly), a = Mathf.Atan2(d.y - d.ly, d.x - d.lx), mx = (d.x + d.lx) / 2, my = (d.y + d.ly) / 2;
                sb.Add(Geo.Box(len, 1.2f, 10), K.C(0x9a7448), Matrix4x4.TRS(K.U(mx, my, K.SEA + 1.6f), K.Face(a), Vector3.one));
                for (int i = 0; i <= len / 14; i++)
                {
                    float k = i * 14 / len, x = d.lx + (d.x - d.lx) * k, y = d.ly + (d.y - d.ly) * k;
                    foreach (int s in new[] { -1, 1 }) sb.Add(post, K.C(0x5a4228), Matrix4x4.Translate(K.U(x - Mathf.Sin(a) * s * 4.5f, y + Mathf.Cos(a) * s * 4.5f, K.SEA - 4)));
                }
            }
            sb.Build(terrainRoot, "Piers", true);
        }

        // ---------- the two bridges (fixed, built once) ----------
        void BuildBridge()
        {
            SceneBuilder sb = new SceneBuilder(); float L = K.BXB - K.BXA, cx = (K.BXA + K.BXB) / 2;
            Action<Geo, int, float, float, float, float, float, float> add = (g, c, x, h, y, rx, ry, rz) => sb.Add(g, K.C(c), Matrix4x4.TRS(K.U(x, y, h), Quaternion.Euler(rx * Mathf.Rad2Deg, ry * Mathf.Rad2Deg, rz * Mathf.Rad2Deg), Vector3.one));
            const int ASP = 0x575c63, CON = 0xb9b4a8, RAIL = 0xd8d2c4, DARK = 0x3a3f45, YEL = 0xf2d24a, WHT = 0xeeeeee;
            float BW = K.BW, DECK = K.DECK;
            foreach (float BY in K.BYS)
            {
            add(Geo.Box(L - 100, 3, BW * 2), ASP, cx, DECK - 1.5f, BY, 0, 0, 0);
            add(Geo.Box(52, 3, BW * 2), ASP, K.BXA + 25, 3.6f, BY, 0, 0, 0.118f);
            add(Geo.Box(52, 3, BW * 2), ASP, K.BXB - 25, 3.6f, BY, 0, 0, -0.118f);
            Geo db = Geo.Box(12, 0.2f, 1.6f), eb = Geo.Box(16, 0.2f, 1.2f);
            for (float x = K.BXA + 60; x < K.BXB - 60; x += 26) { add(db, YEL, x, DECK + 0.1f, BY - 1.4f, 0, 0, 0); add(db, YEL, x, DECK + 0.1f, BY + 1.4f, 0, 0, 0); }
            for (float x = K.BXA + 60; x < K.BXB - 60; x += 34) foreach (int s in new[] { -1, 1 }) add(eb, WHT, x, DECK + 0.1f, BY + s * BW * 0.5f, 0, 0, 0);
            Geo pb = Geo.Box(1.2f, 5, 1.2f);
            foreach (int s in new[] { -1, 1 })
            {
                add(Geo.Box(L - 100, 0.2f, 1.4f), WHT, cx, DECK + 0.1f, BY + s * (BW - 7), 0, 0, 0);
                add(Geo.Box(L - 100, 1.4f, 1.4f), RAIL, cx, DECK + 5, BY + s * (BW - 0.7f), 0, 0, 0);
                add(Geo.Box(L - 100, 1, 1), RAIL, cx, DECK + 3, BY + s * (BW - 0.7f), 0, 0, 0);
                add(Geo.Box(L - 100, 1.8f, 3), CON, cx, DECK + 0.9f, BY + s * (BW - 1.5f), 0, 0, 0);
                for (float x = K.BXA + 52; x <= K.BXB - 52; x += 20) add(pb, RAIL, x, DECK + 2.5f, BY + s * (BW - 0.7f), 0, 0, 0);
            }
            Geo pil = Geo.Cyl(3.6f, 4.2f, 30, 10), cap = Geo.Box(10, 3, BW * 2 - 6);
            for (float x = K.BXA + 90; x < K.BXB - 60; x += 90) { foreach (int s in new[] { -1, 1 }) add(pil, CON, x, DECK - 18, BY + s * (BW - 12), 0, 0, 0); add(cap, CON, x, DECK - 4.5f, BY, 0, 0, 0); }
            Geo pole = Geo.Cyl(0.6f, 0.7f, 20, 6), arm = Geo.Box(6, 0.6f, 0.8f);
            for (float x = K.BXA + 80; x < K.BXB - 60; x += 120) foreach (int s in new[] { -1, 1 }) { add(pole, DARK, x, DECK + 10, BY + s * (BW - 2), 0, 0, 0); add(arm, DARK, x, DECK + 20, BY + s * (BW - 5), 0, Mathf.PI / 2, 0); }
            }
            sb.Build(worldRoot, "Bridge", true);
            Geo bulb = Geo.Sphere(1.7f, 8, 6);
            foreach (float BY in K.BYS) for (float x = K.BXA + 80; x < K.BXB - 60; x += 120) foreach (int s in new[] { -1, 1 }) sb.Add(bulb, K.C(0xfff2b0), Matrix4x4.Translate(K.U(x, BY + s * (BW - 8), DECK + 19.5f)), 0, true);
            sb.Build(worldRoot, "BridgeLamps", false);
        }

        // ---------- military camps behind the flags ----------
        class Camp { public Transform flame; public float fx, fy; }
        List<Camp> camps = new List<Camp>();
        void BuildCamps()
        {
            SceneBuilder sb = new SceneBuilder();
            Geo bag = Geo.Box(5, 2.4f, 3.2f);
            ColorModel tent = new ColorModel();
            for (int tm = 0; tm < 2; tm++)
            {
                int side = tm == 0 ? -1 : 1; float fxx = tm == 0 ? K.FLX : K.W - K.FLX, y0 = K.LANDH - 0.5f;
                int N = 26;
                for (int i = 0; i < N; i++)
                {
                    float a = (i / (N - 1f) - 0.5f) * 2.2f + (side < 0 ? 0 : Mathf.PI), r = K.FLAG_R + 34, x = fxx + Mathf.Cos(a) * r, y = K.H / 2 + Mathf.Sin(a) * r;
                    for (int l = 0; l < 2; l++) sb.Add(bag, K.C(0xcbb98a), Matrix4x4.TRS(K.U(x, y, y0 + 1.2f + l * 2.3f), K.Face(a - Mathf.PI / 2 - l * 0.2f), Vector3.one));
                }
                int[][] tents = { new[] { -80, -70, 0x5b6b3a }, new[] { -80, 0, -1 }, new[] { -80, 70, 0x5b6b3a }, new[] { -135, -35, 0x6a7444 }, new[] { -135, 40, 0x5b6b3a } };
                foreach (int[] t in tents)
                {
                    Color c = t[2] < 0 ? Units.FLAGC[tm] : K.C(t[2]);
                    float tx = side < 0 ? fxx + t[0] : fxx - t[0], ty = K.H / 2 + t[1];
                    Matrix4x4 m = Matrix4x4.TRS(K.U(tx, ty, y0), K.Face(-Mathf.PI / 2), Vector3.one);
                    sb.Add(Geo.Box(26, 10, 18), c, m * Geo.M(0, 5, 0));
                    sb.Add(Geo.Cyl(9, 9, 26, 3, false, 0, Mathf.PI), c, m * Geo.M(0, 10, 0, 0, 0, Mathf.PI / 2));
                }
                float mx = side < 0 ? fxx - 60 : fxx + 60, my = K.H / 2 - 120;
                sb.Add(Geo.Cyl(0.8f, 1.2f, 60, 6), K.C(0x8a8f95), Matrix4x4.Translate(K.U(mx, my, y0 + 30)));
                sb.Add(Geo.Sphere(4, 10, 6, 0, 6.3f, 0, 1.2f), K.C(0xdddddd), Matrix4x4.TRS(K.U(mx, my, y0 + 52), Quaternion.Euler(0, 0, side * 1.1f * Mathf.Rad2Deg), Vector3.one));
                float fx = side < 0 ? fxx - 110 : fxx + 110, fy = K.H / 2 + 120;
                for (int i = 0; i < 4; i++) sb.Add(Geo.Cyl(1.4f, 1.4f, 14, 6), K.C(0x5a3a20), Matrix4x4.TRS(K.U(fx, fy, y0 + 2), Quaternion.Euler((Mathf.PI / 2 - 0.25f) * Mathf.Rad2Deg, i * 45f, 0), Vector3.one));
                GameObject flame = new GameObject("Campfire"); flame.transform.SetParent(worldRoot, false); flame.transform.position = K.U(fx, fy, y0 + 7);
                Mesh fm = Geo.Cone(4.5f, 12, 7).ToMesh(); Color[] fc = new Color[fm.vertexCount]; for (int i = 0; i < fc.Length; i++) fc[i] = Color.white; fm.colors = fc;
                flame.AddComponent<MeshFilter>().sharedMesh = fm; MeshRenderer fr = flame.AddComponent<MeshRenderer>(); fr.sharedMaterial = Mats.Unlit(new Color(1f, 0.63f, 0.19f, 0.85f)); fr.shadowCastingMode = ShadowCastingMode.Off;
                for (int i = 0; i < 5; i++)
                {
                    sb.Add(Geo.Cyl(3, 3.4f, 7, 10), K.C(0x4d6b3a), Matrix4x4.Translate(K.U(fx + (side < 0 ? 35 : -35) + Rnd(-12, 12), fy - 50 + i * 7, y0 + 3.5f)));
                    sb.Add(Geo.Box(7, 7, 7), K.C(0x8c7a4a), Matrix4x4.TRS(K.U(fx + (side < 0 ? 20 : -20) + Rnd(-10, 10), fy + 60 + (i % 4) * 8, y0 + 3.5f + (i == 4 ? 7 : 0)), Quaternion.Euler(0, Rnd(0, 57), 0), Vector3.one));
                }
                camps.Add(new Camp { flame = flame.transform, fx = fx, fy = fy });
            }
            sb.Build(worldRoot, "Camps", true);
        }

        // ---------- flags ----------
        class Flag3 { public GameObject g; public Mesh cloth; public Vector3[] bx; public Vector3[] buf; public Material clothMat, ringMat, beamMat; public GameObject beam; public Color col; }
        Flag3[] flag3 = new Flag3[2];
        void BuildFlags()
        {
            for (int tm = 0; tm < 2; tm++)
            {
                Flag f = flags[tm]; Color col = Units.FLAGC[tm]; Flag3 o = new Flag3 { col = col };
                GameObject g = new GameObject(tm == 0 ? "BlueFlag" : "RedFlag"); g.transform.SetParent(worldRoot, false); g.transform.position = K.U(f.x, f.y, 0); o.g = g;
                ColorModel cm = new ColorModel();
                cm.Add(Geo.Cyl(11, 14, 6, 12), K.C(0x8e938a), Geo.M(0, 3, 0));
                cm.Add(Geo.Cyl(1.3f, 1.7f, 92, 8), K.C(0xd9c7a0), Geo.M(0, 52, 0), 0.3f);
                cm.Add(Geo.Sphere(3.2f, 10, 8), K.C(0xffd23f), Geo.M(0, 99, 0), 0.7f);
                cm.Spawn("Pole", g.transform, true);
                // the cloth: a double-sided grid animated every frame
                int sx = 14, sy = 5; Geo cg = new Geo(); int[,] fi = new int[sx + 1, sy + 1], bi = new int[sx + 1, sy + 1];
                for (int j = 0; j <= sy; j++) for (int i = 0; i <= sx; i++) { Vector3 p = new Vector3(44f * i / sx, 14 - 28f * j / sy, 0); fi[i, j] = cg.V(p, Vector3.back); }
                for (int j = 0; j <= sy; j++) for (int i = 0; i <= sx; i++) { Vector3 p = new Vector3(44f * i / sx, 14 - 28f * j / sy, 0); bi[i, j] = cg.V(p, Vector3.forward); }
                for (int j = 0; j < sy; j++) for (int i = 0; i < sx; i++) { cg.Quad(fi[i, j], fi[i + 1, j], fi[i + 1, j + 1], fi[i, j + 1], Vector3.back); cg.Quad(bi[i, j], bi[i + 1, j], bi[i + 1, j + 1], bi[i, j + 1], Vector3.forward); }
                o.cloth = cg.ToMesh(); o.cloth.MarkDynamic(); o.bx = o.cloth.vertices; o.buf = new Vector3[o.bx.Length];
                GameObject cloth = new GameObject("Cloth"); cloth.transform.SetParent(g.transform, false); cloth.transform.localPosition = new Vector3(0, 82, 0);
                cloth.AddComponent<MeshFilter>().sharedMesh = o.cloth; MeshRenderer cr = cloth.AddComponent<MeshRenderer>(); o.clothMat = Mats.LitNew(col); cr.sharedMaterial = o.clothMat;
                o.ringMat = Mats.UnlitNew(new Color(col.r, col.g, col.b, 0.7f));
                Spawn(g.transform, "Ring", Geo.Ring(K.FLAG_R - 3.5f, K.FLAG_R, 72), new Vector3(0, 0.9f, 0), o.ringMat);
                Spawn(g.transform, "Disc", Geo.Disc(K.FLAG_R, 48), new Vector3(0, 0.7f, 0), Mats.UnlitNew(new Color(col.r, col.g, col.b, 0.13f)));
                o.beamMat = Mats.UnlitNew(new Color(1, 1, 1, 0));
                o.beam = Spawn(g.transform, "Beam", Geo.Cyl(K.FLAG_R * 0.95f, K.FLAG_R, 150, 40, true), new Vector3(0, 75, 0), o.beamMat); o.beam.SetActive(false);
                flag3[tm] = o;
            }
        }
        GameObject Spawn(Transform parent, string name, Geo g, Vector3 pos, Material m)
        {
            GameObject go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = pos;
            Mesh mesh = g.ToMesh(); Color[] cc = new Color[mesh.vertexCount]; for (int i = 0; i < cc.Length; i++) cc[i] = Color.white; mesh.colors = cc;
            go.AddComponent<MeshFilter>().sharedMesh = mesh; MeshRenderer mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = m; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            return go;
        }
    }
}
