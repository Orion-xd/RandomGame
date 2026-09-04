using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// メインアクション（ジャンプ / ダッシュ / 攻撃）の発動を司る。
/// スペースキーでキュー先頭のアクションを1つ消費して実行し、クールタイム経過まで次を受け付けない。
///
/// 将来「2アクションの組み合わせ」を足せるよう、
///  - 予定データ (MainActionQueue) と実行処理 (Execute) を分離
///  - Execute は単発 MainActionType を受けるだけ
/// にしてある。コンボ実装時は TryTrigger 内で queue.Peek(0)/Peek(1) を見て
/// 「2消費して合成アクションを実行」する分岐を挟む。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(PlayerController))]
[RequireComponent(typeof(MainActionQueue))]
public class MainActionController : MonoBehaviour
{
    [Header("共通")]
    [Tooltip("アクション発動から次を受け付けるまでの待機時間（連打防止）")]
    [SerializeField] private float cooldown = 0.6f;

    [Header("ジャンプ")]
    [Tooltip("ジャンプ時に与える上向きの初速")]
    [SerializeField] private float jumpForce = 12f;

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
    [SerializeField] private float attackDuration = 0.2f;
    [Tooltip("攻撃1ヒットのダメージ")]
    [SerializeField] private int attackDamage = 1;

    private Rigidbody2D _rb;
    private PlayerController _player;
    private MainActionQueue _queue;
    private float _nextReadyTime;

    // ダッシュ状態（FixedUpdate で処理する）
    private float _dashTimeLeft;
    private int _dashDir;
    private float _savedGravityScale;
    private float _dashCurSpeed;   // 現在のダッシュ速度（大きさ）
    private bool _dashInvBroken;   // 後半に後方入力で無敵を解除したか（一度解除したら効果終了まで戻らない）

    /// <summary>この間はダメージを受けない（ダッシュ中）。</summary>
    public bool IsInvincible { get; private set; }

    /// <summary>この間は PlayerController が移動速度・向きを上書きしない（ダッシュ中）。</summary>
    public bool OverridesMovement { get; private set; }

    /// <summary>次のアクションを発動できるか（クールタイム外か）。</summary>
    public bool IsReady => Time.time >= _nextReadyTime;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _player = GetComponent<PlayerController>();
        _queue = GetComponent<MainActionQueue>();
        if (attackHitbox != null) attackHitbox.gameObject.SetActive(false);
    }

    private void Update()
    {
        // スペースキーが押されていれば、メインアクションを実行する。
        var kb = Keyboard.current;
        if (kb == null || !kb.spaceKey.wasPressedThisFrame) return;
        TryTrigger();
    }

    private void FixedUpdate()
    {
        if (_dashTimeLeft <= 0f) return;

        // FixedUpdate は物理積分の「前」に走るので、ここで設定した速度がそのまま反映される。
        // （コルーチン + WaitForFixedUpdate だと積分の「後」に走るため、
        //  重力で1ステップぶん落ちてから y=0 に戻す形になり、ダッシュ中に少しずつ落下していた。）

        float dt = Time.fixedDeltaTime;
        float elapsed = dashDuration - _dashTimeLeft;
        bool lockedPhase = elapsed < dashDuration * dashLockFraction;

        if (lockedPhase)
        {
            // ── 前半：最高速度を維持。移動入力は完全に無視。完全無敵。──
            _dashCurSpeed = dashSpeed;
            IsInvincible = true;
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
                // 後方入力：急ブレーキ ＋ 無敵解除（一度解除したら効果終了まで戻らない）
                _dashCurSpeed = Mathf.MoveTowards(_dashCurSpeed, 0f, dashBrakeDecel * dt);
                _dashInvBroken = true;
            }
            // 入力なし：_dashCurSpeed 据え置き（速度キープ）

            IsInvincible = !_dashInvBroken;
        }

        // 落下しないよう Y は 0 に固定。進行方向は発動時の向きのまま。
        _rb.linearVelocity = new Vector2(_dashDir * _dashCurSpeed, 0f);

        _dashTimeLeft -= dt;
        if (_dashTimeLeft <= 0f) EndDash();
    }

    /// <summary>クールタイム外ならキュー先頭を消費してアクションを実行する。</summary>
    public void TryTrigger()
    {
        if (!IsReady) return;

        // ── コンボ拡張ポイント ──
        // 例) if (_queue.Peek(0) == Jump && _queue.Peek(1) == Dash) { _queue.Consume(); _queue.Consume(); ExecuteCombo(...); ... return; }

        MainActionType action = _queue.Consume();
        Execute(action);
        _nextReadyTime = Time.time + cooldown;
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
        Vector2 v = _rb.linearVelocity;
        v.y = jumpForce;
        _rb.linearVelocity = v;
    }

    private void StartDash()
    {
        _dashDir = _player.FacingSign;   // 向いている方向へ前進
        _dashTimeLeft = dashDuration;
        _dashCurSpeed = dashSpeed;       // 発動時に最高速度
        _dashInvBroken = false;

        // ダッシュ中は重力を完全に切る（＝落下ゼロ）。終了時に戻す。
        _savedGravityScale = _rb.gravityScale;
        _rb.gravityScale = 0f;

        IsInvincible = true;
        OverridesMovement = true;

        _rb.linearVelocity = new Vector2(_dashDir * dashSpeed, 0f);
    }

    private void EndDash()
    {
        _dashTimeLeft = 0f;
        _rb.gravityScale = _savedGravityScale;
        IsInvincible = false;   // 効果時間が切れたら必ず無敵解除
        OverridesMovement = false;
    }

    private void OnDisable()
    {
        // ダッシュ中に無効化されても重力が切れたままにならないように
        if (_dashTimeLeft > 0f) EndDash();
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
