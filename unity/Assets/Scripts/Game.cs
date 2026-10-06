using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class SaveData
{
    public int coins, vehicle, matches, wins, bestHits, tut;
    public long owned = 1;            // bit per vehicle; OOBI is free
    public bool muted;
    public string name = "";
}

// KART CHAOS: top-down kart battle. 2.5-minute matches, most hits wins; bots fill every empty seat.
public class Game : MonoBehaviour
{
    public static Game I;
    public enum St { Menu, Garage, Finding, Countdown, Play, Results }
    public St State = St.Menu;
    public SaveData Save = new SaveData();
    public Camera Cam;
    public Arena Arena => Arena.I;
    public static bool Dev;

    public readonly List<Kart> Karts = new List<Kart>();
    readonly Kart[] bySlot = new Kart[8];
    readonly List<ItemBox> boxes = new List<ItemBox>();
    readonly Dictionary<int, Projectile> projs = new Dictionary<int, Projectile>();
    readonly Dictionary<int, Coin> coins = new Dictionary<int, Coin>();
    Transform coinRoot; Ring ring; bool ringWarned;
    public int GoldenSlot = -1;
    public Kart Me;
    public bool Online, IsHost, Attract;
    public string RoomCode = "";
    public float Left;                 // seconds left in the match
    public int[] Scores = new int[8];
    LocalRoom local;
    Transform world, kartRoot, projRoot, boxRoot;
    float countT, sendT, shake, connectT, findT;
    int pidSeq;
    NetMsg pendingMatch; float pendingAt;
    bool socketOpen, wantPrivate;
    string joinCode = "";
    readonly System.Random rnd = new System.Random();
    int hitsThisMatch, firstSeen;
    Light sun;
    Vector3 camFocus; float camOrbit;
    bool sentAway;

    public static readonly Color[] SlotColors =
    {
        Kit.Hex("#FFD84A"), Kit.Hex("#FF4F8B"), Kit.Hex("#35D6FF"), Kit.Hex("#7CF06B"),
        Kit.Hex("#B57BFF"), Kit.Hex("#FF8A1F"), Kit.Hex("#FFFFFF"), Kit.Hex("#4BE3C1"),
    };

    void Awake()
    {
        I = this;
        Application.targetFrameRate = 60;
        var url = Application.absoluteURL ?? "";
        Dev = url.Contains("dev=1") && (url.Contains("://localhost") || url.Contains("://127.0.0.1"));
        try { var s = PlayerPrefs.GetString("kc_save", ""); if (!string.IsNullOrEmpty(s)) Save = JsonUtility.FromJson<SaveData>(s) ?? new SaveData(); } catch { Save = new SaveData(); }
        Save.vehicle = Vehicles.Clamp(Save.vehicle); Save.owned |= 1;

        gameObject.AddComponent<Sfx>();
        Sfx.I.SetMuted(Save.muted);
        new GameObject("WebBridge").AddComponent<WebBridge>();
        if (string.IsNullOrEmpty(Save.name)) Save.name = WebBridge.SavedName();
        if (string.IsNullOrEmpty(Save.name)) Save.name = "RACER " + rnd.Next(10, 100);

        Cam = Camera.main;
        Cam.fieldOfView = 38f; Cam.nearClipPlane = 0.5f; Cam.farClipPlane = 220f;
        Cam.clearFlags = CameraClearFlags.SolidColor; Cam.backgroundColor = Kit.Hex("#8FD3FF");
        sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional; sun.intensity = 1.15f; sun.color = Kit.Hex("#FFF4E0");
        sun.transform.rotation = Quaternion.Euler(55, -35, 0);
        sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.55f;
        QualitySettings.shadowDistance = 60f;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = Kit.Hex("#DDEBFF"); RenderSettings.ambientEquatorColor = Kit.Hex("#C7D2E0"); RenderSettings.ambientGroundColor = Kit.Hex("#8A8F99");

        world = new GameObject("World").transform;
        kartRoot = new GameObject("Karts").transform; kartRoot.SetParent(world, false);
        projRoot = new GameObject("Projectiles").transform; projRoot.SetParent(world, false);
        boxRoot = new GameObject("Boxes").transform; boxRoot.SetParent(world, false);
        coinRoot = new GameObject("Coins").transform; coinRoot.SetParent(world, false);
        ring = Ring.Make(world);
        FX.Init(world);
        new GameObject("UI").AddComponent<UI>().Init();
        joinCode = WebBridge.RoomCode().ToUpperInvariant();
        GoMenu();
        WebBridge.Ready();
        WebBridge.Event("boot");
    }

    public void Persist() { PlayerPrefs.SetString("kc_save", JsonUtility.ToJson(Save)); PlayerPrefs.Save(); }

    // ------------------------------------------------------------------ menu / attract mode
    public void GoMenu()
    {
        LeaveNet();
        State = St.Menu;
        StartAttract();
        UI.I.ShowMenu();
        WebBridge.Gameplay(false);
    }

    // Bots-only match running behind the menu.
    void StartAttract()
    {
        Attract = true; Online = false; IsHost = true; local = null; Me = null;
        var karts = new List<NetKart>();
        var used = new HashSet<string>();
        for (int i = 0; i < 6; i++) { string n; do n = BotBrain.RandomName(rnd); while (!used.Add(n)); karts.Add(new NetKart { s = i, n = n, k = rnd.Next(Vehicles.All.Length), b = 1 }); }
        Setup(new NetMsg { t = "match", you = -1, host = -1, map = rnd.Next(Arena.Names.Length), left = 999999, karts = karts.ToArray(), sc = new int[8] }, true);
    }

