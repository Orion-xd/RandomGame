using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// メインアクション（ジャンプ / ダッシュ / 攻撃）の発動を司る。
/// スペース / エンター / テンキー Enter / 左クリックでキュー先頭のアクションを1つ消費して実行し、
/// そのアクションの効果が終わるまで次を受け付けない（会話送りやメニュー決定と操作系を統一するため複数キーを許可）。
///
/// ── クールタイム＝コンボ受付時間（2026-09-21、仕様変更）──
/// 「クールタイム」と「コンボ受付時間」は同じもの（＝そのアクションの効果が続いている間）になった。
/// つまり単発で終わらせてもコンボにしても、次に動けるようになるタイミングは一致する。
///  - ダッシュ / 攻撃：それぞれの AnimationClip（dashClip / attackClip）の長さぶん。
///    Clip の末尾に仕込んだ Animation Event（AnimationEventRelay 経由）が発火した瞬間に終了する。
///    効果時間を変えたい場合は AnimationClip の長さを変えればよい（インスペクターの数値ではない）。
///  - ジャンプ：今まで通り「着地するまで」。滞空時間に上限は設けない（着地することだけが終了条件）。
/// 内部的には「今どのアクションで busy か（_busy）」で一元管理する（Jump/Dash/Attack/None）。
///
/// ── コンボ（連続発動） ──
/// 1つ目のアクションが busy の間（＝上記の効果が続いている間）にもう一度発動すると「2つ目」として
/// 受け付ける（最大2連続）。2つ目を発動したら、その時点でコンボ受付は打ち切り（3つ目は無い）。
/// `StageSet.disableCombos` が true のステージ（ステージ1など）ではコンボの「受付」だけを無効化する。
/// busy の長さ自体（＝そのステージでの実質的なクールタイム）は他ステージと変わらない。
///
/// ── アニメーション ──
/// Idle / Move / Dash / Jump / Attack / Dead の6状態。Dash と Attack だけ、Clip 末尾の Animation Event
/// （AnimationEventRelay.RaiseEvent("DashEnd" / "AttackEnd")）で効果終了を通知する。Jump は着地で
/// 終了（Animation Event は使わない）。Move は水平入力の有無で Idle と自動的に行き来する。
/// Attack はさらに前隙・攻撃判定発生中・後隙の3区間に分かれており、"AttackHitboxOn"/"AttackHitboxOff"
/// という2つの追加 Animation Event で攻撃判定（AttackHitbox）の有効/無効だけを個別に切り替える
/// （2026-09-21）。"AttackEnd" は busy の終了だけを意味し、攻撃判定はそれより前に閉じている想定。
/// 2つのアクションが同時に効果を持つ場合（例：ジャンプ+攻撃）でも、見た目のアニメーションは
/// 後から発動した方が単純に上書きする（レイヤー分けなどは行わない。当面のプレースホルダー仕様）。
///
/// ── 先行入力（バッファ） ──
/// 効果終了の inputBufferTime 秒前（既定 0.1 秒＝約6フレーム）から、発動入力（スペース / エンター /
/// テンキー Enter / 左クリック）を「先行入力」として記憶する。入力を離していても、発動可能になった瞬間に次のアクションを発動する。
///  - ダッシュ / 攻撃：時間ベースなので「残り <= inputBufferTime」で受付。
///  - ジャンプ：時間ではなく着地で明けるため、PlayerController.TryPredictLandingTime（足元からの
///    簡易落下予測）で「着地まで <= inputBufferTime 秒」のときだけ受付。予測できなければ受け付けない。
/// 受付中かどうかは InInputBufferZone、CD ゲージ上での区間割合は InputBufferZoneFraction01 で公開。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(PlayerController))]
[RequireComponent(typeof(MainActionQueue))]
public class MainActionController : MonoBehaviour
{
    private enum BusyAction { None, Dash, Jump, Attack }

    [Header("ジャンプ")]
    [Tooltip("ジャンプ時に与える上向きの初速")]
    [SerializeField] private float jumpForce = 12f;

