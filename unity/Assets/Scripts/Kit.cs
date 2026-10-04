using System.Collections.Generic;
using UnityEngine;

// Asset + procedural-content helpers shared by everything.
public static class Kit
{
    public const float RaceScale = 6f;        // racing kit is authored on a 1-unit road grid; karts are ~2m long

    static readonly Dictionary<Material, Material> matFix = new Dictionary<Material, Material>();
    static readonly Dictionary<string, Material> matByLook = new Dictionary<string, Material>();
    static Font font;
    // Lilita One (SIL OFL 1.1): the arcade-wide display face. It is already heavy, so UI text never asks Unity for faux bold.
    public static Font Font => font ? font : (font = Resources.Load<Font>("Fonts/LilitaOne") ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

    static Material unlitAlpha;
    public static Material UnlitAlpha => unlitAlpha ? unlitAlpha : (unlitAlpha = Resources.Load<Material>("UnlitAlpha"));

    // Spawns a Kenney model normalised so its footprint is centred on the origin and it sits on y=0.
    // The racing kit's FBX files import at a different unit scale than its 1-unit road grid; measure it once.
    static float raceUnit = -1f;
    public static float RaceUnit
    {
        get
        {
            if (raceUnit > 0) return raceUnit;
            var src = Resources.Load<GameObject>("Kenney/Race/barrierWall");
            raceUnit = 1f;
            if (src)
            {
                var g = Object.Instantiate(src);
                float w = WorldBounds(g).size.x;   // the wall block is exactly 1 grid unit long
                Object.DestroyImmediate(g);
                if (w > 0.0001f) raceUnit = 1f / w;
            }
            return raceUnit;
        }
    }

    public static GameObject Spawn(string path, float scale, Transform parent = null, Vector3 pos = default, float yaw = 0f, bool collider = false, float colliderPad = 0f)
    {
        if (path.StartsWith("Race/")) scale *= RaceUnit;
        var src = Resources.Load<GameObject>("Kenney/" + path);
        var root = new GameObject(path.Substring(path.LastIndexOf('/') + 1));
        if (!src)
        {
            // a missing model must never abort a whole course build
            Debug.LogWarning("Kit.Spawn: missing model " + path);
            root.transform.SetParent(parent, false); root.transform.localPosition = pos;
            return root;
        }
        var inst = Object.Instantiate(src, root.transform, false);
        inst.name = "model";
        var b = WorldBounds(inst);
        // Adjust (not overwrite): some kits bake their pivot offset into the model root's position.
        inst.transform.localPosition += new Vector3(-b.center.x, -b.min.y, -b.center.z);
        FixMaterials(root);
        root.transform.SetParent(parent, false);
        root.transform.localPosition = pos;
        root.transform.localRotation = Quaternion.Euler(0, yaw, 0);
        root.transform.localScale = Vector3.one * scale;
        return root;
    }

    // Spawns a model scaled so its length along Z (or its longest side) is `size` metres.
    public static GameObject SpawnFit(string path, float size, Transform parent = null, Vector3 pos = default, float yaw = 0f, bool byZ = true)
    {
        var go = Spawn(path, 1f, parent, pos, yaw);
        var b = WorldBounds(go);
        float len = byZ ? b.size.z : Mathf.Max(b.size.x, b.size.z);
        if (len > 0.001f) go.transform.localScale = Vector3.one * (size / len);
        return go;
    }

    // Bounds from mesh data + current transforms. Renderer.bounds can be stale on the frame a
    // prefab is instantiated in a player build, which mis-centred the Space-kit models.
    public static Bounds WorldBounds(GameObject go)
    {
        bool any = false;
        var b = new Bounds(go.transform.position, Vector3.zero);
        void Add(Matrix4x4 m, Bounds lb)
        {
            var c = lb.center; var e = lb.extents;
            for (int i = 0; i < 8; i++)
            {
                var p = m.MultiplyPoint3x4(c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z));
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
            }
        }
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            if (mf.sharedMesh) Add(mf.transform.localToWorldMatrix, mf.sharedMesh.bounds);
        foreach (var sr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            if (sr.sharedMesh) Add(sr.transform.localToWorldMatrix, sr.sharedMesh.bounds);
        return b;
    }

    // Kenney FBX materials import glossy; matte them down for a soft toy look.
    public static void FixMaterials(GameObject go)
    {
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (!m) continue;
                if (!matFix.TryGetValue(m, out var fixedM))
                {
                    // Kenney kits give every model its own copy of the same "colormap" material:
                    // collapse identical looks into one so static batching can merge them.
                    string look = m.shader.name + "|" + (m.mainTexture ? m.mainTexture.GetHashCode() : 0) + "|" + m.color;
                    if (matByLook.TryGetValue(look, out fixedM)) { matFix[m] = fixedM; mats[i] = fixedM; continue; }
                    fixedM = new Material(m);
                    matByLook[look] = fixedM;
                    if (fixedM.HasProperty("_Glossiness")) fixedM.SetFloat("_Glossiness", m.name.Contains("glass") ? 0.6f : 0.18f);
                    if (fixedM.HasProperty("_Metallic")) fixedM.SetFloat("_Metallic", 0f);
                    matFix[m] = fixedM;
                }
                mats[i] = fixedM;
            }
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            r.receiveShadows = true;
        }
    }

    public static void Tint(GameObject go, Color c)
    {
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            var mats = r.materials;
            foreach (var m in mats) if (m.HasProperty("_Color")) m.color = m.color * c;
            r.materials = mats;
        }
    }

    // ---------------- procedural textures ----------------
    static Texture2D NewTex(int w, int h, bool mips = false)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, mips);
        t.wrapMode = TextureWrapMode.Clamp;
        t.filterMode = FilterMode.Bilinear;
        return t;
    }

    static Texture2D ring, disc, glow, arrow, cash, rounded;

    public static Texture2D Ring => ring ? ring : (ring = MakeRing());
    static Texture2D MakeRing()
    {
        int n = 128; var t = NewTex(n, n); var px = new Color[n * n];
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
            float edge = Mathf.Clamp01((1f - d) * 40f) * Mathf.Clamp01((d - 0.84f) * 40f);
            float fill = Mathf.Clamp01((0.84f - d) * 40f) * 0.18f;
            px[y * n + x] = new Color(1, 1, 1, Mathf.Max(edge, fill));
        }
        t.SetPixels(px); t.Apply(); return t;
    }

    public static Texture2D Disc => disc ? disc : (disc = MakeDisc());
    static Texture2D MakeDisc()
    {
        int n = 64; var t = NewTex(n, n); var px = new Color[n * n];
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
            px[y * n + x] = new Color(1, 1, 1, Mathf.Clamp01((1f - d) * 30f));
        }
        t.SetPixels(px); t.Apply(); return t;
    }

    public static Texture2D Glow => glow ? glow : (glow = MakeGlow());
    static Texture2D MakeGlow()
    {
        int n = 64; var t = NewTex(n, n); var px = new Color[n * n];
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
            float a = Mathf.Clamp01(1f - d); px[y * n + x] = new Color(1, 1, 1, a * a);
        }
        t.SetPixels(px); t.Apply(); return t;
    }

    public static Texture2D Arrow => arrow ? arrow : (arrow = MakeArrow());
    static Texture2D MakeArrow()
    {
        int n = 64; var t = NewTex(n, n); var px = new Color[n * n];
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            float u = (x + .5f) / n - 0.5f, v = (y + .5f) / n;
            bool head = v < 0.55f && Mathf.Abs(u) < (0.55f - v) * 0.85f;
            bool shaft = v >= 0.5f && v < 0.95f && Mathf.Abs(u) < 0.14f;
            px[y * n + x] = new Color(1, 1, 1, head || shaft ? 1 : 0);
        }
        t.SetPixels(px); t.Apply(); return t;
    }

    public static Texture2D Cash => cash ? cash : (cash = MakeCash());
    static Texture2D MakeCash()
    {
        int w = 64, h = 32; var t = NewTex(w, h); var px = new Color[w * h];
        Color g = new Color(0.36f, 0.78f, 0.36f), dg = new Color(0.2f, 0.55f, 0.24f);
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            bool border = x < 3 || x > w - 4 || y < 3 || y > h - 4;
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(w / 2f, h / 2f));
            px[y * w + x] = border || (d > 7 && d < 10) ? dg : g;
        }
        t.SetPixels(px); t.Apply(); return t;
    }

    public static Texture2D Rounded => rounded ? rounded : (rounded = MakeRounded());
    static Texture2D MakeRounded()
    {
        int n = 64, r = 30; var t = NewTex(n, n); var px = new Color[n * n];
        float inner = n / 2f - r;
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            float dx = Mathf.Max(Mathf.Abs(x + .5f - n / 2f) - inner, 0), dy = Mathf.Max(Mathf.Abs(y + .5f - n / 2f) - inner, 0);
            px[y * n + x] = new Color(1, 1, 1, Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + .5f));
        }
        t.SetPixels(px); t.Apply(); return t;
    }

    static Sprite roundedSprite;
    public static Sprite RoundedSprite => roundedSprite ? roundedSprite :
        (roundedSprite = Sprite.Create(Rounded, new Rect(0, 0, 64, 64), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(31, 31, 31, 31)));

    static Sprite cashSprite;
    public static Sprite CashSprite => cashSprite ? cashSprite : (cashSprite = Sprite.Create(Cash, new Rect(0, 0, 64, 32), new Vector2(.5f, .5f), 100));

    public static Texture2D Noise(int n, Color a, Color b, float scale, int seed, float craters = 0)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, true);
        t.wrapMode = TextureWrapMode.Repeat;
        var rng = new System.Random(seed);
        var cr = new List<Vector3>();
        for (int i = 0; i < craters; i++) cr.Add(new Vector3((float)rng.NextDouble() * n, (float)rng.NextDouble() * n, 3 + (float)rng.NextDouble() * 10));
        var px = new Color[n * n];
        float ox = seed * 13.7f;
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            float v = Mathf.PerlinNoise(ox + x * scale, y * scale) * 0.7f + Mathf.PerlinNoise(ox + x * scale * 4f, y * scale * 4f) * 0.3f;
            foreach (var c in cr)
            {
                float dx = Mathf.Abs(x - c.x); dx = Mathf.Min(dx, n - dx);
                float dy = Mathf.Abs(y - c.y); dy = Mathf.Min(dy, n - dy);
                float d = Mathf.Sqrt(dx * dx + dy * dy) / c.z;
                if (d < 1f) v -= 0.25f * (1f - d);
                else if (d < 1.3f) v += 0.15f * (1.3f - d) / 0.3f;
            }
            px[y * n + x] = Color.Lerp(a, b, Mathf.Clamp01(v));
        }
        t.SetPixels(px); t.Apply(true); return t;
    }

    public static Texture2D Tiles(int n, Color a, Color b, Color line)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, true);
        t.wrapMode = TextureWrapMode.Repeat; t.filterMode = FilterMode.Trilinear; t.anisoLevel = 4;
        var px = new Color[n * n];
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            bool checker = (x < n / 2) ^ (y < n / 2);
            bool l = x % (n / 2) < 2 || y % (n / 2) < 2;
            px[y * n + x] = l ? line : checker ? a : b;
        }
        t.SetPixels(px); t.Apply(true); return t;
    }

    // Flat quad lying on the floor (y-up), using an unlit transparent material.
    public static MeshRenderer FloorQuad(string name, Texture tex, Color c, float size, Transform parent, Vector3 pos, float y = 0.02f)
    {
        var go = MeshObject(name, QuadMesh);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos + Vector3.up * y;
        go.transform.localRotation = Quaternion.Euler(90, 0, 0);
        go.transform.localScale = Vector3.one * size;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = new Material(UnlitAlpha) { mainTexture = tex, color = c };
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return mr;
    }

    // ---------------- procedural meshes (this build has no physics module, so no CreatePrimitive)
    public static GameObject MeshObject(string name, Mesh mesh)
    {
        var go = new GameObject(name);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>();
        return go;
    }

    static Mesh quad, sphere;
    public static Mesh QuadMesh => quad ? quad : (quad = BuildQuad(1f, 1f));

    public static Mesh BuildQuad(float size, float uvScale)
    {
        var m = new Mesh();
        float h = size * 0.5f;
        m.vertices = new[] { new Vector3(-h, -h, 0), new Vector3(h, -h, 0), new Vector3(-h, h, 0), new Vector3(h, h, 0) };
        m.uv = new[] { new Vector2(0, 0), new Vector2(uvScale, 0), new Vector2(0, uvScale), new Vector2(uvScale, uvScale) };
        m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
        m.RecalculateBounds();
        return m;
    }

    // Smooth UV sphere (radius 0.5); v runs 0 at the bottom to 1 at the top.
    static Mesh uvSphere;
    public static Mesh UVSphere
    {
        get
        {
            if (uvSphere) return uvSphere;
            int lat = 14, lon = 24;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int i = 0; i <= lat; i++)
                for (int j = 0; j <= lon; j++)
                {
                    float a = Mathf.PI * i / lat, b = 2 * Mathf.PI * j / lon;
                    v.Add(new Vector3(Mathf.Sin(a) * Mathf.Cos(b), Mathf.Cos(a), Mathf.Sin(a) * Mathf.Sin(b)) * 0.5f);
                    uv.Add(new Vector2((float)j / lon, 1f - (float)i / lat));
                }
            for (int i = 0; i < lat; i++)
                for (int j = 0; j < lon; j++)
                {
                    int a = i * (lon + 1) + j, b = a + lon + 1;
                    t.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
                }
            uvSphere = new Mesh { vertices = v.ToArray(), uv = uv.ToArray(), triangles = t.ToArray() };
            uvSphere.RecalculateNormals(); uvSphere.RecalculateBounds();
            return uvSphere;
        }
    }

    public static Mesh SphereMesh
    {
        get
        {
            if (sphere) return sphere;
            int lat = 6, lon = 10;
            var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i <= lat; i++)
                for (int j = 0; j <= lon; j++)
                {
                    float a = Mathf.PI * i / lat, b = 2 * Mathf.PI * j / lon;
                    v.Add(new Vector3(Mathf.Sin(a) * Mathf.Cos(b), Mathf.Cos(a), Mathf.Sin(a) * Mathf.Sin(b)) * 0.5f);
                }
            for (int i = 0; i < lat; i++)
                for (int j = 0; j < lon; j++)
                {
                    int a = i * (lon + 1) + j, b = a + lon + 1;
                    t.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
                }
            sphere = new Mesh { vertices = v.ToArray(), triangles = t.ToArray() };
            sphere.RecalculateNormals(); sphere.RecalculateBounds();
            return sphere;
        }
    }

    public static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }
    public static Color A(Color c, float a) { c.a = a; return c; }

    public static string Money(double v)
    {
        if (v < 1000) return "$" + ((long)v).ToString();
        string[] s = { "K", "M", "B", "T", "Qa" };
        int i = -1;
        while (v >= 1000 && i < s.Length - 1) { v /= 1000; i++; }
        return "$" + (v < 10 ? v.ToString("0.##") : v < 100 ? v.ToString("0.#") : v.ToString("0")) + s[i];
    }

    public static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3) + c1 * Mathf.Pow(t - 1f, 2);
    }

    // ---------------- legibility scrims for menus over busy 3D scenes
    static Sprite fadeUp, fadeDown;
    static Sprite FadeSprite(bool opaqueAtTop)
    {
        var t = new Texture2D(1, 64, TextureFormat.RGBA32, false);
        for (int y = 0; y < 64; y++)
        {
            float k = opaqueAtTop ? y / 63f : 1f - y / 63f;
            t.SetPixel(0, y, new Color(1, 1, 1, k * k * (3f - 2f * k)));
        }
        t.wrapMode = TextureWrapMode.Clamp; t.Apply();
        return Sprite.Create(t, new Rect(0, 0, 1, 64), new Vector2(.5f, .5f));
    }
    // A full-width band fading from `c` at the top (or bottom) screen edge to clear, `height` reference units tall.
    public static void Scrim(Transform parent, bool atTop, float height, Color c)
    {
        var go = new GameObject("scrim", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0, atTop ? 1 : 0); rt.anchorMax = new Vector2(1, atTop ? 1 : 0);
        rt.pivot = new Vector2(.5f, atTop ? 1 : 0); rt.sizeDelta = new Vector2(0, height); rt.anchoredPosition = Vector2.zero;
        var img = go.AddComponent<UnityEngine.UI.Image>();
        img.sprite = atTop ? (fadeDown ? fadeDown : fadeDown = FadeSprite(true)) : (fadeUp ? fadeUp : fadeUp = FadeSprite(false));
        img.color = c; img.raycastTarget = false;
        go.transform.SetAsFirstSibling();
    }

}

// Buttons dip while held: on a touch screen that is the only feedback that the tap registered.
public class Press : MonoBehaviour, UnityEngine.EventSystems.IPointerDownHandler, UnityEngine.EventSystems.IPointerUpHandler, UnityEngine.EventSystems.IPointerExitHandler
{
    public float Sink;              // reference units to drop (buttons drawn over a separate shadow)
    Vector3 scale; Vector2 pos; bool down;
    public void OnPointerDown(UnityEngine.EventSystems.PointerEventData e)
    {
        if (down) return;
        down = true;
        var rt = (RectTransform)transform;
        scale = rt.localScale; pos = rt.anchoredPosition;
        if (Sink > 0) rt.anchoredPosition = pos - new Vector2(0, Sink); else rt.localScale = scale * 0.95f;
    }
    public void OnPointerUp(UnityEngine.EventSystems.PointerEventData e) => Release();
    public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e) => Release();
    void OnDisable() => Release();
    void Release()
    {
        if (!down) return;
        down = false;
        var rt = (RectTransform)transform;
        if (Sink > 0) rt.anchoredPosition = pos; else rt.localScale = scale;
    }
}