    public void OpenGarage()
    {
        State = St.Garage;
        UI.I.ShowGarage();
        WebBridge.Event("garage_open");
    }

    // ------------------------------------------------------------------ connecting
    public void Play(bool makePrivate = false)
    {
        WebBridge.Event(makePrivate ? "play_private" : "play_click");
        wantPrivate = makePrivate;
        WebBridge.I.Midgame(Connect);
    }

    void Connect()
    {
        Attract = false;
        State = St.Finding; findT = 0;
        UI.I.ShowFinding(wantPrivate ? "CREATING ROOM..." : joinCode.Length > 0 ? "JOINING ROOM " + joinCode + "..." : "FINDING PLAYERS...");
        if (!WebBridge.HasPage) { GoOffline("no page"); return; }
        connectT = 0; socketOpen = false;
        WebBridge.NetOpen(NetOut.Hello(Save.name, Save.vehicle));
    }

    public void CancelFind() { WebBridge.Event("find_cancel"); GoMenu(); }

    // page -> unity
    public void OnNetOpen(string _)
    {
        socketOpen = true;
        if (State != St.Finding) return;
        if (joinCode.Length > 0) { Send(NetOut.Join(joinCode)); joinCode = ""; }
        else Send(NetOut.Simple(wantPrivate ? "create" : "quick"));
    }
    public void OnNetClosed(string _)
    {
        bool was = socketOpen; socketOpen = false;
        if (!Online && State == St.Finding) { GoOffline("closed"); return; }
        if (Online && was)
        {
            Online = false;
            WebBridge.Event("net_dropped");
            if (State == St.Play || State == St.Countdown) { UI.I.Toast("CONNECTION LOST - CONTINUING OFFLINE"); ContinueOffline(); }
            else if (State == St.Results) { /* PLAY AGAIN will start offline */ }
        }
    }
    public void OnNet(string json)
    {
        NetMsg m;
        try { m = JsonUtility.FromJson<NetMsg>(json); } catch { return; }
        if (m != null) OnMsg(m);
    }

    void GoOffline(string why)
    {
        WebBridge.Event("net_offline");
        LeaveNet();
        Online = false; Attract = false;
        local = new LocalRoom();
        local.Start(Save.name, Save.vehicle);
    }

    // Mid-match drop: keep racing, take over everyone else as bots.
    void ContinueOffline()
    {
        local = null; IsHost = true;
        foreach (var k in Karts)
            if (k != Me && !k.Local) { k.Local = true; k.IsBot = true; k.Brain = new BotBrain(k, BotSkill(), k.Slot * 31 + 7); k.Away = false; }
        offlineEnd = true;
    }
    bool offlineEnd;

    void LeaveNet()
    {
        if (socketOpen || Online) { WebBridge.NetClose(); }
        socketOpen = false; Online = false; local = null; pendingMatch = null; offlineEnd = false; sentAway = false;
    }

    public void Send(string json)
    {
        if (local != null) { local.Send(json); return; }
        if (Online || socketOpen) WebBridge.NetSend(json);
    }

    // ------------------------------------------------------------------ messages
    public void OnMsg(NetMsg m)
    {
        switch (m.t)
        {
            case "hello": UI.I.SetOnline(m.online); break;
            case "match":
                if (local == null) { Online = true; RoomCode = m.code ?? ""; }
                if (State == St.Results || (WebBridge.I.InAd && State != St.Finding)) { pendingMatch = m; pendingAt = Time.unscaledTime; return; }
                if (State == St.Finding || State == St.Play || State == St.Countdown) StartMatch(m);
                break;
            case "roster": if (!Attract) ApplyRoster(m); break;
            case "s":
                if (m.d == null) return;
                for (int j = 0; j + 6 < m.d.Length; j += 7)
                {
                    int s = (int)m.d[j]; var k = KartAt(s);
                    if (k != null && !k.Local) k.NetApply(m.d[j + 1], m.d[j + 2], m.d[j + 3], m.d[j + 4], m.d[j + 5], (int)m.d[j + 6]);
                }
                break;
            case "f": RemoteFire(m); break;
            case "h": OnHit(m); break;
            case "b": if (m.i >= 0 && m.i < boxes.Count) boxes[m.i].Take(); break;
            case "cs": AddCoins(m.c, null); break;
            case "cg": CoinTaken(m); break;
            case "end": EndMatch(m.sc); break;
            case "error":
                UI.I.Toast(m.msg ?? "SOMETHING WENT WRONG");
                if (State == St.Finding) { joinCode = ""; GoMenu(); }
                break;
        }
    }

    void StartMatch(NetMsg m)
    {
        Attract = false;
        Setup(m, false);
        bool fresh = m.left > (LocalRoom.MatchSecs - 5f) * 1000f;
        hitsThisMatch = 0;
        UI.I.ShowHud(true);
        if (fresh) { State = St.Countdown; countT = 3.99f; }
        else { State = St.Play; UI.I.Banner("JOINED!", "match in progress - get hitting"); }
        WebBridge.Gameplay(true);
        Sfx.I.StartMusic();
        int humans = 0; foreach (var k in m.karts) if (k.b == 0) humans++;
        WebBridge.Event(Online ? "match_online" : "match_offline", humans);
        if (Online && humans > 1) WebBridge.Event("match_with_humans", humans);
        if (Save.tut < 3) UI.I.Coach(Save.tut);
    }

