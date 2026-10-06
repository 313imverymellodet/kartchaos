using UnityEngine;

// One kart. "Local" karts (yours, and bots when this client hosts them) are simulated here;
// everyone else's are smoothed toward the latest network state.
public class Kart : MonoBehaviour
{
    public int Slot; public string Name; public int Vehicle; public Color Tint;
    public bool IsMe, IsBot, Local, Away;
    public Vector2 Pos, Vel; public float Yaw;
    public Item Held; public float RollT;        // item roulette still spinning
    public float SpinT, ShieldT, BoostT, InvulnT;
    public int Score;
    public bool Golden;                  // carrying the most coins: crowned, glowing, and spills more when hit
    public BotBrain Brain;

    public const float R = 0.95f;
    public const float MaxSpeed = 13.5f, BoostSpeed = 22f, Accel = 26f;
    public bool Spinning => SpinT > 0;
    public bool Targetable => !Away && gameObject.activeSelf;
    public bool Vulnerable => SpinT <= 0 && InvulnT <= 0 && !Away;
    public float Speed => Vel.magnitude;
    public Vector2 Fwd => new Vector2(Mathf.Sin(Yaw * Mathf.Deg2Rad), Mathf.Cos(Yaw * Mathf.Deg2Rad));

    Transform body, model, ring, bubble, crown;
    Material ringMat;
    Transform[] wheels = new Transform[0]; bool[] front; Quaternion[] wheelBase;
    Renderer[] rends; Material bubbleMat;
    float lean, pitch, wheelSpin, spinAngle, hop, smokeT, padCd, lastYaw, blinkT;
    float steer;

    // network smoothing
    Vector2 netPos, netVel; float netYaw, netTime; bool hasNet;

    public static Kart Make(int slot, string name, int vehicle, Color tint, Transform parent)
    {
        var go = new GameObject("kart" + slot);
        go.transform.SetParent(parent, false);
        var k = go.AddComponent<Kart>();
        k.Slot = slot; k.Name = name; k.Tint = tint;
        k.body = new GameObject("body").transform; k.body.SetParent(go.transform, false);
        k.SetVehicle(vehicle);
        // coloured ring on the ground so every kart is easy to tell apart from above
        var ringGo = Kit.MeshObject("ring", Kit.QuadMesh); ringGo.transform.SetParent(go.transform, false);
        ringGo.transform.localPosition = new Vector3(0, 0.04f, 0); ringGo.transform.localRotation = Quaternion.Euler(90, 0, 0);
        ringGo.transform.localScale = Vector3.one * 3.0f;
        var rm = new Material(Kit.UnlitAlpha) { mainTexture = Kit.Ring, color = Kit.A(tint, 0.85f) };
        k.ringMat = rm;
        var rr = ringGo.GetComponent<MeshRenderer>(); rr.sharedMaterial = rm; rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        k.ring = ringGo.transform;
        // shield bubble
        var b = Kit.MeshObject("shield", Kit.SphereMesh); b.transform.SetParent(go.transform, false);
        b.transform.localPosition = new Vector3(0, 0.7f, 0); b.transform.localScale = Vector3.one * 2.9f;
        k.bubbleMat = new Material(Kit.UnlitAlpha) { color = Kit.A(Items.Colors[(int)Item.Shield], 0.28f) };
        var br = b.GetComponent<MeshRenderer>(); br.sharedMaterial = k.bubbleMat; br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        k.bubble = b.transform; b.SetActive(false);
        // crown: a gold band of points floating over the leader
        var crownGo = new GameObject("crown"); crownGo.transform.SetParent(go.transform, false);
        var gold = new Material(Shader.Find("Standard")) { color = Kit.Hex("#FFC93C") };
        gold.SetFloat("_Glossiness", 0.8f); gold.SetFloat("_Metallic", 0.5f); gold.EnableKeyword("_EMISSION"); gold.SetColor("_EmissionColor", Kit.Hex("#FFB000") * 0.5f);
        var bandGo = Kit.MeshObject("band", Kit.SphereMesh); bandGo.transform.SetParent(crownGo.transform, false);
        bandGo.transform.localScale = new Vector3(1.1f, 0.35f, 1.1f); bandGo.GetComponent<MeshRenderer>().sharedMaterial = gold;
        for (int i = 0; i < 5; i++)
        {
            float a = i / 5f * Mathf.PI * 2f;
            var pt = Kit.MeshObject("pt", Kit.SphereMesh); pt.transform.SetParent(crownGo.transform, false);
            pt.transform.localPosition = new Vector3(Mathf.Cos(a) * 0.45f, 0.3f, Mathf.Sin(a) * 0.45f);
            pt.transform.localScale = new Vector3(0.22f, 0.55f, 0.22f);
            pt.GetComponent<MeshRenderer>().sharedMaterial = gold;
        }
        k.crown = crownGo.transform; crownGo.SetActive(false);
        return k;
    }

