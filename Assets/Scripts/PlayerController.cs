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

    private Rigidbody2D _rb;
    private SpriteRenderer _sr;
    private Collider2D _col;
    private MainActionController _mainAction;
    private float _moveInput;
    private float _knockbackTimeLeft;

    /// <summary>キャラの向き。+1 = 右, -1 = 左。最後に移動した向きを保持する。</summary>
    public int FacingSign { get; private set; } = 1;

    /// <summary>現在の左右移動入力。-1 / 0 / +1。ダッシュ中でも更新され続ける（MainActionController が参照）。</summary>
    public float MoveInput => _moveInput;

    /// <summary>地面（groundLayer）に足が接しているか。ジャンプの空中発動禁止に使う。</summary>
    public bool IsGrounded { get; private set; }

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _sr = GetComponent<SpriteRenderer>();
        _col = GetComponent<Collider2D>();
        _mainAction = GetComponent<MainActionController>();
    }

    private void Update()
    {
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
        Bounds b = _col.bounds;
        Vector2 origin = new Vector2(b.center.x, b.min.y - groundCheckDistance * 0.5f);
        Vector2 size = new Vector2(b.size.x * 0.9f, groundCheckDistance);
        IsGrounded = Physics2D.OverlapBox(origin, size, 0f, groundLayer);

        // ダッシュ中は MainActionController が速度を制御するので、ここでは触らない。
        if (_mainAction != null && _mainAction.OverridesMovement) return;

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

    private void SetFacing(int sign)
    {
        FacingSign = sign;
        _sr.flipX = sign < 0; // スプライトは右向きが基準
    }
}
