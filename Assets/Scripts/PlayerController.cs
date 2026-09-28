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

    [Header("崖・段差のわずかな引っかかり救済")]
    [Tooltip("当たり判定のうち、これ以上の割合が崖の上面より上にあれば「あと少しで乗り越えられる」とみなして引き上げる")]
    [SerializeField] private float ledgeAssistMinAboveFraction = 0.9f;
    [Tooltip("崖への接触判定を前方にどれだけ伸ばすか")]
    [SerializeField] private float ledgeAssistProbeDistance = 0.08f;
    [Tooltip("引き上げ後に足す余白。境界ぴったりに置くと次のフレームで再度ひっかかり判定してガタつくのを防ぐ")]
    [SerializeField] private float ledgeAssistSkin = 0.02f;

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

    /// <summary>通常時の左右移動速度 (units/sec)。ダッシュ後半の減速目標に MainActionController が参照する。</summary>
    public float MoveSpeed => moveSpeed;

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
        // 会話中・画面切り替え直後（InputLock）は移動入力を止める。メインアクション
        // （MainActionController）も同じロックで発動を止めているため、移動だけ先に解禁すると
        // 「動けるのになぜアクションが出せないのか」という不自然な状態になる。一度は
        // 移動だけ対象外にしたが、この不自然さの方が問題だったため両方ブロックへ差し戻した。
        // ステージ画面にはメニューのカーソル移動に相当する操作が無いため、早期解除の仕組みも無い。
        if (DialoguePlayer.IsPlaying || !InputLock.InputAllowed) { _moveInput = 0f; return; }

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

        // 崖・段差のわずかな引っかかり救済。ダッシュ中（OverridesMovement）でも対象にする
        // （ユーザー指定。ダッシュの終了を待たず、その場で引き上げてよい）ため、
        // 下の OverridesMovement による early return より前に呼ぶ。
        ApplyLedgeAssist();

        // ダッシュ中は MainActionController が速度・重力を制御するので、ここから先は触らない。
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

    /// <summary>崖・段差にわずかに引っかかって乗り越えられない不便さを救済する。
    /// 当たり判定を「下から ledgeAssistMinAboveFraction 未満の帯」と「それ以上の帯」に分けて、進行方向
    /// すぐ前方をそれぞれ別に判定する。下の帯だけが groundLayer にブロックされていて（＝ほぼ乗り越えて
    /// いる）、上の帯は完全にクリアしている（＝背より高い壁ではない）ときだけ、段差の正確な高さまで
    /// 直接引き上げる。差分は当たり判定の高さのごく一部（既定10%）なので、瞬間補正でも不自然に見えない
    /// 想定。ダッシュ中（MainActionController が速度を制御中）も対象。実際の移動方向は入力ではなく
    /// 現在の横速度 _rb.linearVelocity.x の符号で判定する（ダッシュ中は _moveInput と無関係にダッシュの
    /// 向きへ進んでいるため）。ノックバック中は対象外（外部から与えられた速度を尊重する）。</summary>
    private void ApplyLedgeAssist()
    {
        if (_knockbackTimeLeft > 0f) return;
        if (_rb.linearVelocity.y > 0.01f) return; // 上昇中は対象外

        float vx = _rb.linearVelocity.x;
        if (Mathf.Abs(vx) < 0.01f) return;
        float dir = Mathf.Sign(vx);

        Bounds b = _col.bounds;
        float height = b.size.y;
        float bottomBandHeight = height * (1f - ledgeAssistMinAboveFraction);
        if (bottomBandHeight <= 0f) return;

        float frontX = b.center.x + dir * (b.extents.x + ledgeAssistProbeDistance * 0.5f);

        Vector2 lowerOrigin = new Vector2(frontX, b.min.y + bottomBandHeight * 0.5f);
        Vector2 lowerSize = new Vector2(ledgeAssistProbeDistance, bottomBandHeight);
        if (!Physics2D.OverlapBox(lowerOrigin, lowerSize, 0f, groundLayer)) return;

        float upperHeight = height - bottomBandHeight;
        Vector2 upperOrigin = new Vector2(frontX, b.min.y + bottomBandHeight + upperHeight * 0.5f);
        Vector2 upperSize = new Vector2(ledgeAssistProbeDistance, upperHeight);
        if (Physics2D.OverlapBox(upperOrigin, upperSize, 0f, groundLayer)) return; // 背より高い壁は対象外

        // 段差の正確な表面Yを、前方すぐ上から下向きの BoxCast で割り出す（単純な1本のレイだと、
        // タイルの境界ちょうどなどで判定に使った帯からわずかに外れて空振りすることがあるため、
        // 判定に使ったのと同じ幅の帯で確実に拾う）。
        Vector2 castOrigin = new Vector2(frontX, b.max.y);
        Vector2 castSize = new Vector2(ledgeAssistProbeDistance, 0.02f);
        RaycastHit2D hit = Physics2D.BoxCast(castOrigin, castSize, 0f, Vector2.down, height, groundLayer);
        if (hit.collider == null) return;

        float targetBottomY = hit.point.y + ledgeAssistSkin;
        float deltaY = targetBottomY - b.min.y;
        if (deltaY <= 0f) return;

        _rb.position += new Vector2(0f, deltaY);
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
