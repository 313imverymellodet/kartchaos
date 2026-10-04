using System.Collections.Generic;
using UnityEngine;

// The battle arena: a walled square of tarmac with cover, item boxes and boost pads.
// Collision is all 2D on the XZ plane (circles and boxes); the 3D models are dressing.
public class Obstacle
{
    public Vector2 C, Half;   // box: centre + half extents
    public float R;           // circle radius (0 = box)
    public bool Circle => R > 0;
}

public class Arena
{
    public const float Half = 26f;   // playable half-size
    public static Arena I;
    public static readonly string[] Names = { "SPEEDWAY", "PIT STOP" };
    public int Map;
    public Transform Root;
    public readonly List<Obstacle> Obs = new List<Obstacle>();
    public readonly List<Vector2> BoxSpots = new List<Vector2>();
    public readonly List<Vector2> Pads = new List<Vector2>();
    public readonly List<Vector3> Spawns = new List<Vector3>();   // x, z, yaw
    public readonly List<Transform> PadVis = new List<Transform>();
    const float RS = Kit.RaceScale;

    public static Arena Build(int map, Transform parent)
    {
        if (I != null && I.Root) Object.Destroy(I.Root.gameObject);
        var a = new Arena { Map = map };
        I = a;
        a.Root = new GameObject("Arena").transform; a.Root.SetParent(parent, false);
        a.Ground();
        a.Perimeter();
        if (map == 0) a.Speedway(); else a.PitStop();
        for (int i = 0; i < 8; i++)
        {
            float ang = (22.5f + i * 45f) * Mathf.Deg2Rad;
            var p = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 20f;
            a.Spawns.Add(new Vector3(p.x, p.y, Mathf.Atan2(-p.x, -p.y) * Mathf.Rad2Deg));
        }
        foreach (var pad in a.Pads) a.PadVis.Add(a.BoostPad(pad));
        return a;
    }

    // ---------------------------------------------------------------- collision
    void Box(Vector2 c, Vector2 half) => Obs.Add(new Obstacle { C = c, Half = half });
    void Circle(Vector2 c, float r) => Obs.Add(new Obstacle { C = c, R = r });

    // Pushes a circle out of walls and obstacles. Returns the speed it hit with (0 = no contact).
    public float Resolve(ref Vector2 pos, ref Vector2 vel, float r, float bounce = 0.35f)
    {
        float impact = 0;
        float lim = Half - r;
        if (pos.x > lim) { pos.x = lim; if (vel.x > 0) { impact = Mathf.Max(impact, vel.x); vel.x = -vel.x * bounce; } }
        if (pos.x < -lim) { pos.x = -lim; if (vel.x < 0) { impact = Mathf.Max(impact, -vel.x); vel.x = -vel.x * bounce; } }
        if (pos.y > lim) { pos.y = lim; if (vel.y > 0) { impact = Mathf.Max(impact, vel.y); vel.y = -vel.y * bounce; } }
        if (pos.y < -lim) { pos.y = -lim; if (vel.y < 0) { impact = Mathf.Max(impact, -vel.y); vel.y = -vel.y * bounce; } }
        foreach (var o in Obs)
        {
            Vector2 n; float pen;
            if (o.Circle)
            {
                var d = pos - o.C; float dist = d.magnitude;
                if (dist >= o.R + r) continue;
                n = dist > 1e-4f ? d / dist : Vector2.up; pen = o.R + r - dist;
            }
            else
            {
                var q = new Vector2(Mathf.Clamp(pos.x, o.C.x - o.Half.x, o.C.x + o.Half.x), Mathf.Clamp(pos.y, o.C.y - o.Half.y, o.C.y + o.Half.y));
                var d = pos - q; float dist = d.magnitude;
                if (dist >= r) continue;
                if (dist > 1e-4f) { n = d / dist; pen = r - dist; }
                else
                {
                    // centre inside the box: leave by the shallowest side
                    var l = pos - o.C;
                    float px = o.Half.x - Mathf.Abs(l.x), py = o.Half.y - Mathf.Abs(l.y);
                    if (px < py) { n = new Vector2(Mathf.Sign(l.x), 0); pen = px + r; } else { n = new Vector2(0, Mathf.Sign(l.y)); pen = py + r; }
                }
            }
            pos += n * pen;
            float vn = Vector2.Dot(vel, n);
            if (vn < 0) { impact = Mathf.Max(impact, -vn); vel -= (1f + bounce) * vn * n; }
        }
        return impact;
    }

