using System.Collections.Generic;
using UnityEngine;

public enum Item { None, Rocket, Triple, Homing, Mine, Shield, Boost }

public static class Items
{
    public static readonly string[] Names = { "", "ROCKET", "TRIPLE", "HOMING", "MINE", "SHIELD", "BOOST" };
    public static readonly Color[] Colors =
    {
        Color.white, Kit.Hex("#FF4B4B"), Kit.Hex("#FF8A1F"), Kit.Hex("#B57BFF"), Kit.Hex("#FF3B6B"), Kit.Hex("#35D6FF"), Kit.Hex("#FFD84A"),
    };

    // Better items for karts at the back of the scoreboard, so nobody is out of it for long.
    // rank01: 0 = leading .. 1 = last
    public static Item Roll(float rank01, System.Random rnd)
    {
        float[] lead = { 0, 30, 10, 10, 25, 15, 10 };
        float[] back = { 0, 18, 25, 27, 5, 10, 15 };
        float total = 0; var w = new float[7];
        for (int i = 1; i < 7; i++) { w[i] = Mathf.Lerp(lead[i], back[i], rank01); total += w[i]; }
        float r = (float)rnd.NextDouble() * total;
        for (int i = 1; i < 7; i++) { r -= w[i]; if (r <= 0) return (Item)i; }
        return Item.Rocket;
    }

