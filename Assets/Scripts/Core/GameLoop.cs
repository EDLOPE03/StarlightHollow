using UnityEngine;
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

    [Header("References")]
    public TeamManager  teamManager;
    public CombatSystem combatSystem;

    // --- Runtime State ---
    public GamePhase CurrentPhase    { get; private set; }
    public int       CurrentDay      { get; private set; }
    public string    CurrentZone     { get; private set; }
    public SaveData  Save            { get; private set; }
    public int       Difficulty      { get; private set; }
    public bool      IsNGPlus        => Save?.ngPlus ?? false;

    private bool _zoneSelected;
    private bool _combatDone;

    // -------------------------------------------------------
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        Save = SaveSystem.Load();

        if (Save.ngPlus)
            OnNGPlusChanged?.Invoke(true);

        SetPhase(GamePhase.MainMenu);
    }

    // Called by UI when player presses New Game / Continue
    public void StartGame(int selectedDifficulty)
    {
        Difficulty = selectedDifficulty;
        teamManager.SetupTeam(Difficulty);
        CurrentDay = 0;
        StartCoroutine(GameRoutine());
    }

    // -------------------------------------------------------
    private IEnumerator GameRoutine()
    {
        for (int day = 1; day <= GameData.TOTAL_DAYS; day++)
        {
            CurrentDay = day;

            // --- Zone Selection ---
            _zoneSelected = false;
            SetPhase(GamePhase.ZoneSelect);
            yield return new WaitUntil(() => _zoneSelected);

            // --- Day Exploration ---
            SetPhase(GamePhase.DayExploration);
            OnDayStarted?.Invoke(day, CurrentZone);
            yield return new WaitForSeconds(1.5f);

            // --- Combat (probability check) ---
            bool doCombat = IsNGPlus
                ? (CurrentZone == GameData.NG_PLUS_ZONE || UnityEngine.Random.value < 0.8f)
                : (UnityEngine.Random.value < 0.6f);

            if (doCombat && teamManager.IsTeamAlive())
            {
                _combatDone = false;
                SetPhase(GamePhase.Combat);
                combatSystem.StartCombat(teamManager.Team, IsNGPlus, () => _combatDone = true);
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
            CurrentZone = null;
        }

        // --- Ending ---
        SetPhase(GamePhase.Ending);
        TriggerEnding();
    }

    // Called by UI zone buttons
    public void SelectZone(string zoneName)
    {
        if (CurrentPhase != GamePhase.ZoneSelect) return;
        CurrentZone   = zoneName;
        _zoneSelected = true;
        Debug.Log($"[GameLoop] Zone selected: {zoneName}");
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
        OnEndingReached?.Invoke(key);
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
        Debug.Log($"[GameLoop] ▶ Phase: {phase}");
    }
}
