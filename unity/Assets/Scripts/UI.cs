using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UI : MonoBehaviour
{
    public static UI I;
    Canvas canvas; CanvasScaler scaler;
    RectTransform root, hud, screens, tags, controls;
    Font F => Kit.Font;
    public static readonly Color Ink = Kit.Hex("#1B1340"), Pink = Kit.Hex("#FF4F8B"), Cyan = Kit.Hex("#35D6FF"), Gold = Kit.Hex("#FFD84A"),
        Lime = Kit.Hex("#7CF06B"), Purple = Kit.Hex("#8E6BFF"), Orange = Kit.Hex("#FF8A1F"), Soft = new Color(1, 1, 1, 0.75f);
    static readonly Color Dim = new Color(0.1f, 0.07f, 0.28f, 0.72f);

    VirtualStick stick; HoldButton fireBtn;
    Text timerText, bannerText, bannerSub, countText, toastText, plusText, coachText, itemLabel, roomText, keysHint;
    Image itemIcon, itemRing; RectTransform itemSlot, coachBox, board, feedBox, timerBox;
    float bannerT, countT, toastT, plusT, rollT, rollTick;
    readonly List<(RectTransform row, Text rank, Image dot, Text name, Text score)> rows = new List<(RectTransform, Text, Image, Text, Text)>();
    readonly List<(Text t, float life)> feed = new List<(Text, float)>();
    readonly Dictionary<Kart, Text> tagMap = new Dictionary<Kart, Text>();
    static Sprite disc, ring, coinSpr;
    static Sprite Spr(Texture2D t) => Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(.5f, .5f));
    public static bool Touch => Input.touchSupported || Application.isMobilePlatform;
    int online = -1;

    public void Init()
    {
        I = this;
        var es = new GameObject("EventSystem"); es.AddComponent<EventSystem>().pixelDragThreshold = 4; es.AddComponent<StandaloneInputModule>();
        canvas = gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1080, 1920);
        gameObject.AddComponent<GraphicRaycaster>();
        root = (RectTransform)transform;
        disc = Spr(Kit.Disc); ring = Spr(Kit.Ring); coinSpr = Spr(CoinTex());

        tags = Fill("tags", root);
        BuildHud();
        screens = Fill("screens", root);

        countText = Txt(root, "", 300, new Vector2(.5f, .58f), Vector2.zero, Color.white, TextAnchor.MiddleCenter, 1200);
        countText.fontStyle = FontStyle.Italic; Outline(countText, 8); countText.gameObject.SetActive(false);
        bannerText = Txt(root, "", 96, new Vector2(.5f, .72f), Vector2.zero, Gold, TextAnchor.MiddleCenter, 1400);
        bannerText.fontStyle = FontStyle.Italic; Outline(bannerText, 5);
        bannerSub = Txt(root, "", 46, new Vector2(.5f, .72f), new Vector2(0, -88), Color.white, TextAnchor.MiddleCenter, 1400);
        Outline(bannerSub, 3);
        bannerText.gameObject.SetActive(false); bannerSub.gameObject.SetActive(false);
        toastText = Txt(root, "", 40, new Vector2(.5f, 1), new Vector2(0, -330), Color.white, TextAnchor.MiddleCenter, 1300);
        Outline(toastText, 3); toastText.gameObject.SetActive(false);
        plusText = Txt(root, "", 84, new Vector2(.5f, .64f), Vector2.zero, Lime, TextAnchor.MiddleCenter, 1200);
        plusText.fontStyle = FontStyle.Italic; Outline(plusText, 5); plusText.gameObject.SetActive(false);
        ApplyScaling();
    }

    // ---------------------------------------------------------------- building blocks
    RectTransform Rect(string n, Transform p, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(n, typeof(RectTransform)); go.transform.SetParent(p, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(.5f, .5f); rt.anchoredPosition = pos; rt.sizeDelta = size;
        return rt;
    }
    RectTransform Fill(string n, Transform p)
    {
        var rt = Rect(n, p, Vector2.zero, Vector2.zero, Vector2.zero);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; return rt;
    }
    RectTransform Box(Transform p, Vector2 anchor, Vector2 pos, Vector2 size, Color c, bool ray = false)
    {
        var rt = Rect("box", p, anchor, pos, size);
        var img = rt.gameObject.AddComponent<Image>(); img.sprite = Kit.RoundedSprite; img.type = Image.Type.Sliced; img.color = c; img.raycastTarget = ray;
        return rt;
    }
    Image Img(Transform p, Sprite s, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        var rt = Rect("img", p, anchor, pos, size);
        var img = rt.gameObject.AddComponent<Image>(); img.sprite = s; img.preserveAspect = true; img.raycastTarget = false; return img;
    }
    Text Txt(Transform p, string s, int size, Vector2 anchor, Vector2 pos, Color c, TextAnchor align = TextAnchor.MiddleCenter, float w = 700)
    {
        size = Mathf.Max(size, 30);   // readable floor: Lilita below this turns to mush on phones and short desktop windows
        var rt = Rect("txt", p, anchor, pos, new Vector2(w, size * 1.4f));
        var t = rt.gameObject.AddComponent<Text>();
        t.font = F; t.fontSize = size; t.fontStyle = FontStyle.Normal; t.alignment = align; t.color = c; t.text = s;
        t.raycastTarget = false; t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }
    static void Outline(Text t, float d) { var o = t.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0.08f, 0.04f, 0.2f, 0.9f); o.effectDistance = new Vector2(d, -d); }
    Button Btn(Transform p, string label, Vector2 anchor, Vector2 pos, Vector2 size, Color bg, Color fg, Action onClick, int fs = 48)
    {
        var rt = Box(p, anchor, pos, size, bg, true);
        var lip = Box(rt, new Vector2(.5f, 0), new Vector2(0, -6), new Vector2(size.x, 16), Color.Lerp(bg, Color.black, 0.35f));
        lip.SetAsFirstSibling(); lip.pivot = new Vector2(.5f, 0);
        var b = rt.gameObject.AddComponent<Button>(); b.targetGraphic = rt.GetComponent<Image>();
        b.onClick.AddListener(() => { Sfx.I.Click(); onClick(); });
        rt.gameObject.AddComponent<Press>();
        var t = Txt(rt, label, fs, new Vector2(.5f, .5f), Vector2.zero, fg, TextAnchor.MiddleCenter, size.x);
        t.fontStyle = FontStyle.Italic;
        return b;
    }
    void Title(Transform p, string a, string b, float y, int size)
    {
        var t1 = Txt(p, a, size, new Vector2(.5f, 1), new Vector2(-size * 0.02f, y), Gold, TextAnchor.MiddleCenter, 1100);
        t1.fontStyle = FontStyle.Italic; Outline(t1, 8);
        var t2 = Txt(p, b, size, new Vector2(.5f, 1), new Vector2(size * 0.05f, y - size * 0.92f), Pink, TextAnchor.MiddleCenter, 1100);
        t2.fontStyle = FontStyle.Italic; Outline(t2, 8);
    }

    static Texture2D CoinTex()
    {
        int n = 64; var t = new Texture2D(n, n, TextureFormat.RGBA32, false); var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + .5f) / n - .5f, v = (y + .5f) / n - .5f, r = Mathf.Sqrt(u * u + v * v);
                float a = Mathf.Clamp01((0.48f - r) * n);
                var c = r > 0.38f ? Kit.Hex("#E0A21A") : Kit.Hex("#FFD84A");
                if (r < 0.26f && r > 0.2f) c = Kit.Hex("#E0A21A");
                if (Mathf.Abs(u) < 0.05f && Mathf.Abs(v) < 0.16f) c = Kit.Hex("#FFF3B0");
                px[y * n + x] = Kit.A(c, a);
            }
        t.SetPixels(px); t.Apply(); return t;
    }

    void ApplyScaling()
    {
        float aspect = (float)UnityEngine.Screen.width / Mathf.Max(1, UnityEngine.Screen.height);
        scaler.matchWidthOrHeight = aspect > 0.75f ? 1f : 0f;
    }
    public static bool Landscape => (float)UnityEngine.Screen.width / Mathf.Max(1, UnityEngine.Screen.height) > 1.05f;

    // A 1080x1920 column: centred on phones, at the right in landscape so the arena shows on the left.
    RectTransform Column(RectTransform s)
    {
        var p = Rect("col", s, new Vector2(.5f, .5f), Vector2.zero, new Vector2(1080, 1920));
        if (Landscape) { p.anchorMin = p.anchorMax = new Vector2(1, .5f); p.pivot = new Vector2(1, .5f); p.anchoredPosition = new Vector2(-30, 0); }
        return p;
    }
    RectTransform Screen(bool dim = true)
    {
        foreach (Transform c in screens) Destroy(c.gameObject);
        var s = Fill("screen", screens);
        if (dim) { var img = s.gameObject.AddComponent<Image>(); img.color = new Color(0.06f, 0.03f, 0.18f, 0.72f); }
        return s;
    }
    public void CloseScreens() { foreach (Transform c in screens) Destroy(c.gameObject); }

    // ---------------------------------------------------------------- HUD
    void BuildHud()
    {
        hud = Fill("hud", root);
        // timer
        timerBox = Box(hud, new Vector2(.5f, 1), new Vector2(0, -80), new Vector2(260, 104), Dim);
        timerText = Txt(timerBox, "2:30", 66, new Vector2(.5f, .5f), new Vector2(0, 2), Color.white);
        timerText.fontStyle = FontStyle.Italic;
        roomText = Txt(hud, "", 44, new Vector2(.5f, 1), new Vector2(0, -168), Cyan); roomText.fontStyle = FontStyle.Italic; Outline(roomText, 3);
        // scoreboard
        board = Rect("board", hud, new Vector2(0, 1), new Vector2(20 + 230, -40 - 0), new Vector2(460, 520));
        board.pivot = new Vector2(.5f, 1); board.anchoredPosition = new Vector2(250, -28);
        for (int i = 0; i < 8; i++)
        {
            var r = Box(board, new Vector2(.5f, 1), new Vector2(0, -32 - i * 60), new Vector2(460, 54), Dim);
            var rank = Txt(r, (i + 1).ToString(), 32, new Vector2(0, .5f), new Vector2(34, 0), Soft, TextAnchor.MiddleCenter, 60);
            var dot = Img(r, disc, new Vector2(0, .5f), new Vector2(80, 0), new Vector2(26, 26));
            var nm = Txt(r, "", 32, new Vector2(0, .5f), new Vector2(102 + 140, 0), Color.white, TextAnchor.MiddleLeft, 280);
            var sc = Txt(r, "0", 36, new Vector2(1, .5f), new Vector2(-40, 0), Gold, TextAnchor.MiddleCenter, 80);
            rows.Add((r, rank, dot, nm, sc));
        }
        // kill feed
        feedBox = Rect("feed", hud, new Vector2(1, 1), new Vector2(-260, -40), new Vector2(500, 300));
        feedBox.pivot = new Vector2(1, 1); feedBox.anchoredPosition = new Vector2(-24, -150);

        controls = Fill("controls", hud);
        // item / fire button
        itemSlot = Rect("item", controls, new Vector2(1, 0), new Vector2(-220, 230), new Vector2(300, 300));
        var bg = itemSlot.gameObject.AddComponent<Image>(); bg.sprite = disc; bg.color = Dim; bg.raycastTarget = true;
        itemRing = Img(itemSlot, ring, new Vector2(.5f, .5f), Vector2.zero, new Vector2(300, 300)); itemRing.color = Kit.A(Color.white, 0.5f);
        itemIcon = Img(itemSlot, Items.Icon(Item.Rocket), new Vector2(.5f, .5f), new Vector2(0, 14), new Vector2(170, 170));
        itemLabel = Txt(itemSlot, "", 36, new Vector2(.5f, 0), new Vector2(0, 46), Color.white);
        Outline(itemLabel, 3);
        fireBtn = itemSlot.gameObject.AddComponent<HoldButton>();
        keysHint = Txt(controls, "", 32, new Vector2(1, 0), new Vector2(-220, 52), Soft, TextAnchor.MiddleCenter, 460);
        // joystick on the left half (touch)
        var zone = Rect("stickzone", controls, new Vector2(0, 0), Vector2.zero, Vector2.zero);
        zone.anchorMin = new Vector2(0, 0); zone.anchorMax = new Vector2(0.55f, 0.7f); zone.offsetMin = zone.offsetMax = Vector2.zero;
        var zi = zone.gameObject.AddComponent<Image>(); zi.color = new Color(0, 0, 0, 0); zi.raycastTarget = true;
        var baseImg = Img(zone, ring, new Vector2(0, 0), new Vector2(240, 260), new Vector2(260, 260)); baseImg.color = Kit.A(Color.white, 0.45f);
        var knob = Img(zone, disc, new Vector2(0, 0), new Vector2(240, 260), new Vector2(130, 130)); knob.color = Kit.A(Color.white, 0.75f);
        stick = zone.gameObject.AddComponent<VirtualStick>(); stick.Base = baseImg.rectTransform; stick.Knob = knob.rectTransform; stick.Home = new Vector2(240, 260);
        baseImg.gameObject.SetActive(Touch); knob.gameObject.SetActive(Touch);
        // pause
        Btn(hud, "II", new Vector2(1, 1), new Vector2(-80, -80), new Vector2(110, 110), Dim, Color.white, ShowPause, 46);
        // coach
        coachBox = Box(hud, new Vector2(.5f, 0), new Vector2(0, 520), new Vector2(900, 110), new Color(0.1f, 0.07f, 0.28f, 0.85f));
        coachText = Txt(coachBox, "", 44, new Vector2(.5f, .5f), Vector2.zero, Gold, TextAnchor.MiddleCenter, 880);
        coachBox.gameObject.SetActive(false);
        hud.gameObject.SetActive(false);
    }

    public void ShowHud(bool on)
    {
        CloseScreens();
        hud.gameObject.SetActive(on);
        tags.gameObject.SetActive(true);
        if (on)
        {
            keysHint.text = Touch ? "" : "SPACE TO FIRE";
            roomText.text = Game.I.RoomCode.Length > 0 ? "ROOM " + Game.I.RoomCode : "";
            foreach (var f in feed) if (f.t) Destroy(f.t.gameObject);
            feed.Clear();
        }
    }

    public Vector2 DriveInput()
    {
        var v = new Vector2(
            (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1 : 0) - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1 : 0),
            (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1 : 0) - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1 : 0));
        if (v.sqrMagnitude > 0.01f) return v.normalized;
        return stick ? stick.Value : Vector2.zero;
    }
    public bool FirePressed()
    {
        bool k = Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.J) || Input.GetKeyDown(KeyCode.K);
        if (fireBtn && fireBtn.Pressed) { fireBtn.Pressed = false; k = true; }
        return k;
    }

    public void UpdateHud()
    {
        var g = Game.I; float dt = Time.unscaledDeltaTime;
        // phones: the scoreboard fills the top-left, so the timer moves beside the pause button
        bool land = Landscape;
        timerBox.anchorMin = timerBox.anchorMax = land ? new Vector2(.5f, 1) : new Vector2(1, 1);
        timerBox.anchoredPosition = land ? new Vector2(0, -80) : new Vector2(-280, -80);
        feedBox.anchoredPosition = land ? new Vector2(-24, -150) : new Vector2(-24, -170);
        int s = Mathf.CeilToInt(g.Left);
        timerText.text = (s / 60) + ":" + (s % 60).ToString("00");
        timerText.color = s <= 10 && g.State == Game.St.Play ? (Mathf.Repeat(Time.time, 0.5f) < 0.25f ? Pink : Color.white) : Color.white;

        // scoreboard: everyone present, best first
        var order = new List<Kart>();
        foreach (var k in g.Karts) if (!k.Away) order.Add(k);
        order.Sort((a, b) => g.Scores[b.Slot] != g.Scores[a.Slot] ? g.Scores[b.Slot].CompareTo(g.Scores[a.Slot]) : a.Slot.CompareTo(b.Slot));
        int show = Landscape ? 8 : 5;
        int meIdx = g.Me != null ? order.IndexOf(g.Me) : -1;
        for (int i = 0; i < rows.Count; i++)
        {
            int idx = i;
            if (i == show - 1 && meIdx >= show) idx = meIdx;   // always show yourself
            bool on = i < show && idx < order.Count;
            rows[i].row.gameObject.SetActive(on);
            if (!on) continue;
            var k = order[idx];
            rows[i].rank.text = (idx + 1).ToString();
            rows[i].dot.color = k.Tint;
            rows[i].name.text = k.IsMe ? "YOU" : k.Name;
            rows[i].name.color = k.IsMe ? Gold : Color.white;
            rows[i].score.text = g.Scores[k.Slot].ToString();
            rows[i].row.GetComponent<Image>().color = k.IsMe ? new Color(0.35f, 0.2f, 0.6f, 0.85f) : Dim;
        }

        // item slot
        var me = g.Me;
        if (me != null)
        {
            if (me.Held != Item.None && me.RollT > 0)
            {
                rollTick -= dt;
                if (rollTick <= 0) { rollTick = 0.07f; itemIcon.sprite = Items.Icon((Item)UnityEngine.Random.Range(1, 7)); Sfx.I.Roll(); }
                itemIcon.color = Color.white; itemLabel.text = "";
                itemRing.color = Kit.A(Color.white, 0.5f);
            }
            else if (me.Held != Item.None)
            {
                itemIcon.sprite = Items.Icon(me.Held); itemIcon.color = Color.white;
                itemLabel.text = Items.Names[(int)me.Held];
                float pulse = 1f + Mathf.Sin(Time.time * 8f) * 0.04f;
                itemSlot.localScale = Vector3.one * pulse;
                itemRing.color = Items.Colors[(int)me.Held];
            }
            else
            {
                itemIcon.sprite = Items.Icon(Item.Rocket); itemIcon.color = new Color(1, 1, 1, 0.15f);
                itemLabel.text = "GET A ? BOX"; itemSlot.localScale = Vector3.one;
                itemRing.color = Kit.A(Color.white, 0.3f);
            }
            if (g.Save.tut == 0 && me.Speed > 6f) { g.Save.tut = 1; g.Persist(); Coach(1); WebBridge.Event("tut_drove"); }
        }

        // feed fade
        for (int i = feed.Count - 1; i >= 0; i--)
        {
            var f = feed[i]; f.life -= dt; feed[i] = f;
            if (!f.t) { feed.RemoveAt(i); continue; }
            f.t.color = Kit.A(f.t.color, Mathf.Clamp01(f.life));
            if (f.life <= 0) { Destroy(f.t.gameObject); feed.RemoveAt(i); }
        }
        for (int i = 0; i < feed.Count; i++) if (feed[i].t) ((RectTransform)feed[i].t.transform).anchoredPosition = new Vector2(0, -24 - i * 50);
    }

    public void Feed(string text, Color c)
    {
        var t = Txt(feedBox, text, 34, new Vector2(1, 1), Vector2.zero, c, TextAnchor.MiddleRight, 600);
        ((RectTransform)t.transform).pivot = new Vector2(1, .5f);
        Outline(t, 3);
        feed.Insert(0, (t, 4f));
        while (feed.Count > 4) { var last = feed[feed.Count - 1]; if (last.t) Destroy(last.t.gameObject); feed.RemoveAt(feed.Count - 1); }
    }

    public void Roll() { rollTick = 0; }
    public void ItemUsed() { itemSlot.localScale = Vector3.one * 0.9f; }
    public void PlusOne(string victim) { plusText.text = "+1  HIT " + victim; plusT = 1.2f; plusText.gameObject.SetActive(true); }
    public void Count(string s) { countText.text = s; countT = 0.9f; countText.gameObject.SetActive(true); }
    public void Banner(string title, string sub)
    {
        bannerText.text = title; bannerSub.text = sub; bannerT = 2f;
        bannerText.gameObject.SetActive(!string.IsNullOrEmpty(title)); bannerSub.gameObject.SetActive(!string.IsNullOrEmpty(sub));
    }
    public void Toast(string s) { toastText.text = s; toastT = 2.6f; toastText.gameObject.SetActive(true); }
    public void SetOnline(int n) { online = n; }

    public void Coach(int stage)
    {
        string t = stage == 0 ? (Touch ? "DRAG ON THE LEFT TO DRIVE" : "WASD OR ARROWS TO DRIVE")
                 : stage == 1 ? "DRIVE THROUGH A  ?  BOX"
                 : stage == 2 ? (Touch ? "TAP THE ITEM BUTTON TO FIRE" : "PRESS SPACE TO FIRE")
                 : "";
        coachBox.gameObject.SetActive(t.Length > 0);
        coachText.text = t;
        if (stage == 3) Banner("NICE!", "most hits in 2:30 wins");
    }

    // ---------------------------------------------------------------- name tags
    public void RebuildTags()
    {
        foreach (var t in tagMap.Values) if (t) Destroy(t.gameObject);
        tagMap.Clear();
        foreach (var k in Game.I.Karts)
        {
            var t = Txt(tags, k.IsMe ? "YOU" : k.Name, 30, new Vector2(.5f, .5f), Vector2.zero, k.Tint, TextAnchor.MiddleCenter, 400);
            Outline(t, 3);
            tagMap[k] = t;
        }
    }
    public void UpdateTags()
    {
        var g = Game.I; var cam = g.Cam;
        bool on = g.State == Game.St.Play || g.State == Game.St.Countdown;
        foreach (var kv in tagMap)
        {
            var k = kv.Key; var t = kv.Value;
            if (!k || !t) continue;
            bool vis = on && !k.Away;
            if (vis)
            {
                var sp = cam.WorldToScreenPoint(k.transform.position + Vector3.up * 2.6f);
                vis = sp.z > 0;
                if (vis)
                {
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(tags, sp, null, out var lp);
                    ((RectTransform)t.transform).anchoredPosition = lp;
                    t.text = (k.IsMe ? "YOU" : k.Name) + (g.Scores[k.Slot] > 0 ? "  " + g.Scores[k.Slot] : "");
                }
            }
            t.gameObject.SetActive(vis);
        }
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        if (bannerT > 0) { bannerT -= dt; if (bannerT <= 0) { bannerText.gameObject.SetActive(false); bannerSub.gameObject.SetActive(false); } }
        if (toastT > 0) { toastT -= dt; if (toastT <= 0) toastText.gameObject.SetActive(false); }
        if (countT > 0)
        {
            countT -= dt;
            countText.transform.localScale = Vector3.one * (1f + Mathf.Max(0, countT - 0.6f) * 1.5f);
            if (countT <= 0) countText.gameObject.SetActive(false);
        }
        if (plusT > 0)
        {
            plusT -= dt;
            plusText.transform.localScale = Vector3.one * (plusT > 1f ? Kit.EaseOutBack((1.2f - plusT) / 0.2f) : 1f);
            if (plusT <= 0) plusText.gameObject.SetActive(false);
        }
        if (itemSlot && itemSlot.localScale.x < 1f) itemSlot.localScale = Vector3.MoveTowards(itemSlot.localScale, Vector3.one, dt * 2f);
        if (Input.GetKeyDown(KeyCode.Escape) && (Game.I.State == Game.St.Play || Game.I.State == Game.St.Countdown)) ShowPause();
        if (lastW != UnityEngine.Screen.width || lastH != UnityEngine.Screen.height)
        {
            lastW = UnityEngine.Screen.width; lastH = UnityEngine.Screen.height; ApplyScaling();
            var st = Game.I.State;
            if (st == Game.St.Menu) ShowMenu(); else if (st == Game.St.Garage) ShowGarage();
        }
        if (spinner) spinner.Rotate(0, 0, -dt * 360f);
        if (Game.Dev && Input.GetKeyDown(KeyCode.H)) canvas.enabled = !canvas.enabled;   // dev: clean screenshots
    }
    int lastW, lastH;
    Transform spinner;

    IEnumerator Pulse(Transform t) { while (t) { t.localScale = Vector3.one * (1f + Mathf.Sin(Time.unscaledTime * 4f) * 0.035f); yield return null; } }

    // ---------------------------------------------------------------- screens
    RectTransform CoinPill(Transform p, Vector2 anchor, Vector2 pos)
    {
        var b = Box(p, anchor, pos, new Vector2(300, 96), Dim);
        Img(b, coinSpr, new Vector2(0, .5f), new Vector2(58, 0), new Vector2(64, 64));
        Txt(b, Game.I.Save.coins.ToString("N0"), 50, new Vector2(.5f, .5f), new Vector2(30, 0), Gold, TextAnchor.MiddleCenter, 220).fontStyle = FontStyle.Italic;
        return b;
    }

    public void ShowMenu()
    {
        hud.gameObject.SetActive(false);
        var g = Game.I;
        var s = Screen(false);
        var col = Column(s);
        if (Landscape) { var back = Box(col, new Vector2(.5f, .5f), Vector2.zero, new Vector2(1060, 1880), new Color(0.06f, 0.03f, 0.18f, 0.55f)); back.SetAsFirstSibling(); }
        else { Kit.Scrim(s, true, 700, new Color(0.06f, 0.03f, 0.18f, 0.6f)); Kit.Scrim(s, false, 1000, new Color(0.06f, 0.03f, 0.18f, 0.75f)); }
        Title(col, "KART", "CHAOS", -250, 230);
        Txt(col, "bump - blast - be the last kart spinning", 38, new Vector2(.5f, 1), new Vector2(0, -620), Color.white, TextAnchor.MiddleCenter, 1000);
        CoinPill(s, new Vector2(1, 1), new Vector2(-190, -80));

        string code = WebBridge.RoomCode();
        var play = Btn(col, code.Length > 0 ? "JOIN ROOM " + code.ToUpperInvariant() : "PLAY", new Vector2(.5f, 0), new Vector2(0, 880), new Vector2(760, 200), Pink, Color.white, () => g.Play(false), 100);
        StartCoroutine(Pulse(play.transform));
        Txt(col, "online battle  -  2:30 matches  -  bots fill empty seats", 32, new Vector2(.5f, 0), new Vector2(0, 750), Soft, TextAnchor.MiddleCenter, 1000);
        Btn(col, "PLAY WITH FRIENDS", new Vector2(.5f, 0), new Vector2(0, 620), new Vector2(760, 130), Cyan, Ink, ShowFriends, 52);
        Btn(col, "GARAGE  -  " + Vehicles.All[g.Save.vehicle].Name, new Vector2(.5f, 0), new Vector2(0, 470), new Vector2(760, 130), Gold, Ink, () => g.OpenGarage(), 52);
        Btn(col, "DRIVER: " + g.Save.name, new Vector2(.5f, 0), new Vector2(-190, 330), new Vector2(370, 110), Dim, Color.white, () => WebBridge.AskName(g.Save.name), 34);
        Btn(col, g.Save.muted ? "SOUND OFF" : "SOUND ON", new Vector2(.5f, 0), new Vector2(190, 330), new Vector2(370, 110), Dim, Color.white, () => { g.ToggleMute(); ShowMenu(); }, 34);
        Btn(col, "HOW TO PLAY", new Vector2(.5f, 0), new Vector2(0, 200), new Vector2(560, 100), Dim, Color.white, ShowHowTo, 34);
        if (g.Save.matches > 0)
            Txt(col, "WINS " + g.Save.wins + "   -   BEST HITS " + g.Save.bestHits + "   -   MATCHES " + g.Save.matches, 30, new Vector2(.5f, 0), new Vector2(0, 100), Soft, TextAnchor.MiddleCenter, 1000);
    }

    public void ShowFriends()
    {
        var g = Game.I;
        var s = Screen();
        var col = Column(s);
        var t = Txt(col, "PLAY WITH FRIENDS", 84, new Vector2(.5f, 1), new Vector2(0, -260), Cyan); t.fontStyle = FontStyle.Italic; Outline(t, 5);
        Txt(col, "Make a private room and give your friends the code.\nBots keep it busy until everyone is in.", 38, new Vector2(.5f, 1), new Vector2(0, -420), Color.white, TextAnchor.MiddleCenter, 980).lineSpacing = 1.15f;
        Btn(col, "CREATE ROOM", new Vector2(.5f, .5f), new Vector2(0, 160), new Vector2(700, 170), Pink, Color.white, () => g.Play(true), 70);
        Btn(col, "JOIN WITH CODE", new Vector2(.5f, .5f), new Vector2(0, -40), new Vector2(700, 150), Cyan, Ink, () => WebBridge.AskCode(), 60);
        Btn(col, "BACK", new Vector2(.5f, .5f), new Vector2(0, -230), new Vector2(420, 120), Dim, Color.white, () => ShowMenu(), 46);
    }

    public void ShowHowTo()
    {
        var s = Screen();
        var col = Column(s);
        var t = Txt(col, "HOW TO PLAY", 96, new Vector2(.5f, 1), new Vector2(0, -200), Gold); t.fontStyle = FontStyle.Italic; Outline(t, 5);
        Txt(col, Touch ? "Drag on the left side to steer and go.\nTap the item button to fire." : "WASD / arrows to drive.  SPACE to fire.", 40, new Vector2(.5f, 1), new Vector2(0, -340), Color.white, TextAnchor.MiddleCenter, 980).lineSpacing = 1.1f;
        Txt(col, "Drive through  ?  boxes to get an item.\nEvery hit you land is a point.  Most points in 2:30 wins.", 36, new Vector2(.5f, 1), new Vector2(0, -470), Soft, TextAnchor.MiddleCenter, 980).lineSpacing = 1.1f;
        string[] what = { "", "flies straight - lead your target", "three rockets in a spread", "chases the nearest kart ahead", "drops behind you - don't follow too close", "blocks one hit", "speed burst - ram anyone for a point" };
        for (int i = 1; i <= 6; i++)
        {
            var r = Box(col, new Vector2(.5f, 1), new Vector2(0, -640 - (i - 1) * 140), new Vector2(940, 124), Dim);
            Img(r, Items.Icon((Item)i), new Vector2(0, .5f), new Vector2(76, 0), new Vector2(100, 100));
            var n = Txt(r, Items.Names[i], 44, new Vector2(0, .5f), new Vector2(150 + 140, 16), Items.Colors[i], TextAnchor.MiddleLeft, 280); n.fontStyle = FontStyle.Italic;
            Txt(r, what[i], 30, new Vector2(0, .5f), new Vector2(150 + 330, -26), Soft, TextAnchor.MiddleLeft, 660);
        }
        Btn(col, "GOT IT", new Vector2(.5f, 0), new Vector2(0, 170), new Vector2(560, 140), Pink, Color.white, () => ShowMenu(), 60);
    }

    public void ShowFinding(string text)
    {
        hud.gameObject.SetActive(false);
        var s = Screen();
        var col = Column(s);
        var sp = Img(col, ring, new Vector2(.5f, .5f), new Vector2(0, 160), new Vector2(220, 220)); sp.color = Cyan;
        Img(sp.transform, disc, new Vector2(.5f, 1), new Vector2(0, -10), new Vector2(46, 46)).color = Pink;
        spinner = sp.transform;
        var t = Txt(col, text, 60, new Vector2(.5f, .5f), new Vector2(0, -40), Color.white, TextAnchor.MiddleCenter, 1000); t.fontStyle = FontStyle.Italic; Outline(t, 4);
        Txt(col, "bots will fill any empty seats", 34, new Vector2(.5f, .5f), new Vector2(0, -120), Soft, TextAnchor.MiddleCenter, 1000);
        Btn(col, "CANCEL", new Vector2(.5f, .5f), new Vector2(0, -300), new Vector2(420, 120), Dim, Color.white, () => Game.I.CancelFind(), 46);
    }

    public void ShowResults(List<Kart> order, int place, int coins)
    {
        hud.gameObject.SetActive(false);
        var g = Game.I;
        var s = Screen();
        var col = Column(s);
        string[] ord = { "1ST", "2ND", "3RD", "4TH", "5TH", "6TH", "7TH", "8TH" };
        var t = Txt(col, place == 1 ? "WINNER!" : ord[Mathf.Clamp(place - 1, 0, 7)] + " PLACE", 130, new Vector2(.5f, 1), new Vector2(0, -190), place == 1 ? Gold : place <= 3 ? Cyan : Color.white);
        t.fontStyle = FontStyle.Italic; Outline(t, 7);
        int n = Mathf.Min(order.Count, 8);
        for (int i = 0; i < n; i++)
        {
            var k = order[i];
            var r = Box(col, new Vector2(.5f, 1), new Vector2(0, -360 - i * 86), new Vector2(900, 78), k.IsMe ? new Color(0.35f, 0.2f, 0.6f, 0.92f) : Dim);
            Txt(r, ord[i], 40, new Vector2(0, .5f), new Vector2(70, 0), i == 0 ? Gold : Color.white, TextAnchor.MiddleCenter, 120).fontStyle = FontStyle.Italic;
            Img(r, disc, new Vector2(0, .5f), new Vector2(150, 0), new Vector2(30, 30)).color = k.Tint;
            Txt(r, k.IsMe ? "YOU (" + k.Name + ")" : k.Name, 40, new Vector2(0, .5f), new Vector2(180 + 250, 0), Color.white, TextAnchor.MiddleLeft, 500);
            Txt(r, g.Scores[k.Slot] + (g.Scores[k.Slot] == 1 ? " HIT" : " HITS"), 40, new Vector2(1, .5f), new Vector2(-110, 0), Gold, TextAnchor.MiddleCenter, 200);
        }
        float y = 520;
        var cb = Box(col, new Vector2(.5f, 0), new Vector2(0, y + 200), new Vector2(560, 120), Dim);
        Img(cb, coinSpr, new Vector2(0, .5f), new Vector2(70, 0), new Vector2(80, 80));
        var ct = Txt(cb, "+" + coins, 70, new Vector2(.5f, .5f), new Vector2(40, 0), Gold, TextAnchor.MiddleCenter, 400); ct.fontStyle = FontStyle.Italic;
        if (WebBridge.AdsAvailable)
        {
            Button dbl = null;
            dbl = Btn(col, "2X COINS  (AD)", new Vector2(.5f, 0), new Vector2(0, y + 60), new Vector2(560, 110), Lime, Ink, () =>
            {
                g.DoubleCoins(coins, ok =>
                {
                    if (!ok || !dbl) return;
                    ct.text = "+" + (coins * 2); dbl.interactable = false;
                    dbl.GetComponentInChildren<Text>().text = "COINS DOUBLED!";
                });
            }, 46);
            y -= 70;
        }
        var again = Btn(col, "PLAY AGAIN", new Vector2(.5f, 0), new Vector2(0, y - 90), new Vector2(760, 170), Pink, Color.white, () => g.PlayAgain(), 80);
        StartCoroutine(Pulse(again.transform));
        Btn(col, "MENU", new Vector2(.5f, 0), new Vector2(-200, y - 260), new Vector2(360, 110), Dim, Color.white, () => g.GoMenu(), 44);
        if (g.RoomCode.Length > 0 && !WebBridge.OnPortal)
        {
            var inv = Btn(col, "INVITE", new Vector2(.5f, 0), new Vector2(200, y - 260), new Vector2(360, 110), Cyan, Ink, () => { }, 44);
            inv.gameObject.AddComponent<ShareOnPress>().Text = () => "Race me in KART CHAOS - room " + g.RoomCode + "\n" + InviteUrl();
        }
        else Btn(col, "GARAGE", new Vector2(.5f, 0), new Vector2(200, y - 260), new Vector2(360, 110), Gold, Ink, () => { g.LeaveToGarage(); }, 44);
    }

    string InviteUrl() => "https://kartchaos.vercel.app/?room=" + Game.I.RoomCode;

    // ---------------------------------------------------------------- garage
    int gIdx = -1;
    public void ShowGarage()
    {
        hud.gameObject.SetActive(false);
        var g = Game.I;
        if (gIdx < 0) gIdx = g.Save.vehicle;
        g.ShowVehicle(gIdx);
        var s = Screen(false);
        Kit.Scrim(s, false, 900, new Color(0.06f, 0.03f, 0.18f, 0.9f));
        Kit.Scrim(s, true, 300, new Color(0.06f, 0.03f, 0.18f, 0.6f));
        var t = Txt(s, "GARAGE", 90, new Vector2(.5f, 1), new Vector2(0, Landscape ? -90 : -210), Gold); t.fontStyle = FontStyle.Italic; Outline(t, 5);
        CoinPill(s, new Vector2(1, 1), new Vector2(-190, -80));
        Btn(s, "BACK", new Vector2(0, 1), new Vector2(130, -80), new Vector2(200, 100), Dim, Color.white, () => { gIdx = -1; g.HideShowroom(); g.GoMenu(); }, 44);

        var panel = Rect("panel", s, new Vector2(.5f, 0), new Vector2(0, 330), new Vector2(1000, 600));
        var v = Vehicles.All[gIdx];
        bool owned = g.Owns(gIdx), equipped = g.Save.vehicle == gIdx;
        var nm = Txt(panel, v.Name, 96, new Vector2(.5f, .5f), new Vector2(0, 170), Color.white); nm.fontStyle = FontStyle.Italic; Outline(nm, 5);
        Txt(panel, (gIdx + 1) + " / " + Vehicles.All.Length + "   -   looks only, every kart drives the same", 30, new Vector2(.5f, .5f), new Vector2(0, 90), Soft, TextAnchor.MiddleCenter, 1000);
        Btn(panel, "<", new Vector2(.5f, .5f), new Vector2(-420, 170), new Vector2(130, 130), Dim, Color.white, () => { gIdx = (gIdx + Vehicles.All.Length - 1) % Vehicles.All.Length; ShowGarage(); }, 70);
        Btn(panel, ">", new Vector2(.5f, .5f), new Vector2(420, 170), new Vector2(130, 130), Dim, Color.white, () => { gIdx = (gIdx + 1) % Vehicles.All.Length; ShowGarage(); }, 70);
        if (equipped) Btn(panel, "DRIVING THIS", new Vector2(.5f, .5f), new Vector2(0, -40), new Vector2(620, 140), Dim, Lime, () => { }, 54);
        else if (owned) Btn(panel, "DRIVE THIS", new Vector2(.5f, .5f), new Vector2(0, -40), new Vector2(620, 140), Lime, Ink, () => { g.Equip(gIdx); ShowGarage(); }, 58);
        else
        {
            bool can = g.Save.coins >= v.Cost;
            var b = Btn(panel, "UNLOCK  " + v.Cost.ToString("N0"), new Vector2(.5f, .5f), new Vector2(0, -40), new Vector2(620, 140), can ? Gold : Dim, can ? Ink : Soft, () => { if (g.Buy(gIdx)) { Banner("UNLOCKED!", v.Name); ShowGarage(); } else Toast("NOT ENOUGH COINS - WIN MATCHES TO EARN MORE"); }, 54);
        }
        if (WebBridge.AdsAvailable)
            Btn(panel, "+75 COINS  (AD)", new Vector2(.5f, .5f), new Vector2(0, -200), new Vector2(520, 110), Cyan, Ink, () => g.FreeCoins(ok => { if (ok) ShowGarage(); }), 44);
    }

    // ---------------------------------------------------------------- pause
    public void ShowPause()
    {
        var g = Game.I;
        if (g.State != Game.St.Play && g.State != Game.St.Countdown) return;
        if (g.CanPause) Time.timeScale = 0;
        var s = Screen();
        var col = Column(s);
        var t = Txt(col, g.CanPause ? "PAUSED" : "MENU", 120, new Vector2(.5f, .5f), new Vector2(0, 420), Gold); t.fontStyle = FontStyle.Italic; Outline(t, 6);
        if (!g.CanPause) Txt(col, "online matches keep going", 36, new Vector2(.5f, .5f), new Vector2(0, 310), Soft, TextAnchor.MiddleCenter, 900);
        Btn(col, "RESUME", new Vector2(.5f, .5f), new Vector2(0, 120), new Vector2(620, 160), Lime, Ink, () => { Time.timeScale = 1; CloseScreens(); }, 64);
        Btn(col, "QUIT MATCH", new Vector2(.5f, .5f), new Vector2(0, -80), new Vector2(620, 130), Dim, Pink, () => g.QuitMatch(), 50);
        if (g.RoomCode.Length > 0)
        {
            var c = Txt(col, "ROOM CODE  " + g.RoomCode, 64, new Vector2(.5f, .5f), new Vector2(0, -260), Cyan); c.fontStyle = FontStyle.Italic; Outline(c, 4);
            Txt(col, "friends: PLAY WITH FRIENDS > JOIN WITH CODE", 32, new Vector2(.5f, .5f), new Vector2(0, -330), Soft, TextAnchor.MiddleCenter, 1000);
            if (!WebBridge.OnPortal)
            {
                var inv = Btn(col, "SEND INVITE LINK", new Vector2(.5f, .5f), new Vector2(0, -450), new Vector2(560, 110), Cyan, Ink, () => { }, 44);
                inv.gameObject.AddComponent<ShareOnPress>().Text = () => "Race me in KART CHAOS - room " + g.RoomCode + "\n" + InviteUrl();
            }
        }
    }
}