    // ---------------------------------------------------------------- icons (procedural, no image files)
    static readonly Dictionary<Item, Sprite> icons = new Dictionary<Item, Sprite>();
    public static Sprite Icon(Item it)
    {
        if (icons.TryGetValue(it, out var s)) return s;
        int n = 128; var t = new Texture2D(n, n, TextureFormat.RGBA32, false); var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var p = new Vector2((x + .5f) / n - .5f, (y + .5f) / n - .5f);   // -.5..+.5
                float d = Shape(it, p);                                            // signed distance (<0 inside)
                float a = Mathf.Clamp01(0.5f - d * n);
                float o = Mathf.Clamp01(0.5f - (d - 0.035f) * n);                  // dark outline
                var c = Color.Lerp(new Color(0.1f, 0.06f, 0.25f, o), Detail(it, p), a);
                c.a = Mathf.Max(a, o);
                px[y * n + x] = c;
            }
        t.SetPixels(px); t.Apply();
        s = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(.5f, .5f));
        icons[it] = s; return s;
    }

    static float Capsule(Vector2 p, Vector2 a, Vector2 b, float r)
    {
        var pa = p - a; var ba = b - a; float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / ba.sqrMagnitude);
        return (pa - ba * h).magnitude - r;
    }
    static float Circ(Vector2 p, Vector2 c, float r) => (p - c).magnitude - r;
    static float Rocket(Vector2 p, float s, Vector2 off)
    {
        p = (p - off) / s;
        float body = Capsule(p, new Vector2(0, -0.22f), new Vector2(0, 0.22f), 0.11f);
        float fins = Mathf.Max(Mathf.Abs(p.x) - 0.2f, Mathf.Abs(p.y + 0.24f) - 0.08f);
        return Mathf.Min(body, fins) * s;
    }
    static float Shape(Item it, Vector2 p)
    {
        switch (it)
        {
            case Item.Rocket: return Rocket(Rot(p, -35), 1f, Vector2.zero);
            case Item.Triple: return Mathf.Min(Rocket(p, 0.62f, new Vector2(0, 0.08f)), Mathf.Min(Rocket(p, 0.55f, new Vector2(-0.26f, -0.1f)), Rocket(p, 0.55f, new Vector2(0.26f, -0.1f))));
            case Item.Homing: return Mathf.Min(Rocket(Rot(p, -35), 0.8f, new Vector2(-0.05f, -0.03f)), Mathf.Abs(Circ(p, Vector2.zero, 0.36f)) - 0.035f);
            case Item.Mine:
            {
                float c = Circ(p, Vector2.zero, 0.25f);
                float spikes = 9f;
                for (int i = 0; i < 8; i++) { float a = i * Mathf.PI / 4f; spikes = Mathf.Min(spikes, Capsule(p, Vector2.zero, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.38f, 0.045f)); }
                return Mathf.Min(c, spikes);
            }
            case Item.Shield:
            {
                // rounded shield: top half box, bottom a point
                var q = new Vector2(Mathf.Abs(p.x), p.y);
                float top = Mathf.Max(q.x - 0.3f, Mathf.Abs(q.y - 0.12f) - 0.22f);
                float tip = Vector2.Dot(q - new Vector2(0, -0.4f), new Vector2(0.83f, -0.55f).normalized * -1f);
                float bottom = Mathf.Max(Mathf.Max(q.x - 0.3f, q.y - 0.1f), -tip);
                return Mathf.Min(top, bottom) - 0.02f;
            }
            case Item.Boost:
            {
                // lightning bolt as two slanted bars
                float a = Capsule(p, new Vector2(0.14f, 0.36f), new Vector2(-0.1f, 0.0f), 0.08f);
                float b = Capsule(p, new Vector2(-0.1f, 0.02f), new Vector2(0.12f, 0.0f), 0.08f);
                float c = Capsule(p, new Vector2(0.12f, 0.0f), new Vector2(-0.12f, -0.36f), 0.08f);
                return Mathf.Min(a, Mathf.Min(b, c));
            }
        }
        return 1f;
    }
    static Color Detail(Item it, Vector2 p)
    {
        var c = Colors[(int)it];
        float shade = 0.85f + 0.3f * Mathf.Clamp01(0.5f - p.y);   // lit from the top
        return new Color(Mathf.Min(1, c.r * shade), Mathf.Min(1, c.g * shade), Mathf.Min(1, c.b * shade), 1);
    }
    static Vector2 Rot(Vector2 p, float deg) { float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r); return new Vector2(c * p.x - s * p.y, s * p.x + c * p.y); }

    // ---------------------------------------------------------------- meshes
    static Mesh cube;
    // Unit cube with every face mapped to the full texture (for the "?" boxes).
    public static Mesh Cube
    {
        get
        {
            if (cube) return cube;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tr = new List<int>(); var nm = new List<Vector3>();
            Vector3[] dirs = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
            foreach (var d in dirs)
            {
                var u = Mathf.Abs(Vector3.Dot(d, Vector3.up)) > 0.9f ? Vector3.right : Vector3.Cross(Vector3.up, d);
                var w = Vector3.Cross(d, u);
                int b = v.Count;
                v.Add((d - u - w) * 0.5f); v.Add((d + u - w) * 0.5f); v.Add((d + u + w) * 0.5f); v.Add((d - u + w) * 0.5f);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
                for (int i = 0; i < 4; i++) nm.Add(d);
                tr.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
            }
            cube = new Mesh(); cube.SetVertices(v); cube.SetUVs(0, uv); cube.SetNormals(nm); cube.SetTriangles(tr, 0); cube.RecalculateBounds();
            return cube;
        }
    }

    static Texture2D qTex;
    public static Texture2D QuestionTex
    {
        get
        {
            if (qTex) return qTex;
            int n = 64; qTex = new Texture2D(n, n, TextureFormat.RGBA32, true); var px = new Color[n * n];
            // 5x7 pixel "?" scaled up, on a white face with a darker rim
            string[] g = { ".###.", "#...#", "....#", "...#.", "..#..", ".....", "..#.." };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + .5f) / n, w = (y + .5f) / n;
                    bool rim = u < 0.08f || u > 0.92f || w < 0.08f || w > 0.92f;
                    int gx = Mathf.FloorToInt((u - 0.2f) / 0.6f * 5), gy = 6 - Mathf.FloorToInt((w - 0.15f) / 0.7f * 7);
                    bool on = gx >= 0 && gx < 5 && gy >= 0 && gy < 7 && g[gy][gx] == '#';
                    px[y * n + x] = on ? Color.white : rim ? new Color(1, 1, 1, 1) * 0.55f + new Color(0, 0, 0, 0.45f) : new Color(1, 1, 1, 1) * 0.82f + new Color(0, 0, 0, 0.18f);
                }
            qTex.SetPixels(px); qTex.Apply(true); qTex.filterMode = FilterMode.Bilinear;
            return qTex;
        }
    }
}

// The "?" crate. Taken by whoever drives through it empty-handed; back after a few seconds.
public class ItemBox : MonoBehaviour
{
    public int Index; public Vector2 Pos; public float Respawn;
    public bool Active => Respawn <= 0;
    Transform cubeT; Material mat;
    float seed;

    public static ItemBox Make(int index, Vector2 pos, Transform parent)
    {
        var go = new GameObject("box" + index);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(pos.x, 0, pos.y);
        var b = go.AddComponent<ItemBox>(); b.Index = index; b.Pos = pos; b.seed = index * 0.7f;
        var c = new GameObject("cube"); c.transform.SetParent(go.transform, false);
        c.AddComponent<MeshFilter>().sharedMesh = Items.Cube;
        var mr = c.AddComponent<MeshRenderer>();
        b.mat = new Material(Shader.Find("Standard")); b.mat.mainTexture = Items.QuestionTex; b.mat.SetFloat("_Glossiness", 0.4f);
        b.mat.EnableKeyword("_EMISSION"); b.mat.SetTexture("_EmissionMap", Items.QuestionTex);
        mr.sharedMaterial = b.mat;
        b.cubeT = c.transform; c.transform.localScale = Vector3.one * 1.15f;
        return b;
    }