    public void SetVehicle(int v)
    {
        Vehicle = Vehicles.Clamp(v);
        if (model) Destroy(model.gameObject);
        bool kart = Vehicles.All[Vehicle].Model.StartsWith("kart");
        model = Kit.SpawnFit("Car/" + Vehicles.All[Vehicle].Model, kart ? 2.0f : 2.35f, body).transform;
        var ws = new System.Collections.Generic.List<Transform>(); var fr = new System.Collections.Generic.List<bool>();
        foreach (var t in model.GetComponentsInChildren<Transform>())
            if (t.name.StartsWith("wheel")) { ws.Add(t); fr.Add(t.name.Contains("front")); }
        wheels = ws.ToArray(); front = fr.ToArray();
        wheelBase = new Quaternion[wheels.Length];
        for (int i = 0; i < wheels.Length; i++) wheelBase[i] = wheels[i].localRotation;
        rends = body.GetComponentsInChildren<Renderer>();
    }

    public void Place(Vector3 spawn)
    {
        Pos = new Vector2(spawn.x, spawn.y); Yaw = spawn.z; Vel = Vector2.zero;
        netPos = Pos; netVel = Vector2.zero; netYaw = Yaw; hasNet = false;
        SpinT = ShieldT = BoostT = 0; InvulnT = 2f; Held = Item.None; RollT = 0;
        transform.localPosition = new Vector3(Pos.x, 0, Pos.y);
    }

    // ---------------------------------------------------------------- simulation (local karts)
    // input: desired travel direction in world XZ (screen up = +Z), length = throttle 0..1
    public void Drive(Vector2 input, float dt)
    {
        if (SpinT > 0)
        {
            SpinT -= dt;
            Vel *= Mathf.Exp(-2.8f * dt);
            if (SpinT <= 0) InvulnT = 1.3f;
        }
        else
        {
            float mag = Mathf.Min(1f, input.magnitude);
            float throttle = 0;
            if (mag > 0.15f)
            {
                float want = Mathf.Atan2(input.x, input.y) * Mathf.Rad2Deg;
                float diff = Mathf.DeltaAngle(Yaw, want);
                float turn = Mathf.Lerp(260f, 190f, Speed / MaxSpeed);
                float step = Mathf.Clamp(diff, -turn * dt, turn * dt);
                Yaw += step;
                steer = Mathf.Lerp(steer, Mathf.Clamp(diff / 45f, -1, 1), dt * 10f);
                throttle = mag * (Mathf.Abs(diff) > 110f ? 0.35f : 1f);
            }
            else steer = Mathf.Lerp(steer, 0, dt * 10f);
            var f = Fwd;
            bool boosting = BoostT > 0;
            if (boosting) throttle = 1f;
            Vel += f * Accel * (boosting ? 1.6f : 1f) * throttle * dt;
            // split into forward and sideways: sideways bleeds off (grip), forward is capped
            float fwd = Vector2.Dot(Vel, f);
            var lat = Vel - f * fwd;
            lat *= Mathf.Exp(-(boosting ? 3.5f : 6.5f) * dt);
            float cap = boosting ? BoostSpeed : MaxSpeed;
            if (fwd > cap) fwd = Mathf.Lerp(fwd, cap, 1f - Mathf.Exp(-6f * dt));
            if (throttle < 0.05f) fwd *= Mathf.Exp(-1.9f * dt);
            Vel = f * fwd + lat;
            if (lat.magnitude > 3.2f) Smoke(0.45f);
        }
        if (BoostT > 0) BoostT -= dt;
        if (ShieldT > 0) ShieldT -= dt;
        if (InvulnT > 0) InvulnT -= dt;
        if (padCd > 0) padCd -= dt;

        Pos += Vel * dt;
        float impact = Arena.I.Resolve(ref Pos, ref Vel, R);
        if (impact > 6f)
        {
            float near = Game.I.Near(Pos);
            Sfx.I.Bonk(near);
            FX.Sparks(new Vector3(Pos.x, 0.5f, Pos.y) + new Vector3(Fwd.x, 0, Fwd.y) * 0.9f, 6);
            if (IsMe) { Game.I.Shake(0.15f); WebBridge.Vibrate(20); }
        }
        // boost pads
        if (padCd <= 0)
            foreach (var p in Arena.I.Pads)
                if ((p - Pos).sqrMagnitude < 1.9f * 1.9f) { padCd = 1.4f; StartBoost(1.0f, true); break; }
    }

