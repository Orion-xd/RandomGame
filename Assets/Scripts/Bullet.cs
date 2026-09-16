using UnityEngine;

/// <summary>
/// 敵が発射する弾。発射時に設定した方向へ直線的に飛び続ける（ホーミングは発射時の一度きり）。
/// ただし <see cref="homingTurnSpeed"/> が 0 より大きいときは、飛行中も継続してプレイヤーへ
/// 向きを補正し続ける「追尾弾」になる（Enemy3の③用。Enemy2はこれを使わず発射時一度きりのまま）。
/// Enemy と同じ接触仕様：プレイヤー本体に触れるとダメージ、ダッシュ中はすり抜け、
/// プレイヤーの攻撃（AttackHitbox）に触れると一撃で消滅。地面に当たっても消滅する。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class Bullet : MonoBehaviour
{
    [SerializeField] private float speed = 8f;
    [SerializeField] private int damage = 1;
    [Tooltip("この時間が経過すると何にも当たらなくても自動的に消える（画面外に飛び続けるのを防ぐ）")]
    [SerializeField] private float maxLifetime = 6f;
    [Tooltip("これに触れると弾が消滅する（地面・高台など）")]
    [SerializeField] private LayerMask groundLayer;
    [Tooltip("秒速あたりの旋回角度（度）。0なら発射時の方向のまま直進（既定・Enemy2用）。" +
             "0より大きいと飛行中もプレイヤーへ継続して向きを補正し続ける追尾弾になる（Enemy3用、ホーミングの強度に相当）")]
    [SerializeField] private float homingTurnSpeed = 0f;

    private Vector2 _direction = Vector2.right;
    private Collider2D _col;
    private Collider2D _playerCollider;
    private MainActionController _playerMainAction;
    private bool _ignoringPlayer;

    private void Awake()
    {
        _col = GetComponent<Collider2D>();
    }

    private void Start()
    {
        var p = GameObject.FindGameObjectWithTag("Player");
        if (p != null)
        {
            _playerCollider = p.GetComponent<Collider2D>();
            _playerMainAction = p.GetComponent<MainActionController>();
        }
        Destroy(gameObject, maxLifetime);
    }

    /// <summary>飛んでいく方向と速度・威力を設定する。direction は正規化不要。
    /// homingTurnSpeedDegPerSec を 0 より大きくすると、飛行中も継続してプレイヤーを追尾する。</summary>
    public void Configure(Vector2 direction, float bulletSpeed, int bulletDamage, float homingTurnSpeedDegPerSec = 0f)
    {
        _direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        speed = bulletSpeed;
        damage = bulletDamage;
        homingTurnSpeed = homingTurnSpeedDegPerSec;
    }

    private void Update()
    {
        if (homingTurnSpeed > 0f && _playerCollider != null)
        {
            Vector2 toPlayer = (Vector2)_playerCollider.transform.position - (Vector2)transform.position;
            if (toPlayer.sqrMagnitude > 0.0001f)
            {
                Vector2 desired = toPlayer.normalized;
                _direction = Vector3.RotateTowards(_direction, desired, homingTurnSpeed * Mathf.Deg2Rad * Time.deltaTime, 0f);
            }
        }

        transform.position += (Vector3)(_direction * speed * Time.deltaTime);
    }

    private void FixedUpdate()
    {
        // Enemy と同じ：ダッシュ中だけ物理衝突を無視してすり抜けさせる。
        if (_playerCollider == null || _playerMainAction == null) return;

        bool shouldIgnore = _playerMainAction.IsDashing;
        if (shouldIgnore != _ignoringPlayer)
        {
            Physics2D.IgnoreCollision(_playerCollider, _col, shouldIgnore);
            _ignoringPlayer = shouldIgnore;
        }
    }

    private void OnCollisionEnter2D(Collision2D collision) => HandleCollision(collision.collider);
    private void OnCollisionStay2D(Collision2D collision) => HandleCollision(collision.collider);

    private void HandleCollision(Collider2D other)
    {
        if (IsGround(other))
        {
            Destroy(gameObject);
            return;
        }

        var health = other.GetComponent<PlayerHealth>();
        if (health == null) return;

        health.TakeDamage(damage);
        Destroy(gameObject);
    }

    private bool IsGround(Collider2D other) => (groundLayer.value & (1 << other.gameObject.layer)) != 0;

    /// <summary>プレイヤーの攻撃判定（AttackHitbox）から呼ばれる。一撃で消滅する。</summary>
    public void DestroyByAttack()
    {
        Destroy(gameObject);
    }
}
