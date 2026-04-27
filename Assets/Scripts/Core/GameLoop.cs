using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.Collections;
using RPGCharacterAnims;

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
    private bool _allZonesDone = false;
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
        Save.lastSceneBuildIndex = Mathf.Clamp(
            SceneManager.GetActiveScene().buildIndex, 1, 3);
        SaveSystem.Save(Save);

        RefreshSceneReferences();
        teamManager?.SetupTeamForLevel(Mathf.Max(1, CurrentLevel), Difficulty);
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

        yield return new WaitUntil(() => _combatDone);
        yield return new WaitForSeconds(1f);

        _encounterCombatActive = false;

        if (!teamManager.IsTeamAlive())
        {
            Debug.Log("[GameLoop] All characters downed — scheduling retry");
            _defeatPending = true;
            yield break;
        }

        SetPhase(GamePhase.DayExploration);
        OnDayStarted?.Invoke(CurrentDay, CurrentZone);

        CheckAllZonesDone();

        Debug.Log("[GameLoop] Encounter done — back to exploration");
    }

    // -------------------------------------------------------
    private void CheckAllZonesDone()
    {
        var zones   = FindObjectsByType<EncounterZone>(FindObjectsSortMode.None);
        int total   = zones.Length;
        int cleared = 0;

        foreach (var z in zones)
            if (z.hasTriggered) cleared++;

        Debug.Log($"[GameLoop] Zones cleared: {cleared} / {total}");

        if (cleared >= total && total > 0)
        {
            Debug.Log("[GameLoop] All encounter zones cleared — triggering night");
            _allZonesDone = true;
        }
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
            CurrentDay    = day;
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

            // Wait until all zones cleared OR team wiped
            yield return new WaitUntil(() =>
                _allZonesDone ||
                _defeatPending
            );

            // Wait for any active combat to finish
            yield return new WaitUntil(() => !_encounterCombatActive);

            // Handle defeat retry
            if (_defeatPending)
            {
                yield return StartCoroutine(HandleDefeatRetry());
                day--;
                continue;
            }

            // All zones cleared — level complete
            if (_allZonesDone)
            {
                yield return new WaitForSeconds(0.5f);

                // Night phase before level complete
                _nightDone = false;
                SetPhase(GamePhase.NightPhase);
                OnNightStarted?.Invoke(day);
                teamManager.ApplyNightAbilities();

                Save.totalRuns++;
                Save.lastSceneBuildIndex = Mathf.Clamp(
                    SceneManager.GetActiveScene().buildIndex, 1, 3);
                if (Difficulty > 0) Save.lastDifficulty = Difficulty;
                SaveSystem.Save(Save);

                Debug.Log($"[GameLoop] Night {day} — waiting for player to continue");
                yield return new WaitUntil(() => _nightDone);
                yield return new WaitForSeconds(0.5f);

                // If this was the last day go to level complete
                SetPhase(GamePhase.LevelComplete);
                OnLevelComplete?.Invoke(CurrentLevel);
                yield break;
            }

            // Normal night after partial exploration
            yield return new WaitForSeconds(1f);

            _nightDone = false;
            SetPhase(GamePhase.NightPhase);
            OnNightStarted?.Invoke(day);
            teamManager.ApplyNightAbilities();

            Save.totalRuns++;
            Save.lastSceneBuildIndex = Mathf.Clamp(
                SceneManager.GetActiveScene().buildIndex, 1, 3);
            if (Difficulty > 0) Save.lastDifficulty = Difficulty;
            SaveSystem.Save(Save);

            Debug.Log($"[GameLoop] Night {day} — waiting for player to continue");
            yield return new WaitUntil(() => _nightDone);
            yield return new WaitForSeconds(0.5f);

            Debug.Log($"[GameLoop] Morning — starting Day {day + 1}");
        }

        // All 5 days done — level complete
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
        Save.lastSceneBuildIndex = Mathf.Clamp(
            SceneManager.GetActiveScene().buildIndex, 1, 3);
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
        ResetTransitionState(scene.buildIndex);
        HandleSceneEntry(scene.buildIndex);
    }

    // -------------------------------------------------------
    // KEY FIX — Always auto-start, never wait for difficulty select
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

        // Always auto-start — no difficulty select screen in game scenes
        // Difficulty select happens at Main Menu level only
        int restoredDifficulty = Mathf.Clamp(
            Save.lastDifficulty > 0 ? Save.lastDifficulty : Save.maxDifficulty,
            1, 7);

        if (!_runInitialized || Difficulty <= 0)
        {
            Difficulty = restoredDifficulty;
            _runInitialized = true;
            Debug.Log($"[GameLoop] Auto-starting Level {CurrentLevel} at difficulty {Difficulty} in {CurrentZone}");
        }

        teamManager?.SetupTeamForLevel(CurrentLevel, Difficulty);
        Debug.Log($"[GameLoop] Team configured for level {CurrentLevel}: {teamManager?.GetAliveCount() ?? 0} active");

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

    private void ResetTransitionState(int buildIndex)
    {
        _combatDone = false;
        _nightDone = false;
        _encounterCombatActive = false;
        _defeatPending = false;
        _isDefeatRecoveryNight = false;

        if (PauseManager.Instance != null)
        {
            PauseManager.Instance.Resume();
            PauseManager.Instance.RefreshPauseables();
        }

        bool isGameplayScene = buildIndex != SceneLoader.SCENE_MAIN_MENU &&
                       buildIndex != SceneLoader.SCENE_ENDING;

        if (isGameplayScene)
        {
            // Enforce correct controller state immediately and again next frame
            // to beat any first-Update race where the legacy controller could
            // start the Face action (A/D = look instead of move).
            EnforceInputControllers();
            StartCoroutine(EnforceInputControllersNextFrame());
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
        }
    }

    private void EnforceInputControllers()
    {
        // Disable every legacy input controller in the scene.
        foreach (var c in FindObjectsByType<RPGCharacterInputController>(FindObjectsSortMode.None))
            c.enabled = false;

        // Make sure the new Input System controller is enabled.
        foreach (var c in FindObjectsByType<RPGCharacterInputSystemController>(FindObjectsSortMode.None))
            c.enabled = true;

        // Clear any Face / Strafe state left over from the previous scene or
        // from the legacy controller running for one frame before being disabled.
        foreach (var c in FindObjectsByType<RPGCharacterController>(FindObjectsSortMode.None))
        {
            if (c.CanEndAction("Face"))   c.EndAction("Face");
            if (c.CanEndAction("Strafe")) c.EndAction("Strafe");
        }
    }

    private IEnumerator EnforceInputControllersNextFrame()
    {
        yield return null;
        EnforceInputControllers();
    }

    // -------------------------------------------------------
    private IEnumerator HandleDefeatRetry()
    {
        _isDefeatRecoveryNight = true;
        _nightDone = false;

        SetPhase(GamePhase.NightPhase);
        OnNightStarted?.Invoke(CurrentDay);

        Debug.Log("[GameLoop] Defeat recovery — waiting for Continue");
        yield return new WaitUntil(() => _nightDone);

        RecoverTeamForRetry();

        _defeatPending         = false;
        _isDefeatRecoveryNight = false;
    }

    private void RecoverTeamForRetry()
    {
        if (teamManager == null || teamManager.Team == null) return;

        foreach (var ch in teamManager.Team)
        {
            if (ch == null) continue;
            if (ch.IsAlive()) ch.Heal(ch.maxHp);
            else              ch.Revive(ch.maxHp);
            ch.OnNightPhase();
        }

        Debug.Log("[GameLoop] Team restored for retry");
    }
}