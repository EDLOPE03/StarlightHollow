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
    public static event Action<string,int> OnCharacterAttack;     // (name, dmg)
    public static event Action<string,int> OnCharacterTakeDamage; // (name, dmg)
    public static event Action<string>     OnCharacterDowned;     // (name)
    public static event Action<string>     OnAbilityTriggered;    // (description)
    public static event Action<int>        OnEnemyHpChanged;      // (currentHp)

    [Header("Timing")]
    [SerializeField] private float attackDelay   = 0.8f; // pause between each character attack
    [SerializeField] private float endDelay      = 1.5f; // pause after combat ends

    private bool _isBusy;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // Call this from GameLoop to run a full combat sequence
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

        int enemyHp = GameData.BASE_ENEMY_HP + (ngPlus ? GameData.NG_PLUS_ENEMY_HP_BONUS : 0);
        Debug.Log($"[Combat] Enemy appears! HP: {enemyHp}");
        OnEnemyHpChanged?.Invoke(enemyHp);

        // === PHASE 1: Team attacks ===
        foreach (var ch in team)
        {
            if (!ch.IsAlive()) continue;

            int dmg = ch.GetAttackDamage(ngPlus);
            enemyHp -= dmg;

            OnCharacterAttack?.Invoke(ch.name, dmg);
            OnEnemyHpChanged?.Invoke(Mathf.Max(0, enemyHp));

            yield return new WaitForSeconds(attackDelay);

            if (enemyHp <= 0)
            {
                Debug.Log("[Combat] Enemy defeated!");
                OnEnemyDefeated?.Invoke();
                yield return new WaitForSeconds(endDelay);
                FinishCombat(onComplete);
                yield break;
            }
        }

        // === PHASE 2: Enemy counter-attacks ===
        var alive = team.Where(c => c.IsAlive()).ToList();
        if (alive.Count > 0)
        {
            var victim = alive[UnityEngine.Random.Range(0, alive.Count)];
            int dmg    = GameData.ENEMY_BASE_DAMAGE + (ngPlus ? GameData.NG_PLUS_DAMAGE_BONUS : 0);

            bool hadDodge = victim.name == "Phoenix" && victim.hasDodge;
            bool tookDmg  = victim.TakeDamage(dmg);

            if (!tookDmg && hadDodge)
            {
                // Phoenix dodged
                OnAbilityTriggered?.Invoke($"{victim.name} used Feather Dodge!");
                Debug.Log($"[Combat] {victim.name} dodged the attack!");
            }
            else
            {
                OnCharacterTakeDamage?.Invoke(victim.name, dmg);
                Debug.Log($"[Combat] {victim.name} took {dmg} dmg. HP: {victim.hp}");

                if (!victim.IsAlive())
                {
                    OnCharacterDowned?.Invoke(victim.name);
                    yield return StartCoroutine(ReviveRoutine(victim));
                }
            }
        }

        yield return new WaitForSeconds(endDelay);
        FinishCombat(onComplete);
    }

    // Auto-revival at First-Aid Station
    private IEnumerator ReviveRoutine(Character ch)
    {
        yield return new WaitForSeconds(1f);
        ch.Revive(GameData.REVIVAL_HP);
        Debug.Log($"[Revival] {ch.name} revived with {GameData.REVIVAL_HP} HP.");
    }

    private void FinishCombat(Action onComplete)
    {
        _isBusy = false;
        OnCombatEnd?.Invoke();
        onComplete?.Invoke();
    }
}