    public void StartBoost(float t, bool pad = false)
    {
        BoostT = Mathf.Max(BoostT, t);
        Sfx.I.Boost(Game.I.Near(Pos) * (pad ? 0.7f : 1f));
        if (IsMe) WebBridge.Vibrate(25);
    }

    public void StartShield() { ShieldT = 6f; Sfx.I.Shield(Game.I.Near(Pos)); }

    // A hit landed. Returns false if the shield ate it.
    public bool TakeHit(Vector2 from)
    {
        if (ShieldT > 0)
        {
            ShieldT = 0; InvulnT = 0.6f;
            Sfx.I.ShieldPop(Game.I.Near(Pos));
            FX.Burst(transform.position + Vector3.up, Items.Colors[(int)Item.Shield], 20);
            return false;
        }
        Spin(from);
        return true;
    }

    public void Spin(Vector2 from)
    {
        if (SpinT > 0) return;
        SpinT = 1.5f; BoostT = 0;
        var away = (Pos - from); if (away.sqrMagnitude < 0.01f) away = -Fwd;
        Vel = away.normalized * 7f + Vel * 0.2f;
        FX.Explosion(transform.position, 1f);
        Sfx.I.Boom(Game.I.Near(Pos)); Sfx.I.Hit(Game.I.Near(Pos) * 0.8f);
        hop = 0.001f;
        if (IsMe) { Game.I.Shake(0.5f); WebBridge.Vibrate(60); }
        else Game.I.Shake(0.25f * Game.I.Near(Pos));
    }

    // ---------------------------------------------------------------- network (remote karts)
    public void NetApply(float x, float z, float yaw, float vx, float vz, int flags)
    {
        netPos = new Vector2(x, z); netVel = new Vector2(vx, vz); netYaw = yaw; netTime = Time.time;
        if (!hasNet || (netPos - Pos).sqrMagnitude > 36f) { Pos = netPos; Yaw = yaw; Vel = netVel; }
        hasNet = true;
        bool spin = (flags & 1) != 0;
        if (spin && SpinT <= 0) SpinT = 1.2f;          // missed the hit message: still show the tumble
        if (!spin && SpinT > 0 && !Local) SpinT = Mathf.Min(SpinT, 0.1f);
        ShieldT = (flags & 2) != 0 ? Mathf.Max(ShieldT, 0.2f) : 0;
        BoostT = (flags & 4) != 0 ? Mathf.Max(BoostT, 0.2f) : 0;
        InvulnT = (flags & 8) != 0 ? Mathf.Max(InvulnT, 0.2f) : 0;
    }
    public int Flags => (SpinT > 0 ? 1 : 0) | (ShieldT > 0 ? 2 : 0) | (BoostT > 0 ? 4 : 0) | (InvulnT > 0 ? 8 : 0);

    public void Follow(float dt)
    {
        if (!hasNet) return;
        float lag = Mathf.Min(Time.time - netTime, 0.35f);
        var predicted = netPos + netVel * lag;
        Pos = Vector2.Lerp(Pos, predicted, 1f - Mathf.Exp(-12f * dt));
        Vel = Vector2.Lerp(Vel, netVel, 1f - Mathf.Exp(-12f * dt));
        Yaw = Mathf.LerpAngle(Yaw, netYaw, 1f - Mathf.Exp(-14f * dt));
        if (SpinT > 0) SpinT -= dt;
        if (BoostT > 0) BoostT -= dt * 0.25f;
        if (ShieldT > 0) ShieldT -= dt * 0.25f;
        if (InvulnT > 0) InvulnT -= dt * 0.25f;
    }

