using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.EventSystems;

// Page integrations: portal ads, share sheet, haptics, name prompt and the match socket (kart.js).
public class WebBridge : MonoBehaviour
{
    public static WebBridge I;
    Action<bool> pending;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void SD_Rewarded(string goName);
    [DllImport("__Internal")] static extern void SD_Gameplay(int on);
    [DllImport("__Internal")] static extern void SD_Event(string name, int value);
    [DllImport("__Internal")] static extern void SD_Ready();
    [DllImport("__Internal")] static extern int SD_AdsAvailable();
    [DllImport("__Internal")] static extern void SD_Midgame(string goName);
    [DllImport("__Internal")] static extern void SD_Happy();
    [DllImport("__Internal")] static extern string SD_Portal();
    [DllImport("__Internal")] static extern void KC_ArmShare(string text);
    [DllImport("__Internal")] static extern void KC_Vibrate(int ms);
    [DllImport("__Internal")] static extern void KC_AskName(string current);
    [DllImport("__Internal")] static extern void KC_AskCode();
    [DllImport("__Internal")] static extern string KC_SavedName();
    [DllImport("__Internal")] static extern void KC_NetOpen(string hello);
    [DllImport("__Internal")] static extern void KC_NetSend(string json);
    [DllImport("__Internal")] static extern void KC_NetClose();
    [DllImport("__Internal")] static extern string KC_RoomCode();
#endif

    void Awake() { I = this; gameObject.name = "WebBridge"; }

    public static bool AdsAvailable
    {
        get
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return SD_AdsAvailable() == 1;
#else
            return false;
#endif
        }
    }

    // Which build this is: "web" (our own site), "crazygames", "poki" or "gd" (GameDistribution).
    static string portal;
    public static string Portal
    {
        get
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (portal == null) portal = SD_Portal() ?? "web";
#else
            if (portal == null) portal = "web";
#endif
            return portal;
        }
    }
    public static bool OnPortal => Portal != "web";

    // Every ad mutes the game and freezes it until the ad is over (portal rule).
    int holds; float heldScale = 1f;
    public bool InAd => holds > 0;
    void Hold(bool on)
    {
        if (on)
        {
            if (holds++ == 0) { heldScale = Time.timeScale; Time.timeScale = 0f; Sfx.I.SetMuted(true); }
        }
        else if (holds > 0 && --holds == 0) { Time.timeScale = heldScale; Sfx.I.SetMuted(Game.I.Save.muted); }
    }

    public void ShowRewarded(Action<bool> done)
    {
        pending = done;
        Hold(true);
        Event("rewarded_ask");
#if UNITY_WEBGL && !UNITY_EDITOR
        SD_Rewarded(gameObject.name);
#else
        OnRewarded("1");
#endif
    }

    public void OnRewarded(string ok)
    {
        Hold(false);
        Event(ok == "1" ? "rewarded_ok" : "rewarded_fail");
        var cb = pending; pending = null;
        cb?.Invoke(ok == "1");
    }

    // Interstitial at a natural break (before a match). The portal decides whether one actually plays.
    Action midDone;
    public void Midgame(Action done)
    {
        if (!OnPortal || !AdsAvailable) { done(); return; }
        midDone = done;
        Hold(true);
#if UNITY_WEBGL && !UNITY_EDITOR
        SD_Midgame(gameObject.name);
#else
        OnMidgame("1");
#endif
    }
    public void OnMidgame(string _)
    {
        Hold(false);
        var cb = midDone; midDone = null;
        cb?.Invoke();
    }

    // The portal paused the game on its own (GameDistribution shows ads through SDK_GAME_PAUSE / START).
    public void OnAdPause(string on) => Hold(on == "1");

#if UNITY_WEBGL && !UNITY_EDITOR
    public static void Happy() => SD_Happy();
    public static void Gameplay(bool on) => SD_Gameplay(on ? 1 : 0);
    public static void Event(string name, int value = 0) => SD_Event(name, value);
    public static void Ready() => SD_Ready();
    public static void Vibrate(int ms) => KC_Vibrate(ms);
    public static void AskName(string current) => KC_AskName(current);
    public static void AskCode() => KC_AskCode();
    public static string SavedName() => KC_SavedName() ?? "";
    public static void NetOpen(string hello) => KC_NetOpen(hello);
    public static void NetSend(string json) => KC_NetSend(json);
    public static void NetClose() => KC_NetClose();
    public static string RoomCode() => KC_RoomCode() ?? "";
    // Web Share needs a live gesture: arm on pointer-down, the page fires it on pointer-up.
    public static void ArmShare(string text) => KC_ArmShare(text);
    public static bool HasPage => true;
#else
    public static void Happy() { }
    public static void Gameplay(bool on) { }
    public static void Event(string name, int value = 0) { }
    public static void Ready() { }
    public static void Vibrate(int ms) { }
    public static void AskName(string current) { }
    public static void AskCode() { }
    public static string SavedName() => "";
    public static void NetOpen(string hello) { }
    public static void NetSend(string json) { }
    public static void NetClose() { }
    public static string RoomCode() => "";
    public static void ArmShare(string text) { GUIUtility.systemCopyBuffer = text; }
    public static bool HasPage => false;
#endif
}

public class ShareOnPress : MonoBehaviour, IPointerDownHandler
{
    public Func<string> Text;
    public void OnPointerDown(PointerEventData e) { if (Text != null) WebBridge.ArmShare(Text()); }
}