    // Builds the arena and every kart from a match snapshot.
    void Setup(NetMsg m, bool attract)
    {
        foreach (var k in Karts) if (k) Destroy(k.gameObject);
        Karts.Clear(); Array.Clear(bySlot, 0, 8);
        foreach (var p in projs.Values) if (p) Destroy(p.gameObject);
        projs.Clear();
        foreach (var b in boxes) if (b) Destroy(b.gameObject);
        boxes.Clear();
        foreach (var c in coins.Values) if (c) Destroy(c.gameObject);
        coins.Clear(); ringWarned = false; GoldenSlot = -1;
        Arena.Build(m.map, world);
        for (int i = 0; i < Arena.BoxSpots.Count; i++) boxes.Add(ItemBox.Make(i, Arena.BoxSpots[i], boxRoot));
        Scores = m.sc != null && m.sc.Length >= 8 ? (int[])m.sc.Clone() : new int[8];
        Left = m.left / 1000f;
        IsHost = attract || m.host == m.you;
        Me = null;
        foreach (var nk in m.karts) AddKart(nk, m.you);
        foreach (var k in Karts) k.Place(Arena.Spawns[k.Slot % Arena.Spawns.Count]);
        AddCoins(m.c, null);
        if (Me != null) camFocus = new Vector3(Me.Pos.x, 0, Me.Pos.y);
        UI.I.RebuildTags();
    }

    Kart AddKart(NetKart nk, int you)
    {
        if (nk.s < 0 || nk.s >= 8) return null;
        var k = Kart.Make(nk.s, nk.n, nk.k, SlotColors[nk.s], kartRoot);
        k.IsMe = nk.s == you; k.IsBot = nk.b == 1; k.Away = nk.a == 1;
        k.Local = k.IsMe || (k.IsBot && IsHost);
        if (k.IsBot && k.Local) k.Brain = new BotBrain(k, Attract ? 0.6f : BotSkill(), nk.s * 977 + rnd.Next(1000));
        if (k.IsMe) Me = k;
        Karts.Add(k); bySlot[nk.s] = k;
        k.Score = Scores[nk.s];
        return k;
    }

    // New players meet gentler bots; it ramps up over their first matches.
    float BotSkill() => Mathf.Lerp(0.35f, 0.85f, Mathf.Clamp01(Save.matches / 8f)) + (float)rnd.NextDouble() * 0.12f;

    void ApplyRoster(NetMsg m)
    {
        if (State != St.Play && State != St.Countdown) return;
        var seen = new bool[8];
        IsHost = m.host >= 0 && Me != null && m.host == Me.Slot;
        foreach (var nk in m.karts)
        {
            if (nk.s < 0 || nk.s >= 8) continue;
            seen[nk.s] = true;
            var k = bySlot[nk.s];
            bool isBot = nk.b == 1;
            if (k != null && (k.IsBot != isBot || k.Name != nk.n))
            {
                // a player took over a bot's seat (or the other way round)
                Karts.Remove(k); Destroy(k.gameObject); bySlot[nk.s] = null; k = null;
                if (!isBot) UI.I.Feed(nk.n + " JOINED", Color.white);
            }
            if (k == null) { k = AddKart(nk, Me != null ? Me.Slot : -1); k.Place(Arena.Spawns[nk.s % Arena.Spawns.Count]); Scores[nk.s] = 0; k.Score = 0; }
            k.Away = nk.a == 1;
            if (k.IsBot)
            {
                bool shouldSim = IsHost;
                if (shouldSim && !k.Local) { k.Local = true; k.Brain = new BotBrain(k, BotSkill(), nk.s * 131 + 5); }
                if (!shouldSim && k.Local) { k.Local = false; k.Brain = null; }
            }
        }
        for (int s = 0; s < 8; s++)
            if (!seen[s] && bySlot[s] != null && bySlot[s] != Me)
            {
                var k = bySlot[s];
                if (!k.IsBot) UI.I.Feed(k.Name + " LEFT", Kit.A(Color.white, 0.7f));
                Karts.Remove(k); Destroy(k.gameObject); bySlot[s] = null;
            }
        UI.I.RebuildTags();
    }

    public Kart KartAt(int slot) => slot >= 0 && slot < 8 ? bySlot[slot] : null;

