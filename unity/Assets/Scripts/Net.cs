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
    public static string Hit(int victim, int by, int pid) => "{\"t\":\"h\",\"v\":" + victim + ",\"by\":" + by + ",\"p\":" + pid + "}";
    public static string Box(int i) => "{\"t\":\"b\",\"i\":" + i + "}";
    public static string Simple(string t) => "{\"t\":\"" + t + "\"}";
    public static string Hello(string name, int vehicle) => "{\"t\":\"hello\",\"name\":" + Json(name) + ",\"kart\":" + vehicle + "}";
    public static string Join(string code) => "{\"t\":\"join\",\"code\":" + Json(code) + "}";
    static string Json(string s) => "\"" + (s ?? "").Replace("\\", "").Replace("\"", "") + "\"";
}

// Offline stand-in for the server: same messages, you plus five bots, you host them.
public class LocalRoom
{
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
        Game.I.OnMsg(new NetMsg { t = "match", you = 0, host = 0, map = map, left = (int)(left * 1000), karts = karts, sc = (int[])sc.Clone() });
    }

    public void Send(string json)
    {
        var m = JsonUtility.FromJson<NetMsg>(json);
        if (m == null) return;
        switch (m.t)
        {
            case "h":
                if (!playing || m.v < 0 || m.v >= karts.Length || m.by < 0 || m.by >= karts.Length || m.v == m.by) return;
                sc[m.by]++;
                Game.I.OnMsg(new NetMsg { t = "h", v = m.v, by = m.by, p = m.p, sc = (int[])sc.Clone() });
                break;
            case "back": if (!playing) NewMatch(); break;
        }
    }

    public void Tick(float dt)
    {
        if (!playing) return;
        left -= dt;
        if (left <= 0) { playing = false; Game.I.OnMsg(new NetMsg { t = "end", sc = (int[])sc.Clone() }); }
    }
}
