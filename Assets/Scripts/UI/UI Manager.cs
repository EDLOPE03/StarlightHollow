using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("─── Panels ───")]
    [FormerlySerializedAs("mainMenuPanel")]
    [SerializeField] private GameObject pausePanel;
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
    [SerializeField] private Button          pauseButton;

    [Header("─── Combat ───")]
    [SerializeField] private TextMeshProUGUI combatLog;
    [SerializeField] private TextMeshProUGUI enemyLabel;
    [SerializeField] private Slider          enemyHpSlider;
    [SerializeField] private TextMeshProUGUI enemyHpText;
    [SerializeField] private float           combatLogDownShift = 120f;
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

    // Track what panel was showing before pause
    private GameObject _panelBeforePause;
    private bool       _isPaused = false;
    private string     _currentEnemyName = "";

    // Cache current level so it is always accurate
    private int  _currentLevel = 1;
    private bool _combatLogShiftApplied;

    // HP slider refs keyed by character name
    private Dictionary<string, Slider>          _hpSliders = new();
    private Dictionary<string, TextMeshProUGUI> _hpTexts   = new();

    // -------------------------------------------------------
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        Debug.Log($"[UI START] Scene name: {SceneManager.GetActiveScene().name} " +
            $"Build index: {SceneManager.GetActiveScene().buildIndex} " +
            $"Level: {SceneLoader.GetCurrentLevel()}");

        FixEventSystem();

        _currentLevel = SceneLoader.GetCurrentLevel();
        if (_currentLevel <= 0) _currentLevel = 1;

        if (levelLabel) levelLabel.text = $"Level {_currentLevel} / 3";

        ApplyCombatLogShift();

        HideAllPanels();

        // Always show HUD immediately — GameLoop fires phases to switch panels
        ShowOnly(gameHudPanel);

        // Start with cursor unlocked — phases manage it from here
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;

        Debug.Log($"[UI] Started — Level {_currentLevel} — showing HUD");
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
        TeamManager.OnCharacterDowned  += ch => AppendLog($"! {ch.name} is downed!");
        TeamManager.OnCharacterRevived += ch => AppendLog($"+ {ch.name} revived!");

        CombatSystem.OnCombatStart        += () => { combatLog?.SetText(""); _maxEnemyHp = 0; };
        CombatSystem.OnCharacterAttack    += (n, d) => AppendLog($"{n} deals {d} dmg");
        CombatSystem.OnCharacterTakeDamage+= (n, d) => AppendLog($"{n} takes {d} dmg!");
        CombatSystem.OnEnemyDefeated      += () => AppendLog("Enemy defeated!");
        CombatSystem.OnAbilityTriggered   += s  => AppendLog(s);
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

    void Update()
    {
        if (Keyboard.current != null &&
            Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (_isPaused) OnResumePressed();
            else           OnPausePressed();
        }
    }

    // -------------------------------------------------------
    // Fix EventSystem to use new Input System module
    private void FixEventSystem()
    {
        var es = FindFirstObjectByType<EventSystem>();
        if (es == null)
        {
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            Debug.Log("[UI] Created missing EventSystem");
            return;
        }

        var oldModule = es.GetComponent<StandaloneInputModule>();
        if (oldModule != null)
        {
            Destroy(oldModule);
            es.gameObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            Debug.Log("[UI] Replaced StandaloneInputModule with InputSystemUIInputModule");
        }

        var newModule = es.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        if (newModule == null)
        {
            es.gameObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            Debug.Log("[UI] Added InputSystemUIInputModule to existing EventSystem");
        }
    }

    // -------------------------------------------------------
    private void ApplyCombatLogShift()
    {
        if (_combatLogShiftApplied) return;
        if (combatLog == null) return;
        var rt = combatLog.rectTransform;
        if (rt == null) return;
        rt.anchoredPosition -= new Vector2(0f, Mathf.Abs(combatLogDownShift));
        _combatLogShiftApplied = true;
    }

    // -------------------------------------------------------
    // Called by GameLoop to check if difficulty UI is properly set up
    public bool HasDifficultyUI()
    {
        if (difficultyPanel == null) return false;
        if (difficultyContainer == null && difficultyButtonPrefab == null) return false;
        return true;
    }

    // -------------------------------------------------------
    // Phase routing
    private void HandlePhase(GamePhase phase)
    {
        _currentLevel = SceneLoader.GetCurrentLevel();
        if (_currentLevel <= 0) _currentLevel = 1;
        if (levelLabel) levelLabel.text = $"Level {_currentLevel} / 3";

        switch (phase)
        {
            case GamePhase.DifficultySelect:
                ShowOnly(difficultyPanel);
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible   = true;
                break;

            case GamePhase.ZoneSelect:
                BuildZoneButtons();
                ShowOnly(zonePanel);
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible   = true;
                break;

            case GamePhase.DayExploration:
                ShowOnly(gameHudPanel);
                // Lock cursor for 3D movement during exploration
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible   = false;
                break;

            case GamePhase.Combat:
                ShowOnly(combatPanel);
                combatLog?.SetText("");
                // Keep cursor locked during combat
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible   = false;
                break;

            case GamePhase.NightPhase:
                ShowOnly(nightPanel);
                // Unlock so player can click Continue to Morning
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible   = true;
                break;

            case GamePhase.LevelComplete:
                ShowOnly(levelCompletePanel ?? gameHudPanel);
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible   = true;
                break;

            case GamePhase.Ending:
                ShowOnly(endingPanel);
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible   = true;
                break;
        }

        Debug.Log($"[UI] Phase: {phase} | Level: {_currentLevel}");
    }

    private void HandleDayStart(int day, string zone)
    {
        _currentLevel = SceneLoader.GetCurrentLevel();
        if (_currentLevel <= 0) _currentLevel = 1;

        if (dayLabel)   dayLabel.text   = $"Day {day} / {GameData.TOTAL_DAYS}";
        if (zoneLabel)  zoneLabel.text  = zone;
        if (levelLabel) levelLabel.text = $"Level {_currentLevel} / 3";

        if (enemyLabel)
            enemyLabel.text = string.IsNullOrEmpty(_currentEnemyName)
                ? "Enemy" : _currentEnemyName;

        Debug.Log($"[UI] Day {day} | Zone: {zone} | Level: {_currentLevel}");
    }

    private void HandleNight(int day)
    {
        bool retryNight = GameLoop.Instance != null && GameLoop.Instance.IsDefeatRecoveryNight;

        if (nightText)
        {
            nightText.text = retryNight
                ? $"Night {day} — The team regroups after defeat.\n" +
                  "Press Continue to retry this day from the start."
                : $"Night {day} — Resting at the First-Aid Station.\n" +
                  "Forest's Night Bloom activates. Phoenix's Dodge refreshes.";
        }
    }

    private void HandleZoneEnemySet(string enemyName)
    {
        _currentEnemyName = enemyName;
        if (enemyLabel) enemyLabel.text = enemyName;
    }

    private void HandleLevelComplete(int level)
    {
        _currentLevel = level;

        string zone  = SceneLoader.GetCurrentZoneName();
        int    alive = TeamManager.Instance?.GetAliveCount() ?? 0;

        if (completeTitle) completeTitle.text = "Zone Cleared!";
        if (zoneName)      zoneName.text      = $"{zone} Complete";
        if (aliveCount)    aliveCount.text    = $"Survivors: {alive} / 7";
        if (levelLabel)    levelLabel.text    = $"Level {level} / 3";

        if (nextLevelButton)
        {
            ApplyNextLevelButtonLayout();

            var txt = nextLevelButton.GetComponentInChildren<TextMeshProUGUI>();
            bool isLastZone = level >= 3;
            if (txt) txt.text = isLastZone ? "See Ending ->" : "Enter Next Zone ->";
            nextLevelButton.onClick.RemoveAllListeners();
            nextLevelButton.onClick.AddListener(() =>
                GameLoop.Instance?.ProceedToNextZone());
        }
    }

    private void ApplyNextLevelButtonLayout()
    {
        var rectTransform = nextLevelButton != null ? nextLevelButton.GetComponent<RectTransform>() : null;
        if (rectTransform == null) return;

        rectTransform.anchorMin = new Vector2(0.5f, 0f);
        rectTransform.anchorMax = new Vector2(0.5f, 0f);
        rectTransform.pivot     = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = new Vector2(0f, 80f);
        rectTransform.sizeDelta = new Vector2(350f, 60f);
    }

    // -------------------------------------------------------
    // Pause logic
    public void OnPausePressed()
    {
        if (_isPaused) return;
        if (pausePanel == null) return;

        _panelBeforePause = GetActivePanel();
        _isPaused = true;
        PauseManager.Instance?.Pause();
        pausePanel.SetActive(true);

        // Unlock cursor so player can click pause buttons
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;

        Debug.Log("[UI] Game Paused");
    }

    public void OnResumePressed()
    {
        if (!_isPaused) return;
        if (pausePanel == null) return;

        _isPaused = false;
        PauseManager.Instance?.Resume();
        pausePanel.SetActive(false);

        if (_panelBeforePause != null)
            _panelBeforePause.SetActive(true);

        // Re-lock cursor for 3D gameplay
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

        Debug.Log("[UI] Game Resumed");
    }

    public void OnReturnToMenuPressed()
    {
        _isPaused = false;
        PauseManager.Instance?.Resume();

        // Always unlock cursor when going to main menu
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;

        SceneManager.LoadScene(0);
    }

    public void OnSavePressed()
    {
        SaveData current = SaveSystem.Load();
        current.lastSceneBuildIndex = Mathf.Clamp(
            SceneManager.GetActiveScene().buildIndex, 1, 3);
        int currentDifficulty = GameLoop.Instance?.Difficulty ?? current.lastDifficulty;
        current.lastDifficulty = Mathf.Clamp(currentDifficulty, 1, 7);
        SaveSystem.Save(current);
        Debug.Log("[UI] Game Saved!");

        var txt = saveButton?.GetComponentInChildren<TextMeshProUGUI>();
        if (txt) StartCoroutine(FlashSaveConfirm(txt));
    }

    private System.Collections.IEnumerator FlashSaveConfirm(TextMeshProUGUI txt)
    {
        string original = txt.text;
        txt.text = "SAVED";
        yield return new WaitForSecondsRealtime(1.5f);
        txt.text = original;
    }

    // Night Continue button bridges to GameLoop
    public void OnNightContinuePressed()
    {
        GameLoop.Instance?.OnNightContinuePressed();
        Debug.Log("[UI] Night continue pressed");
        // Cursor stays unlocked — HandlePhase(DayExploration) will lock it
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
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;

        var save = SaveSystem.Load();

        if (!difficultyContainer)
        {
            int fallbackDiff = Mathf.Clamp(save.maxDifficulty, 1, 7);
            Debug.LogWarning($"[UI] Difficulty container missing. Auto-starting difficulty {fallbackDiff}.");
            GameLoop.Instance?.StartGame(fallbackDiff);
            return;
        }

        if (difficultyButtonPrefab)
        {
            foreach (Transform t in difficultyContainer) Destroy(t.gameObject);

            int maxDiff = Mathf.Max(save.maxDifficulty, 1);
            for (int d = maxDiff; d >= 1; d--)
            {
                int    diff = d;
                string name = GameData.DIFFICULTIES[d].difficultyName;
                var obj = Instantiate(difficultyButtonPrefab, difficultyContainer);
                var btn = obj.GetComponent<Button>();
                var txt = obj.GetComponentInChildren<TextMeshProUGUI>();
                if (txt) txt.text = $"{d}. {name}";
                if (btn) btn.onClick.AddListener(() => GameLoop.Instance?.StartGame(diff));
            }
            return;
        }

        var existingButtons = difficultyContainer.GetComponentsInChildren<Button>(true);
        if (existingButtons != null && existingButtons.Length > 0)
        {
            int d = Mathf.Max(save.maxDifficulty, 1);
            foreach (var btn in existingButtons)
            {
                if (btn == null) continue;
                if (d < 1) { btn.gameObject.SetActive(false); continue; }
                int diff = d;
                string name = GameData.DIFFICULTIES[diff].difficultyName;
                var txt = btn.GetComponentInChildren<TextMeshProUGUI>();
                if (txt) txt.text = $"{diff}. {name}";
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => GameLoop.Instance?.StartGame(diff));
                btn.gameObject.SetActive(true);
                d--;
            }
            return;
        }

        int fallback = Mathf.Clamp(save.maxDifficulty, 1, 7);
        Debug.LogWarning($"[UI] No difficulty UI found. Auto-starting difficulty {fallback}.");
        GameLoop.Instance?.StartGame(fallback);
    }

    // -------------------------------------------------------
    // Ending screen
    private void HandleEnding(string key)
    {
        ShowOnly(endingPanel);
        _maxEnemyHp = 0;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;

        var (title, body) = key switch
        {
            "TrueEnding"    => ("TRUE ENDING — The Last Spark",
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
                                  gameHudPanel, combatPanel, nightPanel,
                                  endingPanel, levelCompletePanel })
            if (p != null) p.SetActive(false);
    }

    private void ShowOnly(GameObject target)
    {
        foreach (var p in new[] { difficultyPanel, zonePanel,
                                  gameHudPanel, combatPanel, nightPanel,
                                  endingPanel, levelCompletePanel })
            if (p != null) p.SetActive(p == target);
    }

    private GameObject GetActivePanel()
    {
        foreach (var p in new[] { difficultyPanel, zonePanel,
                                  gameHudPanel, combatPanel, nightPanel,
                                  endingPanel, levelCompletePanel })
            if (p != null && p.activeSelf) return p;
        return null;
    }

    // ── Button callbacks wired in Inspector ──
    public void OnNewGamePressed()    => ShowDifficultyScreen();
    public void OnContinuePressed()   => ShowDifficultyScreen();
    public void OnDeleteSavePressed() { SaveSystem.DeleteSave(); ShowDifficultyScreen(); }
    public void OnPlayAgainPressed()  =>
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
}