    // Is this point (with radius) inside a wall or obstacle?
    public bool Blocked(Vector2 p, float r)
    {
        if (Mathf.Abs(p.x) > Half - r || Mathf.Abs(p.y) > Half - r) return true;
        foreach (var o in Obs)
        {
            if (o.Circle) { if ((p - o.C).sqrMagnitude < (o.R + r) * (o.R + r)) return true; }
            else if (Mathf.Abs(p.x - o.C.x) < o.Half.x + r && Mathf.Abs(p.y - o.C.y) < o.Half.y + r) return true;
        }
        return false;
    }

    // Clear straight line between two points (bots use this to decide whether a shot is worth it).
    public bool LineClear(Vector2 a, Vector2 b, float r = 0.3f)
    {
        float len = (b - a).magnitude; int steps = Mathf.Max(2, Mathf.CeilToInt(len / 1.2f));
        for (int i = 1; i < steps; i++) if (Blocked(Vector2.Lerp(a, b, i / (float)steps), r)) return false;
        return true;
    }

    // ---------------------------------------------------------------- dressing
    static Material Flat(Color c, Texture t = null)
    {
        var m = new Material(Shader.Find("Standard"));
        m.color = c; if (t) m.mainTexture = t;
        m.SetFloat("_Glossiness", 0.08f); m.SetFloat("_Metallic", 0f);
        return m;
    }

