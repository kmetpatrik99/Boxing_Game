using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace BoxingGame
{
    public enum GamePhase { Menu, Countdown, Fight, RoundOver, MatchOver }

    /// <summary>Match flow (menu, rounds, timer, KO / decision) and the HUD (drawn with OnGUI, no UI assets needed).</summary>
    public class GameManager : MonoBehaviour
    {
        [Header("References")]
        public Fighter player;
        public Fighter opponent;
        public BoxerAI ai;

        [Header("Ring (filled in by the scene builder)")]
        public Vector3 ringCenter = Vector3.zero;
        public Vector2 ringHalfSize = new Vector2(2.4f, 2.4f);
        public float groundY = 0f;
        public float startDistance = 2.4f;

        [Header("Match rules")]
        public int maxRounds = 3;
        public int roundsToWin = 2;
        public float roundTime = 60f;
        [Range(0, 2)] public int difficultyIndex = 1;   // 0 easy, 1 normal, 2 hard

        public GamePhase Phase { get; private set; } = GamePhase.Menu;

        static readonly string[] DifficultyNames = { "EASY", "NORMAL", "HARD" };
        static readonly float[] DifficultyValues = { 0.2f, 0.5f, 0.85f };

        int round, playerWins, opponentWins;
        float timer, phaseTimer, bannerUntil;
        string banner = "", subBanner = "";
        string matchResult = "";

        GUIStyle label, big;
        string popup = ""; float popupUntil; bool popupGood;

        void OnDodged(Fighter f)
        {
            popupGood = f == player;
            popup = popupGood ? "DODGE!" : "MISSED!";
            popupUntil = Time.time + 0.7f;
        }

        void Start()
        {
            Setup(player);
            Setup(opponent);
            player.opponent = opponent;
            opponent.opponent = player;
            player.Died += OnFighterDied;
            opponent.Died += OnFighterDied;
            player.Dodged += OnDodged;
            opponent.Dodged += OnDodged;
            PlaceFighters();
            SetFightersActive(false);
            Phase = GamePhase.Menu;
        }

        void OnDestroy()
        {
            if (player != null) { player.Died -= OnFighterDied; player.Dodged -= OnDodged; }
            if (opponent != null) { opponent.Died -= OnFighterDied; opponent.Dodged -= OnDodged; }
        }

        void Setup(Fighter f)
        {
            f.ringCenter = new Vector3(ringCenter.x, groundY, ringCenter.z);
            f.ringHalfSize = ringHalfSize;
        }

        void PlaceFighters()
        {
            Vector3 c = new Vector3(ringCenter.x, groundY, ringCenter.z);
            Vector3 pPos = c + new Vector3(0f, 0f, -startDistance * 0.5f);
            Vector3 oPos = c + new Vector3(0f, 0f, startDistance * 0.5f);
            player.ResetFighter(pPos, Quaternion.LookRotation(Vector3.forward));
            opponent.ResetFighter(oPos, Quaternion.LookRotation(Vector3.back));
            if (ai != null) ai.ResetAI();
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
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            bool confirm = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
                           || (pad != null && pad.startButton.wasPressedThisFrame);

            switch (Phase)
            {
                case GamePhase.Menu:
                    if (kb != null)
                    {
                        if (kb.digit1Key.wasPressedThisFrame) difficultyIndex = 0;
                        if (kb.digit2Key.wasPressedThisFrame) difficultyIndex = 1;
                        if (kb.digit3Key.wasPressedThisFrame) difficultyIndex = 2;
                    }
                    if (pad != null)
                    {
                        if (pad.dpad.left.wasPressedThisFrame) difficultyIndex = Mathf.Max(0, difficultyIndex - 1);
                        if (pad.dpad.right.wasPressedThisFrame) difficultyIndex = Mathf.Min(2, difficultyIndex + 1);
                    }
                    if (confirm || (kb != null && kb.spaceKey.wasPressedThisFrame)) StartMatch();
                    break;

                case GamePhase.Countdown:
                    phaseTimer -= Time.deltaTime;
                    int n = Mathf.CeilToInt(phaseTimer);
                    if (phaseTimer > 0f) { banner = n.ToString(); subBanner = ""; bannerUntil = Time.time + 0.2f; }
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

        void StartMatch()
        {
            if (ai != null) ai.SetDifficulty(DifficultyValues[difficultyIndex]);
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
            phaseTimer = 3.5f;
            Phase = GamePhase.Countdown;
            subBanner = "ROUND " + round;
        }

        void BeginFight()
        {
            Phase = GamePhase.Fight;
            SetFightersActive(true);
            if (ai != null) ai.ResetAI();
            banner = "FIGHT!";
            subBanner = "";
            bannerUntil = Time.time + 1.0f;
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

            banner = text;
            subBanner = winner == null ? "No point awarded" : (winner == player ? "You win the round" : "Opponent wins the round");
            bannerUntil = Time.time + 3.5f;
            phaseTimer = 3.5f;
            Phase = GamePhase.RoundOver;
        }

        void FinishMatch()
        {
            Phase = GamePhase.MatchOver;
            if (playerWins > opponentWins) matchResult = "YOU WIN!";
            else if (opponentWins > playerWins) matchResult = "YOU LOSE";
            else matchResult = "DRAW";
            banner = matchResult;
            subBanner = "Press Enter / R to play again";
            bannerUntil = float.MaxValue;
        }

        // ------------------------------------------------------------------ HUD

        void OnGUI()
        {
            if (player == null || opponent == null) return;
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                big = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            }

            float s = Screen.height / 720f;
            float W = Screen.width, H = Screen.height;

            if (Phase == GamePhase.Menu) { DrawMenu(W, H, s); return; }

            // health + stamina bars
            float barW = 400f * s, barH = 26f * s, pad = 30f * s, top = 28f * s;
            DrawFighterBar(new Rect(pad, top, barW, barH), player, false, s, new Color(0.2f, 0.8f, 0.3f));
            DrawFighterBar(new Rect(W - pad - barW, top, barW, barH), opponent, true, s, new Color(0.9f, 0.25f, 0.2f));

            // timer + round
            Text(new Rect(W / 2f - 80f * s, top - 8f * s, 160f * s, 40f * s), Mathf.CeilToInt(timer).ToString(), 34f * s, Color.white);
            Text(new Rect(W / 2f - 120f * s, top + 30f * s, 240f * s, 24f * s),
                 "ROUND " + Mathf.Max(1, round) + " / " + maxRounds + "    " + Pips(playerWins) + " - " + Pips(opponentWins), 16f * s, new Color(1f, 1f, 1f, 0.85f));

            // banner
            if (Time.time < bannerUntil || Phase == GamePhase.Countdown)
            {
                if (!string.IsNullOrEmpty(subBanner)) Text(new Rect(0, H * 0.28f, W, 40f * s), subBanner, 26f * s, new Color(1f, 0.9f, 0.4f));
                if (!string.IsNullOrEmpty(banner)) Text(new Rect(0, H * 0.34f, W, 120f * s), banner, 90f * s, Color.white);
            }

            if (Time.time < popupUntil)
                Text(new Rect(0, H * 0.62f, W, 50f * s), popup, 36f * s, popupGood ? new Color(0.5f, 1f, 0.6f) : new Color(1f, 0.6f, 0.5f));

            // controls hint
            Text(new Rect(0, H - 34f * s, W, 26f * s),
                 "WASD move   LMB jab   RMB cross   E uppercut   Ctrl/Space dodge   Shift guard", 15f * s, new Color(1f, 1f, 1f, 0.7f));
        }

        void DrawMenu(float W, float H, float s)
        {
            Fill(new Rect(0, 0, W, H), new Color(0f, 0f, 0f, 0.55f));
            Text(new Rect(0, H * 0.14f, W, 120f * s), "BOXING", 100f * s, new Color(1f, 0.85f, 0.2f));
            Text(new Rect(0, H * 0.34f, W, 40f * s), "Select difficulty (1 / 2 / 3)", 24f * s, Color.white);

            for (int i = 0; i < 3; i++)
            {
                bool sel = i == difficultyIndex;
                Rect r = new Rect(W / 2f - 270f * s + i * 190f * s, H * 0.42f, 170f * s, 48f * s);
                Fill(r, sel ? new Color(0.95f, 0.75f, 0.1f, 0.95f) : new Color(1f, 1f, 1f, 0.15f));
                Text(r, (i + 1) + "  " + DifficultyNames[i], 22f * s, sel ? Color.black : Color.white);
            }

            Text(new Rect(0, H * 0.58f, W, 40f * s), "Press ENTER or SPACE to start", 30f * s, Color.white);
            Text(new Rect(0, H * 0.70f, W, 120f * s),
                 "WASD  move toward / away / circle\nLeft click or J  jab    Right click or K  cross    E or L  uppercut\nCtrl or Space  dodge (hold A/D/S for direction)    Hold Shift  guard\nGamepad: stick move, X jab, B cross, Y uppercut, A dodge, LT guard, Start",
                 18f * s, new Color(1f, 1f, 1f, 0.85f));
        }

        void DrawFighterBar(Rect r, Fighter f, bool rightAligned, float s, Color hpColor)
        {
            Fill(new Rect(r.x - 3f * s, r.y - 3f * s, r.width + 6f * s, r.height + 6f * s), new Color(0f, 0f, 0f, 0.7f));
            float hp = Mathf.Clamp01(f.Health / f.maxHealth);
            Rect hpRect = new Rect(r.x, r.y, r.width * hp, r.height);
            if (rightAligned) hpRect.x = r.xMax - hpRect.width;
            Fill(hpRect, hpColor);

            float st = Mathf.Clamp01(f.Stamina / f.maxStamina);
            Rect sr = new Rect(r.x, r.yMax + 6f * s, r.width * st, 9f * s);
            if (rightAligned) sr.x = r.xMax - sr.width;
            Fill(new Rect(r.x, r.yMax + 6f * s, r.width, 9f * s), new Color(0f, 0f, 0f, 0.6f));
            Fill(sr, new Color(0.95f, 0.8f, 0.2f));

            label.alignment = rightAligned ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
            label.fontSize = Mathf.RoundToInt(16f * s);
            label.normal.textColor = Color.white;
            GUI.Label(new Rect(r.x, r.yMax + 14f * s, r.width, 22f * s), f.displayName, label);
            label.alignment = TextAnchor.MiddleCenter;
        }

        static string Pips(int wins) { return wins.ToString(); }

        void Fill(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        void Text(Rect r, string text, float size, Color c)
        {
            big.fontSize = Mathf.RoundToInt(size);
            big.alignment = TextAnchor.MiddleCenter;
            big.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
            GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), text, big);
            big.normal.textColor = c;
            GUI.Label(r, text, big);
        }
    }
}
