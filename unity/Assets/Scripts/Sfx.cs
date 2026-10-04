using UnityEngine;

// All audio is synthesized at boot: zero audio files.
public class Sfx : MonoBehaviour
{
    public static Sfx I;
    const int SR = 22050;
    const float TAU = Mathf.PI * 2f;
    AudioSource[] voices; int next;
    AudioSource music, engine;
    AudioClip launch, homing, boom, mine, shield, shieldPop, boost, pickup, roll, itemReady, hit, bonk, beep, go, win, lose, click, coin, whoosh, score;
    public bool Muted { get; private set; }
    System.Random rnd = new System.Random(7);
    float N() => (float)(rnd.NextDouble() * 2 - 1);
    float lastBonk, lastBoom;

    void Awake()
    {
        I = this;
        voices = new AudioSource[12];
        for (int i = 0; i < voices.Length; i++) { voices[i] = gameObject.AddComponent<AudioSource>(); voices[i].playOnAwake = false; }
        music = gameObject.AddComponent<AudioSource>();
        music.loop = true; music.volume = 0.2f; music.playOnAwake = false;
        engine = gameObject.AddComponent<AudioSource>();
        engine.loop = true; engine.volume = 0f; engine.playOnAwake = false;
        Build();
        music.clip = Music();
    }

    public void SetMuted(bool m) { Muted = m; AudioListener.volume = m ? 0 : 1; }
    public void StartMusic() { if (!music.isPlaying) music.Play(); if (!engine.isPlaying) engine.Play(); }

    // speed01: 0 idle .. 1 top speed (boost > 1)
    public void Engine(float speed01, bool on)
    {
        engine.volume = Mathf.MoveTowards(engine.volume, on ? 0.10f + 0.06f * Mathf.Clamp01(speed01) : 0f, Time.unscaledDeltaTime * 0.6f);
        engine.pitch = Mathf.Lerp(engine.pitch, 0.7f + speed01 * 0.75f, Time.unscaledDeltaTime * 6f);
    }

    void Play(AudioClip c, float vol, float pitch = 1f)
    {
        var s = voices[next]; next = (next + 1) % voices.Length;
        s.pitch = pitch; s.PlayOneShot(c, vol);
    }

    // `near` 0..1 scales other karts' sounds by distance from the camera target
    public void Launch(float near = 1) => Play(launch, 0.45f * near, Random.Range(0.92f, 1.08f));
    public void Homing(float near = 1) => Play(homing, 0.45f * near, Random.Range(0.95f, 1.05f));
    public void Boom(float near = 1) { if (Time.unscaledTime - lastBoom < 0.05f) return; lastBoom = Time.unscaledTime; Play(boom, 0.7f * near, Random.Range(0.85f, 1.1f)); }
    public void Mine(float near = 1) => Play(mine, 0.45f * near);
    public void Shield(float near = 1) => Play(shield, 0.45f * near);
    public void ShieldPop(float near = 1) => Play(shieldPop, 0.55f * near);
    public void Boost(float near = 1) => Play(boost, 0.5f * near);
    public void Pickup() => Play(pickup, 0.45f);
    public void Roll() => Play(roll, 0.3f);
    public void ItemReady() => Play(itemReady, 0.4f);
    public void Hit(float near = 1) => Play(hit, 0.6f * near);
    public void Bonk(float near = 1) { if (Time.unscaledTime - lastBonk < 0.15f) return; lastBonk = Time.unscaledTime; Play(bonk, 0.4f * near, Random.Range(0.9f, 1.1f)); }
    public void Beep(bool isGo) => Play(isGo ? go : beep, 0.5f);
    public void Win() => Play(win, 0.6f);
    public void Lose() => Play(lose, 0.5f);
    public void Click() => Play(click, 0.4f);
    public void Coin() => Play(coin, 0.4f, Random.Range(0.97f, 1.05f));
    public void Whoosh() => Play(whoosh, 0.35f, Random.Range(0.9f, 1.1f));
    public void Score() => Play(score, 0.5f);

    static AudioClip Clip(string n, float[] d) { var c = AudioClip.Create(n, d.Length, 1, SR, false); c.SetData(d, 0); return c; }
    delegate float Gen(float t, float dt);
    static float[] R(float dur, Gen g, bool loop = false)
    {
        int n = (int)(SR * dur); var d = new float[n]; float dt = 1f / SR;
        for (int i = 0; i < n; i++) d[i] = Mathf.Clamp(g(i * dt, dt) * (loop ? 1f : Mathf.Clamp01((n - i) / (SR * 0.008f))), -1, 1);
        return d;
    }

    float[] Arp(float[] notes, float step, float tail, float vol, float bright = 0.25f)
    {
        float ph = 0;
        return R(step * notes.Length + tail, (t, dt) =>
        {
            int k = Mathf.Min((int)(t / step), notes.Length - 1);
            ph += TAU * notes[k] * dt;
            float lt = t - k * step;
            float tri = Mathf.Abs(2f * (ph / TAU % 1f) - 1f) * 2f - 1f;
            return (Mathf.Sin(ph) * 0.75f + tri * bright) * Mathf.Exp(-lt * (k < notes.Length - 1 ? 9 : 3f)) * vol;
        });
    }

