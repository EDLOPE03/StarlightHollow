using System.Collections.Generic;
using UnityEngine;

// Typed data class for characters (replaces the tuple system)
[System.Serializable]
public class CharacterData
{
    public string characterName;
    public int maxHp;
    public int baseAtk;
    public string abilityName;
    public string abilityDescription;
    public Color characterColor = Color.white;
}

[System.Serializable]
public class DifficultyData
{
    public string difficultyName;
    public int activeCount;
}

public static class GameData
{
    // Full character roster with proper ability data
    public static readonly Dictionary<string, CharacterData> CHARACTERS =
        new Dictionary<string, CharacterData>()
    {
        { "Forest", new CharacterData { characterName = "Forest", maxHp = 100, baseAtk = 10,
            abilityName = "Night Bloom",
            abilityDescription = "Heals 5 HP each night phase",
            characterColor = new Color(0.2f, 0.8f, 0.2f) }},

        { "Monty", new CharacterData { characterName = "Monty", maxHp = 90, baseAtk = 12,
            abilityName = "Trap Master",
            abilityDescription = "Turns one trap into treasure per 5 days",
            characterColor = new Color(0.8f, 0.6f, 0.2f) }},

        { "Phoenix", new CharacterData { characterName = "Phoenix", maxHp = 80, baseAtk = 15,
            abilityName = "Feather Dodge",
            abilityDescription = "Avoids one attack per battle — refreshes each night",
            characterColor = new Color(1f, 0.4f, 0.1f) }},

        { "Astra", new CharacterData { characterName = "Astra", maxHp = 85, baseAtk = 14,
            abilityName = "Danger Sense",
            abilityDescription = "50% chance to predict zone danger before entering",
            characterColor = new Color(0.6f, 0.4f, 1f) }},

        { "Coral", new CharacterData { characterName = "Coral", maxHp = 95, baseAtk = 11,
            abilityName = "Last Stand",
            abilityDescription = "Deals +2 bonus damage when HP is below 50%",
            characterColor = new Color(1f, 0.4f, 0.6f) }},

        { "Jade", new CharacterData { characterName = "Jade", maxHp = 80, baseAtk = 13,
            abilityName = "Mend",
            abilityDescription = "Restores 10 HP to one teammate during rest phase",
            characterColor = new Color(0.2f, 0.9f, 0.6f) }},

        { "Winter", new CharacterData { characterName = "Winter", maxHp = 85, baseAtk = 12,
            abilityName = "Phase Shift",
            abilityDescription = "Bypasses one zone event outcome per zone",
            characterColor = new Color(0.6f, 0.8f, 1f) }},
    };

    // Preserved character order — matters for difficulty (who starts Downed)
    public static readonly List<string> CHARACTER_ORDER = new List<string>
    { "Forest", "Monty", "Phoenix", "Astra", "Coral", "Jade", "Winter" };

    public static readonly Dictionary<int, DifficultyData> DIFFICULTIES =
        new Dictionary<int, DifficultyData>()
    {
        { 7, new DifficultyData { difficultyName = "Full Spark",     activeCount = 7 }},
        { 6, new DifficultyData { difficultyName = "Fading Light",   activeCount = 6 }},
        { 5, new DifficultyData { difficultyName = "Dim Paths",      activeCount = 5 }},
        { 4, new DifficultyData { difficultyName = "Shattered Crew", activeCount = 4 }},
        { 3, new DifficultyData { difficultyName = "Thin Hope",      activeCount = 3 }},
        { 2, new DifficultyData { difficultyName = "Last Watch",     activeCount = 2 }},
        { 1, new DifficultyData { difficultyName = "Last Spark",     activeCount = 1 }},
    };

    public static readonly List<string> ZONES = new List<string>
    {
        "Whimsy Woods",
        "Giggle Gardens",
        "Dream Coaster",
        "Splash Zone",
        "Prize Vault"
    };

    // Game constants — change values here and they update everywhere
    public const string NG_PLUS_ZONE    = "Hidden Core";
    public const int    TOTAL_DAYS      = 5;
    public const int    BASE_ENEMY_HP   = 20;
    public const int    NG_PLUS_ENEMY_HP_BONUS  = 10;
    public const int    ENEMY_BASE_DAMAGE       = 8;
    public const int    NG_PLUS_DAMAGE_BONUS    = 5;
    public const int    REVIVAL_HP              = 3;
    public const int    POTION_HEAL_AMOUNT      = 10;
    public const int    NIGHT_HEAL_FOREST       = 5;
    public const int    JADE_HEAL_AMOUNT        = 10;
}
