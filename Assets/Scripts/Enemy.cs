using UnityEngine;

/// <summary>
/// 敵キャラ。通常は体力1で一撃。ボス等は maxHealth を 2 以上にすると頭上に体力ゲージを表示する。
///
/// プレイヤーとの接触仕様：
///  - 通常時：当たり判定は「実体」。プレイヤーが触れるとすり抜けずダメージ＋ノックバックを受ける。
///  - プレイヤーがダッシュ中：Physics2D.IgnoreCollision で衝突を無視し、すり抜けさせる（ダッシュは無敵）。
///  - プレイヤーの攻撃判定（AttackHitbox、トリガー）に対しては常に反応する（これは物理衝突ではなくトリガー通知）。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class Enemy : MonoBehaviour
{
    [SerializeField] private int maxHealth = 1;
    [SerializeField] private int contactDamage = 1;
    [Tooltip("頭上の体力ゲージ。maxHealth >= 2 のときだけ表示される")]
    [SerializeField] private EnemyHealthBar healthBar;

    [Header("ノックバック（ダッシュ中でない接触時）")]
    [Tooltip("プレイヤーを押し返す水平方向の速さ")]
    [SerializeField] private float knockbackSpeed = 8f;
    [Tooltip("押し返す際に上方向へも少し跳ねさせる速さ")]
    [SerializeField] private float knockbackUpSpeed = 4f;
    [Tooltip("ノックバックで入力を受け付けなくする時間")]
    [SerializeField] private float knockbackDuration = 0.25f;

    private Collider2D _col;
    private Collider2D _playerCollider;
    private PlayerController _playerController;
    private MainActionController _playerMainAction;
    private bool _ignoringPlayer;

    private int _health;

    public int Health => _health;
    public int MaxHealth => maxHealth;

    private void Awake()
    {
        _col = GetComponent<Collider2D>();
        _health = maxHealth;

        if (healthBar != null)
        {
            healthBar.gameObject.SetActive(maxHealth >= 2);
            healthBar.Set(_health, maxHealth);
        }
    }

    private void Start()
    {
        var p = GameObject.FindGameObjectWithTag("Player");
        if (p != null)
        {
            _playerCollider = p.GetComponent<Collider2D>();
            _playerController = p.GetComponent<PlayerController>();
            _playerMainAction = p.GetComponent<MainActionController>();
        }
    }

    private void FixedUpdate()
    {
        // ダッシュ中だけ物理衝突を無視してすり抜けさせる。それ以外は実体として扱う。
        if (_playerCollider == null || _playerMainAction == null) return;

        bool shouldIgnore = _playerMainAction.IsDashing;
        if (shouldIgnore != _ignoringPlayer)
        {
            Physics2D.IgnoreCollision(_playerCollider, _col, shouldIgnore);
            _ignoringPlayer = shouldIgnore;
        }
    }

    /// <summary>攻撃判定などから呼ばれる。</summary>
    public void TakeDamage(int amount)
    {
        if (_health <= 0) return;

        _health -= amount;
        if (healthBar != null && maxHealth >= 2) healthBar.Set(Mathf.Max(_health, 0), maxHealth);

        if (_health <= 0) Die();
    }

    private void Die()
    {
        Destroy(gameObject);
    }

    // 通常時（ダッシュ中でない）は当たり判定が実体なので物理衝突として届く。
    private void OnCollisionEnter2D(Collision2D collision) => TryTouchPlayer(collision.collider);
    private void OnCollisionStay2D(Collision2D collision) => TryTouchPlayer(collision.collider);

    private void TryTouchPlayer(Collider2D other)
    {
        // プレイヤー「本体」のコライダーにだけ反応する（AttackHitbox は別途トリガーで処理される）。
        var health = other.GetComponent<PlayerHealth>();
        if (health == null) return;

        bool damaged = health.TakeDamage(contactDamage);
        if (!damaged) return; // 無敵時間中などで実際にダメージが入らなかった場合はノックバックもしない

        var controller = other.GetComponent<PlayerController>();
        if (controller == null) return;

        float dir = Mathf.Sign(other.transform.position.x - transform.position.x);
        if (dir == 0f) dir = 1f;
        controller.ApplyKnockback(new Vector2(dir * knockbackSpeed, knockbackUpSpeed), knockbackDuration);
    }
}