    void Build()
    {
        float ph = 0, lp = 0, ph2 = 0;
        // rocket: noisy rising whoosh with a sine core
        launch = Clip("launch", R(0.45f, (t, dt) => { lp += (N() - lp) * 0.35f; ph += TAU * Mathf.Lerp(180, 520, t / 0.45f) * dt; return (lp * 0.6f + Mathf.Sin(ph) * 0.35f) * Mathf.Exp(-t * 5f) * Mathf.Min(1, t * 60); }));
        ph = 0; ph2 = 0;
        homing = Clip("homing", R(0.5f, (t, dt) => { ph += TAU * (700 + Mathf.Sin(t * 40f) * 160f) * dt; ph2 += TAU * 350 * dt; return (Mathf.Sin(ph) * 0.4f + (Mathf.Sin(ph2) > 0 ? 0.12f : -0.12f)) * Mathf.Exp(-t * 4f); }));
        // explosion: low thump + filtered noise tail
        ph = 0; lp = 0; float lp2 = 0;
        boom = Clip("boom", R(0.9f, (t, dt) => { lp += (N() - lp) * Mathf.Lerp(0.5f, 0.05f, t / 0.9f); lp2 += (lp - lp2) * 0.3f; ph += TAU * Mathf.Lerp(110, 35, Mathf.Sqrt(t / 0.9f)) * dt; return (Mathf.Sin(ph) * 0.8f * Mathf.Exp(-t * 6f) + lp2 * 1.6f * Mathf.Exp(-t * 3.2f)); }));
        ph = 0;
        mine = Clip("mine", R(0.25f, (t, dt) => { ph += TAU * (t < 0.1f ? 880 : 660) * dt; return (Mathf.Sin(ph) > 0 ? 0.25f : -0.25f) * Mathf.Exp(-(t % 0.1f) * 25f); }));
        ph = 0; ph2 = 0;
        shield = Clip("shield", R(0.6f, (t, dt) => { ph += TAU * Mathf.Lerp(300, 900, t / 0.6f) * dt; ph2 += TAU * Mathf.Lerp(450, 1350, t / 0.6f) * dt; return (Mathf.Sin(ph) * 0.4f + Mathf.Sin(ph2) * 0.25f) * Mathf.Sin(t / 0.6f * Mathf.PI); }));
        lp = 0; ph = 0;
        shieldPop = Clip("shieldpop", R(0.35f, (t, dt) => { lp += (N() - lp) * 0.6f; ph += TAU * Mathf.Lerp(1400, 300, t / 0.35f) * dt; return (Mathf.Sin(ph) * 0.5f + lp * 0.3f) * Mathf.Exp(-t * 9f); }));
        lp = 0; ph = 0;
        boost = Clip("boost", R(0.8f, (t, dt) => { lp += (N() - lp) * 0.2f; ph += TAU * Mathf.Lerp(90, 260, Mathf.Min(1, t * 3f)) * dt; float saw = (ph / TAU % 1f) * 2f - 1f; return (saw * 0.35f + lp * 0.7f) * Mathf.Min(1, t * 20f) * Mathf.Exp(-t * 2.2f); }));
        pickup = Clip("pickup", Arp(new[] { 659.25f, 880f, 1318.5f }, 0.05f, 0.3f, 0.45f, 0.15f));
        ph = 0;
        roll = Clip("roll", R(0.05f, (t, dt) => { ph += TAU * 1500 * dt; return (Mathf.Sin(ph) > 0 ? 0.3f : -0.3f) * Mathf.Exp(-t * 70); }));
        itemReady = Clip("ready", Arp(new[] { 987.77f, 1318.5f }, 0.06f, 0.3f, 0.45f, 0.2f));
        // being hit: descending wobble + crunch
        ph = 0; lp = 0;
        hit = Clip("hit", R(0.7f, (t, dt) => { lp += (N() - lp) * 0.5f; ph += TAU * (Mathf.Lerp(700, 150, t / 0.7f) + Mathf.Sin(t * 50f) * 60f) * dt; return (Mathf.Sin(ph) * 0.5f + lp * 0.4f * Mathf.Exp(-t * 12f)) * Mathf.Exp(-t * 3f); }));
        ph = 0; lp = 0;
        bonk = Clip("bonk", R(0.22f, (t, dt) => { lp += (N() - lp) * 0.5f; ph += TAU * Mathf.Lerp(260, 90, Mathf.Sqrt(t / 0.22f)) * dt; return (Mathf.Sin(ph) * 0.7f + lp * 0.4f * Mathf.Exp(-t * 40)) * Mathf.Exp(-t * 12); }));
        beep = Clip("beep", R(0.22f, (t, dt) => (Mathf.Sin(TAU * 660 * t) * 0.6f + (Mathf.Sin(TAU * 660 * t) > 0 ? 0.12f : -0.12f)) * Mathf.Min(1, (0.22f - t) * 20)));
        go = Clip("go", R(0.6f, (t, dt) => (Mathf.Sin(TAU * 1320 * t) * 0.55f + (Mathf.Sin(TAU * 1320 * t) > 0 ? 0.12f : -0.12f)) * Mathf.Exp(-t * 3f)));
        win = Clip("win", Arp(new[] { 523.25f, 659.25f, 783.99f, 1046.5f, 783.99f, 1046.5f, 1318.5f, 1568f }, 0.09f, 1.4f, 0.42f));
        lose = Clip("lose", Arp(new[] { 659.25f, 587.33f, 523.25f, 392f }, 0.16f, 0.8f, 0.4f));
        ph = 0;
        click = Clip("click", R(0.04f, (t, dt) => { ph += TAU * 1200 * dt; return Mathf.Sin(ph) * Mathf.Exp(-t * 90) * 0.6f; }));
        coin = Clip("coin", Arp(new[] { 1318.5f, 1975.5f }, 0.06f, 0.25f, 0.4f, 0.2f));
        lp = 0;
        whoosh = Clip("whoosh", R(0.4f, (t, dt) => { lp += (N() - lp) * Mathf.Lerp(0.05f, 0.5f, Mathf.Sin(t / 0.4f * Mathf.PI)); return lp * Mathf.Sin(t / 0.4f * Mathf.PI) * 1.2f; }));
        score = Clip("score", Arp(new[] { 783.99f, 1046.5f, 1318.5f }, 0.06f, 0.35f, 0.45f));

        // engine: a looping two-oscillator buzz; pitch follows speed at runtime
        float e1 = 0, e2 = 0; float elp = 0;
        var eng = R(1f, (t, dt) =>
        {
            e1 += TAU * 82f * dt; e2 += TAU * 123f * dt;
            elp += (N() - elp) * 0.1f;
            float saw = (e1 / TAU % 1f) * 2f - 1f, sq = Mathf.Sin(e2) > 0 ? 1f : -1f;
            return (saw * 0.5f + sq * 0.18f + elp * 0.25f) * (0.8f + 0.2f * Mathf.Sin(TAU * 25f * t));
        }, true);
        engine.clip = Clip("engine", eng);
    }