    public void Take(float time = 5f)
    {
        if (!Active) return;
        Respawn = time;
        FX.Pickup(transform.position + Vector3.up, Color.HSVToRGB(Mathf.Repeat(Time.time * 0.3f + seed, 1f), 0.6f, 1f));
        cubeT.gameObject.SetActive(false);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (Respawn > 0)
        {
            Respawn -= dt;
            if (Respawn <= 0) { cubeT.gameObject.SetActive(true); cubeT.localScale = Vector3.zero; }
            return;
        }
        float t = Time.time + seed;
        cubeT.localPosition = new Vector3(0, 1.0f + Mathf.Sin(t * 2.5f) * 0.15f, 0);
        cubeT.localRotation = Quaternion.Euler(20, t * 70f, 15);
        cubeT.localScale = Vector3.MoveTowards(cubeT.localScale, Vector3.one * 1.15f, dt * 3f);
        var c = Color.HSVToRGB(Mathf.Repeat(t * 0.25f, 1f), 0.45f, 1f);
        mat.color = c; mat.SetColor("_EmissionColor", c * 0.85f);
    }
}

// Rockets, homing missiles and mines. Every client simulates every projectile; only the
// victim's own client (or the bot host) decides that a hit happened.
public class Projectile : MonoBehaviour
{
    public int Pid, Owner; public Item Type;
    public Vector2 Pos, Vel;
    public float Age, Life;
    public int Target = -1;
    Transform vis; Renderer blink; Color col;

    public const float RocketSpeed = 30f, HomingSpeed = 21f;
    public float HitRadius => Type == Item.Mine ? 1.25f : 0.55f;
    public bool Armed => Type != Item.Mine || Age > 0.6f;

    public static Projectile Make(int pid, int owner, Item type, Vector2 pos, float yaw, Transform parent)
    {
        var go = new GameObject("proj" + pid);
        go.transform.SetParent(parent, false);
        var p = go.AddComponent<Projectile>();
        p.Pid = pid; p.Owner = owner; p.Type = type; p.Pos = pos;
        var dir = new Vector2(Mathf.Sin(yaw * Mathf.Deg2Rad), Mathf.Cos(yaw * Mathf.Deg2Rad));
        p.col = Items.Colors[(int)type];
        if (type == Item.Mine)
        {
            p.Life = 25f;
            var m = Kit.MeshObject("mine", Kit.SphereMesh); m.transform.SetParent(go.transform, false);
            m.transform.localScale = new Vector3(1.0f, 0.35f, 1.0f); m.transform.localPosition = new Vector3(0, 0.15f, 0);
            m.GetComponent<MeshRenderer>().sharedMaterial = Mat(Kit.Hex("#3A3550"));
            var light = Kit.MeshObject("light", Kit.SphereMesh); light.transform.SetParent(go.transform, false);
            light.transform.localScale = Vector3.one * 0.32f; light.transform.localPosition = new Vector3(0, 0.38f, 0);
            p.blink = light.GetComponent<MeshRenderer>(); p.blink.sharedMaterial = Mat(Items.Colors[(int)Item.Mine], true);
            for (int i = 0; i < 6; i++)
            {
                var sp = Kit.MeshObject("spike", Kit.SphereMesh); sp.transform.SetParent(go.transform, false);
                float a = i * 60f * Mathf.Deg2Rad;
                sp.transform.localPosition = new Vector3(Mathf.Cos(a) * 0.5f, 0.15f, Mathf.Sin(a) * 0.5f); sp.transform.localScale = Vector3.one * 0.18f;
                sp.GetComponent<MeshRenderer>().sharedMaterial = Mat(Kit.Hex("#8A8FA8"));
            }
        }
        else
        {
            p.Life = type == Item.Homing ? 4f : 2.2f;
            p.Vel = dir * (type == Item.Homing ? HomingSpeed : RocketSpeed);
            p.vis = new GameObject("rocket").transform; p.vis.SetParent(go.transform, false);
            p.vis.localPosition = new Vector3(0, 0.75f, 0);
            var body = Kit.MeshObject("body", Kit.SphereMesh); body.transform.SetParent(p.vis, false);
            body.transform.localScale = new Vector3(0.36f, 0.36f, 1.1f);
            body.GetComponent<MeshRenderer>().sharedMaterial = Mat(p.col, true);
            var tip = Kit.MeshObject("tip", Kit.SphereMesh); tip.transform.SetParent(p.vis, false);
            tip.transform.localScale = new Vector3(0.26f, 0.26f, 0.4f); tip.transform.localPosition = new Vector3(0, 0, 0.42f);
            tip.GetComponent<MeshRenderer>().sharedMaterial = Mat(Color.white);
            for (int i = 0; i < 2; i++)
            {
                var fin = Kit.MeshObject("fin", Kit.SphereMesh); fin.transform.SetParent(p.vis, false);
                fin.transform.localScale = i == 0 ? new Vector3(0.75f, 0.06f, 0.3f) : new Vector3(0.06f, 0.6f, 0.3f);
                fin.transform.localPosition = new Vector3(0, 0, -0.4f);
                fin.GetComponent<MeshRenderer>().sharedMaterial = Mat(Kit.Hex("#2E2A44"));
            }
        }
        go.transform.localPosition = new Vector3(pos.x, 0, pos.y);
        p.Face();
        return p;
    }

