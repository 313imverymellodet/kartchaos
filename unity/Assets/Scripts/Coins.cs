using System.Collections.Generic;
using UnityEngine;

// COIN HEIST: coins are the score. They pop up around the arena; a hit spills the victim's coins
// for anyone to grab. The server owns every coin (id + position) so all players see the same ones.
public static class Heist
{
    public const float SpillShare = 0.5f, GoldenShare = 0.75f;   // fraction of carried coins a hit spills
    public const int GoldenMin = 5;                                 // the leader turns golden once they carry this many
    public const float RingStart = 30f;                             // seconds left when the ring starts closing
    public const float RingFrom = 38f, RingTo = 11f;

    // Ring radius for the time left (big enough to cover the corners until the last 30 s).
    public static float RingRadius(float left) => left >= RingStart ? 99f : Mathf.Lerp(RingTo, RingFrom, left / RingStart);

    // The golden kart: the unique leader carrying at least GoldenMin coins (-1 if none).
    public static int Golden(int[] sc, IEnumerable<Kart> karts)
    {
        int best = -1, top = -1; bool tie = false;
        foreach (var k in karts)
        {
            if (k.Away) continue;
            int s = sc[k.Slot];
            if (s > top) { top = s; best = k.Slot; tie = false; } else if (s == top) tie = true;
        }
        return tie || top < GoldenMin ? -1 : best;
    }
}

// One coin on the floor. Spilled coins arc out from the victim before they can be grabbed.
public class Coin : MonoBehaviour
{
    public int Id;
    public Vector2 Pos;
    public bool Pending;                 // we've claimed it; waiting for the server
    float flight, flightT; Vector3 from;
    Transform vis;
    static Mesh disc;
    static Material gold, glowMat;
    public bool Ready => flight <= 0;

    public static Coin Make(int id, Vector2 pos, Vector3? spillFrom, Transform parent)
    {
        var go = new GameObject("coin" + id);
        go.transform.SetParent(parent, false);
        var c = go.AddComponent<Coin>();
        c.Id = id; c.Pos = pos;
        if (!gold) { gold = new Material(Shader.Find("Standard")) { color = Kit.Hex("#FFC93C") }; gold.SetFloat("_Glossiness", 0.75f); gold.SetFloat("_Metallic", 0.4f); gold.EnableKeyword("_EMISSION"); gold.SetColor("_EmissionColor", Kit.Hex("#FFB000") * 0.35f); }
        var m = Kit.MeshObject("disc", Kit.SphereMesh);
        m.transform.SetParent(go.transform, false);
        m.transform.localScale = new Vector3(1.25f, 1.25f, 0.22f);
        // soft gold glow on the floor so coins read from the top-down camera
        if (!glowMat) glowMat = new Material(Kit.UnlitAlpha) { mainTexture = Kit.Glow, color = new Color(1f, 0.8f, 0.2f, 0.55f) };
        var gl = Kit.MeshObject("glow", Kit.QuadMesh); gl.transform.SetParent(go.transform, false);
        gl.transform.localPosition = new Vector3(0, -0.55f, 0); gl.transform.localRotation = Quaternion.Euler(90, 0, 0); gl.transform.localScale = Vector3.one * 2.2f;
        var glr = gl.GetComponent<MeshRenderer>(); glr.sharedMaterial = glowMat; glr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var mr = m.GetComponent<MeshRenderer>(); mr.sharedMaterial = gold;
        c.vis = m.transform;
        if (spillFrom.HasValue) { c.from = spillFrom.Value; c.flight = c.flightT = Random.Range(0.45f, 0.65f); }
        c.Place();
        return c;
    }

    void Place()
    {
        if (flight > 0)
        {
            float k = 1f - flight / flightT;
            var to = new Vector3(Pos.x, 0, Pos.y);
            var p = Vector3.Lerp(from, to, k);
            p.y = 0.6f + Mathf.Sin(k * Mathf.PI) * 2.6f;
            transform.localPosition = p;
        }
        else transform.localPosition = new Vector3(Pos.x, 0.75f + Mathf.Sin(Time.time * 3f + Id) * 0.12f, Pos.y);
    }

    void Update()
    {
        if (flight > 0) flight -= Time.deltaTime;
        Place();
        vis.localRotation = Quaternion.Euler(0, Time.time * 180f + Id * 37f, 0);
        vis.gameObject.SetActive(!Pending);
    }
}

// Visual for the closing ring: a glowing edge plus a dim "storm" band outside it.
public class Ring : MonoBehaviour
{
    LineRenderer edge;
    Mesh band; MeshFilter mf;
    const int Seg = 72;
    public static Ring Make(Transform parent)
    {
        var go = new GameObject("Ring"); go.transform.SetParent(parent, false);
        var r = go.AddComponent<Ring>();
        r.edge = go.AddComponent<LineRenderer>();
        r.edge.loop = true; r.edge.positionCount = Seg; r.edge.useWorldSpace = false; r.edge.widthMultiplier = 0.5f;
        r.edge.sharedMaterial = new Material(Kit.UnlitAlpha); r.edge.startColor = r.edge.endColor = Kit.Hex("#FF4F8B");
        var b = new GameObject("storm"); b.transform.SetParent(go.transform, false);
        r.mf = b.AddComponent<MeshFilter>();
        var mr = b.AddComponent<MeshRenderer>();
        mr.sharedMaterial = new Material(Kit.UnlitAlpha) { color = new Color(0.35f, 0.08f, 0.45f, 0.45f) };
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.band = new Mesh(); r.mf.sharedMesh = r.band;
        go.SetActive(false);
        return r;
    }

    public void Set(float radius)
    {
        bool on = radius < 60f;
        gameObject.SetActive(on);
        if (!on) return;
        var v = new Vector3[Seg * 2]; var tri = new int[Seg * 6];
        for (int i = 0; i < Seg; i++)
        {
            float a = i / (float)Seg * Mathf.PI * 2f;
            var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            edge.SetPosition(i, d * radius + Vector3.up * 0.25f);
            v[i * 2] = d * radius + Vector3.up * 0.08f; v[i * 2 + 1] = d * 70f + Vector3.up * 0.08f;
            int j = (i + 1) % Seg, t = i * 6;
            tri[t] = i * 2; tri[t + 1] = i * 2 + 1; tri[t + 2] = j * 2; tri[t + 3] = j * 2; tri[t + 4] = i * 2 + 1; tri[t + 5] = j * 2 + 1;
        }
        band.Clear(); band.vertices = v; band.triangles = tri; band.RecalculateBounds();
        edge.widthMultiplier = 0.45f + Mathf.Sin(Time.time * 8f) * 0.1f;
    }
}
