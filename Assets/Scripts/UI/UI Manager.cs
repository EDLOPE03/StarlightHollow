using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("─── Panels ───")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject difficultyPanel;
    [SerializeField] private GameObject zonePanel;
    [SerializeField] private GameObject gameHudPanel;
    [SerializeField] private GameObject combatPanel;
    [SerializeField] private GameObject nightPanel;
    [SerializeField] private GameObject endingPanel;

    [Header("─── Main Menu ───")]
    [SerializeField] private TextMeshProUGUI ngPlusBadge;   // shows "NG+ Available"
    [SerializeField] private Button         continueButton;

    [Header("─── Difficulty ───")]
    [SerializeField] private Transform  difficultyContainer;
    [SerializeField] private GameObject difficultyButtonPrefab;

    [Header("─── HUD ───")]
    [SerializeField] private TextMeshProUGUI dayLabel;      // "Day 3 / 5"
    [SerializeField] private TextMeshProUGUI zoneLabel;     // "Zone: Whimsy Woods"
    [SerializeField] private Transform       teamContainer; // parent for HP cards
    [SerializeField] private GameObject      charCardPrefab;

    [Header("─── Combat ───")]
    [SerializeField] private TextMeshProUGUI combatLog;
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
        GameLoop.OnPhaseChanged  += HandlePhase;
        GameLoop.OnDayStarted    += HandleDayStart;
        GameLoop.OnNightStarted  += HandleNight;
        GameLoop.OnEndingReached += HandleEnding;
        GameLoop.OnNGPlusChanged += HandleNGPlus;

        TeamManager.OnTeamSetup        += BuildTeamCards;
        TeamManager.OnCharacterDowned  += ch => AppendLog($"⚠ {ch.name} is downed!");
        TeamManager.OnCharacterRevived += ch => AppendLog($"✦ {ch.name} revived!");

        CombatSystem.OnCombatStart       += () => { combatLog?.SetText(""); };
        CombatSystem.OnCharacterAttack   += (n,d) => AppendLog($"{n} deals {d} dmg →");
        CombatSystem.OnCharacterTakeDamage+=(n,d) => AppendLog($"← {n} takes {d} dmg!");
        CombatSystem.OnEnemyDefeated     += () => AppendLog("✓ Enemy defeated!");
        CombatSystem.OnAbilityTriggered  += s  => AppendLog($"✨ {s}");
        CombatSystem.OnEnemyHpChanged    += UpdateEnemyHp;
    }

    void OnDisable()
    {
        GameLoop.OnPhaseChanged  -= HandlePhase;
        GameLoop.OnDayStarted    -= HandleDayStart;
        GameLoop.OnNightStarted  -= HandleNight;
        GameLoop.OnEndingReached -= HandleEnding;
        GameLoop.OnNGPlusChanged -= HandleNGPlus;

        TeamManager.OnTeamSetup        -= BuildTeamCards;
        CombatSystem.OnEnemyHpChanged  -= UpdateEnemyHp;
    }

    void Start()
    {
        ShowOnly(mainMenuPanel);
        var save = SaveSystem.Load();
        if (continueButton) continueButton.interactable = SaveSystem.HasSave();
        if (ngPlusBadge)    ngPlusBadge.gameObject.SetActive(save.ngPlus);
    }

    // -------------------------------------------------------
    // Phase routing
    private void HandlePhase(GamePhase phase)
    {
        switch (phase)
        {
            case GamePhase.ZoneSelect:    BuildZoneButtons(); ShowOnly(zonePanel);    break;
            case GamePhase.DayExploration:ShowOnly(gameHudPanel);                     break;
            case GamePhase.Combat:        ShowOnly(combatPanel); combatLog?.SetText(""); break;
            case GamePhase.NightPhase:    ShowOnly(nightPanel);                       break;
        }
    }

    private void HandleDayStart(int day, string zone)
    {
        if (dayLabel)  dayLabel.text  = $"Day {day} / {GameData.TOTAL_DAYS}";
        if (zoneLabel) zoneLabel.text = $"Zone: {zone}";
    }

    private void HandleNight(int day)
    {
        if (nightText) nightText.text =
            $"Night {day} — Resting at the First-Aid Station.\n" +
            "Forest's Night Bloom activates. Phoenix's Dodge refreshes.";
    }

    private void HandleNGPlus(bool on)
    {
        if (ngPlusBadge) ngPlusBadge.gameObject.SetActive(on);
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

            if (nameT)  nameT.text    = ch.name;
            if (slider) { slider.maxValue = ch.maxHp; slider.value = ch.hp; _hpSliders[ch.name] = slider; }
            if (hpT)    { hpT.text = $"{ch.hp}/{ch.maxHp}"; _hpTexts[ch.name] = hpT; }

            // Dim downed characters
            var cg = card.GetComponent<CanvasGroup>();
            if (cg && !ch.IsAlive()) cg.alpha = 0.35f;

            // Subscribe so cards live-update
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
            var z = zone; // capture for lambda
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
            int   diff = d;
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
        _maxEnemyHp = 0; // reset for next run

        var (title, body) = key switch
        {
            "TrueEnding"   => ("✦ TRUE ENDING — The Last Spark ✦",
                               "Astra alone remained, her vision guiding the team home.\n" +
                               "The Spark is restored. New Game+ Unlocked!"),
            "StillStanding"=> ("ENDING: Still Standing",
                               "The team endures. Together you kept the light alive."),
            "QuietPaths"   => ("ENDING: Quiet Paths",
                               "Not everything is perfect — but the survivors press on."),
            "Watcher"      => ("ENDING: Watcher",
                               "One soul remains to carry the others' memory forward."),
            "LightsOut"    => ("ENDING: Lights Out",
                               "The park falls silent. No one remains to tell the tale."),
            _              => ("THE END", "")
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

    private void ShowOnly(GameObject target)
    {
        foreach (var p in new[] { mainMenuPanel, difficultyPanel, zonePanel,
                                  gameHudPanel, combatPanel, nightPanel, endingPanel })
            if (p != null) p.SetActive(p == target);
    }

    // ── Button callbacks wired in Inspector ──
    public void OnNewGamePressed()    => ShowDifficultyScreen();
    public void OnContinuePressed()   => ShowDifficultyScreen();
    public void OnPlayAgainPressed()  =>
        UnityEngine.SceneManagement.SceneManager.LoadScene(
        UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
    public void OnDeleteSavePressed() { SaveSystem.DeleteSave(); Start(); }
}
