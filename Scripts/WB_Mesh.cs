// «Война шариков» — procedural geometry and materials (no model files needed).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WarOfBalls
{
    // a small triangle soup with normals; triangles are wound automatically to face their normal
    public class Geo
    {
        public List<Vector3> v = new List<Vector3>();
        public List<Vector3> n = new List<Vector3>();
        public List<int> t = new List<int>();
        public List<Vector2> uv;

        public int V(Vector3 p, Vector3 nn) { v.Add(p); n.Add(nn); return v.Count - 1; }
        public void Tri(int a, int b, int c, Vector3 hint)
        {
            Vector3 cr = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            if (Vector3.Dot(cr, hint) < 0) { t.Add(a); t.Add(c); t.Add(b); } else { t.Add(a); t.Add(b); t.Add(c); }
        }
        public void Quad(int a, int b, int c, int d, Vector3 hint) { Tri(a, b, c, hint); Tri(a, c, d, hint); }

        public void Append(Geo g, Matrix4x4 m)
        {
            Matrix4x4 nm = m.inverse.transpose; int o = v.Count;
            for (int i = 0; i < g.v.Count; i++) { v.Add(m.MultiplyPoint3x4(g.v[i])); Vector3 nn = nm.MultiplyVector(g.n[i]); n.Add(nn.sqrMagnitude > 1e-12f ? nn.normalized : Vector3.up); }
            float det = m.determinant;
            for (int i = 0; i < g.t.Count; i += 3)
            {
                if (det >= 0) { t.Add(g.t[i] + o); t.Add(g.t[i + 1] + o); t.Add(g.t[i + 2] + o); }
                else { t.Add(g.t[i] + o); t.Add(g.t[i + 2] + o); t.Add(g.t[i + 1] + o); }
            }
        }
        public Geo Transformed(Matrix4x4 m) { Geo g = new Geo(); g.Append(this, m); return g; }

        public Mesh ToMesh()
        {
            Mesh m = new Mesh();
            if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(v); m.SetNormals(n); if (uv != null && uv.Count == v.Count) m.SetUVs(0, uv);
            m.SetTriangles(t, 0); m.RecalculateBounds(); return m;
        }

        // ---------- primitives (same conventions as three.js: centred, cylinders along Y) ----------
        public static Geo Box(float w, float h, float d)
        {
            Geo g = new Geo(); float x = w / 2, y = h / 2, z = d / 2;
            Face(g, Vector3.right, new Vector3(x, -y, -z), new Vector3(x, y, -z), new Vector3(x, y, z), new Vector3(x, -y, z));
            Face(g, Vector3.left, new Vector3(-x, -y, -z), new Vector3(-x, -y, z), new Vector3(-x, y, z), new Vector3(-x, y, -z));
            Face(g, Vector3.up, new Vector3(-x, y, -z), new Vector3(-x, y, z), new Vector3(x, y, z), new Vector3(x, y, -z));
            Face(g, Vector3.down, new Vector3(-x, -y, -z), new Vector3(x, -y, -z), new Vector3(x, -y, z), new Vector3(-x, -y, z));
            Face(g, Vector3.forward, new Vector3(-x, -y, z), new Vector3(x, -y, z), new Vector3(x, y, z), new Vector3(-x, y, z));
            Face(g, Vector3.back, new Vector3(-x, -y, -z), new Vector3(-x, y, -z), new Vector3(x, y, -z), new Vector3(x, -y, -z));
            return g;
        }
        static void Face(Geo g, Vector3 nn, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int i0 = g.V(a, nn), i1 = g.V(b, nn), i2 = g.V(c, nn), i3 = g.V(d, nn); g.Quad(i0, i1, i2, i3, nn);
        }
        public static Geo Cyl(float rTop, float rBot, float h, int seg, bool open = false, float th0 = 0f, float thLen = 2f * Mathf.PI)
        {
            Geo g = new Geo(); float hy = h / 2f, slope = (rBot - rTop) / h;
            int[] top = new int[seg + 1], bot = new int[seg + 1];
            for (int i = 0; i <= seg; i++)
            {
                float th = th0 + thLen * i / seg, s = Mathf.Sin(th), c = Mathf.Cos(th);
                Vector3 nn = new Vector3(s, slope, c).normalized;
                top[i] = g.V(new Vector3(rTop * s, hy, rTop * c), nn);
                bot[i] = g.V(new Vector3(rBot * s, -hy, rBot * c), nn);
            }
            for (int i = 0; i < seg; i++)
            {
                float th = th0 + thLen * (i + 0.5f) / seg; Vector3 hint = new Vector3(Mathf.Sin(th), 0, Mathf.Cos(th));
                if (rTop > 0) g.Tri(top[i], top[i + 1], bot[i + 1], hint);
                if (rBot > 0) g.Tri(top[i], bot[i + 1], bot[i], hint);
            }
            if (!open)
            {
                for (int k = 0; k < 2; k++)
                {
                    float r = k == 0 ? rTop : rBot, yy = k == 0 ? hy : -hy; if (r <= 0) continue;
                    Vector3 nn = k == 0 ? Vector3.up : Vector3.down; int cI = g.V(new Vector3(0, yy, 0), nn);
                    int[] ring = new int[seg + 1];
                    for (int i = 0; i <= seg; i++) { float th = th0 + thLen * i / seg; ring[i] = g.V(new Vector3(r * Mathf.Sin(th), yy, r * Mathf.Cos(th)), nn); }
                    for (int i = 0; i < seg; i++) g.Tri(cI, ring[i], ring[i + 1], nn);
                }
            }
            return g;
        }
        public static Geo Cone(float r, float h, int seg, bool open = false) { return Cyl(0f, r, h, seg, open); }
        public static Geo Sphere(float r, int ws, int hs, float phi0 = 0f, float phiLen = 2f * Mathf.PI, float th0 = 0f, float thLen = Mathf.PI)
        {
            Geo g = new Geo(); int[,] idx = new int[ws + 1, hs + 1];
            for (int y = 0; y <= hs; y++)
                for (int x = 0; x <= ws; x++)
                {
                    float u = (float)x / ws, vv = (float)y / hs, ph = phi0 + u * phiLen, th = th0 + vv * thLen;
                    Vector3 p = new Vector3(-Mathf.Cos(ph) * Mathf.Sin(th), Mathf.Cos(th), Mathf.Sin(ph) * Mathf.Sin(th));
                    idx[x, y] = g.V(p * r, p);
                }
            for (int y = 0; y < hs; y++)
                for (int x = 0; x < ws; x++)
                {
                    int a = idx[x, y], b = idx[x + 1, y], c = idx[x + 1, y + 1], d = idx[x, y + 1];
                    Vector3 hint = (g.n[a] + g.n[b] + g.n[c] + g.n[d]);
                    if (y != 0 || th0 > 0) g.Tri(a, b, c, hint);
                    if (y != hs - 1 || th0 + thLen < Mathf.PI - 1e-3f) g.Tri(a, c, d, hint);
                }
            return g;
        }
        public static Geo Ico(float r)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            Vector3[] p = { new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0), new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t), new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1) };
            int[] f = { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8, 3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 };
            Geo g = new Geo();
            for (int i = 0; i < f.Length; i += 3)
            {
                Vector3 a = p[f[i]].normalized * r, b = p[f[i + 1]].normalized * r, c = p[f[i + 2]].normalized * r, nn = ((a + b + c) / 3f).normalized;
                int ia = g.V(a, nn), ib = g.V(b, nn), ic = g.V(c, nn); g.Tri(ia, ib, ic, nn);
            }
            return g;
        }
        // flat ring / disc lying in the XZ plane, facing up
        public static Geo Ring(float ri, float ro, int seg)
        {
            Geo g = new Geo();
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * 2f * Mathf.PI / seg, a1 = (i + 1) * 2f * Mathf.PI / seg;
                int p0 = g.V(new Vector3(Mathf.Cos(a0) * ri, 0, Mathf.Sin(a0) * ri), Vector3.up), p1 = g.V(new Vector3(Mathf.Cos(a0) * ro, 0, Mathf.Sin(a0) * ro), Vector3.up);
                int p2 = g.V(new Vector3(Mathf.Cos(a1) * ro, 0, Mathf.Sin(a1) * ro), Vector3.up), p3 = g.V(new Vector3(Mathf.Cos(a1) * ri, 0, Mathf.Sin(a1) * ri), Vector3.up);
                g.Quad(p0, p1, p2, p3, Vector3.up);
            }
            return g;
        }
        public static Geo Disc(float r, int seg)
        {
            Geo g = new Geo(); int c = g.V(Vector3.zero, Vector3.up);
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * 2f * Mathf.PI / seg, a1 = (i + 1) * 2f * Mathf.PI / seg;
                int p0 = g.V(new Vector3(Mathf.Cos(a0) * r, 0, Mathf.Sin(a0) * r), Vector3.up), p1 = g.V(new Vector3(Mathf.Cos(a1) * r, 0, Mathf.Sin(a1) * r), Vector3.up);
                g.Tri(c, p0, p1, Vector3.up);
            }
            return g;
        }
        public static Matrix4x4 M(float x, float y, float z, float rx = 0, float ry = 0, float rz = 0, float sx = 1, float sy = float.NaN, float sz = float.NaN)
        {
            if (float.IsNaN(sy)) sy = sx; if (float.IsNaN(sz)) sz = sx;
            return Matrix4x4.TRS(new Vector3(x, y, z), Quaternion.Euler(rx * Mathf.Rad2Deg, ry * Mathf.Rad2Deg, rz * Mathf.Rad2Deg), new Vector3(sx, sy, sz));
        }
    }

    // a model made of coloured pieces: one sub-mesh per colour
    public class ColorModel
    {
        public List<Geo> parts = new List<Geo>();
        public List<Color> cols = new List<Color>();
        public List<float> gloss = new List<float>();
        public ColorModel Add(Geo g, Color c, Matrix4x4 m, float glossy = 0f)
        {
            int i = -1; for (int k = 0; k < cols.Count; k++) if (cols[k] == c && Mathf.Approximately(gloss[k], glossy)) { i = k; break; }
            if (i < 0) { parts.Add(new Geo()); cols.Add(c); gloss.Add(glossy); i = parts.Count - 1; }
            parts[i].Append(g, m); return this;
        }
        public ColorModel Add(Geo g, Color c) { return Add(g, c, Matrix4x4.identity); }
        public Mesh ToMesh()
        {
            Mesh m = new Mesh(); List<Vector3> V = new List<Vector3>(), N = new List<Vector3>();
            foreach (Geo g in parts) { V.AddRange(g.v); N.AddRange(g.n); }
            if (V.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(V); m.SetNormals(N); m.subMeshCount = parts.Count;
            int o = 0;
            for (int i = 0; i < parts.Count; i++) { List<int> T = new List<int>(parts[i].t.Count); foreach (int k in parts[i].t) T.Add(k + o); m.SetTriangles(T, i); o += parts[i].v.Count; }
            m.RecalculateBounds(); return m;
        }
        public Material[] Materials(bool inst) { Material[] r = new Material[cols.Count]; for (int i = 0; i < cols.Count; i++) r[i] = Mats.Lit(cols[i], gloss[i], inst); return r; }
        public GameObject Spawn(string name, Transform parent, bool shadows = true)
        {
            GameObject go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = ToMesh();
            MeshRenderer mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = Materials(false);
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off; mr.receiveShadows = true;
            return go;
        }
    }

    // big static scenery: geometry is grouped per colour and split into meshes of at most ~60k vertices
    public class SceneBuilder
    {
        class Bucket { public Color c; public float gloss; public bool unlit; public List<Geo> chunks = new List<Geo>(); }
        List<Bucket> buckets = new List<Bucket>();
        public void Add(Geo g, Color c, Matrix4x4 m, float gloss = 0f, bool unlit = false)
        {
            Bucket b = buckets.Find(x => x.c == c && Mathf.Approximately(x.gloss, gloss) && x.unlit == unlit);
            if (b == null) { b = new Bucket { c = c, gloss = gloss, unlit = unlit }; buckets.Add(b); }
            if (b.chunks.Count == 0 || b.chunks[b.chunks.Count - 1].v.Count + g.v.Count > 60000) b.chunks.Add(new Geo());
            b.chunks[b.chunks.Count - 1].Append(g, m);
        }
        public void Add(ColorModel cm, Matrix4x4 m) { for (int i = 0; i < cm.parts.Count; i++) Add(cm.parts[i], cm.cols[i], m, cm.gloss[i]); }
        public void Build(Transform parent, string name, bool castShadow)
        {
            foreach (Bucket b in buckets)
                foreach (Geo g in b.chunks)
                {
                    if (g.v.Count == 0) continue;
                    GameObject go = new GameObject(name); go.transform.SetParent(parent, false);
                    Mesh mesh = g.ToMesh();
                    if (b.unlit) { Color[] cc = new Color[mesh.vertexCount]; for (int i = 0; i < cc.Length; i++) cc[i] = Color.white; mesh.colors = cc; }
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    MeshRenderer mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = b.unlit ? Mats.Unlit(b.c) : Mats.Lit(b.c, b.gloss, false);
                    mr.shadowCastingMode = castShadow && !b.unlit ? ShadowCastingMode.On : ShadowCastingMode.Off; mr.receiveShadows = !b.unlit;
                }
            buckets.Clear();
        }
    }

    // materials that work in both the Built-in pipeline and URP
    public static class Mats
    {
        static Shader lit, unlit; static bool ready;
        public static bool URP;
        static Dictionary<string, Material> cache = new Dictionary<string, Material>();
        static Dictionary<Texture, int> texIds = new Dictionary<Texture, int>();
        static string TexKey(Texture t) { int id; if (!texIds.TryGetValue(t, out id)) { id = texIds.Count + 1; texIds[t] = id; } return id.ToString(); } // works on every Unity version
        public static void Reset() { ready = false; cache.Clear(); texIds.Clear(); }
        public static void Init()
        {
            if (ready) return; ready = true;
            URP = GraphicsSettings.currentRenderPipeline != null;
            if (URP) { lit = Shader.Find("Universal Render Pipeline/Lit"); if (lit == null) lit = Shader.Find("Universal Render Pipeline/Simple Lit"); }
            // the Resources materials make sure the shaders (and their instancing variants) end up in a built .exe
            if (lit == null) { Material rl = Resources.Load<Material>("WB_Lit"); if (rl != null && rl.shader != null && rl.shader.isSupported) lit = rl.shader; }
            if (lit == null) lit = Shader.Find("Standard");
            if (lit == null) lit = Shader.Find("Legacy Shaders/Diffuse");
            Material ru = Resources.Load<Material>("WB_Sprites"); if (ru != null && ru.shader != null && ru.shader.isSupported) unlit = ru.shader;
            if (unlit == null) unlit = Shader.Find("Sprites/Default");
            if (unlit == null) unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit == null) unlit = Shader.Find("Unlit/Color");
        }
        public static Shader UnlitTransparent()
        {
            Material r = Resources.Load<Material>("WB_UnlitTransparent"); if (r != null && r.shader != null && r.shader.isSupported) return r.shader;
            return Shader.Find("Unlit/Transparent");
        }
        static string Key(Color c) { Color32 k = c; return k.r + "," + k.g + "," + k.b + "," + k.a; }
        public static Material Lit(Color c, float gloss = 0f, bool inst = false)
        {
            Init(); string key = "L" + Key(c) + "|" + gloss.ToString("0.00") + (inst ? "i" : "");
            Material m; if (cache.TryGetValue(key, out m) && m != null) return m;
            m = new Material(lit); m.color = c;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", gloss);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", gloss);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (gloss <= 0.2f) { m.EnableKeyword("_GLOSSYREFLECTIONS_OFF"); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF"); m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF"); if (m.HasProperty("_SpecularHighlights")) m.SetFloat("_SpecularHighlights", 0f); if (m.HasProperty("_EnvironmentReflections")) m.SetFloat("_EnvironmentReflections", 0f); }
            m.enableInstancing = inst; cache[key] = m; return m;
        }
        // a fresh (not shared) lit material — used where colours change at runtime (wrecks, flags)
        public static Material LitNew(Color c, float gloss = 0f)
        {
            Init(); Material m = new Material(lit); m.color = c;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", gloss);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", gloss);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            return m;
        }
        public static Material LitTex(Texture2D t)
        {
            Init(); Material m = new Material(lit); m.mainTexture = t; m.color = Color.white;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.05f);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.05f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (m.HasProperty("_EnvironmentReflections")) m.SetFloat("_EnvironmentReflections", 0f);
            if (m.HasProperty("_SpecularHighlights")) m.SetFloat("_SpecularHighlights", 0f);
            m.EnableKeyword("_GLOSSYREFLECTIONS_OFF"); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF"); m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            return m;
        }
        // unlit, alpha-blended, vertex-coloured, double sided (Sprites/Default)
        public static Material Unlit(Color c, Texture tex = null, int queue = 3000)
        {
            Init(); string key = "U" + Key(c) + "|" + (tex != null ? TexKey(tex) : "-") + "|" + queue;
            Material m; if (cache.TryGetValue(key, out m) && m != null) return m;
            m = new Material(unlit); m.color = c; if (tex != null) m.mainTexture = tex; m.renderQueue = queue; cache[key] = m; return m;
        }
        public static Material UnlitNew(Color c, Texture tex = null, int queue = 3000)
        {
            Init(); Material m = new Material(unlit); m.color = c; if (tex != null) m.mainTexture = tex; m.renderQueue = queue; return m;
        }
    }

    // a mesh rebuilt every frame (particles, tracers, rings): unlit, vertex-coloured, alpha-blended
    public class DynMesh
    {
        public Mesh mesh = new Mesh();
        public List<Vector3> V = new List<Vector3>(); public List<Color> C = new List<Color>(); public List<Vector2> UV = new List<Vector2>(); public List<int> T = new List<int>();
        public Material mat;
        public DynMesh(Material m) { mat = m; mesh.MarkDynamic(); mesh.indexFormat = IndexFormat.UInt32; }
        public void Clear() { V.Clear(); C.Clear(); UV.Clear(); T.Clear(); }
        public void AddGeo(Geo g, Matrix4x4 m, Color c)
        {
            int o = V.Count;
            for (int i = 0; i < g.v.Count; i++) { V.Add(m.MultiplyPoint3x4(g.v[i])); C.Add(c); UV.Add(new Vector2(0.5f, 0.5f)); }
            for (int i = 0; i < g.t.Count; i++) T.Add(g.t[i] + o);
        }
        public void AddQuad(Vector3 p, Vector3 right, Vector3 up, Color c)
        {
            int o = V.Count;
            V.Add(p - right - up); V.Add(p + right - up); V.Add(p + right + up); V.Add(p - right + up);
            UV.Add(new Vector2(0, 0)); UV.Add(new Vector2(1, 0)); UV.Add(new Vector2(1, 1)); UV.Add(new Vector2(0, 1));
            C.Add(c); C.Add(c); C.Add(c); C.Add(c);
            T.Add(o); T.Add(o + 1); T.Add(o + 2); T.Add(o); T.Add(o + 2); T.Add(o + 3);
        }
        public void Draw()
        {
            mesh.Clear(); mesh.indexFormat = IndexFormat.UInt32;
            if (V.Count == 0) return;
            mesh.SetVertices(V); mesh.SetColors(C); mesh.SetUVs(0, UV); mesh.SetTriangles(T, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
            Graphics.DrawMesh(mesh, Matrix4x4.identity, mat, 0, null, 0, null, ShadowCastingMode.Off, false);
        }
    }

    // many copies of the same small model, drawn with GPU instancing (unit bodies, helmets, guns, …)
    public class InstPart
    {
        public Mesh mesh; public Material[] fixedMats; public bool tint, shadow; public float gloss;
        Dictionary<Color32, List<Matrix4x4>> lists = new Dictionary<Color32, List<Matrix4x4>>();
        static Matrix4x4[] buf = new Matrix4x4[1023];
        public InstPart(ColorModel cm, bool tint, bool shadow, float gloss = 0f)
        {
            mesh = cm.ToMesh(); this.tint = tint; this.shadow = shadow; this.gloss = gloss;
            fixedMats = tint ? null : cm.Materials(true);
        }
        public void Begin() { foreach (var kv in lists) kv.Value.Clear(); }
        public void Add(Matrix4x4 m, Color c)
        {
            Color32 k = tint ? (Color32)c : new Color32(255, 255, 255, 255);
            List<Matrix4x4> l; if (!lists.TryGetValue(k, out l)) { l = new List<Matrix4x4>(); lists[k] = l; }
            l.Add(m);
        }
        public void Add(Matrix4x4 m) { Add(m, Color.white); }
        public void Draw()
        {
            ShadowCastingMode sc = shadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
            foreach (var kv in lists)
            {
                List<Matrix4x4> l = kv.Value; if (l.Count == 0) continue;
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    Material mat = tint ? Mats.Lit(kv.Key, gloss, true) : fixedMats[s];
                    for (int i = 0; i < l.Count; i += 1023)
                    {
                        int n = Mathf.Min(1023, l.Count - i); l.CopyTo(i, buf, 0, n);
                        Graphics.DrawMeshInstanced(mesh, s, mat, buf, n, null, sc, true, 0, null, LightProbeUsage.Off);
                    }
                }
            }
        }
    }
}