    static readonly Dictionary<Color, Material> mats = new Dictionary<Color, Material>();
    static Material Mat(Color c, bool glow = false)
    {
        if (mats.TryGetValue(glow ? c * 0.999f : c, out var m)) return m;
        m = new Material(Shader.Find("Standard")); m.color = c; m.SetFloat("_Glossiness", 0.45f);
        if (glow) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * 0.6f); }
        mats[glow ? c * 0.999f : c] = m; return m;
    }

    void Face()
    {
        if (vis && Vel.sqrMagnitude > 0.01f) vis.localRotation = Quaternion.LookRotation(new Vector3(Vel.x, 0, Vel.y));
    }

    // Returns false when the projectile is spent (hit a wall or ran out of time).
    public bool Step(float dt)
    {
        Age += dt;
        if (Age > Life) return false;
        if (Type == Item.Mine)
        {
            if (blink) blink.enabled = Armed ? Mathf.Repeat(Age, 0.6f) < 0.3f : true;
            transform.localScale = Vector3.one * Mathf.Min(1f, Age * 4f);
            return true;
        }
        if (Type == Item.Homing && Age > 0.2f)
        {
            // steer toward the target picked at launch (or the nearest kart ahead)
            var g = Game.I;
            if (Target < 0 || g.KartAt(Target) == null || !g.KartAt(Target).Targetable) Target = g.HomingTarget(Pos, Vel, Owner);
            var tk = Target >= 0 ? g.KartAt(Target) : null;
            if (tk != null)
            {
                var want = (tk.Pos - Pos).normalized;
                float cur = Mathf.Atan2(Vel.x, Vel.y) * Mathf.Rad2Deg, des = Mathf.Atan2(want.x, want.y) * Mathf.Rad2Deg;
                float nd = Mathf.MoveTowardsAngle(cur, des, 170f * dt) * Mathf.Deg2Rad;
                Vel = new Vector2(Mathf.Sin(nd), Mathf.Cos(nd)) * HomingSpeed;
            }
        }
        Pos += Vel * dt;
        if (Arena.I.Blocked(Pos, 0.25f)) return false;
        transform.localPosition = new Vector3(Pos.x, 0, Pos.y);
        Face();
        if (vis) vis.localRotation *= Quaternion.Euler(0, 0, 720f * dt);
        if (Random.value < 0.7f && vis) FX.Trail(vis.position - vis.forward * 0.5f, col);
        return true;
    }
}

// Every vehicle is cosmetic: same speed and handling, so unlocks never buy an edge.
public static class Vehicles
{
    public struct V { public string Model, Name; public int Cost; public V(string m, string n, int c) { Model = m; Name = n; Cost = c; } }
    public static readonly V[] All =
    {
        new V("kart-oobi", "OOBI", 0),
        new V("kart-oodi", "OODI", 150),
        new V("kart-ooli", "OOLI", 300),
        new V("kart-oopi", "OOPI", 500),
        new V("kart-oozi", "OOZI", 800),
        new V("taxi", "TAXI", 1000),
        new V("tractor", "TRACTOR", 1200),
        new V("police", "POLICE", 1500),
        new V("sedan-sports", "SPORTS", 1600),
        new V("garbage-truck", "GARBAGE", 1800),
        new V("ambulance", "AMBULANCE", 2000),
        new V("race", "RACER", 2200),
        new V("firetruck", "FIRE TRUCK", 2500),
        new V("race-future", "FUTURE", 3500),
    };
    public static int Clamp(int i) => Mathf.Clamp(i, 0, All.Length - 1);
}