    // ------------------------------------------------------------------ the loop
    void Update()
    {
        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        FX.Tick(dt);
        if (Arena.I != null) Arena.I.Tick(Time.time);
        if (Input.anyKeyDown || Input.touchCount > 0) Sfx.I.StartMusic();

        if (State == St.Finding)
        {
            findT += Time.unscaledDeltaTime; connectT += Time.unscaledDeltaTime;
            // no socket, or no seat after a few seconds: play offline right away rather than wait
            if ((!socketOpen && connectT > 4f) || (socketOpen && findT > 7f)) GoOffline(socketOpen ? "slow" : "timeout");
        }

        bool live = State == St.Play || Attract;
        if (State == St.Countdown)
        {
            float before = countT; countT -= dt;
            if (Mathf.CeilToInt(before) != Mathf.CeilToInt(countT) && countT > 0) { Sfx.I.Beep(false); UI.I.Count(Mathf.CeilToInt(countT).ToString()); }
            if (countT <= 0) { State = St.Play; Sfx.I.Beep(true); UI.I.Count("GO!"); live = true; }
        }

        if (live || State == St.Countdown) Simulate(dt, live);
        if (local != null) local.Tick(dt);
        if ((State == St.Play || State == St.Countdown) && !Attract)
        {
            Left = Mathf.Max(0, Left - dt);
            if (offlineEnd && Left <= 0) { offlineEnd = false; EndMatch(Scores); }
        }

        // send our karts ~12 times a second
        if (Online && (State == St.Play || State == St.Countdown))
        {
            sendT -= Time.unscaledDeltaTime;
            if (sendT <= 0)
            {
                sendT = 1f / 12f;
                var mine = new List<Kart>();
                foreach (var k in Karts) if (k.Local) mine.Add(k);
                if (mine.Count > 0) Send(NetOut.States(mine));
            }
        }
        if (Me != null) Sfx.I.Engine(Me.Speed / Kart.MaxSpeed + (Me.BoostT > 0 ? 0.4f : 0), State == St.Play || State == St.Countdown);
        else Sfx.I.Engine(0, false);
        UpdateCamera(dt);
        if (State == St.Play || State == St.Countdown) UI.I.UpdateHud();
        UI.I.UpdateTags();
    }

    void Simulate(float dt, bool live)
    {
        // drive
        foreach (var k in Karts)
        {
            if (k.Local)
            {
                Vector2 input = Vector2.zero;
                if (live)
                {
                    if (k.IsMe) input = devAuto && k.Brain != null ? k.Brain.Think(dt) : UI.I.DriveInput();
                    else if (k.Brain != null) input = k.Brain.Think(dt);
                }
                k.Drive(input, dt);
                if (k.RollT > 0) { k.RollT -= dt; if (k.RollT <= 0 && k.IsMe) { Sfx.I.ItemReady(); } }
            }
            else k.Follow(dt);
        }
        if (live && Me != null && UI.I.FirePressed()) UseItem(Me);
        Collide();
        if (!live) return;
        Pickups();
        GrabCoins();
        StepProjectiles(dt);
        // the closing ring: anyone outside is dragged back in and slowed
        if (!Attract)
        {
            float rr = Heist.RingRadius(Left);
            ring.Set(rr);
            if (rr < 60f && !ringWarned) { ringWarned = true; UI.I.Banner("RING CLOSING!", "get to the middle"); Sfx.I.Beep(true); WebBridge.Event("ring_closing"); }
            foreach (var k in Karts)
            {
                if (!k.Local || k.Away) continue;
                float d = k.Pos.magnitude;
                if (d > rr - Kart.R)
                {
                    var inward = -k.Pos / Mathf.Max(0.01f, d);
                    k.Vel += inward * 30f * dt;
                    if (k.Vel.magnitude > 8f) k.Vel = k.Vel.normalized * 8f;
                    if (k.IsMe && UnityEngine.Random.value < 0.1f) Shake(0.08f);
                }
            }
        }
        else ring.Set(99f);
        // the golden kart: unique leader with enough coins
        GoldenSlot = Attract ? -1 : Heist.Golden(Scores, Karts);
        foreach (var k in Karts) k.Golden = k.Slot == GoldenSlot;
    }

    // kart vs kart bumps; boosting karts knock others for six
    void Collide()
    {
        for (int i = 0; i < Karts.Count; i++)
            for (int j = i + 1; j < Karts.Count; j++)
            {
                var a = Karts[i]; var b = Karts[j];
                if (a.Away || b.Away || (!a.Local && !b.Local)) continue;
                var d = a.Pos - b.Pos; float dist = d.magnitude, min = Kart.R * 2f;
                if (dist >= min || dist < 1e-4f) continue;
                var n = d / dist; float pen = min - dist;
                if (a.Local && b.Local) { a.Pos += n * pen * 0.5f; b.Pos -= n * pen * 0.5f; }
                else if (a.Local) a.Pos += n * pen; else b.Pos -= n * pen;
                float rel = Vector2.Dot(a.Vel - b.Vel, n);
                if (rel < 0)
                {
                    var imp = n * rel * 0.9f;
                    if (a.Local) a.Vel -= imp;
                    if (b.Local) b.Vel += imp;
                    if (-rel > 5f) { Sfx.I.Bonk(Near(a.Pos)); FX.Sparks(new Vector3((a.Pos.x + b.Pos.x) / 2, 0.6f, (a.Pos.y + b.Pos.y) / 2), 8); }
                }
                // rams: the victim's own simulator decides
                if (b.BoostT > 0 && a.Local && a.Vulnerable && a.BoostT <= 0) Hit(a, b.Slot, -1, b.Pos);
                if (a.BoostT > 0 && b.Local && b.Vulnerable && b.BoostT <= 0) Hit(b, a.Slot, -1, a.Pos);
            }
    }

    void Pickups()
    {
        foreach (var k in Karts)
        {
            if (!k.Local || k.Spinning || k.Held != Item.None) continue;
            foreach (var b in boxes)
            {
                if (!b.Active || (b.Pos - k.Pos).sqrMagnitude > 1.7f * 1.7f) continue;
                b.Take();
                Send(NetOut.Box(b.Index));
                k.Held = Items.Roll(Rank01(k), rnd);
                k.RollT = k.IsMe ? 0.9f : 0.5f;
                if (k.IsMe)
                {
                    Sfx.I.Pickup(); UI.I.Roll();
                    if (Save.tut == 1) { Save.tut = 2; Persist(); UI.I.Coach(2); WebBridge.Event("tut_item"); }
                    WebBridge.Event("item_get");
                }
                break;
            }
        }
    }

