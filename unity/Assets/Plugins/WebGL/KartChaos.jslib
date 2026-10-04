mergeInto(LibraryManager.library, {
  SD_Rewarded: function (goPtr) {
    var go = UTF8ToString(goPtr);
    var reply = function (ok) { try { window.unityInstance && window.unityInstance.SendMessage(go, "OnRewarded", ok ? "1" : "0"); } catch (e) {} };
    if (window.SD && window.SD.rewarded) window.SD.rewarded().then(function (ok) { reply(ok); }, function () { reply(false); });
    else reply(false);
  },
  SD_AdsAvailable: function () { return (window.SD && window.SD.adsAvailable && window.SD.adsAvailable()) ? 1 : 0; },
  SD_Gameplay: function (on) { if (window.SD && window.SD.gameplay) window.SD.gameplay(!!on); },
  SD_Event: function (namePtr, value) { if (window.SD && window.SD.track) window.SD.track(UTF8ToString(namePtr), value); },
  SD_Ready: function () { if (window.SD && window.SD.ready) window.SD.ready(); },
  SD_Midgame: function (goPtr) {
    var go = UTF8ToString(goPtr);
    var done = function () { try { window.unityInstance && window.unityInstance.SendMessage(go, "OnMidgame", "1"); } catch (e) {} };
    if (window.SD && window.SD.midgame) window.SD.midgame().then(done, done); else done();
  },
  SD_Happy: function () { if (window.SD && window.SD.happy) window.SD.happy(); },
  SD_Portal: function () {
    var p = (window.SD && window.SD.portal) || "web";
    var n = lengthBytesUTF8(p) + 1, b = _malloc(n);
    stringToUTF8(p, b, n);
    return b;
  },

  KC_Vibrate: function (ms) { try { if (navigator.vibrate && (!navigator.userActivation || navigator.userActivation.hasBeenActive)) navigator.vibrate(ms); } catch (e) {} },

  KC_ArmShare: function (textPtr) {
    var text = UTF8ToString(textPtr), w = window;
    var doShare = function () {
      if (!w.__kcPending) return;
      var t = w.__kcPending; w.__kcPending = null;
      if (navigator.share) navigator.share({ title: "KART CHAOS", text: t }).catch(function () {});
      else if (navigator.clipboard) navigator.clipboard.writeText(t).then(function () { w.kcToast && w.kcToast("Copied! Paste it anywhere"); });
      if (w.SD && w.SD.track) w.SD.track("share", 0);
    };
    if (!w.__kcHooked) {
      w.__kcHooked = true;
      ["pointerup", "touchend", "click"].forEach(function (ev) { w.addEventListener(ev, doShare, true); });
    }
    w.__kcPending = text;
    setTimeout(doShare, 450);
  },

  KC_AskName: function (cur) { if (window.kart) window.kart.askName(UTF8ToString(cur)); },
  KC_AskCode: function () { if (window.kart) window.kart.askCode(); },
  KC_SavedName: function () {
    var p = (window.kart && window.kart.savedName()) || "";
    var n = lengthBytesUTF8(p) + 1, b = _malloc(n);
    stringToUTF8(p, b, n);
    return b;
  },
  KC_RoomCode: function () {
    var p = (window.kart && window.kart.roomCode()) || "";
    var n = lengthBytesUTF8(p) + 1, b = _malloc(n);
    stringToUTF8(p, b, n);
    return b;
  },
  KC_NetOpen: function (helloPtr) { if (window.kart) window.kart.open(UTF8ToString(helloPtr)); },
  KC_NetSend: function (jsonPtr) { if (window.kart) window.kart.send(UTF8ToString(jsonPtr)); },
  KC_NetClose: function () { if (window.kart) window.kart.close(); }
});
