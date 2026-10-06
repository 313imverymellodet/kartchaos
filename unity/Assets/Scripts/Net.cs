using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Wire format for the match socket (server: orbyt/server/kart.js). One loose message class keeps JsonUtility happy.
[System.Serializable] public class NetKart { public int s; public string n; public int k; public int b; public int a; }

[System.Serializable]
public class NetMsg
{
    public string t;
    public int you = -1, host = -1, map, left, online;
    public string code, msg;
    public NetKart[] karts;
    public int[] sc;
    public float[] d;                 // kart states, 7 numbers each: slot, x, z, yaw, vx, vz, flags
    public int[] c;                   // coins, 4 numbers each: id, spot (-1 = spilled), x*100, z*100
    public int k = -1, i, p, v = -1, by = -1;
    public float x, z, a;
}

public static class NetOut
{
    static readonly StringBuilder sb = new StringBuilder(512);
    static string F(float f) => (Mathf.Round(f * 100f) / 100f).ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static string States(System.Collections.Generic.IEnumerable<Kart> karts)
    {
        sb.Clear(); sb.Append("{\"t\":\"s\",\"d\":[");
        bool first = true;
        foreach (var k in karts)
        {
            if (!first) sb.Append(','); first = false;
            sb.Append(k.Slot).Append(',').Append(F(k.Pos.x)).Append(',').Append(F(k.Pos.y)).Append(',').Append(F(Mathf.Repeat(k.Yaw, 360f)))
              .Append(',').Append(F(k.Vel.x)).Append(',').Append(F(k.Vel.y)).Append(',').Append(k.Flags);
        }
        return sb.Append("]}").ToString();
    }
    public static string Fire(int slot, Item it, Vector2 pos, float yaw, int pid) =>
        "{\"t\":\"f\",\"k\":" + slot + ",\"i\":" + (int)it + ",\"x\":" + F(pos.x) + ",\"z\":" + F(pos.y) + ",\"a\":" + F(yaw) + ",\"p\":" + pid + "}";
    public static string Hit(int victim, int by, int pid, Vector2 at) => "{\"t\":\"h\",\"v\":" + victim + ",\"by\":" + by + ",\"p\":" + pid + ",\"x\":" + F(at.x) + ",\"z\":" + F(at.y) + "}";
    public static string Pick(int coin, int slot) => "{\"t\":\"cp\",\"i\":" + coin + ",\"k\":" + slot + "}";
    public static string Box(int i) => "{\"t\":\"b\",\"i\":" + i + "}";
    public static string Simple(string t) => "{\"t\":\"" + t + "\"}";
    public static string Hello(string name, int vehicle) => "{\"t\":\"hello\",\"name\":" + Json(name) + ",\"kart\":" + vehicle + "}";
    public static string Join(string code) => "{\"t\":\"join\",\"code\":" + Json(code) + "}";
    static string Json(string s) => "\"" + (s ?? "").Replace("\\", "").Replace("\"", "") + "\"";
}

// Offline stand-in for the server: same messages, you plus five bots, you host them.
// Mirrors orbyt/server/kart.js, coin heist included.
public class LocalRoom
{
    readonly Dictionary<int, int[]> coins = new Dictionary<int, int[]>();   // id -> [spot, x100, z100]
    int coinSeq; float coinT;
    int[] CoinMsg(IEnumerable<int> ids)
    {
        var l = new List<int>();
        foreach (var id in ids) { var c = coins[id]; l.Add(id); l.Add(c[0]); l.Add(c[1]); l.Add(c[2]); }
        return l.ToArray();
    }
    List<int> SpawnCoins(int n)
    {
        var made = new List<int>();
        for (int i = 0; i < n; i++) { int id = ++coinSeq; coins[id] = new[] { rnd.Next(1000), 0, 0 }; made.Add(id); }
        return made;
    }
    public const float MatchSecs = 150f;
    readonly int[] sc = new int[8];
    NetKart[] karts;
    float left; bool playing; int lastMap = -1;
    readonly System.Random rnd = new System.Random();
    string name; int vehicle;
    public bool Playing => playing;

    public void Start(string myName, int myVehicle) { name = myName; vehicle = myVehicle; NewMatch(); }

    void NewMatch()
    {
        int n = 6;
        karts = new NetKart[n];
        karts[0] = new NetKart { s = 0, n = name, k = vehicle, b = 0 };
        var used = new System.Collections.Generic.HashSet<string>();
        for (int i = 1; i < n; i++)
        {
            string bn; do bn = BotBrain.RandomName(rnd); while (!used.Add(bn));
            karts[i] = new NetKart { s = i, n = bn, k = rnd.Next(Vehicles.All.Length), b = 1 };
        }
        for (int i = 0; i < 8; i++) sc[i] = 0;
        int map = rnd.Next(Arena.Names.Length); if (map == lastMap) map = (map + 1) % Arena.Names.Length; lastMap = map;
        left = MatchSecs; playing = true;
        coins.Clear(); coinT = 2f;
        var start = SpawnCoins(12);
        Game.I.OnMsg(new NetMsg { t = "match", you = 0, host = 0, map = map, left = (int)(left * 1000), karts = karts, sc = (int[])sc.Clone(), c = CoinMsg(start) });
    }

    public void Send(string json)
    {
        var m = JsonUtility.FromJson<NetMsg>(json);
        if (m == null) return;
        switch (m.t)
        {
            case "h":
            {
                if (!playing || m.v < 0 || m.v >= karts.Length || m.by < 0 || m.by >= karts.Length || m.v == m.by) return;
                // the victim spills coins in a ring (the golden leader spills more); the hitter pockets one
                bool golden = Heist.Golden(sc, Game.I.Karts) == m.v;
                int n = Mathf.CeilToInt(sc[m.v] * (golden ? Heist.GoldenShare : Heist.SpillShare));
                sc[m.v] -= n; sc[m.by]++;
                var spilled = new List<int>();
                for (int i = 0; i < n; i++)
                {
                    float a = (float)rnd.NextDouble() * Mathf.PI * 2f, r = 1.8f + (float)rnd.NextDouble() * 1.8f;
                    int id = ++coinSeq;
                    coins[id] = new[] { -1, Mathf.RoundToInt(Mathf.Clamp(m.x + Mathf.Cos(a) * r, -24.5f, 24.5f) * 100), Mathf.RoundToInt(Mathf.Clamp(m.z + Mathf.Sin(a) * r, -24.5f, 24.5f) * 100) };
                    spilled.Add(id);
                }
                Game.I.OnMsg(new NetMsg { t = "h", v = m.v, by = m.by, p = m.p, sc = (int[])sc.Clone(), c = CoinMsg(spilled) });
                break;
            }
            case "cp":
                if (!playing || !coins.ContainsKey(m.i) || m.k < 0 || m.k >= karts.Length) return;
                coins.Remove(m.i); sc[m.k]++;
                Game.I.OnMsg(new NetMsg { t = "cg", i = m.i, k = m.k, sc = (int[])sc.Clone() });
                break;
            case "back": if (!playing) NewMatch(); break;
        }
    }

    public void Tick(float dt)
    {
        if (!playing) return;
        left -= dt;
        coinT -= dt;
        if (coinT <= 0) { coinT = 2f; if (coins.Count < 22) { var made = SpawnCoins(1 + rnd.Next(2)); Game.I.OnMsg(new NetMsg { t = "cs", c = CoinMsg(made) }); } }
        if (left <= 0) { playing = false; Game.I.OnMsg(new NetMsg { t = "end", sc = (int[])sc.Clone() }); }
    }
}
