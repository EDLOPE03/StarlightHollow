using System;
using UnityEngine;

[System.Serializable]
public class Character
{
    // --- Core Stats ---
    public string name;
    public int    hp;
    public int    maxHp;
    public int    atk;
    public string status; // "Active" or "Downed"

    // --- Ability Flags ---
    public bool hasDodge;           // Phoenix: refreshes each night
    public bool hasUsedTrapAbility; // Monty: once per 5 days
    public bool hasUsedPhaseShift;  // Winter: once per zone
    public bool hasUsedJadeHeal;    // Jade: once per rest phase

    // --- Events (UI subscribes to these) ---
    public event Action<int, int> OnHpChanged;    // (currentHp, maxHp)
    public event Action<string>   OnStatusChanged; // ("Active" / "Downed")
    public event Action<string>   OnAbilityUsed;   // (abilityName)
    public event Action<string>   OnDialogueLine;  // (line of dialogue)

    // -------------------------------------------------------
    public Character(string n, int h, int a)
    {
        name   = n;
        hp     = h;
        maxHp  = h;
        atk    = a;
        status = h > 0 ? "Active" : "Downed";

        // Phoenix starts every run with dodge ready
        if (name == "Phoenix") hasDodge = true;
    }

    public bool IsAlive() => hp > 0;
    public float GetHpPercent() => maxHp > 0 ? (float)hp / maxHp : 0f;

    // --- Take Damage ---
    // Phoenix dodge is handled here so the UI sees it correctly
    public bool TakeDamage(int amount)
    {
        if (!IsAlive()) return false;

        // Phoenix: Feather Dodge
        if (name == "Phoenix" && hasDodge)
        {
            hasDodge = false;
            OnAbilityUsed?.Invoke("Feather Dodge");
            OnDialogueLine?.Invoke("Phoenix adjusts glasses. \"Statistically… unlikely, but fine.\"");
            return false; // no damage taken
        }

        hp = Mathf.Max(0, hp - amount);
        OnHpChanged?.Invoke(hp, maxHp);

        if (hp <= 0)
        {
            status = "Downed";
            OnStatusChanged?.Invoke("Downed");
        }

        return true; // damage was taken
    }

    // --- Heal ---
    public void Heal(int amount)
    {
        if (!IsAlive()) return; // Use Revive() for downed characters
        int before = hp;
        hp = Mathf.Min(maxHp, hp + amount);
        if (hp != before)
            OnHpChanged?.Invoke(hp, maxHp);
    }

    // --- Revive (from Downed) ---
    public void Revive(int reviveHp = -1)
    {
        if (reviveHp < 0) reviveHp = GameData.REVIVAL_HP;
        hp     = reviveHp;
        status = "Active";
        OnHpChanged?.Invoke(hp, maxHp);
        OnStatusChanged?.Invoke("Active");

        // Speak revival line
        string line = GetRevivalLine();
        if (!string.IsNullOrEmpty(line))
            OnDialogueLine?.Invoke(line);
    }

    // --- Calculate Attack Damage (includes Coral ability) ---
    public int GetAttackDamage(bool ngPlus)
    {
        int dmg = UnityEngine.Random.Range(3, 7);

        // Coral: Last Stand — +2 damage when HP < 50%
        if (name == "Coral" && hp < maxHp / 2)
        {
            dmg += 2;
            OnAbilityUsed?.Invoke("Last Stand");
        }

        // NG+ bonus damage
        if (ngPlus) dmg += UnityEngine.Random.Range(0, 3);

        return dmg;
    }

    // --- Night Phase refresh ---
    public void OnNightPhase()
    {
        // Phoenix dodge refreshes
        if (name == "Phoenix" && IsAlive())
            hasDodge = true;

        // Jade heal resets
        hasUsedJadeHeal = false;
    }

    private string GetRevivalLine()
    {
        return name switch
        {
            "Forest"  => "Forest takes a deep breath. \"I need to keep going...\"",
            "Monty"   => "Monty stretches, wincing. \"Guess I'm not done yet.\"",
            "Phoenix" => "Phoenix adjusts glasses. \"Statistically... unlikely, but fine.\"",
            "Astra"   => "Astra opens eyes slowly. \"I... I can still see.\"",
            "Coral"   => "Coral groans. \"Pain means alive, right?\"",
            "Jade"    => "Jade exhales shakily. \"We'll make it through.\"",
            "Winter"  => "Winter rubs temples. \"Well... systems off, but we're okay.\"",
            _         => ""
        };
    }
}