    float Rank01(Kart k)
    {
        int better = 0, n = 0;
        foreach (var o in Karts) { if (o.Away) continue; n++; if (o != k && Scores[o.Slot] > Scores[k.Slot]) better++; }
        return n > 1 ? better / (float)(n - 1) : 0.5f;
    }

    // ------------------------------------------------------------------ items
    public void UseItem(Kart k)
    {
        if (k.Held == Item.None || k.RollT > 0 || k.Spinning) return;
        var it = k.Held; k.Held = Item.None;
        var f = k.Fwd;
        int pid = NextPid(k.Slot);
        switch (it)
        {
            case Item.Rocket:
            case Item.Homing:
            {
                var pos = k.Pos + f * 1.6f;
                Spawn(pid, k.Slot, it, pos, k.Yaw, it == Item.Homing ? HomingTarget(pos, f, k.Slot) : -1);
                if (it == Item.Homing) Sfx.I.Homing(Near(k.Pos)); else Sfx.I.Launch(Near(k.Pos));
                Send(NetOut.Fire(k.Slot, it, pos, k.Yaw, pid));
                break;
            }
            case Item.Triple:
            {
                var pos = k.Pos + f * 1.6f;
                for (int i = 0; i < 3; i++) Spawn(pid + i, k.Slot, Item.Rocket, pos, k.Yaw + (i - 1) * 13f, -1);
                pidSeq += 2;
                Sfx.I.Launch(Near(k.Pos));
                Send(NetOut.Fire(k.Slot, it, pos, k.Yaw, pid));
                break;
            }
            case Item.Mine:
            {
                var pos = k.Pos - f * 1.9f;
                Spawn(pid, k.Slot, it, pos, k.Yaw, -1);
                Sfx.I.Mine(Near(k.Pos));
                Send(NetOut.Fire(k.Slot, it, pos, k.Yaw, pid));
                break;
            }
            case Item.Shield: k.StartShield(); Send(NetOut.Fire(k.Slot, it, k.Pos, k.Yaw, pid)); break;
            case Item.Boost: k.StartBoost(1.6f); Send(NetOut.Fire(k.Slot, it, k.Pos, k.Yaw, pid)); break;
        }
        if (k.IsMe)
        {
            UI.I.ItemUsed();
            if (Save.tut == 2) { Save.tut = 3; Persist(); UI.I.Coach(3); WebBridge.Event("tut_done"); }
            WebBridge.Event("fire_" + Items.Names[(int)it].ToLowerInvariant());
        }
    }

    int NextPid(int slot) { pidSeq = (pidSeq + 1) % 9000; return slot * 10000 + pidSeq; }

    Projectile Spawn(int pid, int owner, Item it, Vector2 pos, float yaw, int target)
    {
        if (projs.ContainsKey(pid)) return projs[pid];
        var p = Projectile.Make(pid, owner, it, pos, yaw, projRoot);
        p.Target = target;
        projs[pid] = p;
        return p;
    }

    void RemoteFire(NetMsg m)
    {
        var k = KartAt(m.k); if (k == null || k.Local) return;
        var pos = new Vector2(m.x, m.z);
        var it = (Item)m.i;
        switch (it)
        {
            case Item.Rocket: Spawn(m.p, m.k, it, pos, m.a, -1); Sfx.I.Launch(Near(pos)); break;
            case Item.Homing: Spawn(m.p, m.k, it, pos, m.a, -1); Sfx.I.Homing(Near(pos)); break;
            case Item.Triple: for (int i = 0; i < 3; i++) Spawn(m.p + i, m.k, Item.Rocket, pos, m.a + (i - 1) * 13f, -1); Sfx.I.Launch(Near(pos)); break;
            case Item.Mine: Spawn(m.p, m.k, it, pos, m.a, -1); Sfx.I.Mine(Near(pos)); break;
            case Item.Shield: k.ShieldT = 6f; Sfx.I.Shield(Near(pos)); break;
            case Item.Boost: k.BoostT = 1.6f; Sfx.I.Boost(Near(pos)); break;
        }
    }

    readonly List<int> dead = new List<int>();
    void StepProjectiles(float dt)
    {
        dead.Clear();
        foreach (var kv in projs)
        {
            var p = kv.Value;
            if (!p || !p.Step(dt))
            {
                if (p && p.Type != Item.Mine) { FX.Explosion(p.transform.position + Vector3.up * 0.5f, 0.6f); Sfx.I.Boom(Near(p.Pos) * 0.6f); }
                dead.Add(kv.Key); continue;
            }
            if (!p.Armed) continue;
            foreach (var k in Karts)
            {
                if (k.Away || k.Spinning) continue;
                if (k.Slot == p.Owner && (p.Type != Item.Mine || p.Age < 2.5f)) continue;
                float r = p.HitRadius + Kart.R;
                if ((k.Pos - p.Pos).sqrMagnitude > r * r) continue;
                if (k.Local) { if (k.Vulnerable) Hit(k, p.Owner, p.Pid, p.Pos); else if (k.InvulnT > 0 && k.SpinT <= 0) continue; }
                else { FX.Explosion(k.transform.position, 0.7f); Sfx.I.Boom(Near(k.Pos) * 0.7f); }
                dead.Add(kv.Key);
                break;
            }
        }
        foreach (var id in dead) { if (projs.TryGetValue(id, out var p) && p) Destroy(p.gameObject); projs.Remove(id); }
    }

