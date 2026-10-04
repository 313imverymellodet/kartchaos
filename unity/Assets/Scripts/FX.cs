using System.Collections.Generic;
using UnityEngine;

// Lightweight pooled sprite particles, always facing the camera: dust, explosions, sparks, smoke, confetti.
public static class FX
{
    class P { public SpriteRenderer sr; public Vector3 v; public float life, max, size, grow, grav, spin, drag; public Color c; public bool on; }
    static readonly List<P> pool = new List<P>();
    static Sprite disc, glow, square;
    static Transform root;

    public static void Init(Transform parent)
    {
        root = new GameObject("FX").transform; root.SetParent(parent, false);
        disc = Sprite.Create(Kit.Disc, new Rect(0, 0, Kit.Disc.width, Kit.Disc.height), new Vector2(.5f, .5f), Kit.Disc.width);
        glow = Sprite.Create(Kit.Glow, new Rect(0, 0, Kit.Glow.width, Kit.Glow.height), new Vector2(.5f, .5f), Kit.Glow.width);
        var t = new Texture2D(4, 4); var px = new Color[16]; for (int i = 0; i < 16; i++) px[i] = Color.white; t.SetPixels(px); t.Apply();
        square = Sprite.Create(t, new Rect(0, 0, 4, 4), new Vector2(.5f, .5f), 4);
        for (int i = 0; i < 420; i++)
        {
            var sr = new GameObject("p").AddComponent<SpriteRenderer>();
            sr.sharedMaterial = Kit.UnlitAlpha;
            sr.transform.SetParent(root, false); sr.gameObject.SetActive(false);
            pool.Add(new P { sr = sr });
        }
    }

    static P Get() { foreach (var p in pool) if (!p.on) return p; return null; }

    static void Emit(Vector3 at, Sprite s, Color c, Vector3 v, float life, float size, float grow, float grav, float drag)
    {
        var p = Get(); if (p == null) return;
        p.on = true; p.sr.gameObject.SetActive(true); p.sr.sprite = s; p.sr.color = c;
        p.sr.transform.position = at; p.v = v; p.life = p.max = life; p.size = size; p.grow = grow; p.grav = grav; p.drag = drag; p.c = c;
        p.spin = Random.Range(-400f, 400f);
        p.sr.transform.localScale = Vector3.one * size;
    }

    // dust ring at the feet (jumps, landings)
    public static void Puff(Vector3 at, int n)
    {
        for (int i = 0; i < n; i++)
        {
            float a = i / (float)n * Mathf.PI * 2f + Random.value;
            var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            Emit(at + d * 0.3f + Vector3.up * 0.15f, disc, new Color(1, 1, 1, 0.75f), d * Random.Range(1.5f, 3f) + Vector3.up * 0.6f, Random.Range(0.3f, 0.5f), Random.Range(0.25f, 0.4f), 0.6f, 0, 3f);
        }
    }

    public static void Burst(Vector3 at, Color c, int n)
    {
        for (int i = 0; i < n; i++)
            Emit(at, glow, c, Random.onUnitSphere * Random.Range(2f, 6f), Random.Range(0.4f, 0.8f), Random.Range(0.4f, 0.8f), -0.4f, 3f, 1.5f);
    }

    static readonly Color[] party = { Kit.Hex("#FF5A6E"), Kit.Hex("#FFD84A"), Kit.Hex("#5BE37D"), Kit.Hex("#3FA9F5"), Kit.Hex("#B57BFF"), Color.white };
    public static void Confetti(Vector3 at, int n)
    {
        for (int i = 0; i < n; i++)
        {
            var v = new Vector3(Random.Range(-1f, 1f), Random.Range(0.6f, 1.4f), Random.Range(-1f, 1f)) * Random.Range(4f, 9f);
            Emit(at, square, party[i % party.Length], v, Random.Range(1.2f, 2f), Random.Range(0.12f, 0.2f), 0, 9f, 1.2f);
        }
    }

