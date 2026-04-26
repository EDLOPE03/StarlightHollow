using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.Collections;

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
    public static event Action<int,string> OnDayStarted;
    public static event Action<int>        OnNightStarted;
    public static event Action<string>     OnEndingReached;
    public static event Action<bool>       OnNGPlusChanged;
    public static event Action<int>        OnLevelComplete;
    public static event Action<string>     OnZoneEnemySet;

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
    private bool _nightDone;
    private bool _encounterCombatActive = false;
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
        if (Save.ngPlus) OnNGPlusChanged?.Invoke(true);
        RefreshSceneState();
        HandleSceneEntry(SceneManager.GetActiveScene().buildIndex);
    }

    // Called by UI difficulty button
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
    // Called by Continue button on Night Panel
    public void OnNightContinuePressed()
    {
        if (CurrentPhase == GamePhase.NightPhase)
            _nightDone = true;
    }

    // -------------------------------------------------------
    // Called by EncounterZone when player walks in
    public void TriggerEncounterCombat(string enemyName)
    {
        // Only trigger during exploration and not during another combat
        if (CurrentPhase != GamePhase.DayExploration) return;
        if (_encounterCombatActive) return;
        if (teamManager == null || !teamManager.IsTeamAlive()) return;

        StartCoroutine(HandleEncounterCombat(enemyName));
    }

    private IEnumerator HandleEncounterCombat(string enemyName)
    {
        _encounterCombatActive = true;
        _combatDone = false;

        // Announce the enemy
        OnZoneEnemySet?.Invoke(enemyName);

        // Switch to combat panel
        SetPhase(GamePhase.Combat);

        if (combatSystem == null)
            combatSystem = FindFirstObjectByType<CombatSystem>();

        combatSystem?.StartCombat(
            teamManager.Team,
            IsNGPlus,
            () => _combatDone = true
        );

        // Wait until combat finishes
        yield return new WaitUntil(() => _combatDone);

        // Brief pause after combat
        yield return new WaitForSeconds(1f);

        // Return to exploration so player can keep walking
        _encounterCombatActive = false;
        SetPhase(GamePhase.DayExploration);
        OnDayStarted?.Invoke(CurrentDay, CurrentZone);

        Debug.Log($"[GameLoop] Encounter combat done — back to exploration");
    }

    // -------------------------------------------------------
    private void StartZoneRoutine()
    {
        if (_zoneRoutine != null) StopCoroutine(_zoneRoutine);
        _zoneRoutine = StartCoroutine(ZoneRoutine());
    }

    private IEnumerator ZoneRoutine()
    {
        for (int day = 1; day <= GameData.TOTAL_DAYS; day++)
        {
            CurrentDay = day;

            // Reset all encounter zones for the new day
            var zones = FindObjectsByType<EncounterZone>(FindObjectsSortMode.None);
            foreach (var z in zones) z.ResetZone();

            // --- Day Exploration ---
            // Player freely explores and triggers encounters by walking into zones
            SetPhase(GamePhase.DayExploration);
            OnDayStarted?.Invoke(day, CurrentZone);

            if (GameData.ZONE_ENEMIES.TryGetValue(CurrentZone, out string enemyName))
                OnZoneEnemySet?.Invoke(enemyName);

            // Wait for player to explore
            // Exploration time increases per level so later zones feel longer
            float exploreTime = 30f + (CurrentLevel * 10f);
            Debug.Log($"[GameLoop] Day {day} exploration — {exploreTime}s window");
            yield return new WaitForSeconds(exploreTime);

            // --- Night Phase ---
            // Wait for any active encounter combat to finish first
            yield return new WaitUntil(() => !_encounterCombatActive);

            _nightDone = false;
            SetPhase(GamePhase.NightPhase);
            OnNightStarted?.Invoke(day);
            teamManager.ApplyNightAbilities();

            // Auto-save each night
            Save.totalRuns++;
            SaveSystem.Save(Save);

            // Wait until player presses Continue on the Night Panel
            yield return new WaitUntil(() => _nightDone);

            // Small pause before next day
            yield return new WaitForSeconds(0.5f);
        }

        // --- Level Complete ---
        SetPhase(GamePhase.LevelComplete);
        OnLevelComplete?.Invoke(CurrentLevel);

        yield return new WaitForSeconds(1f);

        if (CurrentLevel >= 3)
            TriggerEnding();
    }

    public void ProceedToNextZone()
    {
        if (CurrentPhase != GamePhase.LevelComplete) return;

        if (SceneLoader.Instance != null)
            SceneLoader.Instance.LoadNextZone();
        else
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex + 1);
    }

    public void SelectZone(string zoneName)
    {
        CurrentZone = zoneName;
        Debug.Log($"[GameLoop] Zone set to: {zoneName}");
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

        Save.highestDifficultyClear = Mathf.Max(
            Save.highestDifficultyClear, Difficulty);
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
            Difficulty      = 0;
            CurrentDay      = 0;
            CurrentZone     = null;
            SetPhase(GamePhase.MainMenu);
            return;
        }

        if (buildIndex == SceneLoader.SCENE_ENDING)
        {
            SetPhase(GamePhase.Ending);
            return;
        }

        if (CurrentLevel <= 0) return;

        RefreshSceneReferences();

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
        CurrentZone  = SceneLoader.GetCurrentZoneName();
    }

    private void RefreshSceneReferences()
    {
        if (teamManager == null)
            teamManager = FindFirstObjectByType<TeamManager>();
        if (combatSystem == null)
            combatSystem = FindFirstObjectByType<CombatSystem>();
    }
}