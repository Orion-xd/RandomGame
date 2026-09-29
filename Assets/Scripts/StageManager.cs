using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 1ステージの進行管理。クリア / 失敗の判定、結果画面の表示、結果画面のボタン処理。
/// 各ステージシーンに1つ置く（結果画面の Canvas を子に持つ）。
///
/// クリア = Goal に触れる、またはボスを撃破する（`Enemy.clearStageOnDeath`）。どちらの経路も
/// 最終的にこの `Clear()` 1箇所に集約されるため、クリア条件がステージによって違っても扱いは同じ。
/// 失敗   = 体力0（PlayerHealth.OnDied）または落下（y &lt; killY）。
/// 結果画面表示中は Time.timeScale = 0 で停止し、プレイヤーの操作スクリプトを無効化する。
/// 「もう一度」はシーンの再読み込みなので、アクションの並びなども含めて完全に初期化される。
///
/// ── 体力0での失敗・ステージクリアは、それぞれ専用アニメーションを挟む ──
/// 落下死だけは今まで通り即座にパネルを表示する。体力0での失敗・クリアの場合は、
/// 「即座にゲーム内を全停止（Time.timeScale=0）→ プレイヤーの死亡/クリアアニメーション（UnscaledTime で
/// 再生され続ける、ループせず1周だけ）→ アニメーション側の Animation Event（AnimationEventRelay 経由、
/// "ShowFailPanel"/"ShowClearPanel"）でパネルを表示」という流れになる。
/// Time.timeScale=0 にしても Player の Animator は AnimatorUpdateMode.UnscaledTime のため止まらずに進み続ける
/// （他の全オブジェクトは今まで通り Time.deltaTime ベースなので止まる）。
///
/// ── クリア後会話（StageSet.Stage.outro、任意） ──
/// クリア条件達成の瞬間にプレイヤーの操作スクリプトは即座に無効化する（FreezeGameplay、時間停止より前）。
/// outro が設定されているステージだけ、クリアアニメーションより前に会話を挟む。この会話の再生中は
/// （敵の動きなどを止めたくないという理由により）Time.timeScale を 1 のまま維持し、ゲーム内時間を
/// 止めない。会話が終わってから、初めて「クリアアニメーション再生 → Time.timeScale=0」という
/// 従来の流れに入る。outro が無いステージは今まで通り即座にその流れへ進む。
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
    [Tooltip("true にすると、killY を下回っても失敗パネルを出さず、softRetryPoint の位置へ戻す" +
             "（チュートリアルの落とし穴など、初見の落下を厳しくしたくない場合用。既定は今まで通り false＝通常の失敗）")]
    [SerializeField] private bool softRetryOnFall = false;
    [Tooltip("softRetryOnFall が true のときの復帰位置")]
    [SerializeField] private Transform softRetryPoint;

    [Header("入力ロック")]
    [Tooltip("ステージ開始時（および開始会話の直後）、この秒数だけ入力を無効化する（連打の勢いでの誤アクション防止）")]
    [SerializeField] private float inputLockDuration = 0.25f;

    private PlayerHealth _playerHealth;
    private AnimationEventRelay _playerAnimEvents;
    private MainActionController _playerMainAction;
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
            if (_playerHealth != null) _playerHealth.OnDied += HandlePlayerHpDied;
            _playerAnimEvents = p.GetComponent<AnimationEventRelay>();
            if (_playerAnimEvents != null) _playerAnimEvents.OnAnimationEvent += HandlePlayerAnimationEvent;
            _playerMainAction = p.GetComponent<MainActionController>();
        }
        if (nextButton != null) nextButton.gameObject.SetActive(GameFlow.HasNextStage);

        InputLock.LockFor(inputLockDuration); // ステージ画面に入った直後は入力を無効化

        TryPlayIntro();
    }

    private void OnDestroy()
    {
        if (_playerHealth != null) _playerHealth.OnDied -= HandlePlayerHpDied;
        if (_playerAnimEvents != null) _playerAnimEvents.OnAnimationEvent -= HandlePlayerAnimationEvent;
    }

    private void Update()
    {
        if (_ended || _introPlaying || _playerTf == null) return;
        if (_playerTf.position.y < killY)
        {
            if (softRetryOnFall && softRetryPoint != null) SoftRetry();
            else Fail();
        }
    }

    /// <summary>落とし穴などに落ちたとき、失敗にせず安全な位置へ戻す（softRetryOnFall 用）。</summary>
    private void SoftRetry()
    {
        var rb = _playerTf.GetComponent<Rigidbody2D>();
        _playerTf.position = softRetryPoint.position;
        if (rb != null)
        {
            rb.position = softRetryPoint.position;
            rb.linearVelocity = Vector2.zero;
        }
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

    /// <summary>ステージクリア。プレイヤーの操作は即座に封印するが、outro（クリア後会話）が
    /// 設定されていれば、それを再生し終えるまではゲーム内時間を止めない。会話が無い/終わった後は
    /// 従来通り、ゲーム内を全停止してプレイヤーのクリアアニメーションだけ再生させる。パネルは
    /// クリアアニメーション側の Animation Event（HandlePlayerAnimationEvent の "ShowClearPanel"）で
    /// 表示される。</summary>
    public void Clear()
    {
        if (_ended) return;
        _ended = true;
        GameAudio.StopBgm();
        GameAudio.PlaySfx(GameAudio.Sfx.Clear);
        GameFlow.MarkStageCleared(GameFlow.CurrentStageIndex); // 次のステージを解放
        FreezeGameplay(); // 操作の封印はここで即座に行う（outro再生中もゲーム内時間を止めないため）

        // 倒した瞬間に再生中だった攻撃などのアニメーションが一瞬で打ち切られて見えないよう、
        // 1秒だけ猶予してから強制的にIdleへ切り替える（PlayClearAnimationAndFreezeが先に呼ばれた
        // ら、そちらのクリア演出を優先してこの猶予は打ち切る）。
        _forceIdleCoroutine = StartCoroutine(ForceIdleAfterDelay(1f));

        var outro = GameFlow.Stages != null ? GameFlow.Stages.OutroAt(GameFlow.CurrentStageIndex) : null;
        var player = FindAnyObjectByType<DialoguePlayer>();
        if (player != null && player.HasContent(outro))
        {
            player.Play(outro, PlayClearAnimationAndFreeze);
        }
        else
        {
            PlayClearAnimationAndFreeze();
        }
    }

    private Coroutine _forceIdleCoroutine;

    /// <summary>指定秒数（実時間。Time.timeScale=0でも進む）待ってから、プレイヤーのAnimatorを
    /// 強制的にIdleへ切り替える（MainActionControllerが無効化されると"Moving"パラメータが更新
    /// されなくなり、移動アニメーションがループしたまま固まって見えてしまう問題への対策）。</summary>
    private IEnumerator ForceIdleAfterDelay(float delaySeconds)
    {
        yield return new WaitForSecondsRealtime(delaySeconds);
        _forceIdleCoroutine = null;

        var p = GameObject.FindGameObjectWithTag("Player");
        var animator = p != null ? p.GetComponent<Animator>() : null;
        if (animator != null)
        {
            animator.SetBool("Moving", false);
            animator.Play("Idle", 0, 0f);
        }
    }

    private void PlayClearAnimationAndFreeze()
    {
        // クリア演出（クリアアニメーション）を優先する。まだ強制Idle化が済んでいなければ打ち切る。
        if (_forceIdleCoroutine != null)
        {
            StopCoroutine(_forceIdleCoroutine);
            _forceIdleCoroutine = null;
        }
        if (_playerMainAction != null) _playerMainAction.PlayClearAnimation();
        Time.timeScale = 0f;
    }

    /// <summary>落下死（今まで通り、即座に失敗パネルを表示）。</summary>
    public void Fail()
    {
        if (_ended) return;
        _ended = true;
        GameAudio.PlaySfx(GameAudio.Sfx.Fall);
        FreezeGameplay();
        if (failPanel != null) failPanel.SetActive(true);
        Time.timeScale = 0f;
    }

    /// <summary>体力0での失敗（PlayerHealth.OnDied）。パネルはまだ出さず、ゲーム内を即座に全停止して
    /// プレイヤーの死亡アニメーションだけ再生させる。パネルは死亡アニメーション側の Animation Event
    /// （HandlePlayerAnimationEvent の "ShowFailPanel"）で表示される。</summary>
    private void HandlePlayerHpDied()
    {
        if (_ended) return;
        _ended = true;
        FreezeGameplay();
        Time.timeScale = 0f;
    }

    private void HandlePlayerAnimationEvent(string eventName)
    {
        switch (eventName)
        {
            case "ShowFailPanel":
                if (failPanel != null) failPanel.SetActive(true);
                break;
            case "ShowClearPanel":
                if (clearPanel != null) clearPanel.SetActive(true);
                break;
        }
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