    // A local kart took a hit. The shield may eat it; otherwise it spins and the hit is reported.
    void Hit(Kart victim, int by, int pid, Vector2 from)
    {
        bool landed = victim.TakeHit(from);
        if (!landed || Attract) { if (Attract && landed) { /* attract mode: no scores */ } return; }
        if (by == victim.Slot) return;
        Send(NetOut.Hit(victim.Slot, by, pid, victim.Pos));
        if (victim.IsMe) WebBridge.Event("got_hit");
    }

    void OnHit(NetMsg m)
    {
        if (m.sc != null && m.sc.Length >= 8) Scores = (int[])m.sc.Clone();
        foreach (var k in Karts) k.Score = Scores[k.Slot];
        var v = KartAt(m.v); var by = KartAt(m.by);
        if (projs.TryGetValue(m.p, out var p) && p) { Destroy(p.gameObject); projs.Remove(m.p); }
        if (v != null && !v.Local && !v.Spinning) v.Spin(by != null ? by.Pos : v.Pos - v.Fwd);
        int spilled = m.c != null ? m.c.Length / 4 : 0;
        if (v != null) AddCoins(m.c, v.transform.position + Vector3.up * 0.8f);
        if (spilled >= 6 && v != null) { FX.Confetti(v.transform.position + Vector3.up, 40); Shake(0.3f * Near(v.Pos)); }
        if (v != null && by != null) UI.I.Feed((by == Me ? "YOU" : by.Name) + "  >  " + (v == Me ? "YOU" : v.Name) + (spilled > 0 ? "   -" + spilled : ""), by.Tint);
        if (by != null && by == Me)
        {
            hitsThisMatch++;
            Sfx.I.Score(); UI.I.PlusOne(v != null ? v.Name : "");
            if (hitsThisMatch == 1) WebBridge.Event("first_hit");
            if (hitsThisMatch % 5 == 0) WebBridge.Happy();
        }
        if (v == Me && by != null) UI.I.Banner("", "HIT BY " + by.Name + (spilled > 0 ? "  -  GRAB YOUR " + spilled + " COINS BACK!" : ""));
        if (v != null && v.Slot == GoldenSlot && by != null) UI.I.Toast((by == Me ? "YOU" : by.Name) + " KNOCKED THE CROWN OFF " + (v == Me ? "YOU" : v.Name) + "!");
    }

    // ------------------------------------------------------------------ coins
    // c: id, spot (-1 = spilled), x*100, z*100 per coin; spills arc out from `from`
    void AddCoins(int[] c, Vector3? from)
    {
        if (c == null || Arena.I == null) return;
        for (int i = 0; i + 3 < c.Length; i += 4)
        {
            int id = c[i];
            if (coins.ContainsKey(id)) continue;
            Vector2 pos;
            if (c[i + 1] >= 0 && Arena.CoinSpots.Count > 0) pos = Arena.CoinSpots[c[i + 1] % Arena.CoinSpots.Count];
            else
            {
                pos = new Vector2(c[i + 2] / 100f, c[i + 3] / 100f);
                var v = Vector2.zero;
                Arena.Resolve(ref pos, ref v, 0.6f, 0f);   // nudge spills off cover
            }
            coins[id] = Coin.Make(id, pos, from, coinRoot);
        }
    }

    // Local karts claim coins they touch; the server decides who actually got each one.
    void GrabCoins()
    {
        foreach (var k in Karts)
        {
            if (!k.Local || k.Spinning || k.Away) continue;
            foreach (var c in coins.Values)
            {
                if (!c || c.Pending || !c.Ready) continue;
                if ((c.Pos - k.Pos).sqrMagnitude > 1.5f * 1.5f) continue;
                c.Pending = true;
                Send(NetOut.Pick(c.Id, k.Slot));
                if (k.IsMe) { Sfx.I.Coin(); if (!firstCoin) { firstCoin = true; WebBridge.Event("first_coin"); } }
            }
        }
    }
    bool firstCoin;

    void CoinTaken(NetMsg m)
    {
        if (m.sc != null && m.sc.Length >= 8) Scores = (int[])m.sc.Clone();
        foreach (var k in Karts) k.Score = Scores[k.Slot];
        if (coins.TryGetValue(m.i, out var c) && c)
        {
            var who = KartAt(m.k);
            FX.Pickup(c.transform.position, Kit.Hex("#FFC93C"));
            if (who == Me && !c.Pending) Sfx.I.Coin();
            Destroy(c.gameObject);
        }
        coins.Remove(m.i);
    }

    public Vector2? NearestCoin(Vector2 from, float within)
    {
        Vector2? best = null; float bd = within * within;
        foreach (var c in coins.Values)
        {
            if (!c || c.Pending || !c.Ready) continue;
            float d = (c.Pos - from).sqrMagnitude;
            if (d < bd) { bd = d; best = c.Pos; }
        }
        return best;
    }

    public int HomingTarget(Vector2 from, Vector2 dir, int owner)
    {
        int best = -1; float bestS = 1e9f;
        foreach (var k in Karts)
        {
            if (k.Slot == owner || !k.Targetable) continue;
            var to = k.Pos - from; float d = to.magnitude;
            if (d > 32f) continue;
            float ang = Vector2.Angle(dir, to);
            float s = d + ang * 0.25f;
            if (ang < 75f && s < bestS) { bestS = s; best = k.Slot; }
        }
        return best;
    }

