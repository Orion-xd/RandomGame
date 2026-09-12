using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// メインアクション（ジャンプ / ダッシュ / 攻撃）の発動を司る。
/// スペース / エンター / テンキー Enter / 左クリックでキュー先頭のアクションを1つ消費して実行し、
/// クールタイム経過まで次を受け付けない（会話送りやメニュー決定と操作系を統一するため複数キーを許可）。
///
/// ── クールタイム ──
///  - ダッシュ / 攻撃：技ごとの固定秒数（dashCooldown / attackCooldown）。
///  - ジャンプ：時間ではなく「着地するまで」。着地すれば滞空時間に関係なくクールタイム終了。
///    ただし異常に長く滞空した場合の保険として、地面を離れてから jumpAirCooldownCap 秒で強制解除。
///
/// ── コンボ（連続発動） ──
/// 1つ目の発動から comboGraceTime 秒（組み合わせによらず一定）以内にもう一度発動すると
/// 「2つ目」として受け付ける（最大2連続）。この猶予はクールタイムとは独立したパラメータ。
///  - コンボにジャンプを含まない場合：2つ目のアクションのクールタイムだけ見ればよい
///    （1つ目のクールタイムは必ず先に明けるため）。
///  - コンボにジャンプを含む場合：着地した瞬間にクールタイム終了（上限 jumpAirCooldownCap 秒）。
///    もう片方（ダッシュ / 攻撃）のクールタイムは考慮しない。
/// 2つのアクションの効果は単純に「両方その場で発動」するだけ（ジャンプ＋攻撃＝ジャンプしながら
/// 攻撃判定、ジャンプ＋ダッシュ＝上昇中にダッシュへ移行、など）。唯一の特例はダッシュ→ジャンプで、
/// 空中でもジャンプの発動を許可し（接地チェック免除）、ダッシュ移動を中断してから跳ぶ。
/// このとき無敵はダッシュの通常効果時間ぶん継続する（下記 _dashInvTimeLeft）。
///
/// `StageSet.disableCombos` が true のステージ（ステージ1など）ではコンボを完全に無効化する。
/// 1回発動したら comboGraceTime は無意味で、そのアクションのクールタイムが明けるまで次は出せない。
///
/// ── 先行入力（バッファ） ──
/// クールタイム終了の inputBufferTime 秒前（既定 0.1 秒＝約6フレーム）から、発動入力（スペース / エンター /
/// テンキー Enter / 左クリック）を「先行入力」として記憶する。入力を離していても、クールタイムが明けた瞬間に次のアクションを発動する。
/// これにより「クールタイム明けにすぐ次を出す」操作がやりやすくなる。
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
    [Header("ジャンプ")]
    [Tooltip("ジャンプ時に与える上向きの初速")]
    [SerializeField] private float jumpForce = 12f;
    [Tooltip("ジャンプのクールタイムは『着地するまで』。滞空が異常に長い場合に備えた上限（秒）。" +
             "地面を離れてからこの秒数で強制的にクールタイムを解除する")]
    [SerializeField] private float jumpAirCooldownCap = 3f;

    [Header("ダッシュ")]
    [Tooltip("ダッシュの最高速度（発動時にこの速度になる）")]
    [SerializeField] private float dashSpeed = 18f;
    [Tooltip("ダッシュ継続時間。この間は落下しない")]
    [SerializeField] private float dashDuration = 0.5f;
    [Range(0f, 1f)]
    [Tooltip("継続時間のうち『前半（完全ロック区間）』が占める割合。前半は最高速度固定・入力無視・完全無敵")]
    [SerializeField] private float dashLockFraction = 0.5f;
    [Tooltip("後半に前方入力があるときの減速の強さ (units/秒^2)。ゆるめ")]
    [SerializeField] private float dashForwardDecel = 40f;
    [Tooltip("後半に後方入力があるときの減速の強さ (units/秒^2)。急ブレーキ")]
    [SerializeField] private float dashBrakeDecel = 160f;

    [Header("攻撃")]
    [Tooltip("前方に出す攻撃判定（子オブジェクト）。通常は非アクティブ")]
    [SerializeField] private AttackHitbox attackHitbox;
    [Tooltip("攻撃判定が出ている時間")]
    [SerializeField] private float attackDuration = 0.4f;
    [Tooltip("攻撃1ヒットのダメージ")]
    [SerializeField] private int attackDamage = 1;

    [Header("クールタイム / コンボ")]
    [Tooltip("ダッシュのクールタイム（秒）")]
    [SerializeField] private float dashCooldown = 2f;
    [Tooltip("攻撃のクールタイム（秒）")]
    [SerializeField] private float attackCooldown = 2f;
    [Tooltip("1つ目のアクション発動後、次をコンボとして受け付ける猶予（秒）。組み合わせによらず一定。クールタイムとは別物")]
    [SerializeField] private float comboGraceTime = 0.8f;
    [Tooltip("クールタイム終了のこの秒数前から、スペースキー押下を『先行入力』として記憶する（約6フレーム=0.1秒）。" +
             "キーを離していても、クールタイムが明けた瞬間に次のアクションが発動する。0 で無効")]
    [SerializeField] private float inputBufferTime = 0.1f;

    private Rigidbody2D _rb;
    private PlayerController _player;
    private MainActionQueue _queue;
    private float _nextReadyTime;
    private float _lastCooldownDuration;   // デバッグゲージ用：直近に設定した時間ベースのクールタイム長

    // コンボ（連続発動）状態
    private int _comboStep;                    // 0 = コンボ中でない / 1 = 1つ目発動済み・2つ目待ち
    private MainActionType _comboFirstAction;   // コンボの1つ目に何を使ったか
    private float _comboDeadline;               // この時刻までに2つ目を出せばコンボ扱い
    private bool _combosEnabled = true;         // ステージ設定で無効化されると false（ステージ1など）

    // 先行入力（クールタイム終了直前に押しておくと、明けた瞬間に発動）
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
    private bool _dashInvBroken;   // 後半に後方入力で無敵を解除したか（ラッチ：一度解除したらこのダッシュ中は戻らない）

    // ジャンプのクールタイム（時間ではなく「着地」で明ける）
    private bool _jumpCdActive;
    private bool _jumpCdLeftGround;      // 発動後、実際に地面を離れたか
    private float _jumpCdActivateTime;
    private float _jumpCdLeftGroundTime;
    private const float JumpLiftoffGrace = 0.25f; // これだけ経っても接地したままなら「浮かなかった＝着地済み」とみなす

    /// <summary>この間はダメージを受けない（ダッシュ中）。</summary>
    public bool IsInvincible { get; private set; }

    /// <summary>この間は PlayerController が移動速度・向きを上書きしない（ダッシュ中）。</summary>
    public bool OverridesMovement { get; private set; }

    /// <summary>ダッシュ中か（Enemy が接触をすり抜けさせるかどうかの判定に使う）。</summary>
    public bool IsDashing { get; private set; }

    /// <summary>次のアクションを発動できるか（時間ベースのクールタイム外、かつジャンプのクールタイム中でない）。</summary>
    public bool IsReady => Time.time >= _nextReadyTime && !_jumpCdActive;

    /// <summary>いま先行入力の受付区間内か（CD 終了 inputBufferTime 秒前〜／ジャンプは着地 inputBufferTime 秒前〜）。</summary>
    public bool InInputBufferZone { get; private set; }

    /// <summary>デバッグ表示用：CD ゲージ内で先行入力できる区間の割合（ゲージの空側の端から測った 0..1）。</summary>
    public float InputBufferZoneFraction01 => _bufferZoneFraction;

    /// <summary>デバッグ表示用：コンボ受付ゲージ（1 = 発動直後, 0 = 猶予切れ）。</summary>
    public float ComboGraceFraction01
    {
        get
        {
            if (_comboStep != 1 || comboGraceTime <= 0f) return 0f;
            return Mathf.Clamp01((_comboDeadline - Time.time) / comboGraceTime);
        }
    }

    /// <summary>デバッグ表示用：クールタイム残りゲージ（1 = 発動直後, 0 = 明けた）。
    /// ジャンプ由来のクールタイムは「着地するまで」で時間が不定なので、上限（jumpAirCooldownCap）を基準に減らす。</summary>
    public float CooldownFraction01
    {
        get
        {
            if (_jumpCdActive)
            {
                if (!_jumpCdLeftGround || jumpAirCooldownCap <= 0f) return 1f;
                return Mathf.Clamp01(1f - (Time.time - _jumpCdLeftGroundTime) / jumpAirCooldownCap);
            }
            if (_lastCooldownDuration <= 0f) return 0f;
            return Mathf.Clamp01((_nextReadyTime - Time.time) / _lastCooldownDuration);
        }
    }

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _player = GetComponent<PlayerController>();
        _queue = GetComponent<MainActionQueue>();
        if (attackHitbox != null) attackHitbox.gameObject.SetActive(false);

        // ステージ設定でコンボを無効化する（ステージ1など）。
        _combosEnabled = !(GameFlow.Stages != null
            && GameFlow.Stages.DisableCombosAt(GameFlow.ActiveStageIndex));
    }

    private void Update()
    {
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
        if (_bufferedInput)
        {
            if (Time.time > _bufferedInputExpiry) _bufferedInput = false;
            else if (IsReady) { _bufferedInput = false; TryTrigger(); }
        }
    }

    /// <summary>先行入力の受付区間（InInputBufferZone）と、その CD ゲージ上での割合を更新する。</summary>
    private void UpdateInputBufferState()
    {
        InInputBufferZone = false;
        _bufferZoneFraction = 0f;
        if (inputBufferTime <= 0f) return;

        if (_jumpCdActive)
        {
            // ジャンプ：時間ではなく「着地」で明けるので、着地予測が使えるときだけ受付。
            // ゲージ上の区間は上限（jumpAirCooldownCap）基準の目安表示にとどめる。
            if (jumpAirCooldownCap > 0f)
                _bufferZoneFraction = Mathf.Clamp01(inputBufferTime / jumpAirCooldownCap);
            if (_player != null && _player.TryPredictLandingTime(out float tLand))
                InInputBufferZone = tLand <= inputBufferTime;
            return;
        }

        // ダッシュ / 攻撃：時間ベースの CD 終了 inputBufferTime 秒前から受付。
        if (_lastCooldownDuration > 0f && Time.time < _nextReadyTime)
        {
            _bufferZoneFraction = Mathf.Clamp01(inputBufferTime / _lastCooldownDuration);
            InInputBufferZone = (_nextReadyTime - Time.time) <= inputBufferTime;
        }
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

        // ── ジャンプのクールタイム（着地するまで／滞空上限まで） ──
        UpdateJumpCooldown();

        // ── ダッシュの移動処理 ──
        // FixedUpdate は物理積分の「前」に走るので、ここで設定した速度がそのまま反映される。
        if (_dashTimeLeft > 0f)
        {
            float elapsed = dashDuration - _dashTimeLeft;
            bool lockedPhase = elapsed < dashDuration * dashLockFraction;

            if (lockedPhase)
            {
                // ── 前半：最高速度を維持。移動入力は完全に無視。──
                _dashCurSpeed = dashSpeed;
            }
            else
            {
                // ── 後半：速度はキープ。ただし入力があれば反映する。──
                float mv = _player != null ? _player.MoveInput : 0f;
                int inputDir = Mathf.Abs(mv) > 0.01f ? (mv > 0f ? 1 : -1) : 0;

                if (inputDir == _dashDir)
                {
                    // 前方入力：そのまま進みつつ徐々に減速（ゆるめ）
                    _dashCurSpeed = Mathf.MoveTowards(_dashCurSpeed, 0f, dashForwardDecel * dt);
                }
                else if (inputDir == -_dashDir)
                {
                    // 後方入力：急ブレーキ ＋ 無敵解除（ラッチ）。
                    // ※ 無敵が切れるのはこのケースだけ。コンボでジャンプに移行しても切れない。
                    _dashCurSpeed = Mathf.MoveTowards(_dashCurSpeed, 0f, dashBrakeDecel * dt);
                    _dashInvBroken = true;
                }
                // 入力なし：_dashCurSpeed 据え置き（速度キープ）
            }

            // 落下しないよう Y は 0 に固定。進行方向は発動時の向きのまま。
            _rb.linearVelocity = new Vector2(_dashDir * _dashCurSpeed, 0f);

            _dashTimeLeft -= dt;
            if (_dashTimeLeft <= 0f) EndDash();
        }

        // 無敵状態を確定（この FixedUpdate 内での _dashInvBroken 更新も反映する）。
        IsInvincible = _dashInvTimeLeft > 0f && !_dashInvBroken;
    }

    /// <summary>ジャンプのクールタイム進行。時間ではなく「着地」で明ける（滞空しすぎたら上限で強制解除）。</summary>
    private void UpdateJumpCooldown()
    {
        if (!_jumpCdActive) return;

        if (!_jumpCdLeftGround)
        {
            if (_player != null && !_player.IsGrounded)
            {
                _jumpCdLeftGround = true;
                _jumpCdLeftGroundTime = Time.time;
            }
            else if (Time.time - _jumpCdActivateTime >= JumpLiftoffGrace)
            {
                _jumpCdActive = false; // 実際には浮かなかった＝着地済みとみなす
            }
            return;
        }

        if (_player != null && _player.IsGrounded)
            _jumpCdActive = false;                                        // 着地でクールタイム終了
        else if (Time.time - _jumpCdLeftGroundTime >= jumpAirCooldownCap)
            _jumpCdActive = false;                                        // 滞空しすぎ → 上限で強制解除
    }

    /// <summary>クールタイム外、またはコンボの2つ目として有効な間なら、キュー先頭を消費してアクションを実行する。
    /// 実際に発動できたら true、できなかったら false を返す（先行入力の記憶判定に使う）。</summary>
    public bool TryTrigger()
    {
        // コンボ猶予を過ぎていたらコンボ状態をリセット（念のため）。
        if (_comboStep == 1 && Time.time > _comboDeadline) _comboStep = 0;

        // 1つ目の発動から comboGraceTime 秒以内なら、2つ目としての発動を許可する（クールタイムとは独立）。
        // ただしステージでコンボが無効化されている場合は、常に「クールタイム待ち」のみ。
        bool comboContinuation = _combosEnabled && _comboStep == 1 && Time.time <= _comboDeadline;

        if (!IsReady && !comboContinuation) return false;

        // 空中ではジャンプ「そのものが発動できない」（消費もクールタイムも発生しない）。
        //  - 接地中に加え、コヨーテタイム中（地面を離れて coyoteJumpGrace 秒以内。PlayerController.InCoyoteTime）もジャンプ可。
        //  - さらに例外として、直前（コンボの1つ目）がダッシュだった場合は完全に空中でもジャンプ可
        //    （ダッシュで飛び出した先からジャンプできるようにするための特例）。
        MainActionType? next = _queue.Peek(0);
        bool jumpGroundBypass = comboContinuation && next == MainActionType.Jump && _comboFirstAction == MainActionType.Dash;
        bool jumpGrounded = _player != null && (_player.IsGrounded || _player.InCoyoteTime);
        if (next == MainActionType.Jump && !jumpGroundBypass && !jumpGrounded) return false;

        MainActionType action = _queue.Consume();
        Execute(action);

        bool jumpInvolved = action == MainActionType.Jump
                            || (comboContinuation && _comboFirstAction == MainActionType.Jump);

        if (jumpInvolved)
        {
            // ジャンプを含む場合は「着地するまで（上限あり）」がクールタイム。
            // 相方（ダッシュ / 攻撃）の時間ベースのクールタイムは無視する。
            StartJumpCooldown();
            _nextReadyTime = Time.time; // 時間ゲートは張らない（_jumpCdActive で待たせる）
            _lastCooldownDuration = 0f;
        }
        else
        {
            // 時間ベースのクールタイム。コンボの場合も、2つ目のクールタイムで上書きするだけでよい
            // （1つ目のクールタイムは必ず先に明けるため）。
            _lastCooldownDuration = CooldownFor(action);
            _nextReadyTime = Time.time + _lastCooldownDuration;
        }

        if (!_combosEnabled || comboContinuation)
        {
            // コンボ無効ステージ、または 2つ目まで使ったので打ち止め（最大2連続）。
            // どちらもこの後は純粋にクールタイム待ちになる。
            _comboStep = 0;
        }
        else
        {
            _comboStep = 1;
            _comboFirstAction = action;
            _comboDeadline = Time.time + comboGraceTime;
        }

        return true;
    }

    private float CooldownFor(MainActionType a)
    {
        switch (a)
        {
            case MainActionType.Dash: return dashCooldown;
            case MainActionType.Attack: return attackCooldown;
            default: return 0f; // Jump は着地ベースなので別処理（ここには来ない想定）
        }
    }

    private void StartJumpCooldown()
    {
        _jumpCdActive = true;
        _jumpCdActivateTime = Time.time;

        bool grounded = _player != null && _player.IsGrounded;
        _jumpCdLeftGround = !grounded;
        _jumpCdLeftGroundTime = grounded
            ? Time.time
            : (_player != null ? _player.LastGroundedTime : Time.time); // 既に空中なら、実際に地面を離れた時刻を基準にする
    }

    /// <summary>単発アクションを実行する。</summary>
    public void Execute(MainActionType action)
    {
        switch (action)
        {
            case MainActionType.Jump:
                DoJump();
                break;
            case MainActionType.Dash:
                StartDash();
                break;
            case MainActionType.Attack:
                StartCoroutine(DoAttack());
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
    }

    private void StartDash()
    {
        _dashDir = _player.FacingSign;   // 向いている方向へ前進
        _dashTimeLeft = dashDuration;
        _dashInvTimeLeft = dashDuration; // 無敵タイマー（移動を中断しても残りを走らせる）
        _dashCurSpeed = dashSpeed;       // 発動時に最高速度
        _dashInvBroken = false;

        // ダッシュ中は重力を完全に切る（＝落下ゼロ）。終了時に戻す。
        _savedGravityScale = _rb.gravityScale;
        _rb.gravityScale = 0f;

        IsInvincible = true;
        OverridesMovement = true;
        IsDashing = true;

        _rb.linearVelocity = new Vector2(_dashDir * dashSpeed, 0f);
    }

    /// <summary>ダッシュを完全に終了する（効果時間切れ / 無効化時）。無敵も解除する。</summary>
    private void EndDash()
    {
        _dashTimeLeft = 0f;
        _dashInvTimeLeft = 0f;
        _rb.gravityScale = _savedGravityScale;
        _dashInvBroken = false;
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
        // _dashInvTimeLeft / _dashInvBroken はそのまま → 無敵は通常のダッシュ効果時間ぶん継続
    }

    private void OnDisable()
    {
        // 無効化時に重力が切れたまま／無敵のままにならないように
        if (_dashTimeLeft > 0f || _dashInvTimeLeft > 0f) EndDash();
        _bufferedInput = false;
        InInputBufferZone = false;
        _bufferZoneFraction = 0f;
    }

    private IEnumerator DoAttack()
    {
        if (attackHitbox == null) yield break;

        attackHitbox.Configure(_player.FacingSign, attackDamage);
        attackHitbox.gameObject.SetActive(true);
        yield return new WaitForSeconds(attackDuration);
        attackHitbox.gameObject.SetActive(false);
    }
}
