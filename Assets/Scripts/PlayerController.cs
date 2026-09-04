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

    private Rigidbody2D _rb;
    private SpriteRenderer _sr;
    private MainActionController _mainAction;
    private float _moveInput;

    /// <summary>キャラの向き。+1 = 右, -1 = 左。最後に移動した向きを保持する。</summary>
    public int FacingSign { get; private set; } = 1;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _sr = GetComponent<SpriteRenderer>();
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
        // ダッシュ中は MainActionController が速度を制御するので、ここでは触らない。
        if (_mainAction != null && _mainAction.OverridesMovement) return;

        Vector2 v = _rb.linearVelocity;
        v.x = _moveInput * moveSpeed;
        _rb.linearVelocity = v;
    }

    private void SetFacing(int sign)
    {
        FacingSign = sign;
        _sr.flipX = sign < 0; // スプライトは右向きが基準
    }
}