    public Vector2? NearestBox(Vector2 from, Vector2 fwd)
    {
        Vector2? best = null; float bs = 1e9f;
        foreach (var b in boxes)
        {
            if (!b.Active) continue;
            var to = b.Pos - from; float s = to.magnitude * (Vector2.Dot(to.normalized, fwd) < 0 ? 1.6f : 1f);
            if (s < bs) { bs = s; best = b.Pos; }
        }
        return best;
    }

    // Something dangerous heading this way?
    public bool Threatened(Kart k, float range)
    {
        foreach (var p in projs.Values)
        {
            if (!p || p.Owner == k.Slot || p.Type == Item.Mine) continue;
            var to = k.Pos - p.Pos;
            if (to.sqrMagnitude < range * range && Vector2.Dot(to, p.Vel) > 0) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ end of match
    void EndMatch(int[] sc)
    {
        if (State != St.Play && State != St.Countdown) return;
        if (sc != null && sc.Length >= 8) Scores = (int[])sc.Clone();
        State = St.Results;
        WebBridge.Gameplay(false);
        var order = new List<Kart>(Karts);
        order.RemoveAll(k => k.Away && k != Me);
        order.Sort((a, b) => Scores[b.Slot] != Scores[a.Slot] ? Scores[b.Slot].CompareTo(Scores[a.Slot]) : a.Slot.CompareTo(b.Slot));
        int place = Me != null ? order.IndexOf(Me) + 1 : order.Count;
        // wallet coins: everything you were carrying at the buzzer, plus a placing bonus
        int[] prize = { 30, 20, 12, 8, 6, 5, 5, 5 };
        int carried = Me != null ? Scores[Me.Slot] : 0;
        int earned = prize[Mathf.Clamp(place - 1, 0, 7)] + carried;
        Save.coins += earned; Save.matches++;
        if (place == 1) { Save.wins++; WebBridge.Happy(); Sfx.I.Win(); } else if (place <= 3) Sfx.I.Win(); else Sfx.I.Lose();
        Save.bestHits = Mathf.Max(Save.bestHits, carried);
        Persist();
        WebBridge.Event(Online ? "end_online" : "end_offline", place);
        WebBridge.Event("end_coins", carried);
        WebBridge.Event("end_hits", hitsThisMatch);
        if (Online) { Send(NetOut.Simple("away")); sentAway = true; }
        ring.Set(99f);
        UI.I.ShowResults(order, place, earned);
        if (place == 1) FX.Confetti(Me != null ? Me.transform.position + Vector3.up * 2 : Vector3.zero, 120);
    }

    public void DoubleCoins(int coins, Action<bool> done)
    {
        WebBridge.I.ShowRewarded(ok =>
        {
            if (ok) { Save.coins += coins; Persist(); Sfx.I.Coin(); WebBridge.Event("coins_double", coins); }
            done(ok);
        });
    }

    public void PlayAgain()
    {
        WebBridge.Event("play_again");
        WebBridge.I.Midgame(() =>
        {
            if (Online && socketOpen)
            {
                Send(NetOut.Simple("back")); sentAway = false;
                if (pendingMatch != null)
                {
                    var m = pendingMatch; pendingMatch = null;
                    m.left = Mathf.Max(0, m.left - (int)((Time.unscaledTime - pendingAt) * 1000));
                    State = St.Finding; StartMatch(m);
                }
                else { State = St.Finding; findT = -30f; UI.I.ShowFinding("NEXT MATCH STARTING..."); }
            }
            else if (local != null) { State = St.Finding; local.Send(NetOut.Simple("back")); }
            else { State = St.Finding; GoOffline("again"); }
        });
    }

    public void QuitMatch()
    {
        WebBridge.Event("quit_midmatch", Mathf.RoundToInt(LocalRoom.MatchSecs - Left));
        Time.timeScale = 1;
        GoMenu();
    }

    // ------------------------------------------------------------------ garage
    public bool Owns(int v) => (Save.owned & (1L << v)) != 0;
    public bool Buy(int v)
    {
        var veh = Vehicles.All[v];
        if (Owns(v) || Save.coins < veh.Cost) return false;
        Save.coins -= veh.Cost; Save.owned |= 1L << v; Save.vehicle = v; Persist();
        Sfx.I.Coin(); WebBridge.Event("unlock_" + veh.Model.Replace("-", "_"));
        return true;
    }
    public void Equip(int v) { if (!Owns(v)) return; Save.vehicle = v; Persist(); }
    public void FreeCoins(Action<bool> done)
    {
        WebBridge.I.ShowRewarded(ok => { if (ok) { Save.coins += 75; Persist(); Sfx.I.Coin(); WebBridge.Event("coins_ad"); } done(ok); });
    }

    public void SetName(string n)
    {
        n = (n ?? "").ToUpperInvariant().Trim();
        if (n.Length < 2) return;
        Save.name = n.Length > 12 ? n.Substring(0, 12) : n; Persist();
        WebBridge.Event("name_set");
        UI.I.ShowMenu();
    }
    public void OnName(string n) => SetName(n);
    // page -> unity: a friend's room code from the code prompt
    public void OnCode(string c)
    {
        c = (c ?? "").ToUpperInvariant().Trim();
        if (c.Length < 4) return;
        joinCode = c; wantPrivate = false;
        WebBridge.Event("join_code");
        WebBridge.I.Midgame(Connect);
    }
    public void LeaveToGarage() { GoMenu(); OpenGarage(); }

    public void ToggleMute() { Save.muted = !Save.muted; Sfx.I.SetMuted(Save.muted); Persist(); }

    // ------------------------------------------------------------------ camera
    public void Shake(float s) => shake = Mathf.Max(shake, s);
    public float Near(Vector2 p)
    {
        var c = new Vector2(camFocus.x, camFocus.z);
        return Mathf.Clamp01(1.2f - (p - c).magnitude / 30f);
    }

    void UpdateCamera(float dt)
    {
        float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
        float dist = aspect >= 1f ? 30f : Mathf.Lerp(46f, 30f, Mathf.InverseLerp(0.45f, 1f, aspect));
        float pitch = 58f;
        Vector3 target;
        if (State == St.Garage) { UpdateGarageCam(dt); return; }
        if (Me != null && !Attract)
        {
            target = new Vector3(Me.Pos.x + Me.Vel.x * 0.35f, 0, Me.Pos.y + Me.Vel.y * 0.35f);
            // keep the arena edge from swallowing the view
            float lim = Arena.Half - 8f;
            target.x = Mathf.Clamp(target.x, -lim, lim); target.z = Mathf.Clamp(target.z, -lim - 4f, lim - 2f);
        }
        else { camOrbit += dt * 6f; target = Vector3.zero; dist *= 1.35f; pitch = 52f; }
        camFocus = Vector3.Lerp(camFocus, target, 1f - Mathf.Exp(-dt * (Attract ? 1f : 6f)));
        var rot = Quaternion.Euler(pitch, Attract && State == St.Menu ? camOrbit : 0f, 0);
        var pos = camFocus - rot * Vector3.forward * dist;
        if (shake > 0) { pos += UnityEngine.Random.insideUnitSphere * shake * 0.6f; shake = Mathf.Max(0, shake - dt * 2.5f); }
        Cam.transform.SetPositionAndRotation(pos, rot);
    }

    // Garage showroom: the chosen vehicle on a turntable just outside the arena.
    Transform showroom; Kart showKart; int showVehicle = -1;
    public void ShowVehicle(int v)
    {
        if (!showroom)
        {
            showroom = new GameObject("Showroom").transform; showroom.SetParent(world, false);
            showroom.position = new Vector3(0, 0, -75f);   // past the trees and grandstands
            var disc = Kit.MeshObject("turntable", Kit.SphereMesh); disc.transform.SetParent(showroom, false);
            disc.transform.localScale = new Vector3(2.4f, 0.1f, 2.4f);
            var m = new Material(Shader.Find("Standard")) { color = Kit.Hex("#2E2A44") }; disc.GetComponent<MeshRenderer>().sharedMaterial = m;
        }
        if (showKart == null) { showKart = Kart.Make(7, "", v, Kit.Hex("#FFD84A"), showroom); showKart.transform.localPosition = new Vector3(0, 0.15f, 0); }
        if (showVehicle != v) { showKart.SetVehicle(v); showVehicle = v; }
        showroom.gameObject.SetActive(true);
    }
    public void HideShowroom() { if (showroom) showroom.gameObject.SetActive(false); }
    void UpdateGarageCam(float dt)
    {
        if (!showroom || showKart == null) return;
        showKart.Pos = Vector2.zero; showKart.Yaw += dt * 45f;   // the kart is a child of the showroom
        float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
        var focus = showroom.position + new Vector3(0, 0.8f, aspect < 1 ? -1.2f : 0f);
        var rot = Quaternion.Euler(24f, 0, 0);
        float dist = aspect < 1 ? 11f : 7.5f;
        var pos = focus - rot * Vector3.forward * dist + (aspect >= 1 ? new Vector3(-1.6f, 0, 0) : Vector3.zero);
        Cam.transform.SetPositionAndRotation(Vector3.Lerp(Cam.transform.position, pos, 1f - Mathf.Exp(-dt * 8f)), Quaternion.Slerp(Cam.transform.rotation, rot, 1f - Mathf.Exp(-dt * 8f)));
    }

    void OnApplicationFocus(bool f)
    {
        // offline matches pause when the tab loses focus; online ones can't
        if (!f && local != null && State == St.Play && Time.timeScale > 0) UI.I.ShowPause();
    }
    public bool CanPause => local != null && !Online;

    // dev: SendMessage("Game", "DevAuto", "1") lets a bot brain drive your kart (for preview videos)
    bool devAuto;
    public void DevAuto(string on) { if (!Dev || Me == null) return; devAuto = on == "1"; Me.Brain = devAuto ? new BotBrain(Me, 0.95f, 4242) : null; }
    // dev: SendMessage("Game", "DevGive", "1".."6") hands you an item; "DevFire" uses it
    public void DevGive(string i) { if (Dev && Me != null && int.TryParse(i, out var n)) { Me.Held = (Item)Mathf.Clamp(n, 1, 6); Me.RollT = 0; } }
    public void DevFire(string _) { if (Dev && Me != null) UseItem(Me); }
    // dev: SendMessage("Game", "DevEnd", "") ends the match now
    public void DevEnd(string _) { if (Dev) { if (local != null) { Left = 0; local.Tick(999); } else EndMatch(Scores); } }
    public void DevHit(string _) { if (Dev && Me != null) foreach (var k in Karts) if (k != Me) { Hit(k, Me.Slot, -1, Me.Pos); if (!Online && local != null) { } break; } }
}
