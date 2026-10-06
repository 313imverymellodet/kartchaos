using UnityEngine;

// Bot driver: grab a box, hunt someone, fire when the shot lines up, steer round cover.
// Skill 0..1 sets aim error and reaction time; new players face gentler bots.
public class BotBrain
{
    readonly Kart k;
    public float Skill;
    int target = -1; float retarget, fireWait, holdT, stuckT, unstickT, aimErr, wobble;
    Vector2 unstickDir;
    readonly System.Random rnd;

    public BotBrain(Kart kart, float skill, int seed)
    {
        k = kart; Skill = skill; rnd = new System.Random(seed);
        wobble = (float)rnd.NextDouble() * 10f;
    }
    float Rand(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

    public Vector2 Think(float dt)
    {
        var g = Game.I;
        if (k.Spinning) return Vector2.zero;
        wobble += dt;

        // unstick: if pushing but not moving, back off sideways for a moment
        if (unstickT > 0) { unstickT -= dt; return unstickDir; }
        if (k.Speed < 1.2f) { stuckT += dt; if (stuckT > 0.8f) { stuckT = 0; unstickT = 0.6f; var f = k.Fwd; unstickDir = (rnd.Next(2) == 0 ? new Vector2(f.y, -f.x) : new Vector2(-f.y, f.x)) - f * 0.6f; return unstickDir; } }
        else stuckT = 0;

        // pick / refresh a target
        retarget -= dt;
        var tk = target >= 0 ? g.KartAt(target) : null;
        if (retarget <= 0 || tk == null || !tk.Targetable || tk.Spinning)
        {
            retarget = Rand(3f, 6f);
            target = PickTarget();
            tk = target >= 0 ? g.KartAt(target) : null;
        }

        Vector2 goal;
        var coin = g.NearestCoin(k.Pos, k.Held == Item.None ? 11f : 5f);
        if (k.Held == Item.None || k.RollT > 0)
        {
            // go shopping: coins first if one is close, else the nearest live box (weighted toward ones ahead)
            var box = g.NearestBox(k.Pos, k.Fwd);
            if (coin.HasValue && (!box.HasValue || (coin.Value - k.Pos).sqrMagnitude < (box.Value - k.Pos).sqrMagnitude * 0.8f)) goal = coin.Value;
            else goal = box.HasValue ? box.Value : (tk != null ? tk.Pos : Vector2.zero);
        }
        else
        {
            holdT += dt;
            goal = coin.HasValue ? coin.Value : (tk != null ? tk.Pos + tk.Vel * 0.4f : Vector2.zero);   // grab coins on the way
            UseItem(tk, dt);
        }
        // inside the closing ring, keep to the middle
        float rr = Heist.RingRadius(g.Left);
        if (rr < 60f && goal.magnitude > rr - 3f) goal = goal.normalized * (rr - 4f);

        var to = goal - k.Pos;
        if (to.sqrMagnitude < 0.01f) to = k.Fwd;
        var dir = to.normalized;
        // a little human wander so bots don't drive like rails
        float wob = Mathf.Sin(wobble * 1.3f) * (1f - Skill) * 18f;
        dir = Rot(dir, wob);
        dir = Avoid(dir);
        return dir;
    }

    int PickTarget()
    {
        var g = Game.I; int best = -1; float bestScore = 1e9f;
        foreach (var o in g.Karts)
        {
            if (o == k || !o.Targetable) continue;
            float d = (o.Pos - k.Pos).magnitude;
            float s = d + Rand(0f, 12f) - (o.Score > k.Score ? 4f : 0f);   // a bit of grudge against leaders
            if (o.IsMe && Game.I.Save.matches < 3) s += 14f;                  // go easy on brand-new players
            if (o.Golden) s -= 10f;                                           // everyone hunts the crown
            if (s < bestScore) { bestScore = s; best = o.Slot; }
        }
        return best;
    }

    void UseItem(Kart tk, float dt)
    {
        var g = Game.I;
        fireWait -= dt;
        if (fireWait > 0) return;
        var it = k.Held;
        // incoming fire? a shield first
        if (it == Item.Shield)
        {
            if (g.Threatened(k, 9f) || holdT > Rand(4f, 9f)) Fire();
            return;
        }
        if (it == Item.Mine)
        {
            // drop it when someone is on our tail, or along a busy lane after a while
            bool chased = false;
            foreach (var o in g.Karts)
                if (o != k && o.Targetable && Vector2.Dot(o.Pos - k.Pos, k.Fwd) < -1f && (o.Pos - k.Pos).sqrMagnitude < 81f) chased = true;
            if (chased || holdT > Rand(5f, 10f)) Fire();
            return;
        }
        if (tk == null) { if (holdT > 10f) Fire(); return; }
        var to = tk.Pos - k.Pos; float dist = to.magnitude;
        float ang = Vector2.Angle(k.Fwd, to);
        switch (it)
        {
            case Item.Rocket:
            case Item.Triple:
            {
                // lead the target
                float t = dist / Projectile.RocketSpeed;
                var lead = tk.Pos + tk.Vel * t - k.Pos;
                float la = Vector2.Angle(k.Fwd, lead);
                float tol = it == Item.Triple ? 16f : 8f;
                if (dist < 22f && la < tol + aimErr && g.Arena.LineClear(k.Pos, tk.Pos)) Fire();
                break;
            }
            case Item.Homing:
                if (dist < 26f && ang < 60f) Fire();
                break;
            case Item.Boost:
                if (dist < 12f && ang < 14f && g.Arena.LineClear(k.Pos, tk.Pos)) Fire();
                else if (holdT > 9f) Fire();
                break;
        }
    }

    void Fire()
    {
        Game.I.UseItem(k);
        holdT = 0;
        aimErr = Mathf.Lerp(10f, 0f, Skill) * Rand(0.3f, 1f);
        fireWait = Mathf.Lerp(0.9f, 0.25f, Skill) * Rand(0.6f, 1.4f);
    }

    // Steer away from walls and obstacles using two feelers.
    Vector2 Avoid(Vector2 dir)
    {
        var a = Game.I.Arena;
        float look = 2.5f + k.Speed * 0.25f;
        if (!a.Blocked(k.Pos + dir * look, Kart.R * 0.9f)) return dir;
        for (int i = 1; i <= 6; i++)
        {
            float off = i * 25f;
            var l = Rot(dir, off); var r = Rot(dir, -off);
            bool lb = a.Blocked(k.Pos + l * look, Kart.R * 0.9f), rb = a.Blocked(k.Pos + r * look, Kart.R * 0.9f);
            if (!lb && (rb || ((int)wobble % 2 == 0))) return l;
            if (!rb) return r;
        }
        return -dir;
    }

    static Vector2 Rot(Vector2 v, float deg) { float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r); return new Vector2(c * v.x - s * v.y, s * v.x + c * v.y); }

    static readonly string[] names =
    {
        "ZOOMER", "TURBO TOM", "SKIDMARK", "NITRO NAT", "BUMPER", "DRIFTY", "VROOM", "PIXEL PETE", "CRASHLEY", "HONK",
        "SPARKY", "LUGNUT", "MAX DASH", "ROCKETTE", "WHEELIE", "GRIDLOCK", "TOAST", "BLITZ", "PISTON", "ZIGZAG",
        "CHAOS CAT", "BEEP BEEP", "RALLY", "SPIN CITY", "DONUT", "BOLT", "SCOOT", "FUMES", "COG", "PEDAL",
    };
    public static string RandomName(System.Random r) => names[r.Next(names.Length)];
}
