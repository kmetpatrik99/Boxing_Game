using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace BoxingGame
{
    public enum GamePhase { Menu, Countdown, Fight, RoundOver, MatchOver }

    /// <summary>One selectable arena. The builder fills these in.</summary>
    [Serializable]
    public class MapDefinition
    {
        public string name = "Arena";
        public string subtitle = "";
        public GameObject root;                       // toggled on/off when the map is selected
        public Vector3 center = Vector3.zero;
        public Vector2 halfSize = new Vector2(2.4f, 2.4f);
        public float radius = 0f;                     // > 0 = circular fighting area
        public float groundY = 0f;

        [Header("Environment")]
        public bool applyEnvironment = true;
        public bool skyboxClear = true;
        public Material skybox;
        public AmbientMode ambientMode = AmbientMode.Skybox;
        public Color ambientColor = new Color(0.3f, 0.3f, 0.35f);
        public bool fog = false;
        public FogMode fogMode = FogMode.ExponentialSquared;
        public Color fogColor = Color.black;
        public float fogDensity = 0.02f;
        public float fogStart = 10f;
        public float fogEnd = 60f;
        public Color cameraBackground = Color.black;
    }

    /// <summary>Match flow (menu, rounds, timer, KO / decision), difficulty presets, map switching and the HUD.</summary>
    public class GameManager : MonoBehaviour
    {
        [Header("References")]
        public Fighter player;
        public Fighter opponent;
        public BoxerAI ai;

        [Header("Maps (filled in by the scene builder)")]
        public MapDefinition[] maps;

        [Header("Legacy single-ring settings (used only if 'maps' is empty)")]
        public Vector3 ringCenter = Vector3.zero;
        public Vector2 ringHalfSize = new Vector2(2.4f, 2.4f);
        public float groundY = 0f;
        public float startDistance = 2.4f;

        [Header("Match rules")]
        public int maxRounds = 3;
        public int roundsToWin = 2;
        public float roundTime = 60f;
        [Range(0, 4)] public int difficultyIndex = 1;
        public int mapIndex = 0;

        public GamePhase Phase { get; private set; } = GamePhase.Menu;

        // ------------------------------------------------------------------ difficulty presets
        struct Preset
        {
            public string label, opponent, perk;
            public float ai, damage, health;
            public Color color;
        }

        static readonly Preset[] Presets =
        {
            new Preset { label = "EASY",       opponent = "Rookie Rico",       perk = "Slow reactions\nForgiving",        ai = 0.20f, damage = 1f, health = 1f,    color = new Color(0.35f, 0.85f, 0.50f) },
            new Preset { label = "NORMAL",     opponent = "Iron Jack",         perk = "Balanced fighter",                  ai = 0.50f, damage = 1f, health = 1f,    color = new Color(0.35f, 0.70f, 1.00f) },
            new Preset { label = "HARD",       opponent = "Savage Sam",        perk = "Sharp and relentless",              ai = 0.85f, damage = 1f, health = 1f,    color = new Color(1.00f, 0.65f, 0.20f) },
            new Preset { label = "EXTREME",    opponent = "The Butcher",       perk = "2x damage",                         ai = 0.95f, damage = 2f, health = 1f,    color = new Color(1.00f, 0.30f, 0.30f) },
            new Preset { label = "IMPOSSIBLE", opponent = "Prime Mike Tyson",  perk = "3x damage\n+50% health",            ai = 1.00f, damage = 3f, health = 1.5f,  color = new Color(0.75f, 0.40f, 1.00f) },
        };

        // remembered across "play again" scene reloads
        static int lastDifficulty = -1, lastMap = -1;

        // ------------------------------------------------------------------ state
        int round, playerWins, opponentWins;
        float timer, phaseTimer;
        int lastCount = -1;
        float baseOpponentHealth;
        float ringRadius;
        BoxingCamera boxCam;
        Camera cam;

        string bannerMain = "", bannerSub = "";
        Color bannerColor = Color.white;
        float bannerStart, bannerUntil;

        string popup = ""; float popupUntil; bool popupGood;
        float lagP = 1f, lagO = 1f;

        // ------------------------------------------------------------------ lifecycle
        void Start()
        {
            boxCam = FindFirstObjectByType<BoxingCamera>();
            if (boxCam != null) cam = boxCam.GetComponent<Camera>();

            baseOpponentHealth = opponent.maxHealth;
            player.opponent = opponent;
            opponent.opponent = player;
            player.Died += OnFighterDied;
            opponent.Died += OnFighterDied;
            player.Dodged += OnDodged;
            opponent.Dodged += OnDodged;

            if (maps == null || maps.Length == 0)
            {
                maps = new[]
                {
                    new MapDefinition
                    {
                        name = "Arena", center = ringCenter, halfSize = ringHalfSize, groundY = groundY, applyEnvironment = false
                    }
                };
            }

            if (lastDifficulty >= 0) difficultyIndex = lastDifficulty;
            if (lastMap >= 0) mapIndex = lastMap;
            difficultyIndex = Mathf.Clamp(difficultyIndex, 0, Presets.Length - 1);
            mapIndex = Mathf.Clamp(mapIndex, 0, maps.Length - 1);

            ApplyMap(mapIndex);
            PlaceFighters();
            SetFightersActive(false);
            Phase = GamePhase.Menu;
        }

        void OnDestroy()
        {
            if (player != null) { player.Died -= OnFighterDied; player.Dodged -= OnDodged; }
            if (opponent != null) { opponent.Died -= OnFighterDied; opponent.Dodged -= OnDodged; }
        }

        void OnDodged(Fighter f)
        {
            popupGood = f == player;
            popup = popupGood ? "DODGE!" : "MISSED!";
            popupUntil = Time.time + 0.7f;
        }

        // ------------------------------------------------------------------ maps
        void ApplyMap(int index)
        {
            for (int i = 0; i < maps.Length; i++)
                if (maps[i].root != null) maps[i].root.SetActive(i == index);

            var m = maps[index];
            ringCenter = m.center;
            ringHalfSize = m.halfSize;
            ringRadius = m.radius;
            groundY = m.groundY;
            Setup(player);
            Setup(opponent);
            if (boxCam != null) boxCam.groundY = m.groundY;

            if (m.applyEnvironment)
            {
                RenderSettings.skybox = m.skybox;
                RenderSettings.ambientMode = m.ambientMode;
                if (m.ambientMode == AmbientMode.Flat) RenderSettings.ambientLight = m.ambientColor;
                RenderSettings.fog = m.fog;
                RenderSettings.fogMode = m.fogMode;
                RenderSettings.fogColor = m.fogColor;
                RenderSettings.fogDensity = m.fogDensity;
                RenderSettings.fogStartDistance = m.fogStart;
                RenderSettings.fogEndDistance = m.fogEnd;
                if (cam != null)
                {
                    cam.clearFlags = m.skyboxClear ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
                    cam.backgroundColor = m.cameraBackground;
                }
                DynamicGI.UpdateEnvironment();
            }
        }

        void Setup(Fighter f)
        {
            f.ringCenter = new Vector3(ringCenter.x, groundY, ringCenter.z);
            f.ringHalfSize = ringHalfSize;
            f.ringRadius = ringRadius;
        }

        void PlaceFighters()
        {
            Vector3 c = new Vector3(ringCenter.x, groundY, ringCenter.z);
            Vector3 pPos = c + new Vector3(0f, 0f, -startDistance * 0.5f);
            Vector3 oPos = c + new Vector3(0f, 0f, startDistance * 0.5f);
            player.ResetFighter(pPos, Quaternion.LookRotation(Vector3.forward));
            opponent.ResetFighter(oPos, Quaternion.LookRotation(Vector3.back));
            if (ai != null) ai.ResetAI();
            lagP = lagO = 1f;
        }

        void SetFightersActive(bool on)
        {
            player.Active = on;
            opponent.Active = on;
            if (!on)
            {
                player.MoveInput = opponent.MoveInput = Vector2.zero;
                player.GuardHeld = opponent.GuardHeld = false;
            }
        }

        // ------------------------------------------------------------------ flow
        void Update()
        {
            if (player == null || opponent == null) return;

            // trailing "damage" bars
            float hpP = Mathf.Clamp01(player.Health / player.maxHealth);
            float hpO = Mathf.Clamp01(opponent.Health / opponent.maxHealth);
            lagP = hpP >= lagP ? hpP : Mathf.MoveTowards(lagP, hpP, 0.35f * Time.deltaTime);
            lagO = hpO >= lagO ? hpO : Mathf.MoveTowards(lagO, hpO, 0.35f * Time.deltaTime);

            var kb = Keyboard.current;
            var pad = Gamepad.current;
            bool confirm = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
                           || (pad != null && pad.startButton.wasPressedThisFrame);

            switch (Phase)
            {
                case GamePhase.Menu:
                    HandleMenuInput(kb, pad);
                    if (confirm || (kb != null && kb.spaceKey.wasPressedThisFrame)) StartMatch();
                    break;

                case GamePhase.Countdown:
                    phaseTimer -= Time.deltaTime;
                    if (phaseTimer > 0f)
                    {
                        int n = Mathf.CeilToInt(phaseTimer);
                        if (n != lastCount)
                        {
                            lastCount = n;
                            SetBanner(n.ToString(), "ROUND " + round, 1f, Color.white);
                        }
                    }
                    else BeginFight();
                    break;

                case GamePhase.Fight:
                    timer -= Time.deltaTime;
                    if (timer <= 0f) { timer = 0f; EndRoundByDecision(); }
                    break;

                case GamePhase.RoundOver:
                    phaseTimer -= Time.deltaTime;
                    if (phaseTimer <= 0f)
                    {
                        bool over = playerWins >= roundsToWin || opponentWins >= roundsToWin || round >= maxRounds;
                        if (over) FinishMatch(); else StartRound();
                    }
                    break;

                case GamePhase.MatchOver:
                    if (confirm || (kb != null && kb.rKey.wasPressedThisFrame))
                        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                    break;
            }
        }

        void HandleMenuInput(Keyboard kb, Gamepad pad)
        {
            int d = difficultyIndex;
            int m = mapIndex;

            if (kb != null)
            {
                if (kb.digit1Key.wasPressedThisFrame) d = 0;
                if (kb.digit2Key.wasPressedThisFrame) d = 1;
                if (kb.digit3Key.wasPressedThisFrame) d = 2;
                if (kb.digit4Key.wasPressedThisFrame) d = 3;
                if (kb.digit5Key.wasPressedThisFrame) d = 4;
                if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) d--;
                if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) d++;
                if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) m--;
                if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame || kb.mKey.wasPressedThisFrame) m++;
            }
            if (pad != null)
            {
                if (pad.dpad.left.wasPressedThisFrame) d--;
                if (pad.dpad.right.wasPressedThisFrame) d++;
                if (pad.dpad.up.wasPressedThisFrame) m--;
                if (pad.dpad.down.wasPressedThisFrame) m++;
            }

            difficultyIndex = Mathf.Clamp(d, 0, Presets.Length - 1);
            int count = maps.Length;
            m = ((m % count) + count) % count;
            if (m != mapIndex)
            {
                mapIndex = m;
                ApplyMap(mapIndex);
                PlaceFighters();
            }
        }

        void StartMatch()
        {
            lastDifficulty = difficultyIndex;
            lastMap = mapIndex;

            var p = Presets[difficultyIndex];
            if (ai != null) ai.SetDifficulty(p.ai);
            opponent.damageMultiplier = p.damage;
            opponent.maxHealth = baseOpponentHealth * p.health;
            opponent.displayName = p.opponent;
            player.displayName = "YOU";

            round = 0;
            playerWins = opponentWins = 0;
            StartRound();
        }

        void StartRound()
        {
            round++;
            PlaceFighters();
            SetFightersActive(false);
            timer = roundTime;
            phaseTimer = 3f;
            lastCount = -1;
            Phase = GamePhase.Countdown;
        }

        void BeginFight()
        {
            Phase = GamePhase.Fight;
            SetFightersActive(true);
            if (ai != null) ai.ResetAI();
            SetBanner("FIGHT!", "", 1.0f, new Color(1f, 0.82f, 0.25f));
        }

        void OnFighterDied(Fighter dead)
        {
            if (Phase != GamePhase.Fight) return;
            EndRound(dead == player ? opponent : player, "K.O.!");
        }

        void EndRoundByDecision()
        {
            float p = player.Health / player.maxHealth;
            float o = opponent.Health / opponent.maxHealth;
            if (Mathf.Abs(p - o) < 0.001f) EndRound(null, "TIME! DRAW");
            else EndRound(p > o ? player : opponent, "TIME!");
        }

        void EndRound(Fighter winner, string text)
        {
            SetFightersActive(false);
            if (winner == player) playerWins++;
            else if (winner == opponent) opponentWins++;

            string sub = winner == null ? "No point awarded"
                       : (winner == player ? "You win the round" : opponent.displayName + " wins the round");
            Color c = winner == player ? new Color(0.45f, 1f, 0.6f) : (winner == opponent ? new Color(1f, 0.4f, 0.4f) : Color.white);
            SetBanner(text, sub, 3.5f, c);
            phaseTimer = 3.5f;
            Phase = GamePhase.RoundOver;
        }

        void FinishMatch()
        {
            Phase = GamePhase.MatchOver;
        }

        void SetBanner(string main, string sub, float duration, Color color)
        {
            bannerMain = main;
            bannerSub = sub;
            bannerColor = color;
            bannerStart = Time.time;
            bannerUntil = Time.time + duration;
        }

        // ================================================================== UI (IMGUI, drawn with rounded rects)

        static readonly Color PanelBg = new Color(0.04f, 0.05f, 0.08f, 0.74f);
        static readonly Color Gold = new Color(1f, 0.82f, 0.25f);
        static readonly Color Muted = new Color(1f, 1f, 1f, 0.55f);

        GUIStyle tx;
        Font uiFont;

        void EnsureStyle()
        {
            if (tx != null) return;
            try { uiFont = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI", "Helvetica Neue", "Roboto", "Arial" }, 32); }
            catch (Exception) { uiFont = null; }
            tx = new GUIStyle(GUI.skin.label) { wordWrap = false, clipping = TextClipping.Overflow };
            if (uiFont != null) tx.font = uiFont;
        }

        void OnGUI()
        {
            if (player == null || opponent == null) return;

            // Handle menu clicks separately from drawing so mouse selection works
            // while the existing keyboard/gamepad controls remain available.
            if (Phase == GamePhase.Menu && Event.current.type == EventType.MouseDown && Event.current.button == 0)
                HandleMenuMouse(Screen.width, Screen.height);

            if (Event.current.type != EventType.Repaint) return;
            EnsureStyle();

            float W = Screen.width, H = Screen.height;
            float s = Mathf.Min(H / 720f, W / 1100f);

            if (Phase == GamePhase.Menu) { DrawMenu(W, H, s); return; }

            DrawHud(W, H, s);
            DrawBanner(W, H, s);
            DrawPopup(W, H, s);
            if (Phase == GamePhase.MatchOver) DrawMatchOver(W, H, s);
            else DrawControlChips(W, H - 52f * s, s, true);
        }

        // ---- primitives
        void RR(Rect r, Color c, float radius)
        {
            if (r.width < 1f || r.height < 1f) return;
            radius = Mathf.Min(radius, Mathf.Min(r.width, r.height) * 0.5f);
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, 0f, radius);
        }

        void Fill(Rect r, Color c) { RR(r, c, 0f); }

        void Label(Rect r, string text, float size, Color c, TextAnchor anchor = TextAnchor.MiddleCenter, bool bold = true, bool shadow = true)
        {
            tx.fontSize = Mathf.Max(8, Mathf.RoundToInt(size));
            tx.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            tx.alignment = anchor;
            tx.normal.textColor = c;
            GUI.Label(r, text, tx);
        }

        float TextW(string text, float size, bool bold = true)
        {
            tx.fontSize = Mathf.Max(8, Mathf.RoundToInt(size));
            tx.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            return tx.CalcSize(new GUIContent(text)).x;
        }

        void BarFill(Rect track, float t, Color c, bool right, float radius)
        {
            float w = track.width * Mathf.Clamp01(t);
            if (w < 1f) return;
            Rect r = right ? new Rect(track.xMax - w, track.y, w, track.height) : new Rect(track.x, track.y, w, track.height);
            RR(r, c, radius);
        }

        static Color HpColor(float t)
        {
            Color green = new Color(0.30f, 0.90f, 0.50f), yellow = new Color(1f, 0.85f, 0.25f), red = new Color(1f, 0.30f, 0.30f);
            return t > 0.5f ? Color.Lerp(yellow, green, (t - 0.5f) * 2f) : Color.Lerp(red, yellow, t * 2f);
        }

        void Dot(float cx, float cy, float r, Color c) { RR(new Rect(cx - r, cy - r, r * 2f, r * 2f), c, r); }

        // ---- HUD
        void DrawHud(float W, float H, float s)
        {
            float m = 24f * s;
            float pw = Mathf.Min(W * 0.34f, 430f * s);
            float ph = 72f * s;

            DrawFighterPanel(new Rect(m, m, pw, ph), player, lagP, false, "YOU", null, default(Color), s);
            var pr = Presets[difficultyIndex];
            DrawFighterPanel(new Rect(W - m - pw, m, pw, ph), opponent, lagO, true, opponent.displayName.ToUpperInvariant(), pr.label, pr.color, s);

            // timer
            Rect tr = new Rect(W * 0.5f - 55f * s, m, 110f * s, 44f * s);
            RR(tr, PanelBg, 14f * s);
            int secs = Mathf.CeilToInt(Mathf.Max(0f, timer));
            Color tc = Color.white;
            if (Phase == GamePhase.Fight && secs <= 10) tc = Color.Lerp(Color.white, new Color(1f, 0.35f, 0.35f), 0.5f + 0.5f * Mathf.Sin(Time.time * 7f));
            Label(tr, secs.ToString(), 28f * s, tc);

            // round pill + win dots
            Rect rp = new Rect(W * 0.5f - 95f * s, tr.yMax + 6f * s, 190f * s, 26f * s);
            RR(rp, PanelBg, 13f * s);
            Label(rp, "ROUND " + Mathf.Max(1, round) + " / " + maxRounds, 12f * s, Muted, TextAnchor.MiddleCenter, true, false);
            for (int i = 0; i < roundsToWin; i++)
            {
                float cy = rp.y + rp.height * 0.5f;
                Dot(rp.x + 16f * s + i * 16f * s, cy, 5f * s, playerWins > i ? new Color(0.45f, 1f, 0.6f) : new Color(1f, 1f, 1f, 0.18f));
                Dot(rp.xMax - 16f * s - i * 16f * s, cy, 5f * s, opponentWins > i ? new Color(1f, 0.4f, 0.4f) : new Color(1f, 1f, 1f, 0.18f));
            }
        }

        void DrawFighterPanel(Rect r, Fighter f, float lag, bool right, string name, string chipText, Color chipColor, float s)
        {
            RR(r, PanelBg, 14f * s);
            float pad = 14f * s;

            Rect nameR = new Rect(r.x + pad, r.y + 8f * s, r.width - pad * 2f, 24f * s);
            Label(nameR, name, 17f * s, Color.white, right ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft);

            if (!string.IsNullOrEmpty(chipText))
            {
                float cw = TextW(chipText, 11f * s) + 18f * s;
                Rect cr = right ? new Rect(r.x + pad, r.y + 11f * s, cw, 18f * s) : new Rect(r.xMax - pad - cw, r.y + 11f * s, cw, 18f * s);
                RR(cr, new Color(chipColor.r, chipColor.g, chipColor.b, 0.28f), 9f * s);
                Label(cr, chipText, 11f * s, chipColor, TextAnchor.MiddleCenter, true, false);
            }

            Rect bar = new Rect(r.x + pad, r.y + 38f * s, r.width - pad * 2f, 16f * s);
            float hp = Mathf.Clamp01(f.Health / f.maxHealth);
            RR(bar, new Color(1f, 1f, 1f, 0.10f), 8f * s);
            BarFill(bar, lag, new Color(1f, 1f, 1f, 0.38f), right, 8f * s);
            BarFill(bar, hp, HpColor(hp), right, 8f * s);

            Rect sb = new Rect(bar.x, bar.yMax + 7f * s, bar.width, 5f * s);
            RR(sb, new Color(1f, 1f, 1f, 0.08f), 2.5f * s);
            BarFill(sb, f.Stamina / f.maxStamina, new Color(0.35f, 0.80f, 1f), right, 2.5f * s);
        }

        void DrawBanner(float W, float H, float s)
        {
            if (Phase == GamePhase.MatchOver || Time.time >= bannerUntil) return;
            float age = Time.time - bannerStart;
            float left = bannerUntil - Time.time;
            float a = Mathf.Clamp01(age / 0.12f) * Mathf.Clamp01(left / 0.35f);
            float pop = 1f + 0.35f * Mathf.Pow(1f - Mathf.Clamp01(age / 0.3f), 2f);

            Fill(new Rect(0f, H * 0.29f, W, 170f * s), new Color(0f, 0f, 0f, 0.38f * a));
            if (!string.IsNullOrEmpty(bannerSub))
                Label(new Rect(0f, H * 0.29f + 14f * s, W, 30f * s), bannerSub.ToUpperInvariant(), 18f * s, new Color(1f, 1f, 1f, 0.8f * a));
            Color c = bannerColor; c.a = a;
            Label(new Rect(0f, H * 0.29f + 36f * s, W, 130f * s), bannerMain, 96f * s * pop, c);
        }

        void DrawPopup(float W, float H, float s)
        {
            if (Time.time >= popupUntil) return;
            float a = Mathf.Clamp01((popupUntil - Time.time) / 0.3f);
            Color c = popupGood ? new Color(0.5f, 1f, 0.65f) : new Color(1f, 0.6f, 0.5f);
            c.a = a;
            Label(new Rect(0f, H * 0.62f - (1f - a) * 10f * s, W, 50f * s), popup, 34f * s, c);
        }

        void DrawControlChips(float W, float y, float s, bool background)
        {
            string[,] items =
            {
                { "WASD", "Move" }, { "LMB", "Jab" }, { "RMB", "Cross" }, { "E", "Uppercut" }, { "CTRL", "Dodge" }, { "SHIFT", "Guard" }
            };
            float keyPad = 16f * s, gap = 20f * s, inner = 8f * s, fs = 12f * s;
            int n = items.GetLength(0);
            float total = 0f;
            for (int i = 0; i < n; i++)
            {
                float kw = Mathf.Max(30f * s, TextW(items[i, 0], fs) + keyPad);
                total += kw + inner + TextW(items[i, 1], fs, false) + (i < n - 1 ? gap : 0f);
            }

            float x = (W - total) * 0.5f;
            float h = 24f * s;
            if (background) RR(new Rect(x - 18f * s, y - 7f * s, total + 36f * s, h + 14f * s), new Color(0.04f, 0.05f, 0.08f, 0.6f), (h + 14f * s) * 0.5f);

            for (int i = 0; i < n; i++)
            {
                float kw = Mathf.Max(30f * s, TextW(items[i, 0], fs) + keyPad);
                Rect kr = new Rect(x, y, kw, h);
                RR(kr, new Color(1f, 1f, 1f, 0.16f), 7f * s);
                Label(kr, items[i, 0], fs, Color.white, TextAnchor.MiddleCenter, true, false);
                x += kw + inner;
                float lw = TextW(items[i, 1], fs, false);
                Label(new Rect(x, y, lw + 4f * s, h), items[i, 1], fs, Muted, TextAnchor.MiddleLeft, false, false);
                x += lw + gap;
            }
        }

        void HandleMenuMouse(float W, float H)
        {
            float s = Mathf.Min(H / 720f, W / 1100f);

            // Difficulty cards
            float cw = 188f * s, ch = 168f * s, gap = 14f * s;
            float total = Presets.Length * cw + (Presets.Length - 1) * gap;
            float x0 = (W - total) * 0.5f;
            float y0 = 212f * s;
            for (int i = 0; i < Presets.Length; i++)
            {
                Rect card = new Rect(x0 + i * (cw + gap), y0 - (i == difficultyIndex ? 8f * s : 0f), cw, ch);
                if (card.Contains(Event.current.mousePosition))
                {
                    difficultyIndex = i;
                    Event.current.Use();
                    return;
                }
            }

            // Arena cards
            float my = y0 + ch + 34f * s;
            float mw = 300f * s, mh = 64f * s, mgap = 14f * s;
            float mtotal = maps.Length * mw + (maps.Length - 1) * mgap;
            float mx0 = (W - mtotal) * 0.5f;
            float my0 = my + 48f * s;
            for (int i = 0; i < maps.Length; i++)
            {
                Rect card = new Rect(mx0 + i * (mw + mgap), my0, mw, mh);
                if (card.Contains(Event.current.mousePosition))
                {
                    if (mapIndex != i)
                    {
                        mapIndex = i;
                        ApplyMap(mapIndex);
                        PlaceFighters();
                    }
                    Event.current.Use();
                    return;
                }
            }
        }

        // ---- menu
        void DrawMenu(float W, float H, float s)
        {
            Fill(new Rect(0f, 0f, W, H), new Color(0.02f, 0.03f, 0.06f, 0.84f));

            Label(new Rect(0f, 24f * s, W, 110f * s), "BOXING", 96f * s, Color.white);
            RR(new Rect(W * 0.5f - 36f * s, 134f * s, 72f * s, 4f * s), Gold, 2f * s);

            // difficulty
            Label(new Rect(0f, 156f * s, W, 22f * s), "DIFFICULTY", 14f * s, Muted);
            Label(new Rect(0f, 176f * s, W, 20f * s), "LEFT / RIGHT or 1-5", 11f * s, new Color(1f, 1f, 1f, 0.35f), TextAnchor.MiddleCenter, false, false);

            float cw = 188f * s, ch = 168f * s, gap = 14f * s;
            float total = Presets.Length * cw + (Presets.Length - 1) * gap;
            float x0 = (W - total) * 0.5f;
            float y0 = 212f * s;

            for (int i = 0; i < Presets.Length; i++)
            {
                var p = Presets[i];
                bool sel = i == difficultyIndex;
                Rect cr = new Rect(x0 + i * (cw + gap), y0 - (sel ? 8f * s : 0f), cw, ch);

                if (sel) RR(new Rect(cr.x - 3f * s, cr.y - 3f * s, cr.width + 6f * s, cr.height + 6f * s), p.color, 17f * s);
                RR(cr, sel ? new Color(0.09f, 0.10f, 0.15f, 0.98f) : new Color(0.07f, 0.08f, 0.11f, 0.88f), 14f * s);

                Label(new Rect(cr.x + 14f * s, cr.y + 10f * s, 30f * s, 20f * s), (i + 1).ToString(), 13f * s, Muted, TextAnchor.MiddleLeft, true, false);
                RR(new Rect(cr.x + 16f * s, cr.y + 38f * s, 34f * s, 4f * s), p.color, 2f * s);
                Label(new Rect(cr.x, cr.y + 46f * s, cr.width, 32f * s), p.label, 24f * s, p.color);
                Label(new Rect(cr.x + 4f * s, cr.y + 84f * s, cr.width - 8f * s, 24f * s), p.opponent, 15f * s, Color.white);
                Label(new Rect(cr.x + 6f * s, cr.y + 112f * s, cr.width - 12f * s, 44f * s), p.perk, 12f * s, new Color(1f, 1f, 1f, 0.62f), TextAnchor.MiddleCenter, false, false);
            }

            // map
            float my = y0 + ch + 34f * s;
            Label(new Rect(0f, my, W, 22f * s), "ARENA", 14f * s, Muted);
            Label(new Rect(0f, my + 20f * s, W, 20f * s), "UP / DOWN or M", 11f * s, new Color(1f, 1f, 1f, 0.35f), TextAnchor.MiddleCenter, false, false);

            float mw = 300f * s, mh = 64f * s, mgap = 14f * s;
            float mtotal = maps.Length * mw + (maps.Length - 1) * mgap;
            float mx0 = (W - mtotal) * 0.5f;
            float my0 = my + 48f * s;
            for (int i = 0; i < maps.Length; i++)
            {
                bool sel = i == mapIndex;
                Rect mr = new Rect(mx0 + i * (mw + mgap), my0, mw, mh);
                if (sel) RR(new Rect(mr.x - 3f * s, mr.y - 3f * s, mr.width + 6f * s, mr.height + 6f * s), Gold, 15f * s);
                RR(mr, sel ? new Color(0.09f, 0.10f, 0.15f, 0.98f) : new Color(0.07f, 0.08f, 0.11f, 0.88f), 12f * s);
                Label(new Rect(mr.x, mr.y + 10f * s, mr.width, 26f * s), maps[i].name, 19f * s, sel ? Color.white : new Color(1f, 1f, 1f, 0.7f));
                Label(new Rect(mr.x, mr.y + 36f * s, mr.width, 20f * s), maps[i].subtitle, 12f * s, Muted, TextAnchor.MiddleCenter, false, false);
            }

            // start prompt
            float pulse = 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 3f);
            Label(new Rect(0f, my0 + mh + 28f * s, W, 36f * s), "PRESS ENTER TO FIGHT", 24f * s, new Color(1f, 1f, 1f, pulse));

            DrawControlChips(W, H - 48f * s, s, false);
        }

        // ---- end of match
        void DrawMatchOver(float W, float H, float s)
        {
            Fill(new Rect(0f, 0f, W, H), new Color(0f, 0f, 0f, 0.6f));

            bool win = playerWins > opponentWins, lose = opponentWins > playerWins;
            string title = win ? "YOU WIN" : (lose ? "YOU LOSE" : "DRAW");
            Color tc = win ? Gold : (lose ? new Color(1f, 0.4f, 0.4f) : Color.white);
            string sub = win ? "Defeated " + opponent.displayName : (lose ? "Beaten by " + opponent.displayName : "Nobody takes the belt");
            if (win && difficultyIndex == Presets.Length - 1) sub = "You beat the impossible.";

            Rect pr = new Rect(W * 0.5f - 270f * s, H * 0.5f - 140f * s, 540f * s, 280f * s);
            RR(new Rect(pr.x - 3f * s, pr.y - 3f * s, pr.width + 6f * s, pr.height + 6f * s), new Color(tc.r, tc.g, tc.b, 0.9f), 22f * s);
            RR(pr, new Color(0.06f, 0.07f, 0.10f, 0.98f), 20f * s);

            Label(new Rect(pr.x, pr.y + 22f * s, pr.width, 90f * s), title, 72f * s, tc);
            Label(new Rect(pr.x, pr.y + 112f * s, pr.width, 30f * s), sub, 19f * s, new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleCenter, false);
            Label(new Rect(pr.x, pr.y + 150f * s, pr.width, 44f * s), playerWins + "  -  " + opponentWins, 34f * s, Color.white);

            float pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 3f);
            Label(new Rect(pr.x, pr.y + 214f * s, pr.width, 30f * s), "PRESS ENTER OR R TO PLAY AGAIN", 16f * s, new Color(1f, 1f, 1f, pulse));
        }
    }
}
