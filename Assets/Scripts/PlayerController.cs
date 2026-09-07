using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// プレイヤーの通常操作（左右移動）と「向き」の管理。
/// 移動した方向にキャラを向かせ、その向きをメインアクション（ダッシュ・攻撃）が参照する。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(SpriteRenderer))]
public class PlayerController : MonoBehaviour
{
    [Header("移動パラメータ")]
    [Tooltip("左右移動の速度 (units/sec)")]
    [SerializeField] private float moveSpeed = 6f;

    [Header("接地判定")]
    [Tooltip("地面とみなすレイヤー（Ground_Left/Right, OneWayPlatform 等）。ジャンプの空中発動禁止に使う")]
    [SerializeField] private LayerMask groundLayer;
    [Tooltip("足元からこの距離だけ下にレイヤーがあれば接地とみなす")]
    [SerializeField] private float groundCheckDistance = 0.1f;

    [Header("空中補助")]
    [Tooltip("地面を離れてからこの秒数は落下しない（コンボの繋ぎで少し高度が落ちて落下死するのを防ぐ猶予）。" +
             "上昇中（ジャンプ直後など）には効かない")]
    [SerializeField] private float noFallGrace = 0.1f;
    [Tooltip("地面を離れてからこの秒数はジャンプを受け付ける（コヨーテタイム）。noFallGrace より長くてよい＝" +
             "少しだけ落下していてもジャンプできる。上昇中には効かない")]
    [SerializeField] private float coyoteJumpGrace = 0.18f;

    private Rigidbody2D _rb;
    private SpriteRenderer _sr;
    private Collider2D _col;
    private MainActionController _mainAction;
    private float _moveInput;
    private float _knockbackTimeLeft;
    private float _baseGravityScale;

    /// <summary>キャラの向き。+1 = 右, -1 = 左。最後に移動した向きを保持する。</summary>
    public int FacingSign { get; private set; } = 1;

    /// <summary>現在の左右移動入力。-1 / 0 / +1。ダッシュ中でも更新され続ける（MainActionController が参照）。</summary>
    public float MoveInput => _moveInput;

    /// <summary>地面（groundLayer）に足が接しているか。ジャンプの空中発動禁止に使う。</summary>
    public bool IsGrounded { get; private set; }

    /// <summary>最後に接地していた時刻（Time.time）。ジャンプのクールタイム上限判定の基準に使う。</summary>
    public float LastGroundedTime { get; private set; }

