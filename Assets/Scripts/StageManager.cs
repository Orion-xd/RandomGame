using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 1ステージの進行管理。クリア / 失敗の判定、結果画面の表示、結果画面のボタン処理。
/// 各ステージシーンに1つ置く（結果画面の Canvas を子に持つ）。
///
/// クリア = Goal に触れる（ボスがいれば撃破後）。
/// 失敗   = 体力0（PlayerHealth.OnDied）または落下（y &lt; killY）。
/// 結果画面表示中は Time.timeScale = 0 で停止し、プレイヤーの操作スクリプトを無効化する。
/// 「もう一度」はシーンの再読み込みなので、アクションの並びなども含めて完全に初期化される。
/// </summary>
public class StageManager : MonoBehaviour
{
    [Header("結果画面パネル")]
    [SerializeField] private GameObject clearPanel;
    [SerializeField] private GameObject failPanel;

    [Tooltip("次のステージが無いとき隠すボタン。OnClick は OnNextStage を各ボタンの OnClick に設定する")]
    [SerializeField] private Button nextButton;

    [Header("落下死")]
    [Tooltip("プレイヤーの y がこれを下回ったら失敗")]
    [SerializeField] private float killY = -12f;

    [Header("入力ロック")]
    [Tooltip("ステージ開始時（および開始会話の直後）、この秒数だけ入力を無効化する（連打の勢いでの誤アクション防止）")]
    [SerializeField] private float inputLockDuration = 0.5f;

    private PlayerHealth _playerHealth;
    private Transform _playerTf;
    private bool _ended;
    private bool _introPlaying;

    private void Awake()
    {
        Time.timeScale = 1f;
        if (clearPanel != null) clearPanel.SetActive(false);
        if (failPanel != null) failPanel.SetActive(false);
    }

    private void Start()
    {
        var p = GameObject.FindGameObjectWithTag("Player");
        if (p != null)
        {
            _playerTf = p.transform;
            _playerHealth = p.GetComponent<PlayerHealth>();
            if (_playerHealth != null) _playerHealth.OnDied += Fail;
        }
        if (nextButton != null) nextButton.gameObject.SetActive(GameFlow.HasNextStage);

        InputLock.LockFor(inputLockDuration); // ステージ画面に入った直後は入力を無効化

        TryPlayIntro();
    }

    private void OnDestroy()
    {
        if (_playerHealth != null) _playerHealth.OnDied -= Fail;
    }

    private void Update()
    {
        if (_ended || _introPlaying || _playerTf == null) return;
        if (_playerTf.position.y < killY) Fail();
    }

    // ── ステージ開始時の会話（そのステージに初めて入ったときだけ） ──

    private void TryPlayIntro()
    {
        int idx = GameFlow.CurrentStageIndex;
        if (GameFlow.HasSeenIntro(idx)) return;

        var seq = GameFlow.Stages != null ? GameFlow.Stages.IntroAt(idx) : null;
        var player = FindAnyObjectByType<DialoguePlayer>();
        if (player == null || seq == null || seq.Count == 0)
        {
            GameFlow.MarkIntroSeen(idx); // 会話が無いステージは既読扱いにして以後スキップ
            return;
        }

        _introPlaying = true;
        Time.timeScale = 0f; // プレイヤーも敵も止める
        player.Play(seq, OnIntroFinished);
    }

    private void OnIntroFinished()
    {
        GameFlow.MarkIntroSeen(GameFlow.CurrentStageIndex);
        _introPlaying = false;
        if (_ended) return;
        Time.timeScale = 1f;
        InputLock.LockFor(inputLockDuration); // 開始会話を送り切った勢いでアクションが出ないように
    }

    public void Clear()
    {
        if (_ended) return;
        _ended = true;
        GameFlow.MarkStageCleared(GameFlow.CurrentStageIndex); // 次のステージを解放
        FreezeGameplay();
        if (clearPanel != null) clearPanel.SetActive(true);
        Time.timeScale = 0f;
    }

    public void Fail()
    {
        if (_ended) return;
        _ended = true;
        FreezeGameplay();
        if (failPanel != null) failPanel.SetActive(true);
        Time.timeScale = 0f;
    }

    private void FreezeGameplay()
    {
        var p = GameObject.FindGameObjectWithTag("Player");
        if (p == null) return;
        var pc = p.GetComponent<PlayerController>();
        var mac = p.GetComponent<MainActionController>();
        if (pc != null) pc.enabled = false;
        if (mac != null) mac.enabled = false;
        var rb = p.GetComponent<Rigidbody2D>();
        if (rb != null) rb.linearVelocity = Vector2.zero;
    }

    // ── 結果画面ボタン ──
    public void OnNextStage() => GameFlow.NextStage();
    public void OnRetry() => GameFlow.RetryStage();
    public void OnStageSelect() => GameFlow.GoStageSelect();
}