    void Ground()
    {
        // tarmac with faint speckle + painted border and centre circle
        int n = 512;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, true);
        var px = new Color[n * n];
        var baseC = Map == 0 ? Kit.Hex("#5B6170") : Kit.Hex("#4D5463");
        var rnd = new System.Random(3 + Map);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float k = (float)rnd.NextDouble() * 0.06f - 0.03f;
                var c = baseC + new Color(k, k, k, 0);
                float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;   // -1..1 across the arena
                float edge = Mathf.Max(Mathf.Abs(u), Mathf.Abs(v));
                if (edge > 0.955f && edge < 0.975f) c = Color.Lerp(c, Color.white, 0.85f);
                float rr = Mathf.Sqrt(u * u + v * v);
                if (Map == 0 && Mathf.Abs(rr - 0.42f) < 0.008f) c = Color.Lerp(c, Color.white, 0.6f);
                if (Map == 1 && (Mathf.Abs(u) < 0.006f || Mathf.Abs(v) < 0.006f) && rr > 0.5f) c = Color.Lerp(c, Kit.Hex("#FFD84A"), 0.8f);
                px[y * n + x] = c;
            }
        tex.SetPixels(px); tex.Apply(true); tex.wrapMode = TextureWrapMode.Clamp; tex.anisoLevel = 4;
        LitQuad("tarmac", Flat(Color.white, tex), Half * 2f + 1f, 0f);
        var grassTex = Kit.Noise(64, Kit.Hex("#6CC24A"), Kit.Hex("#5AAE3C"), 0.2f, 9);
        grassTex.wrapMode = TextureWrapMode.Repeat;
        var gm = Flat(Color.white, grassTex); gm.mainTextureScale = new Vector2(20, 20);
        LitQuad("grass", gm, 160f, -0.05f);
    }

    // Horizontal quad with a lit material so kart shadows land on it.
    void LitQuad(string name, Material m, float size, float y)
    {
        var go = Kit.MeshObject(name, Kit.QuadMesh);
        go.transform.SetParent(Root, false);
        go.transform.localPosition = new Vector3(0, y, 0);
        go.transform.localRotation = Quaternion.Euler(90, 0, 0);
        go.transform.localScale = Vector3.one * size;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = m; mr.receiveShadows = true; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    void Perimeter()
    {
        // low barrier walls all the way round, alternating red and white blocks
        float seg = 1f * RS;
        int count = Mathf.CeilToInt((Half * 2f) / seg);
        for (int side = 0; side < 4; side++)
            for (int i = 0; i < count; i++)
            {
                float t = -Half + seg * (i + 0.5f);
                Vector3 p; float yaw;
                switch (side)
                {
                    case 0: p = new Vector3(t, 0, Half + 0.35f); yaw = 0; break;
                    case 1: p = new Vector3(t, 0, -Half - 0.35f); yaw = 180; break;
                    case 2: p = new Vector3(Half + 0.35f, 0, t); yaw = 90; break;
                    default: p = new Vector3(-Half - 0.35f, 0, t); yaw = -90; break;
                }
                Kit.Spawn("Race/barrierWall", RS, Root, p, yaw);
            }
        // grandstands and trees outside the walls
        for (int i = -3; i <= 3; i++)
        {
            Kit.Spawn("Race/grandStandCovered", RS, Root, new Vector3(i * RS, 0, Half + 5.5f), 180);
            Kit.Spawn("Race/grandStandCovered", RS, Root, new Vector3(i * RS, 0, -Half - 5.5f), 0);
        }
        var rnd = new System.Random(11 + Map);
        for (int i = 0; i < 26; i++)
        {
            float t = -Half - 8 + (float)rnd.NextDouble() * (Half * 2 + 16);
            float off = Half + 4f + (float)rnd.NextDouble() * 7f;
            bool side = rnd.Next(2) == 0;
            var p = side ? new Vector3(off * (rnd.Next(2) == 0 ? 1 : -1), 0, t) : new Vector3(t, 0, off * 1.6f * (rnd.Next(2) == 0 ? 1 : -1));
            Kit.Spawn(rnd.Next(3) == 0 ? "Race/treeSmall" : "Race/treeLarge", RS * (0.9f + (float)rnd.NextDouble() * 0.4f), Root, p, (float)rnd.NextDouble() * 360f);
        }
        foreach (var c in new[] { new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) })
        {
            Kit.Spawn("Race/lightPostLarge", RS * 1.2f, Root, new Vector3(c.x * (Half + 2.5f), 0, c.y * (Half + 2.5f)), c.x * c.y > 0 ? 45 : -45);
            Kit.Spawn("Race/bannerTowerRed", RS, Root, new Vector3(c.x * (Half + 2f), 0, c.y * (Half - 6f)), 0);
        }
        Kit.Spawn("Race/billboard", RS * 1.3f, Root, new Vector3(Half + 6f, 0, 0), -90);
        Kit.Spawn("Race/billboard", RS * 1.3f, Root, new Vector3(-Half - 6f, 0, 0), 90);
    }

    void Speedway()
    {
        // centre: a round grandstand island
        Squash(Kit.Spawn("Race/grandStandRound", 4.6f, Root, Vector3.zero, 0), 0.55f);
        Circle(Vector2.zero, 3.9f);
        // corner cover: closed tents
        foreach (var c in new[] { new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) })
        {
            var p = c * 13f;
            Squash(Kit.Spawn("Race/tentClosed", RS, Root, new Vector3(p.x, 0, p.y), c.x > 0 ? 90 : -90), 0.5f);
            Box(p, new Vector2(3f, 3f));
            Kit.Spawn("Race/flagCheckers", RS * 0.8f, Root, new Vector3(p.x + c.x * 3.6f, 0, p.y + c.y * 3.6f), 45);
        }
        // four short walls make lanes between the tents
        WallRun(new Vector2(0, 17.5f), true, 2); WallRun(new Vector2(0, -17.5f), true, 2);
        WallRun(new Vector2(17.5f, 0), false, 2); WallRun(new Vector2(-17.5f, 0), false, 2);
        // cones for flavour along the wall ends
        foreach (var p in new[] { new Vector2(7, 17.5f), new Vector2(-7, 17.5f), new Vector2(7, -17.5f), new Vector2(-7, -17.5f) })
            Kit.SpawnFit("Car/cone", 0.7f, Root, new Vector3(p.x, 0, p.y), 0, false);

        BoxSpots.AddRange(new[] { new Vector2(0, 8.5f), new Vector2(0, -8.5f), new Vector2(8.5f, 0), new Vector2(-8.5f, 0),
            new Vector2(21, 21), new Vector2(-21, 21), new Vector2(21, -21), new Vector2(-21, -21),
            new Vector2(22, 0), new Vector2(-22, 0), new Vector2(0, 22), new Vector2(0, -22) });
        Pads.AddRange(new[] { new Vector2(10, 21.5f), new Vector2(-10, 21.5f), new Vector2(10, -21.5f), new Vector2(-10, -21.5f) });
    }

    void PitStop()
    {
        // centre: a plus of long tents
        Squash(Kit.Spawn("Race/tentLong", RS, Root, new Vector3(0, 0, 0), 0), 0.45f);
        Squash(Kit.Spawn("Race/tentLong", RS, Root, new Vector3(0, 0, 0), 90), 0.45f);
        Box(Vector2.zero, new Vector2(6f, 3f)); Box(Vector2.zero, new Vector2(3f, 6f));
        // tyre stacks
        foreach (var c in new[] { new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) })
        {
            var p = c * 12.5f;
            // four columns of flat tyres, three high
            for (int c2 = 0; c2 < 4; c2++)
                for (int i = 0; i < 3; i++)
                {
                    var o = new Vector2(c2 % 2 == 0 ? -0.62f : 0.62f, c2 < 2 ? -0.62f : 0.62f);
                    var tire = Kit.SpawnFit("Car/debris-tire", 1.2f, Root, Vector3.zero, 0, true);
                    tire.transform.localRotation = Quaternion.Euler(0, 0, 90);
                    // lying on its side the tyre's footprint swings to -X: shift it back over the spot
                    tire.transform.localPosition = new Vector3(p.x + o.x + 0.6f, 0.19f + i * 0.37f, p.y + o.y);
                }
            Circle(p, 1.5f);
        }
        // long pit walls on the sides
        WallRun(new Vector2(18f, 0), false, 3); WallRun(new Vector2(-18f, 0), false, 3);
        // garages behind the east / west walls for flavour (outside the play area)
        for (int i = -2; i <= 2; i++)
        {
            Kit.Spawn("Race/pitsGarage", RS, Root, new Vector3(Half + 4f, 0, i * RS), -90);
            Kit.Spawn("Race/pitsGarage", RS, Root, new Vector3(-Half - 4f, 0, i * RS), 90);
        }
        BoxSpots.AddRange(new[] { new Vector2(7.5f, 7.5f), new Vector2(-7.5f, 7.5f), new Vector2(7.5f, -7.5f), new Vector2(-7.5f, -7.5f),
            new Vector2(22, 19), new Vector2(-22, 19), new Vector2(22, -19), new Vector2(-22, -19),
            new Vector2(0, 21), new Vector2(0, -21), new Vector2(13, 0), new Vector2(-13, 0) });
        Pads.AddRange(new[] { new Vector2(0, 12f), new Vector2(0, -12f), new Vector2(22, 0), new Vector2(-22, 0) });
    }

    // The camera looks down at an angle: tall cover would hide karts behind it, so keep it low.
    static void Squash(GameObject go, float k) { var s = go.transform.localScale; go.transform.localScale = new Vector3(s.x, s.y * k, s.z); }

    // A straight wall of `n` barrier blocks centred on c, running along X (or Z).
    void WallRun(Vector2 c, bool alongX, int n)
    {
        float seg = 1f * RS;
        for (int i = 0; i < n; i++)
        {
            float t = (i - (n - 1) / 2f) * seg;
            var p = alongX ? new Vector3(c.x + t, 0, c.y) : new Vector3(c.x, 0, c.y + t);
            Kit.Spawn("Race/barrierWall", RS, Root, p, alongX ? 0 : 90);
        }
        float len = n * seg / 2f;
        Box(c, alongX ? new Vector2(len, 0.45f) : new Vector2(0.45f, len));
    }

    // Glowing chevron pad on the floor; driving over it fires a boost.
    Transform BoostPad(Vector2 at)
    {
        int n = 128;
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + .5f) / n - .5f, v = (y + .5f) / n - .5f;
                float r = Mathf.Sqrt(u * u + v * v);
                float a = Mathf.Clamp01((0.5f - r) * 40f);
                // three chevrons pointing "up"
                float ch = Mathf.Repeat(v * 3f + Mathf.Abs(u) * 3f, 1f);
                bool stripe = ch < 0.45f && Mathf.Abs(u) < 0.3f;
                var c = stripe ? Kit.Hex("#FFF27A") : Kit.Hex("#FF8A1F");
                if (r > 0.44f) c = Color.white;
                px[y * n + x] = Kit.A(c, a);
            }
        t.SetPixels(px); t.Apply(); t.wrapMode = TextureWrapMode.Clamp;
        var go = new GameObject("pad");
        go.transform.SetParent(Root, false);
        go.transform.localPosition = new Vector3(at.x, 0.03f, at.y);
        go.transform.localRotation = Quaternion.Euler(90, 0, 0);
        var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = Kit.BuildQuad(3.6f, 1f);
        var mr = go.AddComponent<MeshRenderer>();
        var m = new Material(Kit.UnlitAlpha); m.mainTexture = t; mr.sharedMaterial = m;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go.transform;
    }

    public void Tick(float t)
    {
        // pads pulse and point "forward" in a slow spin so they read as active
        for (int i = 0; i < PadVis.Count; i++)
        {
            var p = PadVis[i]; if (!p) continue;
            float s = 1f + Mathf.Sin(t * 5f + i) * 0.06f;
            p.localScale = new Vector3(s, s, 1);
        }
    }
}