    void Smoke(float a)
    {
        smokeT -= Time.deltaTime;
        if (smokeT > 0) return;
        smokeT = 0.05f;
        var back = transform.position - new Vector3(Fwd.x, 0, Fwd.y) * 0.9f + Vector3.up * 0.2f;
        FX.Smoke(back + Random.insideUnitSphere * 0.3f, 0.35f, a);
    }

    // ---------------------------------------------------------------- visuals
    void LateUpdate()
    {
        float dt = Time.deltaTime;
        transform.localPosition = new Vector3(Pos.x, 0, Pos.y);

        float yawRate = Mathf.DeltaAngle(lastYaw, Yaw) / Mathf.Max(dt, 1e-4f); lastYaw = Yaw;
        lean = Mathf.Lerp(lean, Mathf.Clamp(-yawRate * 0.04f * Mathf.Clamp01(Speed / 6f), -12f, 12f), dt * 8f);
        pitch = Mathf.Lerp(pitch, BoostT > 0 ? -6f : 0f, dt * 6f);
        if (SpinT > 0)
        {
            spinAngle += dt * 900f;
            hop = Mathf.Max(0, hop + dt * 3f);
            float h = Mathf.Sin(Mathf.Min(1f, hop / 1.2f) * Mathf.PI) * 1.4f;
            body.localPosition = new Vector3(0, h, 0);
        }
        else
        {
            spinAngle = Mathf.MoveTowardsAngle(spinAngle, 0, dt * 900f);
            if (Mathf.Abs(Mathf.DeltaAngle(spinAngle, 0)) < 1f) spinAngle = 0;
            hop = 0;
            float bump = Speed > 2f ? Mathf.Abs(Mathf.Sin(Time.time * 18f + Slot)) * 0.04f : 0f;
            body.localPosition = new Vector3(0, bump, 0);
        }
        body.localRotation = Quaternion.Euler(pitch, Yaw + spinAngle, lean);

        wheelSpin += Vector2.Dot(Vel, Fwd) * dt * 120f;
        for (int i = 0; i < wheels.Length; i++)
            if (wheels[i]) wheels[i].localRotation = Quaternion.Euler(0, front[i] ? steer * 25f : 0f, 0) * wheelBase[i] * Quaternion.Euler(wheelSpin, 0, 0);

        bubble.gameObject.SetActive(ShieldT > 0);
        if (ShieldT > 0)
        {
            float s = 2.9f + Mathf.Sin(Time.time * 10f) * 0.08f;
            bubble.localScale = Vector3.one * s;
            bubbleMat.color = Kit.A(Items.Colors[(int)Item.Shield], ShieldT < 1.5f && Mathf.Repeat(Time.time, 0.2f) < 0.1f ? 0.1f : 0.3f);
        }
        if (BoostT > 0 && !Away)
        {
            var back = transform.position - new Vector3(Fwd.x, 0, Fwd.y) * 1.05f + Vector3.up * 0.45f;
            FX.Flame(back, -new Vector3(Fwd.x, 0, Fwd.y));
        }
        // blink while invulnerable
        bool show = !Away && (InvulnT <= 0 || SpinT > 0 || Mathf.Repeat(Time.time, 0.16f) < 0.1f);
        if (rends != null) foreach (var r in rends) if (r) r.enabled = show;
        ring.gameObject.SetActive(!Away);
        crown.gameObject.SetActive(Golden && !Away);
        if (Golden)
        {
            crown.localPosition = new Vector3(0, 2.3f + Mathf.Sin(Time.time * 3f) * 0.12f + body.localPosition.y, 0);
            crown.localRotation = Quaternion.Euler(0, Time.time * 90f, 0);
            if (Random.value < 0.35f) FX.Trail(transform.position + Vector3.up * 0.4f + Random.insideUnitSphere * 0.6f, Kit.Hex("#FFC93C"));
        }
        ringMat.color = Golden ? Kit.A(Kit.Hex("#FFC93C"), 0.95f) : Kit.A(Tint, 0.85f);
        ring.localScale = Vector3.one * (IsMe ? 3.3f + Mathf.Sin(Time.time * 4f) * 0.15f : 2.8f);
    }
}
