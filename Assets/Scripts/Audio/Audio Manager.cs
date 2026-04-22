using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Sources")]
    [SerializeField] private AudioSource musicSource;
    [SerializeField] private AudioSource sfxSource;

    [Header("Music")]
    [SerializeField] private AudioClip menuMusic;
    [SerializeField] private AudioClip exploreMusic;
    [SerializeField] private AudioClip combatMusic;
    [SerializeField] private AudioClip nightMusic;
    [SerializeField] private AudioClip endingMusic;

    [Header("SFX")]
    [SerializeField] private AudioClip attackSfx;
    [SerializeField] private AudioClip damageSfx;
    [SerializeField] private AudioClip healSfx;
    [SerializeField] private AudioClip victorySfx;
    [SerializeField] private AudioClip abilitySfx;
    [SerializeField] private AudioClip downdedSfx;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnEnable()
    {
        GameLoop.OnPhaseChanged += HandlePhaseMusic;

        CombatSystem.OnCharacterAttack    += (_,__) => PlaySfx(attackSfx);
        CombatSystem.OnCharacterTakeDamage+= (_,__) => PlaySfx(damageSfx);
        CombatSystem.OnEnemyDefeated      += ()     => PlaySfx(victorySfx);
        CombatSystem.OnAbilityTriggered   += _      => PlaySfx(abilitySfx);
        CombatSystem.OnCharacterDowned    += _      => PlaySfx(downdedSfx);

        TeamManager.OnCharacterRevived += _ => PlaySfx(healSfx);
    }

    void OnDisable()
    {
        GameLoop.OnPhaseChanged -= HandlePhaseMusic;
    }

    private void HandlePhaseMusic(GamePhase phase)
    {
        switch (phase)
        {
            case GamePhase.MainMenu:
            case GamePhase.DifficultySelect: PlayMusic(menuMusic);    break;
            case GamePhase.ZoneSelect:
            case GamePhase.DayExploration:   PlayMusic(exploreMusic); break;
            case GamePhase.Combat:           PlayMusic(combatMusic);  break;
            case GamePhase.NightPhase:       PlayMusic(nightMusic);   break;
            case GamePhase.Ending:           PlayMusic(endingMusic);  break;
        }
    }

    public void PlayMusic(AudioClip clip)
    {
        if (clip == null || musicSource == null || musicSource.clip == clip) return;
        musicSource.clip  = clip;
        musicSource.loop  = true;
        musicSource.Play();
    }

    public void PlaySfx(AudioClip clip)
    {
        if (clip == null || sfxSource == null) return;
        sfxSource.PlayOneShot(clip);
    }

    public void SetMusicVolume(float v) { if (musicSource) musicSource.volume = Mathf.Clamp01(v); }
    public void SetSfxVolume(float v)   { if (sfxSource)   sfxSource.volume   = Mathf.Clamp01(v); }
}