    [Header("ダッシュ")]
    [Tooltip("ダッシュの最高速度（発動時にこの速度になる）")]
    [SerializeField] private float dashSpeed = 18f;
    [Range(0f, 1f)]
    [Tooltip("継続時間のうち『前半（完全ロック区間）』が占める割合。前半は最高速度固定・入力無視・完全無敵")]
    [SerializeField] private float dashLockFraction = 0.5f;
    [Range(0f, 1f)]
    [Tooltip("後半のうち、最高速度から通常の移動速度まで滑らかに落とすのにかける時間の割合" +
             "（後半の長さに対する割合。既定0.5＝後半の半分＝効果時間全体の1/4ぶん、dashLockFraction=0.5のとき）。" +
             "入力方向（前方/後方/なし）によらず常に同じ挙動になる（2026-09-23、後方入力での即時解除を廃止し統一した）")]
    [SerializeField] private float dashDecelFraction = 0.5f;

    [Header("攻撃")]
    [Tooltip("前方に出す攻撃判定（子オブジェクト）。通常は非アクティブ")]
    [SerializeField] private AttackHitbox attackHitbox;
    [Tooltip("攻撃1ヒットのダメージ")]
    [SerializeField] private int attackDamage = 1;

    [Header("アニメーション")]
    [Tooltip("未設定なら自分自身（同じGameObject）から自動取得")]
    [SerializeField] private Animator animator;
    [Tooltip("Dash/Attack の Animation Event 受信用。未設定なら自分自身から自動取得")]
    [SerializeField] private AnimationEventRelay animEvents;
    [Tooltip("ダッシュの効果時間はこの Clip の長さそのもの。変えたい場合は Clip の長さを変える")]
    [SerializeField] private AnimationClip dashClip;
    [Tooltip("攻撃の効果時間（＝攻撃判定が出ている時間）はこの Clip の長さそのもの")]
    [SerializeField] private AnimationClip attackClip;

    [Header("先行入力")]
    [Tooltip("効果終了のこの秒数前から、スペースキー押下を『先行入力』として記憶する（約6フレーム=0.1秒）。" +
             "キーを離していても、発動可能になった瞬間に次のアクションが発動する。0 で無効")]
    [SerializeField] private float inputBufferTime = 0.1f;

    private Rigidbody2D _rb;
    private PlayerController _player;
    private MainActionQueue _queue;

    // 今どのアクションで busy か（＝クールタイム兼コンボ受付時間の進行中）。None なら発動可能。
    private BusyAction _busy;
    private float _busyStartTime;

    // コンボ（連続発動）状態
    private int _comboStep;                    // 0 = コンボ中でない / 1 = 1つ目発動済み・2つ目待ち
    private MainActionType _comboFirstAction;   // コンボの1つ目に何を使ったか
    private bool _combosEnabled = true;         // ステージ設定で無効化されると false（ステージ1など）

    // 先行入力（効果終了直前に押しておくと、明けた瞬間に発動）
    private bool _bufferedInput;
    private float _bufferedInputExpiry;         // これを過ぎたら記憶を破棄（保険）
    private float _bufferZoneFraction;          // デバッグゲージ用：CD ゲージ内で先行入力できる区間の割合
    private const float BufferedInputMaxLife = 0.4f;

    // ダッシュ状態（FixedUpdate で処理する）
    private float _dashTimeLeft;
    private float _dashInvTimeLeft;   // 無敵の残り時間。ダッシュ移動とは独立して減る（Dash→Jump コンボ後も継続）
    private int _dashDir;
    private float _savedGravityScale;
    private float _dashCurSpeed;   // 現在のダッシュ速度（大きさ）

    // ジャンプ：着地するまで busy。滞空時間に上限は無い。
    private bool _jumpLeftGround;      // 発動後、実際に地面を離れたか
    private const float JumpLiftoffGrace = 0.25f; // これだけ経っても接地したままなら「浮かなかった＝着地済み」とみなす

    private float DashDuration => dashClip != null ? dashClip.length : 0.8f;
    private float AttackDuration => attackClip != null ? attackClip.length : 0.5f;

    /// <summary>この間はダメージを受けない（ダッシュ中）。</summary>
    public bool IsInvincible { get; private set; }

    /// <summary>この間は PlayerController が移動速度・向きを上書きしない（ダッシュ中）。</summary>
    public bool OverridesMovement { get; private set; }

    /// <summary>ダッシュ中か（Enemy が接触をすり抜けさせるかどうかの判定に使う）。</summary>
    public bool IsDashing { get; private set; }

