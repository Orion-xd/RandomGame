using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// BGMとSEを再生する常駐オブジェクト。
/// シーン配置は不要で、Resources/Audio以下の音源をファイル名で読み込む。
/// </summary>
public sealed class GameAudio : MonoBehaviour
{
    public static class Sfx
    {
        public const string Jump = "Jump";
        public const string Dash = "Dash";
        public const string Attack = "Attack";
        public const string PlayerHit = "Slash";
        public const string PlayerDead = "Dead";
        public const string Fall = "Fall";
        public const string Clear = "Clear";
        public const string DialogueText = "DialogueText";
        public const string Button = "Button";
        public const string Enemy2Attack = "SlimeAttacked";
        public const string UnlockItem = "UnlockItem";
    }

    private const string BgmFolder = "Audio/BGM/";
    private const string SfxFolder = "Audio/SFX/";
    private const float BgmVolume = 0.55f;
    private const float BgmFadeInSeconds = 1f;

    private static GameAudio _instance;

    private readonly Dictionary<string, AudioClip> _sfxCache = new Dictionary<string, AudioClip>();
    private AudioSource _bgmSource;
    private AudioSource _sfxSource;
    private AudioSource _dialogueTextSource;
    private AudioSource _dialoguePageAudioSource;
    private AudioListener _fallbackListener;
    private string _currentBgmName;
    private string _pendingBgmName;
    private bool _bgmPending;
    private bool _bgmFadingIn;
    private float _bgmFadeStartedAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        EnsureInstance();
    }

    public static void PlaySfx(string clipName, float volumeScale = 1f)
    {
        if (string.IsNullOrEmpty(clipName)) return;
        EnsureInstance().PlaySfxInternal(clipName, volumeScale);
    }

    public static void StopBgm()
    {
        if (_instance == null) return;
        _instance.StopBgmInternal();
    }

    public static void StartDialogueTextSfx()
    {
        EnsureInstance().StartDialogueTextSfxInternal();
    }

    public static void StopDialogueTextSfx()
    {
        if (_instance == null) return;
        _instance._dialogueTextSource.Stop();
    }

    public static bool IsDialoguePageAudioPlaying =>
        _instance != null && _instance._dialoguePageAudioSource.isPlaying;

    public static void PlayDialoguePageAudio(AudioClip clip)
    {
        if (clip == null) return;

        GameAudio instance = EnsureInstance();
        instance._dialoguePageAudioSource.Stop();
        instance._dialoguePageAudioSource.clip = clip;
        instance._dialoguePageAudioSource.Play();
    }

    public static void StopDialoguePageAudio()
    {
        if (_instance == null) return;
        _instance._dialoguePageAudioSource.Stop();
        _instance._dialoguePageAudioSource.clip = null;
    }

    private static GameAudio EnsureInstance()
    {
        if (_instance != null) return _instance;

        var go = new GameObject("GameAudio");
        _instance = go.AddComponent<GameAudio>();
        return _instance;
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);

        _bgmSource = gameObject.AddComponent<AudioSource>();
        _bgmSource.loop = true;
        _bgmSource.playOnAwake = false;
        _bgmSource.spatialBlend = 0f;
        _bgmSource.volume = BgmVolume;

        _sfxSource = gameObject.AddComponent<AudioSource>();
        _sfxSource.loop = false;
        _sfxSource.playOnAwake = false;
        _sfxSource.spatialBlend = 0f;
        _sfxSource.volume = 0.8f;

        _dialogueTextSource = gameObject.AddComponent<AudioSource>();
        _dialogueTextSource.loop = true;
        _dialogueTextSource.playOnAwake = false;
        _dialogueTextSource.spatialBlend = 0f;
        _dialogueTextSource.volume = 0.35f;

        _dialoguePageAudioSource = gameObject.AddComponent<AudioSource>();
        _dialoguePageAudioSource.loop = false;
        _dialoguePageAudioSource.playOnAwake = false;
        _dialoguePageAudioSource.spatialBlend = 0f;
        _dialoguePageAudioSource.volume = 0.8f;

        _fallbackListener = gameObject.AddComponent<AudioListener>();
        RefreshFallbackAudioListener();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void Start()
    {
        RefreshFallbackAudioListener();
        QueueBgmForScene(SceneManager.GetActiveScene().name);
        StartCoroutine(WireButtonsAfterSceneLoad(SceneManager.GetActiveScene()));
    }

    private void Update()
    {
        if (_bgmPending && !DialoguePlayer.IsPlaying && InputLock.InputAllowed)
        {
            _bgmPending = false;
            PlayBgm(_pendingBgmName);
        }

        if (!_bgmFadingIn) return;

        float progress = Mathf.Clamp01((Time.unscaledTime - _bgmFadeStartedAt) / BgmFadeInSeconds);
        _bgmSource.volume = Mathf.Lerp(0f, BgmVolume, progress);
        if (progress >= 1f) _bgmFadingIn = false;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RefreshFallbackAudioListener();
        QueueBgmForScene(scene.name);
        StartCoroutine(WireButtonsAfterSceneLoad(scene));
    }

    private IEnumerator WireButtonsAfterSceneLoad(Scene scene)
    {
        yield return null;
        if (!scene.isLoaded) yield break;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Button button in root.GetComponentsInChildren<Button>(true))
            {
                button.onClick.RemoveListener(HandleButtonClicked);
                button.onClick.AddListener(HandleButtonClicked);
            }
        }
    }

    private void HandleButtonClicked()
    {
        PlaySfxInternal(Sfx.Button, 1f);
    }

    private void RefreshFallbackAudioListener()
    {
        AudioListener[] listeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
        bool hasSceneListener = false;
        foreach (AudioListener listener in listeners)
        {
            if (listener != _fallbackListener && listener.enabled && listener.gameObject.activeInHierarchy)
            {
                hasSceneListener = true;
                break;
            }
        }

        _fallbackListener.enabled = !hasSceneListener;
    }

    private void QueueBgmForScene(string sceneName)
    {
        StopBgmInternal();

        _pendingBgmName = ResolveBgmName(sceneName);
        _bgmPending = !string.IsNullOrEmpty(_pendingBgmName);
    }

    private void StopBgmInternal()
    {
        _bgmSource.Stop();
        _bgmSource.clip = null;
        _bgmSource.volume = BgmVolume;
        _currentBgmName = null;
        _pendingBgmName = null;
        _bgmPending = false;
        _bgmFadingIn = false;
    }

    private void PlayBgm(string bgmName)
    {
        if (_currentBgmName == bgmName && _bgmSource.isPlaying) return;

        _currentBgmName = bgmName;
        AudioClip clip = LoadClip(BgmFolder, bgmName);

        if (clip == null)
        {
            _bgmSource.Stop();
            _bgmSource.clip = null;
            return;
        }

        _bgmSource.clip = clip;
        _bgmSource.volume = 0f;
        _bgmSource.Play();
        _bgmFadeStartedAt = Time.unscaledTime;
        _bgmFadingIn = true;
    }

    private static string ResolveBgmName(string sceneName)
    {
        switch (sceneName)
        {
            case "Stage1":
            case "Stage2_test":
                return "Forest";
            case "Stage3_test":
                return "Cave";
            case "Stage4_new":
                return "Boss";
            default:
                return null;
        }
    }

    private void PlaySfxInternal(string clipName, float volumeScale)
    {
        AudioClip clip = LoadSfx(clipName);
        if (clip == null) return;

        _sfxSource.PlayOneShot(clip, Mathf.Clamp01(volumeScale));
    }

    private void StartDialogueTextSfxInternal()
    {
        if (_dialogueTextSource.isPlaying) return;

        AudioClip clip = LoadSfx(Sfx.DialogueText);
        if (clip == null) return;

        _dialogueTextSource.clip = clip;
        _dialogueTextSource.Play();
    }

    private AudioClip LoadSfx(string clipName)
    {
        if (_sfxCache.TryGetValue(clipName, out AudioClip clip)) return clip;

        clip = LoadClip(SfxFolder, clipName);
        if (clip != null) _sfxCache.Add(clipName, clip);
        return clip;
    }

    private static AudioClip LoadClip(string folder, string clipName)
    {
        AudioClip clip = Resources.Load<AudioClip>(folder + clipName);
        if (clip != null) return clip;

        string resourceFolder = folder.TrimEnd('/');
        AudioClip[] candidates = Resources.LoadAll<AudioClip>(resourceFolder);
        foreach (AudioClip candidate in candidates)
        {
            if (string.Equals(candidate.name, clipName, System.StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return null;
    }
}
