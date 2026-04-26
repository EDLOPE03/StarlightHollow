using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.Collections;

// All possible game states
public enum GamePhase
{
    MainMenu,
    DifficultySelect,
    ZoneSelect,
    DayExploration,
    Combat,
    NightPhase,
    LevelComplete,
    Ending
}

public class GameLoop : MonoBehaviour
{
    public static GameLoop Instance { get; private set; }

    // --- Events ---
    public static event Action<GamePhase> OnPhaseChanged;
    public static event Action<int,string> OnDayStarted;    // (day, zoneName)
    public static event Action<int>        OnNightStarted;  // (day)
    public static event Action<string>     OnEndingReached; // (endingKey)
    public static event Action<bool>       OnNGPlusChanged; // (isNGPlus)
    public static event Action<int>        OnLevelComplete; // (level)
    public static event Action<string>     OnZoneEnemySet;  // (enemy name)

    [Header("References")]
    public TeamManager  teamManager;
    public CombatSystem combatSystem;

    // --- Runtime State ---
    public GamePhase CurrentPhase    { get; private set; }
    public int       CurrentDay      { get; private set; }
    public string    CurrentZone     { get; private set; }
    public SaveData  Save            { get; private set; }
    public int       Difficulty      { get; private set; }
    public int       CurrentLevel    { get; private set; }
    public bool      IsNGPlus        => Save?.ngPlus ?? false;

    private bool _combatDone;
    private bool _runInitialized;
    private Coroutine _zoneRoutine;

    // -------------------------------------------------------
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        if (Instance == this)
            SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void Start()
    {
        Save = SaveSystem.Load();

        if (Save.ngPlus)
            OnNGPlusChanged?.Invoke(true);

        RefreshSceneState();
        HandleSceneEntry(SceneManager.GetActiveScene().buildIndex);
    }

    // Called by UI difficulty button.
    public void StartGame(int selectedDifficulty)
    {
        Difficulty = selectedDifficulty;
        RefreshSceneReferences();
        teamManager?.SetupTeam(Difficulty);
        _runInitialized = true;
        CurrentDay = 0;
        StartZoneRoutine();
    }

    // -------------------------------------------------------
    private void StartZoneRoutine()
    {
        if (_zoneRoutine != null)
            StopCoroutine(_zoneRoutine);

        _zoneRoutine = StartCoroutine(ZoneRoutine());
    }

    private IEnumerator ZoneRoutine()
    {
        for (int day = 1; day <= GameData.TOTAL_DAYS; day++)
        {
            CurrentDay = day;

            // --- Day Exploration ---
            SetPhase(GamePhase.DayExploration);
            OnDayStarted?.Invoke(day, CurrentZone);

            if (GameData.ZONE_ENEMIES.TryGetValue(CurrentZone, out string enemyName))
                OnZoneEnemySet?.Invoke(enemyName);

            yield return new WaitForSeconds(1.5f);

            // --- Combat (probability check) ---
            bool doCombat = IsNGPlus
                ? (UnityEngine.Random.value < 0.8f)
                : (UnityEngine.Random.value < 0.6f);

            if (doCombat && teamManager != null && teamManager.IsTeamAlive())
            {
                _combatDone = false;
                SetPhase(GamePhase.Combat);
                if (combatSystem == null)
                    combatSystem = FindFirstObjectByType<CombatSystem>();

                combatSystem?.StartCombat(teamManager.Team, IsNGPlus, () => _combatDone = true);
                yield return new WaitUntil(() => _combatDone);
            }

            // --- Night Phase ---
            SetPhase(GamePhase.NightPhase);
            OnNightStarted?.Invoke(day);
            teamManager.ApplyNightAbilities();

            // Auto-save each night
            Save.totalRuns++;
            SaveSystem.Save(Save);

            yield return new WaitForSeconds(2f);
        }

        SetPhase(GamePhase.LevelComplete);
        OnLevelComplete?.Invoke(CurrentLevel);

        yield return new WaitForSeconds(1f);

        if (CurrentLevel >= 3)
            TriggerEnding();
    }

    public void ProceedToNextZone()
    {
        if (CurrentPhase != GamePhase.LevelComplete)
            return;

        if (SceneLoader.Instance != null)
            SceneLoader.Instance.LoadNextZone();
        else
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex + 1);
    }

    // Compatibility shim for legacy UI zone buttons.
    public void SelectZone(string zoneName)
    {
        CurrentZone = zoneName;
        Debug.Log($"[GameLoop] SelectZone called in scene-flow mode. Zone set to: {zoneName}");
    }

    // --- Ending Logic ---
    private void TriggerEnding()
    {
        string key = DetermineEnding();
        Debug.Log($"[Ending] {key}");

        if (key == "TrueEnding")
        {
            Save.trueEnding = true;
            Save.ngPlus     = true;
            OnNGPlusChanged?.Invoke(true);
        }

        Save.highestDifficultyClear = Mathf.Max(Save.highestDifficultyClear, Difficulty);
        SaveSystem.Save(Save);

        SetPhase(GamePhase.Ending);
        OnEndingReached?.Invoke(key);

        if (SceneLoader.Instance != null)
            SceneLoader.Instance.LoadEnding();
        else
            SceneManager.LoadScene("Ending Menu");
    }

    private string DetermineEnding()
    {
        if (teamManager.CanGetTrueEnding()) return "TrueEnding";
        int alive = teamManager.GetAliveCount();
        if (alive >= 4) return "StillStanding";
        if (alive >= 2) return "QuietPaths";
        if (alive == 1) return "Watcher";
        return "LightsOut";
    }

    private void SetPhase(GamePhase phase)
    {
        CurrentPhase = phase;
        OnPhaseChanged?.Invoke(phase);
        Debug.Log($"[GameLoop] Phase: {phase} | Day: {CurrentDay} | Zone: {CurrentZone}");
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RefreshSceneState();
        HandleSceneEntry(scene.buildIndex);
    }

    private void HandleSceneEntry(int buildIndex)
    {
        if (buildIndex == SceneLoader.SCENE_MAIN_MENU)
        {
            _runInitialized = false;
            Difficulty = 0;
            CurrentDay = 0;
            CurrentZone = null;
            SetPhase(GamePhase.MainMenu);
            return;
        }

        if (buildIndex == SceneLoader.SCENE_ENDING)
        {
            SetPhase(GamePhase.Ending);
            return;
        }

        if (CurrentLevel <= 0)
            return;

        RefreshSceneReferences();

        // First zone asks for difficulty via existing UI.
        if (!_runInitialized)
        {
            if (CurrentLevel == 1)
            {
                SetPhase(GamePhase.DifficultySelect);
                return;
            }

            Difficulty = Mathf.Clamp(Save.maxDifficulty, 1, 7);
            teamManager?.SetupTeam(Difficulty);
            _runInitialized = true;
        }

        CurrentDay = 0;
        StartZoneRoutine();
    }

    private void RefreshSceneState()
    {
        CurrentLevel = SceneLoader.GetCurrentLevel();
        CurrentZone = SceneLoader.GetCurrentZoneName();
    }

    private void RefreshSceneReferences()
    {
        if (teamManager == null)
            teamManager = FindFirstObjectByType<TeamManager>();

        if (combatSystem == null)
            combatSystem = FindFirstObjectByType<CombatSystem>();
    }
}
