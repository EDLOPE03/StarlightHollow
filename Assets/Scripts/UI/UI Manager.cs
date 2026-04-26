using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("─── Panels ───")]
    [FormerlySerializedAs("mainMenuPanel")]
    [SerializeField] private GameObject pausePanel;        // replaces mainMenuPanel
    [SerializeField] private GameObject difficultyPanel;
    [SerializeField] private GameObject zonePanel;
    [SerializeField] private GameObject gameHudPanel;
    [SerializeField] private GameObject combatPanel;
    [SerializeField] private GameObject nightPanel;
    [SerializeField] private GameObject endingPanel;

    [Header("─── Pause Menu ───")]
    [SerializeField] private Button saveButton;
    [SerializeField] private Button returnMenuButton;

    [Header("─── Difficulty ───")]
    [SerializeField] private Transform  difficultyContainer;
    [SerializeField] private GameObject difficultyButtonPrefab;

    [Header("─── HUD ───")]
    [SerializeField] private TextMeshProUGUI dayLabel;
    [SerializeField] private TextMeshProUGUI zoneLabel;
    [SerializeField] private TextMeshProUGUI levelLabel;
    [SerializeField] private Transform       teamContainer;
    [SerializeField] private GameObject      charCardPrefab;
    [SerializeField] private Button          pauseButton;   // the ⚙ button in HUD

    [Header("─── Combat ───")]
    [SerializeField] private TextMeshProUGUI combatLog;
    [SerializeField] private TextMeshProUGUI enemyLabel;
    [SerializeField] private Slider          enemyHpSlider;
    [SerializeField] private TextMeshProUGUI enemyHpText;
    private int _maxEnemyHp;

    [Header("─── Zone Selection ───")]
    [SerializeField] private Transform  zoneContainer;
    [SerializeField] private GameObject zoneButtonPrefab;

    [Header("─── Night ───")]
    [SerializeField] private TextMeshProUGUI nightText;

    [Header("─── Ending ───")]
    [SerializeField] private TextMeshProUGUI endingTitle;
    [SerializeField] private TextMeshProUGUI endingBody;
    [SerializeField] private Button          playAgainButton;

    [Header("─── Level Complete ───")]
    [SerializeField] private GameObject      levelCompletePanel;
    [SerializeField] private TextMeshProUGUI completeTitle;
    [SerializeField] private TextMeshProUGUI zoneName;
    [SerializeField] private TextMeshProUGUI aliveCount;
    [SerializeField] private Button          nextLevelButton;

    // Track what panel was showing before pause so we can return to it
    private GameObject _panelBeforePause;
    private bool       _isPaused = false;
    private string     _currentEnemyName = "";

    // HP slider refs keyed by character name
    private Dictionary<string, Slider>          _hpSliders = new();
    private Dictionary<string, TextMeshProUGUI> _hpTexts   = new();

    // -------------------------------------------------------
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnEnable()
    {
        GameLoop.OnPhaseChanged   += HandlePhase;
        GameLoop.OnDayStarted     += HandleDayStart;
        GameLoop.OnNightStarted   += HandleNight;
        GameLoop.OnEndingReached  += HandleEnding;
        GameLoop.OnLevelComplete  += HandleLevelComplete;
        GameLoop.OnZoneEnemySet   += HandleZoneEnemySet;

        TeamManager.OnTeamSetup        += BuildTeamCards;
        TeamManager.OnCharacterDowned  += ch => AppendLog($"⚠ {ch.name} is downed!");
        TeamManager.OnCharacterRevived += ch => AppendLog($"✦ {ch.name} revived!");

        CombatSystem.OnCombatStart        += () => { combatLog?.SetText(""); };
        CombatSystem.OnCharacterAttack    += (n, d) => AppendLog($"{n} deals {d} dmg →");
        CombatSystem.OnCharacterTakeDamage+= (n, d) => AppendLog($"← {n} takes {d} dmg!");
        CombatSystem.OnEnemyDefeated      += () => AppendLog("✓ Enemy defeated!");
        CombatSystem.OnAbilityTriggered   += s  => AppendLog($"✨ {s}");
        CombatSystem.OnEnemyHpChanged     += UpdateEnemyHp;
    }

    void OnDisable()
    {
        GameLoop.OnPhaseChanged   -= HandlePhase;
        GameLoop.OnDayStarted     -= HandleDayStart;
        GameLoop.OnNightStarted   -= HandleNight;
        GameLoop.OnEndingReached  -= HandleEnding;
        GameLoop.OnLevelComplete  -= HandleLevelComplete;
        GameLoop.OnZoneEnemySet   -= HandleZoneEnemySet;

        TeamManager.OnTeamSetup       -= BuildTeamCards;
        CombatSystem.OnEnemyHpChanged -= UpdateEnemyHp;
    }

    void Start()
    {
        // Game scene starts with the difficulty picker
        // (Main Menu is its own scene now)
        HideAllPanels();

        int level = SceneLoader.GetCurrentLevel();
        if (level == 1)
            ShowDifficultyScreen();
        else
            ShowOnly(gameHudPanel);
    }

    void Update()
    {
        // Allow Escape key to toggle pause during gameplay
        if (UnityEngine.InputSystem.Keyboard.current != null &&
            UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (_isPaused) OnResumePressed();
            else           OnPausePressed();
        }
    }

    // -------------------------------------------------------
    // Phase routing
    private void HandlePhase(GamePhase phase)
    {
        switch (phase)
        {
            case GamePhase.DifficultySelect: ShowOnly(difficultyPanel);               break;
            case GamePhase.ZoneSelect:     BuildZoneButtons(); ShowOnly(zonePanel);    break;
            case GamePhase.DayExploration: ShowOnly(gameHudPanel);                     break;
            case GamePhase.Combat:         ShowOnly(combatPanel); combatLog?.SetText(""); break;
            case GamePhase.NightPhase:     ShowOnly(nightPanel);                       break;
            case GamePhase.LevelComplete:  ShowOnly(levelCompletePanel != null ? levelCompletePanel : gameHudPanel); break;
            case GamePhase.Ending:         ShowOnly(endingPanel);                      break;
        }
    }

    private void HandleDayStart(int day, string zone)
    {
        int level = SceneLoader.GetCurrentLevel();

        if (dayLabel)  dayLabel.text  = $"Day {day} / {GameData.TOTAL_DAYS}";
        if (zoneLabel) zoneLabel.text = zone;
        if (levelLabel) levelLabel.text = $"Level {level} / 3";

        if (enemyLabel)
            enemyLabel.text = string.IsNullOrEmpty(_currentEnemyName) ? "Enemy" : _currentEnemyName;
    }

    private void HandleNight(int day)
    {
        if (nightText) nightText.text =
            $"Night {day} — Resting at the First-Aid Station.\n" +
            "Forest's Night Bloom activates. Phoenix's Dodge refreshes.";
    }

    private void HandleZoneEnemySet(string enemyName)
    {
        _currentEnemyName = enemyName;
        if (enemyLabel) enemyLabel.text = enemyName;
    }

    private void HandleLevelComplete(int level)
    {
        string zone = SceneLoader.GetCurrentZoneName();
        int alive = TeamManager.Instance?.GetAliveCount() ?? 0;

        if (completeTitle) completeTitle.text = "✦ Zone Cleared! ✦";
        if (zoneName) zoneName.text = $"{zone} Complete";
        if (aliveCount) aliveCount.text = $"Survivors: {alive} / 7";

        if (nextLevelButton)
        {
            var txt = nextLevelButton.GetComponentInChildren<TextMeshProUGUI>();
            bool isLastZone = level >= 3;
            if (txt) txt.text = isLastZone ? "See Ending ->" : "Enter Next Zone ->";

            nextLevelButton.onClick.RemoveAllListeners();
            nextLevelButton.onClick.AddListener(() =>
                GameLoop.Instance?.ProceedToNextZone());
        }
    }

    // -------------------------------------------------------
    // Pause logic
    public void OnPausePressed()
    {
        if (_isPaused) return;
        if (pausePanel == null) return;

        // Remember which panel was active before pausing
        _panelBeforePause = GetActivePanel();

        _isPaused = true;
        Time.timeScale = 0f;
        pausePanel.SetActive(true);

        Debug.Log("[UI] Game Paused");
    }

    public void OnResumePressed()
    {
        if (!_isPaused) return;
        if (pausePanel == null) return;

        _isPaused = false;
        Time.timeScale = 1f;
        pausePanel.SetActive(false);

        // Return to whatever panel was showing before pause
        if (_panelBeforePause != null)
            _panelBeforePause.SetActive(true);

        Debug.Log("[UI] Game Resumed");
    }

    public void OnSavePressed()
    {
        // Load current save data and re-save it to write latest state
        SaveData current = SaveSystem.Load();
        SaveSystem.Save(current);
        Debug.Log("[UI] Game Saved!");

        // Flash the save button text to confirm
        var txt = saveButton?.GetComponentInChildren<TextMeshProUGUI>();
        if (txt) StartCoroutine(FlashSaveConfirm(txt));
    }

    private System.Collections.IEnumerator FlashSaveConfirm(TextMeshProUGUI txt)
    {
        string original = txt.text;
        txt.text = "SAVED ✓";
        yield return new WaitForSecondsRealtime(1.5f); // realtime so pause doesn't block it
        txt.text = original;
    }

    public void OnReturnToMenuPressed()
    {
        _isPaused = false;
        Time.timeScale = 1f;
        SceneManager.LoadScene(0); // 0 = Main Menu scene
    }

    // -------------------------------------------------------
    // Team HP cards
    private void BuildTeamCards(List<Character> team)
    {
        if (!teamContainer || !charCardPrefab) return;

        foreach (Transform t in teamContainer) Destroy(t.gameObject);
        _hpSliders.Clear();
        _hpTexts.Clear();

        foreach (var ch in team)
        {
            var card   = Instantiate(charCardPrefab, teamContainer);
            var nameT  = card.transform.Find("NameText")?.GetComponent<TextMeshProUGUI>();
            var slider = card.transform.Find("HpBar")?.GetComponent<Slider>();
            var hpT    = card.transform.Find("HpText")?.GetComponent<TextMeshProUGUI>();

            if (nameT)  nameT.text = ch.name;
            if (slider) { slider.maxValue = ch.maxHp; slider.value = ch.hp; _hpSliders[ch.name] = slider; }
            if (hpT)    { hpT.text = $"{ch.hp}/{ch.maxHp}"; _hpTexts[ch.name] = hpT; }

            var cg = card.GetComponent<CanvasGroup>();
            if (cg && !ch.IsAlive()) cg.alpha = 0.35f;

            ch.OnHpChanged += (hp, max) =>
            {
                if (_hpSliders.TryGetValue(ch.name, out var s)) s.value = hp;
                if (_hpTexts.TryGetValue(ch.name, out var t))   t.text  = $"{hp}/{max}";
                if (cg) cg.alpha = hp > 0 ? 1f : 0.35f;
            };
        }
    }

    // -------------------------------------------------------
    // Enemy HP bar
    private void UpdateEnemyHp(int hp)
    {
        if (enemyHpSlider == null) return;
        if (_maxEnemyHp == 0) { _maxEnemyHp = hp; enemyHpSlider.maxValue = hp; }
        enemyHpSlider.value = hp;
        if (enemyHpText) enemyHpText.text = $"Enemy HP: {hp}";
    }

    // -------------------------------------------------------
    // Zone buttons
    private void BuildZoneButtons()
    {
        if (!zoneContainer || !zoneButtonPrefab) return;
        foreach (Transform t in zoneContainer) Destroy(t.gameObject);

        bool ng = GameLoop.Instance?.IsNGPlus ?? false;
        var zones = GameData.ZONES.ToList();
        if (ng) zones.Add(GameData.NG_PLUS_ZONE);

        foreach (var zone in zones)
        {
            var z   = zone;
            var obj = Instantiate(zoneButtonPrefab, zoneContainer);
            var btn = obj.GetComponent<Button>();
            var txt = obj.GetComponentInChildren<TextMeshProUGUI>();
            if (txt) txt.text = z;
            if (btn) btn.onClick.AddListener(() => GameLoop.Instance?.SelectZone(z));
        }
    }

    // -------------------------------------------------------
    // Difficulty buttons
    public void ShowDifficultyScreen()
    {
        ShowOnly(difficultyPanel);
        if (!difficultyContainer || !difficultyButtonPrefab) return;

        foreach (Transform t in difficultyContainer) Destroy(t.gameObject);
        var save = SaveSystem.Load();

        for (int d = save.maxDifficulty; d >= 1; d--)
        {
            int    diff = d;
            string name = GameData.DIFFICULTIES[d].difficultyName;
            var obj = Instantiate(difficultyButtonPrefab, difficultyContainer);
            var btn = obj.GetComponent<Button>();
            var txt = obj.GetComponentInChildren<TextMeshProUGUI>();
            if (txt) txt.text = $"{d}. {name}";
            if (btn) btn.onClick.AddListener(() => GameLoop.Instance?.StartGame(diff));
        }
    }

    // -------------------------------------------------------
    // Ending screen
    private void HandleEnding(string key)
    {
        ShowOnly(endingPanel);
        _maxEnemyHp = 0;

        var (title, body) = key switch
        {
            "TrueEnding"    => ("✦ TRUE ENDING — The Last Spark ✦",
                                "Astra alone remained, her vision guiding the team home.\n" +
                                "The Spark is restored. New Game+ Unlocked!"),
            "StillStanding" => ("ENDING: Still Standing",
                                "The team endures. Together you kept the light alive."),
            "QuietPaths"    => ("ENDING: Quiet Paths",
                                "Not everything is perfect — but the survivors press on."),
            "Watcher"       => ("ENDING: Watcher",
                                "One soul remains to carry the others' memory forward."),
            "LightsOut"     => ("ENDING: Lights Out",
                                "The park falls silent. No one remains to tell the tale."),
            _               => ("THE END", "")
        };

        if (endingTitle) endingTitle.text = title;
        if (endingBody)  endingBody.text  = body;
    }

    // -------------------------------------------------------
    // Helpers
    private void AppendLog(string msg)
    {
        if (combatLog == null) return;
        combatLog.text += $"\n{msg}";
    }

    private void HideAllPanels()
    {
        foreach (var p in new[] { pausePanel, difficultyPanel, zonePanel,
                                  gameHudPanel, combatPanel, nightPanel, endingPanel, levelCompletePanel })
            if (p != null) p.SetActive(false);
    }

    private void ShowOnly(GameObject target)
    {
        // Don't touch the pause panel when routing between game panels
        foreach (var p in new[] { difficultyPanel, zonePanel,
                                  gameHudPanel, combatPanel, nightPanel, endingPanel, levelCompletePanel })
            if (p != null) p.SetActive(p == target);
    }

    // Returns whichever non-pause panel is currently active
    private GameObject GetActivePanel()
    {
        foreach (var p in new[] { difficultyPanel, zonePanel,
                                  gameHudPanel, combatPanel, nightPanel, endingPanel, levelCompletePanel })
            if (p != null && p.activeSelf) return p;
        return null;
    }

    // ── Button callbacks wired in Inspector ──
    public void OnNewGamePressed() => ShowDifficultyScreen();
    public void OnContinuePressed() => ShowDifficultyScreen();
    public void OnDeleteSavePressed()
    {
        SaveSystem.DeleteSave();
        ShowDifficultyScreen();
    }

    public void OnPlayAgainPressed() =>
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
}