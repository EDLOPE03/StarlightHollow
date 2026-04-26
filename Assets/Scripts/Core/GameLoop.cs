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
    private bool _defeatPending;
    private bool _isDefeatRecoveryNight;
    private bool _runInitialized;
    private Coroutine _zoneRoutine;

    public bool IsDefeatRecoveryNight => _isDefeatRecoveryNight;

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
        Save.lastDifficulty = Difficulty;
        Save.lastSceneBuildIndex = Mathf.Clamp(SceneManager.GetActiveScene().buildIndex, 1, 3);
        // Persist selected difficulty immediately so Continue restores the same team size.
        SaveSystem.Save(Save);

        RefreshSceneReferences();
        teamManager?.SetupTeam(Difficulty);
        _runInitialized = true;
        CurrentDay = 0;
        StartZoneRoutine();
    }

    // Called by NightContinueButton via UIManager
    public void OnNightContinuePressed()
    {
        if (CurrentPhase == GamePhase.NightPhase)
        {
            _nightDone = true;
            Debug.Log("[GameLoop] Night continue pressed — advancing to next day");
        }
    }

    // -------------------------------------------------------
    // Called by EncounterZone when player walks in
    public void TriggerEncounterCombat(string enemyName)
    {
        if (CurrentPhase != GamePhase.DayExploration) return;
        if (_encounterCombatActive) return;
        if (teamManager == null || !teamManager.IsTeamAlive()) return;

        StartCoroutine(HandleEncounterCombat(enemyName));
    }

    private IEnumerator HandleEncounterCombat(string enemyName)
    {
        _encounterCombatActive = true;
        _combatDone = false;

        OnZoneEnemySet?.Invoke(enemyName);
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
        yield return new WaitForSeconds(1f);

        _encounterCombatActive = false;

        // Check if team is still alive after combat
        if (!teamManager.IsTeamAlive())
        {
            // All characters downed — defer to retry flow in ZoneRoutine.
            Debug.Log("[GameLoop] All characters downed — scheduling retry flow");
            _defeatPending = true;
            _encounterCombatActive = false;
            yield break;
        }

        // Return to exploration
        SetPhase(GamePhase.DayExploration);
        OnDayStarted?.Invoke(CurrentDay, CurrentZone);

        // Check if all zones for today are done
        CheckAllZonesDone();

        Debug.Log("[GameLoop] Encounter done — back to exploration");
    }

    // -------------------------------------------------------
    // Check if all encounter zones have been triggered
    private void CheckAllZonesDone()
    {
        var zones = FindObjectsByType<EncounterZone>(FindObjectsSortMode.None);

        // Count how many zones exist and how many are done
        int total   = zones.Length;
        int cleared = 0;

        foreach (var z in zones)
            if (z.hasTriggered) cleared++;

        Debug.Log($"[GameLoop] Zones cleared: {cleared} / {total}");

        // If all zones are cleared trigger night early
        if (cleared >= total && total > 0)
        {
            Debug.Log("[GameLoop] All encounter zones cleared — triggering night");
            _allZonesDone = true;
        }
    }

    private bool _allZonesDone = false;

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
            CurrentDay   = day;
            _allZonesDone = false;

            // Reset all encounter zones for the new day
            var zones = FindObjectsByType<EncounterZone>(FindObjectsSortMode.None);
            foreach (var z in zones) z.ResetZone();

            // --- Day Exploration ---
            SetPhase(GamePhase.DayExploration);
            OnDayStarted?.Invoke(day, CurrentZone);

            if (GameData.ZONE_ENEMIES.TryGetValue(CurrentZone, out string enemyName))
                OnZoneEnemySet?.Invoke(enemyName);

            Debug.Log($"[GameLoop] Day {day} started — explore and find encounter zones");

            // Wait until ALL zones are cleared OR team is wiped
            yield return new WaitUntil(() =>
                _allZonesDone ||
                !teamManager.IsTeamAlive()
            );

            // If team was wiped end the game
            if (!teamManager.IsTeamAlive())
            {
                TriggerEnding();
                yield break;
            }

            // Wait for any active combat to fully finish
            yield return new WaitUntil(() => !_encounterCombatActive);

            // Team wipe: use Night screen as a retry/reset opportunity.
            if (_defeatPending)
            {
                yield return StartCoroutine(HandleDefeatRetry());
                day--;
                continue;
            }

            // If all encounter zones are cleared, this level is complete.
            if (_allZonesDone)
            {
                SetPhase(GamePhase.LevelComplete);
                OnLevelComplete?.Invoke(CurrentLevel);
                yield break;
            }

            // Small pause before night
            yield return new WaitForSeconds(1f);

            // --- Night Phase ---
            _nightDone = false;
            SetPhase(GamePhase.NightPhase);
            OnNightStarted?.Invoke(day);
            teamManager.ApplyNightAbilities();

            // Auto save each night
            Save.totalRuns++;
            Save.lastSceneBuildIndex = Mathf.Clamp(SceneManager.GetActiveScene().buildIndex, 1, 3);
            if (Difficulty > 0) Save.lastDifficulty = Difficulty;
            SaveSystem.Save(Save);

            Debug.Log($"[GameLoop] Night {day} — waiting for player to continue");

            // Wait for player to press Continue to Morning
            yield return new WaitUntil(() => _nightDone);

            yield return new WaitForSeconds(0.5f);

            Debug.Log($"[GameLoop] Morning — starting Day {day + 1}");
        }

        // --- All 5 Days Complete — Level Done ---
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

        Save.lastSceneBuildIndex = Mathf.Clamp(SceneManager.GetActiveScene().buildIndex, 1, 3);
        if (Difficulty > 0) Save.lastDifficulty = Difficulty;
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

        // Build a robust restored difficulty for any resumed gameplay scene.
        int restoredDifficulty = Mathf.Clamp(Save.lastDifficulty, 1, 7);
        if (restoredDifficulty == 1 && Save.maxDifficulty > 1)
        {
            restoredDifficulty = Mathf.Clamp(Save.maxDifficulty, 1, 7);
            Debug.LogWarning($"[GameLoop] Promoting restored difficulty to max unlocked ({restoredDifficulty}).");
        }

        if (!_runInitialized)
        {
            if (CurrentLevel == 1)
            {
                SetPhase(GamePhase.DifficultySelect);
                return;
            }

            Difficulty = restoredDifficulty;
            teamManager?.SetupTeam(Difficulty);
            _runInitialized = true;
        }
        else if (Difficulty <= 0)
        {
            Difficulty = restoredDifficulty;
            teamManager?.SetupTeam(Difficulty);
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

    private IEnumerator HandleDefeatRetry()
    {
        _isDefeatRecoveryNight = true;
        _nightDone = false;

        SetPhase(GamePhase.NightPhase);
        OnNightStarted?.Invoke(CurrentDay);

        Debug.Log("[GameLoop] Defeat recovery: waiting for Continue to retry this day");
        yield return new WaitUntil(() => _nightDone);

        RecoverTeamForRetry();

        _defeatPending = false;
        _isDefeatRecoveryNight = false;
    }

    private void RecoverTeamForRetry()
    {
        if (teamManager == null || teamManager.Team == null) return;

        foreach (var ch in teamManager.Team)
        {
            if (ch == null) continue;

            if (ch.IsAlive()) ch.Heal(ch.maxHp);
            else ch.Revive(ch.maxHp);

            // Reset per-night character ability state for the retry attempt.
            ch.OnNightPhase();
        }

        Debug.Log("[GameLoop] Team restored for retry");
    }
}