    /// <summary>ジャンプ由来の busy 中か（着地するまで明けない＝経過割合を安定して計算できない）。
    /// アクションバーUIが、ジャンプの次アクション表示を通常のグラデーションではなく「着地まで一律で暗い」扱いにするために使う。</summary>
    public bool IsJumpCooldownActive => _busy == BusyAction.Jump;

    /// <summary>次のアクションを発動できるか（busy でない）。</summary>
    public bool IsReady => _busy == BusyAction.None;

    /// <summary>いま先行入力の受付区間内か。</summary>
    public bool InInputBufferZone { get; private set; }

    /// <summary>デバッグ表示用：CD ゲージ内で先行入力できる区間の割合（ゲージの空側の端から測った 0..1）。</summary>
    public float InputBufferZoneFraction01 => _bufferZoneFraction;

    /// <summary>デバッグ表示用：コンボ受付ゲージ（1 = 発動直後, 0 = 受付終了）。busy = クールタイムと同じ値を使う。</summary>
    public float ComboGraceFraction01 => _comboStep == 1 ? CooldownFraction01 : 0f;

    /// <summary>デバッグ表示用：クールタイム残りゲージ（1 = 発動直後, 0 = 明けた）。
    /// ジャンプは「着地するまで」で時間が不定なので、busy の間は常に 1 のまま（着地した瞬間に 0）。</summary>
    public float CooldownFraction01
    {
        get
        {
            switch (_busy)
            {
                case BusyAction.Jump: return 1f;
                case BusyAction.Dash: return DashDuration <= 0f ? 0f : Mathf.Clamp01(1f - (Time.time - _busyStartTime) / DashDuration);
                case BusyAction.Attack: return AttackDuration <= 0f ? 0f : Mathf.Clamp01(1f - (Time.time - _busyStartTime) / AttackDuration);
                default: return 0f;
            }
        }
    }

    /// <summary>デバッグ表示用：コンボ受付時間の残り秒数（受付中でなければ0）。</summary>
    public float ComboGraceRemainingSeconds => _comboStep == 1 ? CooldownRemainingSeconds : 0f;

    /// <summary>デバッグ表示用：クールタイムの残り秒数。
    /// ジャンプは「着地するまで」で残り時間そのものが不明なため、負の値（-1）を番兵として返す
    /// （呼び出し側は正の値だけを「残り秒数」として表示すること）。</summary>
    public float CooldownRemainingSeconds
    {
        get
        {
            switch (_busy)
            {
                case BusyAction.Jump: return -1f;
                case BusyAction.Dash: return Mathf.Max(0f, DashDuration - (Time.time - _busyStartTime));
                case BusyAction.Attack: return Mathf.Max(0f, AttackDuration - (Time.time - _busyStartTime));
                default: return 0f;
            }
        }
    }

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _player = GetComponent<PlayerController>();
        _queue = GetComponent<MainActionQueue>();
        if (attackHitbox != null) attackHitbox.gameObject.SetActive(false);

        if (animator == null) animator = GetComponent<Animator>();
        if (animEvents == null) animEvents = GetComponent<AnimationEventRelay>();
        if (animEvents != null) animEvents.OnAnimationEvent += HandleAnimationEvent;

        var health = GetComponent<PlayerHealth>();
        if (health != null) health.OnDied += HandlePlayerDied;