    /// <summary>コヨーテタイム中か（地面を離れて coyoteJumpGrace 秒以内・非上昇）。
    /// この間は MainActionController がジャンプの発動を特別に許可する。落下抑制(noFall)より長め。</summary>
    public bool InCoyoteTime { get; private set; }

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _sr = GetComponent<SpriteRenderer>();
        _col = GetComponent<Collider2D>();
        _mainAction = GetComponent<MainActionController>();
        _baseGravityScale = _rb.gravityScale;
    }

    private void Update()
    {
        // 会話中は移動入力を止める。
        if (DialoguePlayer.IsPlaying) { _moveInput = 0f; return; }

        var kb = Keyboard.current;
        float x = 0f;
        if (kb != null)
        {
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) x -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) x += 1f;
        }
        _moveInput = x;

        // 移動している方向へキャラを向かせる（ダッシュ中は向きを固定）。
        bool facingLocked = _mainAction != null && _mainAction.OverridesMovement;
        if (!facingLocked)
        {
            if (x > 0.01f) SetFacing(1);
            else if (x < -0.01f) SetFacing(-1);
        }
    }

    private void FixedUpdate()
    {
        // 接地判定：足元のすぐ下に groundLayer があるか
        // ※ OverlapBox は Physics2D.IgnoreCollision（すり抜け設定）を無視するので、一方通行の高台を
        //    下から突き抜ける瞬間などに誤検知する。「上昇中は接地とみなさない」ことで弾く
        //    （地上で静止中の vy はほぼ 0、落下着地時は負なので通常の接地判定には影響しない）。
        Bounds b = _col.bounds;
        Vector2 origin = new Vector2(b.center.x, b.min.y - groundCheckDistance * 0.5f);
        Vector2 size = new Vector2(b.size.x * 0.9f, groundCheckDistance);
        bool groundOverlap = Physics2D.OverlapBox(origin, size, 0f, groundLayer);
        IsGrounded = groundOverlap && _rb.linearVelocity.y <= 0.05f;
        if (IsGrounded) LastGroundedTime = Time.time;

        // ダッシュ中は MainActionController が速度・重力を制御するので、ここでは触らない。
        if (_mainAction != null && _mainAction.OverridesMovement) { InCoyoteTime = false; return; }

        // ── 空中補助（2つの独立した猶予） ──
        // 共通条件：非接地・ノックバック中でない・非上昇（vy<=0.01。ジャンプの上昇は妨げない）。
        float airborneFor = Time.time - LastGroundedTime;
        bool notRising = _rb.linearVelocity.y <= 0.01f;
        bool aidBase = !IsGrounded && _knockbackTimeLeft <= 0f && notRising;

        // noFall：地面を離れて noFallGrace 秒は落下させない（重力を切り、下向き速度を止める）。
        bool noFall = aidBase && noFallGrace > 0f && airborneFor <= noFallGrace;
        // コヨーテタイム：ジャンプ受付だけはもう少し長く許可する（少し落下していてもジャンプ可）。
        InCoyoteTime = aidBase && coyoteJumpGrace > 0f && airborneFor <= coyoteJumpGrace;

        _rb.gravityScale = noFall ? 0f : _baseGravityScale;
        if (noFall && _rb.linearVelocity.y < 0f)
        {
            Vector2 nv = _rb.linearVelocity;
            nv.y = 0f;
            _rb.linearVelocity = nv;
        }

        // ノックバック中は与えた速度をそのまま物理演算に任せる（入力で上書きしない）。
        if (_knockbackTimeLeft > 0f)
        {
            _knockbackTimeLeft -= Time.fixedDeltaTime;
            return;
        }

        Vector2 v = _rb.linearVelocity;
        v.x = _moveInput * moveSpeed;
        _rb.linearVelocity = v;
    }

    /// <summary>敵接触時などに呼ばれる。指定した速度を duration 秒間、入力で上書きせず維持させる。</summary>
    public void ApplyKnockback(Vector2 velocity, float duration)
    {
        _knockbackTimeLeft = duration;
        _rb.linearVelocity = velocity;
    }

    /// <summary>着地予測レイの最大距離（先行入力のジャンプ受付判定用）。</summary>
    private const float LandingProbeDistance = 30f;

    /// <summary>
    /// いまの落下軌道で groundLayer に着地するまでのおおよその秒数を返す（メインアクションの先行入力・
    /// ジャンプ受付の判定用）。上昇中、または真下に地面が見つからない場合は false。
    /// 足元中央から真下へのレイ 1 本だけの簡易予測なので厳密ではない（台の端などは誤差が出る）。
    /// コストは軽い（呼び出し側が「ジャンプのクールタイム中かつ非上昇」のときだけ呼ぶ想定）。
    /// </summary>
    public bool TryPredictLandingTime(out float seconds)
    {
        seconds = 0f;

        if (_rb.linearVelocity.y > 0.01f) return false; // 上昇中は対象外
        if (IsGrounded) return true;                    // すでに接地（seconds = 0）

        Bounds b = _col.bounds;
        Vector2 origin = new Vector2(b.center.x, b.min.y);
        RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, LandingProbeDistance, groundLayer);
        if (hit.collider == null) return false;

        float g = Mathf.Abs(_baseGravityScale * Physics2D.gravity.y);
        if (g <= 0.0001f) return false;

        float d = Mathf.Max(hit.distance, 0f);
        float v0 = Mathf.Max(-_rb.linearVelocity.y, 0f); // 下向きの速さ
        // d = v0*t + 0.5*g*t^2 を解く（正の根）。
        seconds = (-v0 + Mathf.Sqrt(v0 * v0 + 2f * g * d)) / g;
        return true;
    }

    private void SetFacing(int sign)
    {
        FacingSign = sign;
        _sr.flipX = sign < 0; // スプライトは右向きが基準
    }
}
