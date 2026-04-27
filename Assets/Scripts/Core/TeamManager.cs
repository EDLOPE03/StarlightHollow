using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;

public class TeamManager : MonoBehaviour
{
    // Singleton
    public static TeamManager Instance { get; private set; }

    // --- Events (UI and other systems subscribe) ---
    public static event Action<List<Character>> OnTeamSetup;
    public static event Action<Character>       OnCharacterDowned;
    public static event Action<Character>       OnCharacterRevived;
    public static event Action                  OnAllTeamDowned;

    // --- State ---
    public List<Character> Team            { get; private set; } = new List<Character>();
    public int             CurrentDifficulty { get; private set; }

    // -------------------------------------------------------
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // --- Build team based on current level rules ---
    public void SetupTeam(int difficulty)
    {
        int level = Mathf.Max(1, SceneLoader.GetCurrentLevel());
        SetupTeamForLevel(level, difficulty);
    }

    public void SetupTeamForLevel(int level, int difficulty)
    {
        CurrentDifficulty = difficulty;
        Team.Clear();

        int activeCount = GetActiveCountForLevel(level);
        var rosterByPower = GetRosterByPower();

        for (int i = 0; i < rosterByPower.Count; i++)
        {
            string       charName = rosterByPower[i];
            CharacterData data    = GameData.CHARACTERS[charName];

            var ch = new Character(charName, data.maxHp, data.baseAtk);

            // Characters beyond the active count start Downed
            if (i >= activeCount)
            {
                ch.hp     = 0;
                ch.status = "Downed";
            }

            // Subscribe to status changes for event forwarding
            ch.OnStatusChanged += (status) => HandleStatusChange(ch, status);

            Team.Add(ch);
        }

        Debug.Log($"[Team] Setup for level {level}: {GetAliveCount()} active of {Team.Count}");
        OnTeamSetup?.Invoke(Team);
    }

    private static int GetActiveCountForLevel(int level)
    {
        if (level <= 1) return 7;
        if (level == 2) return 5;
        return 2;
    }

    private static List<string> GetRosterByPower()
    {
        return GameData.CHARACTERS
            .OrderByDescending(kvp => kvp.Value.baseAtk)
            .ThenByDescending(kvp => kvp.Value.maxHp)
            .ThenBy(kvp => kvp.Key)
            .Select(kvp => kvp.Key)
            .ToList();
    }

    // --- Night Phase: apply all passive abilities ---
    public void ApplyNightAbilities()
    {
        foreach (var ch in Team)
        {
            ch.OnNightPhase(); // resets Phoenix dodge, Jade heal, etc.

            if (!ch.IsAlive()) continue;

            // Forest: Night Bloom — heal 5 HP
            if (ch.name == "Forest")
            {
                ch.Heal(GameData.NIGHT_HEAL_FOREST);
                Debug.Log($"[Forest] Night Bloom: healed {GameData.NIGHT_HEAL_FOREST} HP. Now {ch.hp}/{ch.maxHp}.");
            }
        }
    }

    // --- Jade ability: heal a teammate ---
    public bool UseJadeHeal(string targetName)
    {
        var jade = GetCharacter("Jade");
        if (jade == null || !jade.IsAlive() || jade.hasUsedJadeHeal)
        {
            Debug.Log("[Jade] Mend unavailable.");
            return false;
        }

        var target = GetCharacter(targetName);
        if (target == null || !target.IsAlive()) return false;

        target.Heal(GameData.JADE_HEAL_AMOUNT);
        jade.hasUsedJadeHeal = true;
        Debug.Log($"[Jade] Mend: {targetName} healed for {GameData.JADE_HEAL_AMOUNT} HP.");
        return true;
    }

    // --- Potion use ---
    public bool UsePotion(string targetName)
    {
        var target = GetCharacter(targetName);
        if (target == null || !target.IsAlive()) return false;
        target.Heal(GameData.POTION_HEAL_AMOUNT);
        Debug.Log($"[Potion] {targetName} healed for {GameData.POTION_HEAL_AMOUNT} HP.");
        return true;
    }

    // --- True Ending check ---
    public bool CanGetTrueEnding() =>
        CurrentDifficulty == 1 &&
        GetAliveCount() == 1 &&
        GetAliveCharacters().Any(c => c.name == "Astra");

    // --- Getters ---
    public List<Character> GetAliveCharacters() =>
        Team.Where(c => c.IsAlive()).ToList();
    public int       GetAliveCount()     => Team.Count(c => c.IsAlive());
    public bool      IsTeamAlive()       => Team.Any(c => c.IsAlive());
    public Character GetCharacter(string name) =>
        Team.FirstOrDefault(c => c.name == name);

    // --- Internal: forward character events to team-level events ---
    private void HandleStatusChange(Character ch, string status)
    {
        if (status == "Downed")
        {
            OnCharacterDowned?.Invoke(ch);
            if (GetAliveCount() == 0)
                OnAllTeamDowned?.Invoke();
        }
        else if (status == "Active")
        {
            OnCharacterRevived?.Invoke(ch);
        }
    }
}