        // ステージ設定でコンボを無効化する（ステージ1など）。
        _combosEnabled = !(GameFlow.Stages != null
            && GameFlow.Stages.DisableCombosAt(GameFlow.ActiveStageIndex));
    }

    private void Update()
    {
        // 見た目のアニメーション（Idle⇔Move）と、Animation Event が万一発火しなかった場合の保険は
        // 会話中/InputLock 中でも常に評価する。
        if (animator != null && _player != null)
            animator.SetBool("Moving", Mathf.Abs(_player.MoveInput) > 0.01f);
        WatchdogEffectEnd();

        // 会話中・画面切り替え直後（InputLock）はメインアクションの入力を受け付けない。
        if (DialoguePlayer.IsPlaying || !InputLock.InputAllowed) return;

        // 先行入力の受付区間（InInputBufferZone / _bufferZoneFraction）を毎フレーム更新。
        UpdateInputBufferState();

        // 発動入力：スペース / エンター / テンキー Enter / 左クリック（会話送りやメニュー決定と統一）。
        // まず即時発動を試み、ダメなら受付区間内のとき「先行入力」として記憶する。
        var kb = Keyboard.current;
        bool byKey = kb != null && (kb.spaceKey.wasPressedThisFrame
            || kb.enterKey.wasPressedThisFrame
            || kb.numpadEnterKey.wasPressedThisFrame);

        var mouse = Mouse.current;
        bool byClick = mouse != null && mouse.leftButton.wasPressedThisFrame;

        if (byKey || byClick)
        {
            if (TryTrigger())
            {
                _bufferedInput = false; // 実際に出せたので、残っていた先行入力は破棄
            }
            else if (InInputBufferZone)
            {
                _bufferedInput = true;
                _bufferedInputExpiry = Time.time + BufferedInputMaxLife;
            }
        }

        // 記憶した先行入力の消化：発動可能になった瞬間に実行する（キーを離していてもよい）。
        // ただし IsReady になった「その瞬間」ではなく、Animator が実際に Idle/Move へ戻ったことを
        // 確認してから発動する（AnimatorSettled()、後述）。
        if (_bufferedInput)
        {
            if (Time.time > _bufferedInputExpiry) _bufferedInput = false;
            else if (IsReady && AnimatorSettled()) { _bufferedInput = false; TryTrigger(); }
        }
    }

    /// <summary>安全策：Animation Event が何らかの理由（Clip/Animator の設定漏れなど）で発火しなかった場合に、
    /// 永久に発動不可のまま固まらないようにする保険。通常はここに来る前に Animation Event 側で片付く。</summary>
    private void WatchdogEffectEnd()
    {
        const float grace = 0.5f;
        if (_busy == BusyAction.Dash && Time.time - _busyStartTime > DashDuration + grace) EndBusy(BusyAction.Dash);
        if (_busy == BusyAction.Attack && Time.time - _busyStartTime > AttackDuration + grace) EndBusy(BusyAction.Attack);
    }

    /// <summary>先行入力の受付区間（InInputBufferZone）と、その CD ゲージ上での割合を更新する。</summary>
    private void UpdateInputBufferState()
    {
        InInputBufferZone = false;
        _bufferZoneFraction = 0f;
        if (inputBufferTime <= 0f) return;

        if (_busy == BusyAction.Jump)
        {
            // ジャンプ：時間ではなく「着地」で明けるので、着地予測が使えるときだけ受付。
            // 残り時間そのものが不明なため、ゲージ上の区間表示（_bufferZoneFraction）は出さない。
            if (_player != null && _player.TryPredictLandingTime(out float tLand))
                InInputBufferZone = tLand <= inputBufferTime;
            return;
        }

        float total = _busy == BusyAction.Dash ? DashDuration : _busy == BusyAction.Attack ? AttackDuration : 0f;
        if (total <= 0f) return;

        float remaining = Mathf.Max(0f, total - (Time.time - _busyStartTime));
        _bufferZoneFraction = Mathf.Clamp01(inputBufferTime / total);
        InInputBufferZone = remaining <= inputBufferTime;
    }

    /// <summary>先行入力を発動してよいか＝Animatorが実際にIdle/Moveへ戻っていることを確認する
    /// （2026-09-23追加）。
    ///
    /// 背景: `_busy`はAnimation Event（"DashEnd"/"AttackEnd"等）で即座にNoneへ戻るが、
    /// **Animator自身の状態遷移（例: Dash→Idle）もちょうど同じタイミングで処理される**。
    /// コンボが無効なステージ（Stage1など）では、先行入力による次のアクションはこの
    /// `IsReady`が立った「その瞬間」に`TryTrigger()`→ `animator.SetTrigger("Dash")`等を呼んでいたが、
    /// これがAnimator側の「古い状態から抜ける」処理とちょうど同時に重なると、
    /// `AnyState`遷移そのものは発火する（Animatorウィンドウで矢印が点灯する）ものの、
    /// まだ古いDash等の見た目が再生中のタイミングと被って消費されるだけで、実際の見た目には
    /// 反映されない（＝ゲームプレイ上は次のダッシュが始まっているのに、見た目はIdle/Moveのまま）
    /// という不具合が実際に発生した。
    ///
    /// 対策として、先行入力の発動だけは「Animatorが実際にIdle/Moveへ戻ったこと」を確認してから
    /// 行う（＝古い状態の遷移処理が完全に片付くのを待つ）。即時発動（busyでないときにボタンを押した
    /// 通常のケース）や、コンボ継続（`comboContinuation`、`TryTrigger()`内で別途判定）はこの確認の
    /// 対象外（従来通り即座に割り込む。コンボは「後から発動した方が上書きする」仕様のため）。
    /// ジャンプ・攻撃についても同じ経路（先行入力）を通るため、同様にこの確認の恩恵を受ける。</summary>
    private bool AnimatorSettled()
    {
        if (animator == null) return true;
        if (animator.IsInTransition(0)) return false;
        var info = animator.GetCurrentAnimatorStateInfo(0);
        return info.IsName("Idle") || info.IsName("Move");
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        // ── 無敵タイマー（ダッシュの移動処理とは独立して減る） ──
        // Dash→Jump コンボでダッシュ「移動」を中断しても、無敵はここで通常のダッシュ効果時間ぶん残る。
        if (_dashInvTimeLeft > 0f)
        {
            _dashInvTimeLeft -= dt;
            if (_dashInvTimeLeft < 0f) _dashInvTimeLeft = 0f;
        }

        // ── ジャンプの busy（着地するまで） ──
        UpdateJumpBusy();

        // ── ダッシュの移動処理 ──
        // FixedUpdate は物理積分の「前」に走るので、ここで設定した速度がそのまま反映される。
        if (_dashTimeLeft > 0f)
        {
            float duration = DashDuration;
            float elapsed = duration - _dashTimeLeft;
            bool lockedPhase = elapsed < duration * dashLockFraction;

            if (lockedPhase)
            {
                // ── 前半：最高速度を維持。移動入力は完全に無視。──
                _dashCurSpeed = dashSpeed;
            }
            else
            {
                // ── 後半：入力方向（前方/後方/なし）によらず常に同じ挙動（2026-09-23、後方入力での
                //    即時解除を廃止して統一。原因不明のまま`DashBreak`が意図せずオンになり続け、
                //    アニメーションだけIdle/Moveに戻ってしまう不具合の根本対策）。
                //    後半の残り時間のうち dashDecelFraction ぶんをかけて、最高速度(dashSpeed)から
                //    通常の移動速度(PlayerController.MoveSpeed)まで滑らかに落とす。
                float backHalfDuration = duration * (1f - dashLockFraction);
                float decelDuration = backHalfDuration * dashDecelFraction;
                float backHalfElapsed = elapsed - duration * dashLockFraction;
                float t = decelDuration > 0f ? Mathf.Clamp01(backHalfElapsed / decelDuration) : 1f;
                float normalSpeed = _player != null ? _player.MoveSpeed : dashSpeed;
                _dashCurSpeed = Mathf.Lerp(dashSpeed, normalSpeed, t);
            }

            // 落下しないよう Y は 0 に固定。進行方向は発動時の向きのまま。
            _rb.linearVelocity = new Vector2(_dashDir * _dashCurSpeed, 0f);

            _dashTimeLeft -= dt;
            if (_dashTimeLeft <= 0f) EndDash();
        }

        IsInvincible = _dashInvTimeLeft > 0f;
    }

    /// <summary>ジャンプの busy 進行。時間ではなく「着地」で明ける（滞空時間に上限は無い）。</summary>
    private void UpdateJumpBusy()
    {
        if (_busy != BusyAction.Jump) return;
        if (_player == null) return;

        if (!_jumpLeftGround)
        {
            if (!_player.IsGrounded)
            {
                _jumpLeftGround = true;
                return;
            }
            if (Time.time - _busyStartTime >= JumpLiftoffGrace)
                EndBusy(BusyAction.Jump); // 実際には浮かなかった＝着地済みとみなす
            return;
        }

        if (_player.IsGrounded) EndBusy(BusyAction.Jump);
    }

    /// <summary>busy でない、またはコンボの2つ目として有効な間なら、キュー先頭を消費してアクションを実行する。
    /// 実際に発動できたら true、できなかったら false を返す（先行入力の記憶判定に使う）。</summary>
    public bool TryTrigger()
    {
        // busy の間（＝1つ目発動済み）なら、2つ目としての発動を許可する。
        // ただしステージでコンボが無効化されている場合は、常に「busy が明けるのを待つ」のみ。
        bool comboContinuation = _combosEnabled && _comboStep == 1 && _busy != BusyAction.None;

        if (!IsReady && !comboContinuation) return false;

        // 空中ではジャンプ「そのものが発動できない」（消費もしない）。
        //  - 接地中に加え、コヨーテタイム中（地面を離れて coyoteJumpGrace 秒以内。PlayerController.InCoyoteTime）もジャンプ可。
        //  - さらに例外として、直前（コンボの1つ目）がダッシュだった場合は完全に空中でもジャンプ可
        //    （ダッシュで飛び出した先からジャンプできるようにするための特例）。
        MainActionType? next = _queue.Peek(0);
        bool jumpGroundBypass = comboContinuation && next == MainActionType.Jump && _comboFirstAction == MainActionType.Dash;
        bool jumpGrounded = _player != null && (_player.IsGrounded || _player.InCoyoteTime);
        if (next == MainActionType.Jump && !jumpGroundBypass && !jumpGrounded) return false;

        MainActionType action = _queue.Consume();
        Execute(action);

        if (!_combosEnabled || comboContinuation)
        {
            // コンボ無効ステージ、または 2つ目まで使ったので打ち止め（最大2連続）。
            _comboStep = 0;
        }
        else
        {
            _comboStep = 1;
            _comboFirstAction = action;
        }

        return true;
    }

    /// <summary>単発アクションを実行する。</summary>
    public void Execute(MainActionType action)
    {
        // 前のアクションがまだ効果中に別のアクションへコンボした場合、開いたままのリソースを強制的に閉じる
        // （攻撃判定は Animation Event 待ちで閉じるはずが、コンボで Animator が上書きされると発火しなくなるため）。
        if (_busy == BusyAction.Attack && attackHitbox != null) attackHitbox.gameObject.SetActive(false);

        switch (action)
        {
            case MainActionType.Jump:
                DoJump();
                break;
            case MainActionType.Dash:
                StartDash();
                break;
            case MainActionType.Attack:
                DoAttack();
                break;
        }
    }

    private void DoJump()
    {
        // 接地チェックは TryTrigger 側で行っている（ダッシュ→ジャンプのコンボのときだけ空中でもここに来る）。
        //
        // ダッシュがまだ継続中のことがある。そのまま放置するとダッシュの FixedUpdate が毎フレーム
        // y 速度を 0 に戻してジャンプが不発になるため、ダッシュの「移動」だけ中断する。
        // 無敵（_dashInvTimeLeft）はこのあとも通常のダッシュ効果時間ぶん継続する。
        if (_dashTimeLeft > 0f) InterruptDashMovement();

        Vector2 v = _rb.linearVelocity;
        v.y = jumpForce;
        _rb.linearVelocity = v;

        _busy = BusyAction.Jump;
        _busyStartTime = Time.time;
        _jumpLeftGround = false;
        if (animator != null)
        {
            animator.SetBool("Landed", false);
            animator.SetTrigger("Jump");
        }
    }

    private void StartDash()
    {
        _dashDir = _player.FacingSign;   // 向いている方向へ前進
        _dashTimeLeft = DashDuration;
        _dashInvTimeLeft = DashDuration; // 無敵タイマー（移動を中断しても残りを走らせる）
        _dashCurSpeed = dashSpeed;       // 発動時に最高速度

        // ダッシュ中は重力を完全に切る（＝落下ゼロ）。終了時に戻す。
        _savedGravityScale = _rb.gravityScale;
        _rb.gravityScale = 0f;

        IsInvincible = true;
        OverridesMovement = true;
        IsDashing = true;

        _rb.linearVelocity = new Vector2(_dashDir * dashSpeed, 0f);

        _busy = BusyAction.Dash;
        _busyStartTime = Time.time;
        if (animator != null) animator.SetTrigger("Dash");
    }

    /// <summary>ダッシュの物理的な移動を完全に終了する（効果時間切れ / 無効化時）。無敵も解除する。
    /// busy/コンボの終了は Animation Event（"DashEnd"）が別途担当するので、ここでは触らない
    /// （保険として WatchdogEffectEnd はある）。アニメーション側の Dash→Idle は Animator 自身の
    /// Exit Time で完結するので、C# 側から明示的にトリガーを送る必要は無い。</summary>
    private void EndDash()
    {
        _dashTimeLeft = 0f;
        _dashInvTimeLeft = 0f;
        _rb.gravityScale = _savedGravityScale;
        IsInvincible = false;
        OverridesMovement = false;
        IsDashing = false;
    }

    /// <summary>Dash→Jump コンボ用。ダッシュの「移動と重力オフ」だけ止め、無敵タイマーはそのまま継続させる。</summary>
    private void InterruptDashMovement()
    {
        _dashTimeLeft = 0f;
        _rb.gravityScale = _savedGravityScale;
        OverridesMovement = false;
        IsDashing = false;
        // _dashInvTimeLeft はそのまま → 無敵は通常のダッシュ効果時間ぶん継続
    }

    /// <summary>攻撃判定そのものは有効化しない（前隙があるため）。Animation Event の
    /// "AttackHitboxOn" が発火した瞬間に初めて有効化される（HandleAnimationEvent 参照）。</summary>
    private void DoAttack()
    {
        _busy = BusyAction.Attack;
        _busyStartTime = Time.time;
        if (animator != null) animator.SetTrigger("Attack");
    }

    /// <summary>Dash/Attack の Animation Event から呼ばれる。
    /// Attack は前隙・攻撃判定発生中・後隙の3区間に分かれており、"AttackHitboxOn"/"AttackHitboxOff" で
    /// 攻撃判定の有効/無効を、"AttackEnd"（Clip 末尾）で busy（クールタイム兼コンボ受付）の終了を通知する。</summary>
    private void HandleAnimationEvent(string eventName)
    {
        switch (eventName)
        {
            case "DashEnd": EndBusy(BusyAction.Dash); break;
            case "AttackHitboxOn":
                if (_busy != BusyAction.Attack || attackHitbox == null) return;
                attackHitbox.Configure(_player.FacingSign, attackDamage);
                attackHitbox.gameObject.SetActive(true);
                break;
            case "AttackHitboxOff":
                if (attackHitbox != null) attackHitbox.gameObject.SetActive(false);
                break;
            case "AttackEnd": EndBusy(BusyAction.Attack); break;
        }
    }

    /// <summary>busy（＝クールタイム兼コンボ受付時間）を終了する。呼び出し元は必ず対象を指定し、
    /// 現在の busy と一致しないとき（既にコンボで上書きされた後など）は何もしない。</summary>
    private void EndBusy(BusyAction reason)
    {
        if (_busy != reason) return;
        _busy = BusyAction.None;
        if (_comboStep == 1) _comboStep = 0;

        switch (reason)
        {
            case BusyAction.Attack:
                if (attackHitbox != null) attackHitbox.gameObject.SetActive(false);
                break;
            case BusyAction.Jump:
                // 2026-09-23: Trigger→Boolに変更。AnyState→Jumpの遷移がまだブレンド中（＝Animatorが
                // 本当にはまだJump状態に到達していない）タイミングでここに来ると、旧Trigger実装では
                // 消費先(Jump→Idle)が存在しないため一度も消費されずに残り続け、「Landedが常時trueに
                // 固まる」不具合になっていた（Dashの時と同種、ただし入口側のレース）。Boolならその瞬間に
                // trueへ変わって"居座る"だけなので、AnimatorがJumpへ到達した瞬間に確実に拾われる。
                if (animator != null) animator.SetBool("Landed", true);
                break;
        }
    }

    private void HandlePlayerDied()
    {
        if (animator != null) animator.SetTrigger("Dead");
    }

    /// <summary>ステージクリア時、StageManager から呼ばれる。クリアアニメーションを再生する
    /// （ループせず1周だけ。末尾の Animation Event "ShowClearPanel" で StageManager がパネルを表示する）。</summary>
    public void PlayClearAnimation()
    {
        if (animator != null) animator.SetTrigger("Clear");
    }

    private void OnDisable()
    {
        // 無効化時に重力が切れたまま／無敵のままにならないように
        if (_dashTimeLeft > 0f || _dashInvTimeLeft > 0f) EndDash();
        _bufferedInput = false;
        InInputBufferZone = false;
        _bufferZoneFraction = 0f;
        if (animEvents != null) animEvents.OnAnimationEvent -= HandleAnimationEvent;
    }
}