public class VirtualStick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public RectTransform Base, Knob;
    public Vector2 Home;
    public Vector2 Value;
    int id = -999;
    Vector2 origin;
    const float Radius = 120f;

    Vector2 Local(PointerEventData e)
    {
        var parent = (RectTransform)Base.parent;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, e.position, e.pressEventCamera, out var lp);
        return lp - parent.rect.min;   // bottom-left anchored coordinates
    }
    public void OnPointerDown(PointerEventData e)
    {
        if (id != -999) return;
        id = e.pointerId; origin = Local(e);
        Base.anchoredPosition = Knob.anchoredPosition = origin;
        Value = Vector2.zero;
    }
    public void OnDrag(PointerEventData e)
    {
        if (e.pointerId != id) return;
        var d = Vector2.ClampMagnitude(Local(e) - origin, Radius);
        Knob.anchoredPosition = origin + d;
        Value = d / Radius;
        if (Value.magnitude < 0.15f) Value = Vector2.zero;
        else Value = Value.normalized * Mathf.Max(0.75f, Value.magnitude);   // light touch still drives with purpose
    }
    public void OnPointerUp(PointerEventData e)
    {
        if (e.pointerId != id) return;
        id = -999; Value = Vector2.zero;
        Base.anchoredPosition = Knob.anchoredPosition = Home;
    }
    void OnDisable() { id = -999; Value = Vector2.zero; if (Base) Base.anchoredPosition = Knob.anchoredPosition = Home; }
}

public class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    readonly HashSet<int> ids = new HashSet<int>();
    public bool Held => ids.Count > 0;
    public bool Pressed;
    public void OnPointerDown(PointerEventData e) { if (ids.Count == 0) Pressed = true; ids.Add(e.pointerId); }
    public void OnPointerUp(PointerEventData e) { ids.Remove(e.pointerId); }
    void OnDisable() { ids.Clear(); Pressed = false; }
}