    // 150 bpm driving chiptune in A minor: Am – F – C – G, octave bass, punchy lead.
    AudioClip Music()
    {
        float bpm = 150f, beat = 60f / bpm;
        int bars = 8; float dur = beat * 4 * bars;
        int n = (int)(SR * dur); var d = new float[n];
        float[][] chords =
        {
            new[] { 220f, 261.63f, 329.63f },
            new[] { 174.61f, 220f, 261.63f },
            new[] { 261.63f, 329.63f, 392f },
            new[] { 196f, 246.94f, 293.66f },
        };
        float[] roots = { 55f, 43.65f, 65.41f, 49f };
        float[] lead = { 659.25f, 783.99f, 880f, 783.99f, 659.25f, 587.33f, 523.25f, 587.33f,
                         659.25f, 659.25f, 783.99f, 987.77f, 880f, 783.99f, 659.25f, 587.33f };
        float hp = 0;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / SR, bt = t / beat;
            int bi = (int)bt, barIdx = bi / 4, bar = barIdx % 4;
            float ib = (bt - bi) * beat;
            float nz = N();
            float kick = Mathf.Sin(TAU * (50 + 130 * Mathf.Exp(-ib * 40)) * ib) * Mathf.Exp(-ib * 9) * 0.55f;
            float snare = (bi % 2 == 1) ? nz * Mathf.Exp(-ib * 18) * 0.25f : 0;
            float e8 = bt * 2; int e8i = (int)e8; float i8 = (e8 - e8i) * beat / 2;
            float e16 = bt * 4; int e16i = (int)e16; float i16 = (e16 - e16i) * beat / 4;
            float hat = (nz - hp) * Mathf.Exp(-i16 * 90) * (e16i % 2 == 1 ? 0.07f : 0.03f); hp = nz;
            float bf = roots[bar] * (e8i % 2 == 1 ? 2f : 1f);
            float bass = ((bf * t) % 1f * 2f - 1f) * Mathf.Exp(-i8 * 6) * 0.14f;
            float pad = 0;
            if (e8i % 2 == 1) foreach (var f in chords[bar]) pad += ((f * t) % 1f < 0.5f ? 1f : -1f) * 0.6f + Mathf.Sin(TAU * f * t) * 0.4f;
            pad *= 0.035f * Mathf.Exp(-i8 * 8);
            float ld = 0;
            if (barIdx >= 2)
            {
                float lf = lead[e8i % 16];
                ld = (((lf * t) % 1f < 0.25f ? 1f : -1f) * 0.5f + Mathf.Sin(TAU * lf * t) * 0.5f) * Mathf.Exp(-i8 * 5) * 0.08f;
            }
            float duck = 1f - 0.45f * Mathf.Exp(-ib * 12);
            d[i] = Mathf.Clamp((kick + snare + hat + (bass + pad + ld) * duck) * 0.8f, -1, 1);
        }
        return Clip("music", d);
    }
}