    // ---------------------------------------------------------------- kart battle effects
    static readonly Color Fire1 = Kit.Hex("#FFD84A"), Fire2 = Kit.Hex("#FF7A2E"), Fire3 = Kit.Hex("#FF3B3B");
    public static void Explosion(Vector3 at, float scale = 1f)
    {
        for (int i = 0; i < 18; i++)
            Emit(at + Vector3.up * 0.4f, glow, i % 3 == 0 ? Fire1 : i % 3 == 1 ? Fire2 : Fire3, Random.onUnitSphere * Random.Range(3f, 8f) * scale + Vector3.up * 2f,
                Random.Range(0.35f, 0.6f), Random.Range(0.9f, 1.6f) * scale, -1.2f, 2f, 3f);
        for (int i = 0; i < 10; i++)
            Emit(at + Vector3.up * 0.6f, disc, new Color(0.25f, 0.22f, 0.25f, 0.55f), Random.onUnitSphere * Random.Range(1f, 3f) + Vector3.up * 2.5f,
                Random.Range(0.8f, 1.3f), Random.Range(0.6f, 1.0f) * scale, 1.2f, -0.6f, 1.5f);
        for (int i = 0; i < 12; i++)
        {
            var v = new Vector3(Random.Range(-1f, 1f), Random.Range(0.8f, 1.6f), Random.Range(-1f, 1f)) * Random.Range(4f, 9f);
            Emit(at + Vector3.up * 0.5f, square, Color.Lerp(Fire1, Color.white, Random.value * 0.5f), v, Random.Range(0.5f, 0.9f), 0.14f, 0, 14f, 0.8f);
        }
    }
    public static void Sparks(Vector3 at, int n)
    {
        for (int i = 0; i < n; i++)
        {
            var v = new Vector3(Random.Range(-1f, 1f), Random.Range(0.3f, 1.2f), Random.Range(-1f, 1f)) * Random.Range(3f, 7f);
            Emit(at, square, Color.Lerp(Fire1, Color.white, Random.value * 0.6f), v, Random.Range(0.25f, 0.45f), 0.1f, 0, 12f, 1f);
        }
    }
    // exhaust / drift smoke behind a kart
    public static void Smoke(Vector3 at, float size, float alpha)
    {
        Emit(at, disc, new Color(0.95f, 0.95f, 0.95f, alpha), new Vector3(Random.Range(-0.4f, 0.4f), Random.Range(0.6f, 1.2f), Random.Range(-0.4f, 0.4f)),
            Random.Range(0.4f, 0.7f), size, size * 1.6f, -0.5f, 2f);
    }
    public static void Flame(Vector3 at, Vector3 back)
    {
        Emit(at, glow, Random.value < 0.5f ? Fire1 : Fire2, back * Random.Range(3f, 6f) + Random.insideUnitSphere * 0.6f, Random.Range(0.12f, 0.22f), Random.Range(0.45f, 0.7f), -1.5f, 0, 2f);
    }
    public static void Trail(Vector3 at, Color c)
    {
        Emit(at, glow, c, Random.insideUnitSphere * 0.3f, Random.Range(0.18f, 0.3f), Random.Range(0.35f, 0.5f), -0.8f, 0, 1f);
        Emit(at, disc, new Color(0.85f, 0.85f, 0.85f, 0.45f), Random.insideUnitSphere * 0.3f + Vector3.up * 0.4f, Random.Range(0.4f, 0.7f), 0.3f, 0.7f, -0.3f, 1.5f);
    }
    public static void Pickup(Vector3 at, Color c)
    {
        for (int i = 0; i < 16; i++)
        {
            float a = i / 16f * Mathf.PI * 2f;
            Emit(at, glow, c, new Vector3(Mathf.Cos(a), 0.6f, Mathf.Sin(a)) * Random.Range(4f, 6f), Random.Range(0.3f, 0.5f), 0.5f, -0.6f, 4f, 2.5f);
        }
    }

    public static void Tick(float dt)
    {
        var cam = Camera.main; if (!cam) return;
        var rot = cam.transform.rotation;
        foreach (var p in pool)
        {
            if (!p.on) continue;
            p.life -= dt;
            if (p.life <= 0) { p.on = false; p.sr.gameObject.SetActive(false); continue; }
            p.v.y -= p.grav * dt;
            p.v *= Mathf.Exp(-p.drag * dt);
            var tr = p.sr.transform;
            tr.position += p.v * dt;
            p.size = Mathf.Max(0.01f, p.size + p.grow * dt);
            tr.localScale = Vector3.one * p.size;
            tr.rotation = rot * Quaternion.Euler(0, 0, p.spin * (p.max - p.life));
            p.sr.color = Kit.A(p.c, p.c.a * Mathf.Clamp01(p.life / p.max * 2f));
        }
    }
}
