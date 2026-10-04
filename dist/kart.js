// KART CHAOS page side: the match socket (a thin pipe; all game logic is in Unity), plus the name and room-code prompts.
// Unity calls window.kart.*; socket traffic goes back via SendMessage("Game", "OnNet" | "OnNetOpen" | "OnNetClosed").
(function () {
  var qs = new URLSearchParams(location.search);
  var API = qs.get("api") || "https://orbyt-api-production-29f6.up.railway.app";
  var WS = API.replace(/^http/, "ws") + "/kart";
  var ws = null;

  function store(k, v) { try { if (v === undefined) return localStorage.getItem(k); localStorage.setItem(k, v); } catch (e) { return null; } }
  function toUnity(method, s) { try { window.unityInstance && window.unityInstance.SendMessage("Game", method, s == null ? "" : String(s)); } catch (e) {} }
  function track(n, v) { window.SD && window.SD.track && window.SD.track(n, v || 0); }

  // ---------------------------------------------------------------- socket
  function open(hello) {
    close();
    try { ws = new WebSocket(WS); } catch (e) { toUnity("OnNetClosed", ""); return; }
    var me = ws;
    ws.onopen = function () { if (ws !== me) return; ws.send(hello); toUnity("OnNetOpen", ""); };
    ws.onmessage = function (ev) { if (ws === me) toUnity("OnNet", ev.data); };
    ws.onclose = function () { if (ws === me) { ws = null; toUnity("OnNetClosed", ""); } };
    ws.onerror = function () {};
  }
  function send(s) { if (ws && ws.readyState === 1) ws.send(s); }
  function close() { if (ws) { var w = ws; ws = null; try { w.close(); } catch (e) {} } }
  addEventListener("pagehide", close);

  // ---------------------------------------------------------------- prompts
  var css = document.createElement("style");
  css.textContent = [
    ".kcov{position:fixed;inset:0;z-index:20;display:none;align-items:center;justify-content:center;background:rgba(14,8,40,.6);backdrop-filter:blur(3px);font-family:system-ui,-apple-system,'Segoe UI',Roboto,sans-serif;color:#fff}",
    ".kcov.on{display:flex}",
    ".kcov .card{width:min(420px,92vw);background:#241a5c;border:4px solid #ffd84a;border-radius:26px;box-shadow:0 20px 60px rgba(0,0,0,.5);padding:22px;text-align:center;animation:kcin .25s ease}",
    "@keyframes kcin{from{transform:scale(.9);opacity:0}}",
    ".kcov h2{margin:0 0 6px;font-size:30px;font-weight:900;font-style:italic;letter-spacing:1px;color:#ffd84a}",
    ".kcov p{opacity:.75;font-size:14px;margin:0 0 12px}",
    ".kcov input{width:100%;box-sizing:border-box;padding:14px;border-radius:14px;border:3px solid #35d6ff;background:#fff;color:#1b1340;font-size:24px;font-weight:900;text-align:center;letter-spacing:4px;text-transform:uppercase;outline:none}",
    ".kcov .err{color:#ff7a9c;min-height:18px;font-size:13px;margin-top:6px}",
    ".kcov .btn{display:block;width:100%;padding:16px;margin:8px 0 0;border-radius:16px;border:0;font-weight:900;font-style:italic;font-size:18px;letter-spacing:1px;cursor:pointer}",
    ".kcov .p{background:#ff4f8b;color:#fff}.kcov .s{background:rgba(255,255,255,.12);color:#fff}"
  ].join("\n");
  document.head.appendChild(css);

  var ov = document.createElement("div"); ov.className = "kcov"; ov.innerHTML = '<div class="card"></div>';
  document.body.appendChild(ov);
  ["keydown", "keyup", "keypress"].forEach(function (t) { ov.addEventListener(t, function (e) { e.stopPropagation(); }, true); });

  function ask(title, sub, value, max, method, clean) {
    ov.querySelector(".card").innerHTML = "<h2>" + title + "</h2><p>" + sub + "</p>" +
      '<input maxlength="' + max + '" autocomplete="off" autocapitalize="characters" spellcheck="false">' +
      '<div class="err"></div><button class="btn p" data-a="ok">OK</button><button class="btn s" data-a="no">CANCEL</button>';
    var input = ov.querySelector("input"); input.value = value || "";
    ov.classList.add("on");
    setTimeout(function () { input.focus(); input.select(); }, 50);
    function done(v) { ov.classList.remove("on"); if (v != null) toUnity(method, v); }
    ov.querySelector('[data-a="ok"]').onclick = function () {
      var v = clean(input.value);
      if (v.length < 2) { ov.querySelector(".err").textContent = "Too short."; return; }
      done(v);
    };
    ov.querySelector('[data-a="no"]').onclick = function () { done(null); };
    input.onkeydown = function (e) { if (e.key === "Enter") ov.querySelector('[data-a="ok"]').click(); if (e.key === "Escape") done(null); };
  }
  var cleanName = function (s) { return String(s || "").toUpperCase().replace(/[^A-Z0-9 _.-]/g, "").trim().slice(0, 12); };
  var cleanCode = function (s) { return String(s || "").toUpperCase().replace(/[^A-Z0-9]/g, "").slice(0, 6); };

  // room code from ?room= on our site, or the portal's own invite parameter
  function roomCode() {
    var c = qs.get("room") || "";
    try { if (!c && window.CrazyGames && window.CrazyGames.SDK) c = window.CrazyGames.SDK.game.getInviteParam("room") || ""; } catch (e) {}
    try { if (!c && window.PokiSDK && window.PokiSDK.getURLParam) c = window.PokiSDK.getURLParam("room") || ""; } catch (e) {}
    return cleanCode(c);
  }

  window.kcToast = function (m) { window.smToast && window.smToast(m); };
  window.kart = {
    open: open, send: send, close: close,
    askName: function (cur) {
      ask("YOUR DRIVER NAME", "Shown above your kart and on the scoreboard", cur, 12, "OnName", function (s) { var v = cleanName(s); if (v.length >= 2) store("kc_name", v); return v; });
      track("name_prompt");
    },
    askCode: function () { ask("JOIN A ROOM", "Type the code your friend sees in their pause menu", "", 6, "OnCode", cleanCode); track("code_prompt"); },
    savedName: function () { return store("kc_name") || ""; },
    roomCode: roomCode
  };
})();
