using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class CombatSystem : MonoBehaviour
{
    public static CombatSystem Instance { get; private set; }

    // --- Events ---
    public static event Action             OnCombatStart;
    public static event Action             OnCombatEnd;
    public static event Action             OnEnemyDefeated;
    public static event Action<string,int> OnCharacterAttack;
    public static event Action<string,int> OnCharacterTakeDamage;
    public static event Action<string>     OnCharacterDowned;
    public static event Action<string>     OnAbilityTriggered;
    public static event Action<int>        OnEnemyHpChanged;

    [Header("Timing")]
    [SerializeField] private float attackDelay = 0.8f;
    [SerializeField] private float endDelay    = 1.5f;

    [Header("Combat Settings")]
    [SerializeField] private int maxRounds = 5;

    [Header("Level Win Targets")]
    [SerializeField, Range(0f, 1f)] private float level1PlayerWinChance = 0.80f;
    [SerializeField, Range(0f, 1f)] private float level2PlayerWinChance = 0.60f;
    [SerializeField, Range(0f, 1f)] private float level3PlayerWinChance = 0.50f;

    private bool _isBusy;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void StartCombat(List<Character> team, bool ngPlus, Action onComplete = null)
    {
        if (_isBusy)
        {
            Debug.LogWarning("[Combat] Already in combat — ignoring duplicate call.");
            return;
        }
        StartCoroutine(CombatRoutine(team, ngPlus, onComplete));
    }

    // -------------------------------------------------------
    private IEnumerator CombatRoutine(List<Character> team, bool ngPlus, Action onComplete)
    {
        _isBusy = true;
        OnCombatStart?.Invoke();

        // Set enemy HP based on current level
        int level      = SceneLoader.GetCurrentLevel();
        int baseHp     = GameData.BASE_ENEMY_HP + ((level - 1) * 20);

        float targetWinChance = GetTargetWinChance(level);
        bool playerFavored = UnityEngine.Random.value < targetWinChance;

        float enemyHpScale = GetEnemyHpScale(level, playerFavored);
        float enemyDmgScale = GetEnemyDamageScale(level, playerFavored);
        float playerDmgScale = GetPlayerDamageScale(level, playerFavored);
        float enemyDodgeChance = GetEnemyDodgeChance(level, playerFavored);

        int enemyHp    = Mathf.RoundToInt((baseHp + (ngPlus ? GameData.NG_PLUS_ENEMY_HP_BONUS : 0)) * enemyHpScale);
        enemyHp = Mathf.Max(1, enemyHp);

        // Enemy dodge charges per level
        // Level 1 and 2 = 1 dodge, Level 3 = 2 dodges
        int enemyDodgesLeft = level >= 3 ? 2 : 1;

        Debug.Log($"[Combat] Enemy appears! HP: {enemyHp} | Dodges: {enemyDodgesLeft} | Level {level} | " +
              $"TargetWin={targetWinChance:P0} Favored={playerFavored}");
        OnEnemyHpChanged?.Invoke(enemyHp);

        yield return new WaitForSeconds(0.5f);

        // ===== MULTI ROUND COMBAT LOOP =====
        for (int round = 1; round <= maxRounds; round++)
        {
            Debug.Log($"[Combat] ── Round {round} ──");

            // Check if team is still alive at start of round
            if (!team.Any(c => c.IsAlive()))
            {
                Debug.Log("[Combat] All characters downed — combat over");
                break;
            }

            // === PHASE 1: Each alive character attacks enemy ===
            foreach (var ch in team)
            {
                if (!ch.IsAlive()) continue;

                int dmg = Mathf.Max(1, Mathf.RoundToInt(ch.GetAttackDamage(ngPlus) * playerDmgScale));

                // Enemy dodge check — uses a charge if available
                if (enemyDodgesLeft > 0)
                {
                    // 40% chance enemy dodges this hit
                    bool enemyDodges = UnityEngine.Random.value < enemyDodgeChance;

                    if (enemyDodges)
                    {
                        enemyDodgesLeft--;
                        OnAbilityTriggered?.Invoke(
                            $"Enemy dodged {ch.name}'s attack! " +
                            $"({enemyDodgesLeft} dodges left)");
                        Debug.Log($"[Combat] Enemy dodged {ch.name}'s {dmg} dmg attack — " +
                                  $"dodges left: {enemyDodgesLeft}");

                        yield return new WaitForSeconds(attackDelay);
                        continue; // skip damage this hit
                    }
                }

                // Hit landed — deal damage to enemy
                enemyHp -= dmg;
                enemyHp  = Mathf.Max(0, enemyHp);

                OnCharacterAttack?.Invoke(ch.name, dmg);
                OnEnemyHpChanged?.Invoke(enemyHp);

                Debug.Log($"[Combat] {ch.name} hits for {dmg} — Enemy HP: {enemyHp}");

                yield return new WaitForSeconds(attackDelay);

                // Enemy defeated mid-round
                if (enemyHp <= 0)
                {
                    Debug.Log("[Combat] Enemy defeated!");
                    OnEnemyDefeated?.Invoke();
                    yield return new WaitForSeconds(endDelay);
                    FinishCombat(onComplete);
                    yield break;
                }
            }

            // === PHASE 2: Enemy attacks 2 characters every round ===
            var aliveChars = team.Where(c => c.IsAlive()).ToList();

            int enemyDmg = GameData.ENEMY_BASE_DAMAGE
                + ((level - 1) * 4)
                + (ngPlus ? GameData.NG_PLUS_DAMAGE_BONUS : 0);
            enemyDmg = Mathf.Max(1, Mathf.RoundToInt(enemyDmg * enemyDmgScale));

            // Always hits 2 characters every round
            int hits = Mathf.Min(2, aliveChars.Count);

            // Shuffle alive list so targets are random each round
            var targets = aliveChars
                .OrderBy(_ => UnityEngine.Random.value)
                .Take(hits)
                .ToList();

            Debug.Log($"[Combat] Enemy attacks {hits} character(s) for {enemyDmg} each");

            foreach (var victim in targets)
            {
                bool hadDodge = victim.name == "Phoenix" && victim.hasDodge;
                bool tookDmg  = victim.TakeDamage(enemyDmg);

                if (!tookDmg && hadDodge)
                {
                    OnAbilityTriggered?.Invoke($"{victim.name} used Feather Dodge!");
                    Debug.Log($"[Combat] {victim.name} dodged the enemy attack!");
                }
                else
                {
                    OnCharacterTakeDamage?.Invoke(victim.name, enemyDmg);
                    Debug.Log($"[Combat] {victim.name} took {enemyDmg} dmg — " +
                              $"HP: {victim.hp}/{victim.maxHp}");

                    if (!victim.IsAlive())
                    {
                        OnCharacterDowned?.Invoke(victim.name);
                        Debug.Log($"[Combat] {victim.name} is downed!");
                    }
                }

                yield return new WaitForSeconds(attackDelay);
            }

            // === Check if entire team is downed ===
            if (!team.Any(c => c.IsAlive()))
            {
                Debug.Log("[Combat] Entire team downed — combat over");
                yield return new WaitForSeconds(endDelay);
                FinishCombat(onComplete);
                yield break;
            }

            // Small pause between rounds
            yield return new WaitForSeconds(0.3f);
        }

        // === Max rounds reached — enemy retreats ===
        if (enemyHp > 0)
        {
            Debug.Log($"[Combat] Enemy retreats after {maxRounds} rounds!");
            OnAbilityTriggered?.Invoke("The enemy retreats into the shadows...");
            OnEnemyDefeated?.Invoke();
        }

        yield return new WaitForSeconds(endDelay);
        FinishCombat(onComplete);
    }

    private void FinishCombat(Action onComplete)
    {
        _isBusy = false;
        OnCombatEnd?.Invoke();
        onComplete?.Invoke();
    }

    private float GetTargetWinChance(int level)
    {
        return level switch
        {
            1 => level1PlayerWinChance,
            2 => level2PlayerWinChance,
            _ => level3PlayerWinChance,
        };
    }

    private float GetEnemyHpScale(int level, bool playerFavored)
    {
        if (level == 1) return playerFavored ? 0.70f : 1.45f;
        if (level == 2) return playerFavored ? 0.85f : 1.20f;
        return playerFavored ? 0.95f : 1.05f;
    }

    private float GetEnemyDamageScale(int level, bool playerFavored)
    {
        if (level == 1) return playerFavored ? 0.70f : 1.45f;
        if (level == 2) return playerFavored ? 0.85f : 1.20f;
        return playerFavored ? 0.95f : 1.05f;
    }

    private float GetPlayerDamageScale(int level, bool playerFavored)
    {
        if (level == 1) return playerFavored ? 1.25f : 0.75f;
        if (level == 2) return playerFavored ? 1.10f : 0.90f;
        return playerFavored ? 1.03f : 0.97f;
    }

    private float GetEnemyDodgeChance(int level, bool playerFavored)
    {
        if (level == 1) return playerFavored ? 0.20f : 0.60f;
        if (level == 2) return playerFavored ? 0.30f : 0.50f;
        return 0.40f;
    }
}