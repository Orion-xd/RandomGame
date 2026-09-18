using UnityEngine;

/// <summary>
/// 敵が発射する弾。発射時に設定した方向へ直線的に飛び続ける（ホーミングは発射時の一度きり）。
/// ただし <see cref="homingTurnSpeed"/> が 0 より大きいときは、飛行中も継続してプレイヤーへ
/// 向きを補正し続ける「追尾弾」になる（Enemy3の③用。Enemy2はこれを使わず発射時一度きりのまま）。
/// 当たり判定はトリガー（プレイヤーを物理的に押し返す必要が無いため。接触時の処理は全てスクリプト側で行う）。
/// Enemy と同じ接触仕様：プレイヤー本体に触れるとダメージ、ダッシュ中はすり抜け、
/// プレイヤーの攻撃（AttackHitbox）に触れると一撃で消滅。地面に当たっても消滅する。
/// 敵キャラに触れると enemyDamage を与えて消滅する（selfHitGraceTime の間だけは発射元自身と
/// 重なっているため無視する）。追尾弾をラスボスへ誘導してヒットさせる攻略に対応するための仕様。
///
/// 【反転（急激な方向転換）について、2026-09-17追加】
/// 追尾中にダッシュで弾をすり抜けられると、次の瞬間「プレイヤーへ向かうべき方向」がほぼ真逆になる。
/// これを通常の回転（RotateTowards）で処理すると、ほぼ180度回転する際の回転軸が数値的に不安定になり、
/// 回転の途中で意図しない方向（地面や壁の方向）を一瞬通過してしまうことがあった。
/// そこで、現在の速度ベクトルと目標方向のなす角が reversalAngleThreshold を超えたときだけ、
/// 「回転」ではなく「速度ベクトルの大きさを直線的に減速→0→反対向きへ加速」で向きを変える（反転モード）。
/// 減速・加速は常に元の進行方向の延長線上で起こるため、横方向（地面や壁の方向）を向いてしまうことがない。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class Bullet : MonoBehaviour
{
    [SerializeField] private float speed = 8f;
    [SerializeField] private int damage = 1;
    [Tooltip("この時間が経過すると何にも当たらなくても自動的に消える（画面外に飛び続けるのを防ぐ）。" +
             "追尾弾（homingTurnSpeed > 0）には適用しない＝命中/地面接触/攻撃で消えるまで永久に飛び続ける")]
    [SerializeField] private float maxLifetime = 6f;
    [Tooltip("これに触れると弾が消滅する（地面・高台など）")]
    [SerializeField] private LayerMask groundLayer;
    [Tooltip("秒速あたりの旋回角度（度）。0なら発射時の方向のまま直進（既定・Enemy2用）。" +
             "0より大きいと飛行中もプレイヤーへ継続して向きを補正し続ける追尾弾になる（Enemy3用、ホーミングの強度に相当）")]
    [SerializeField] private float homingTurnSpeed = 0f;
    [Tooltip("現在の進行方向とプレイヤーへ向かう方向のなす角がこれ（度）を超えたら反転モードに入る" +
             "（ダッシュで弾をすり抜けられた直後などの、ほぼ真逆への方向転換で、回転により意図せず地面/壁を向いてしまう問題の対策）")]
    [SerializeField] private float reversalAngleThreshold = 170f;
    [Tooltip("反転（減速→反対向きへの加速）にかかる速さの倍率。既定の1なら" +
             "「homingTurnSpeedで180度回転するのと同じ時間」で反転が完了する。大きいほど反転が速く鋭くなる")]
    [SerializeField] private float reversalRateMultiplier = 1f;
    [Tooltip("敵キャラ（ラスボスなど）にヒットしたときに与えるダメージ。" +
             "追尾弾をラスボスへ誘導してヒットさせる、という攻略に使う")]
    [SerializeField] private int enemyDamage = 2;
    [Tooltip("発射直後、この秒数だけは敵キャラに触れてもダメージを与えず素通りする。" +
             "弾は発射元の敵自身の位置（＝重なった状態）で生成されるため、発射直後に発射元自身へ即座に" +
             "命中してしまうのを防ぐための猶予時間")]
    [SerializeField] private float selfHitGraceTime = 0.2f;

    private Vector2 _velocity;
    private bool _reversing;
    private float _age;
    private Collider2D _col;
    private Collider2D _playerCollider;
    private MainActionController _playerMainAction;
    private bool _ignoringPlayer;

    private void Awake()
    {
        _col = GetComponent<Collider2D>();
        _col.isTrigger = true;
    }

    private void Start()
    {
        var p = GameObject.FindGameObjectWithTag("Player");
        if (p != null)
        {
            _playerCollider = p.GetComponent<Collider2D>();
            _playerMainAction = p.GetComponent<MainActionController>();
        }

        // 追尾弾は「命中/地面接触/攻撃で消える」以外の理由では消えない仕様なので、
        // 時間経過による自動消滅（安全策）の対象から外す。Configure() は Instantiate 直後に
        // 同期的に呼ばれるため、この時点で homingTurnSpeed は既に確定している。
        if (homingTurnSpeed <= 0f) Destroy(gameObject, maxLifetime);
    }

    /// <summary>飛んでいく方向と速度・威力を設定する。direction は正規化不要。
    /// homingTurnSpeedDegPerSec を 0 より大きくすると、飛行中も継続してプレイヤーを追尾する。</summary>
    public void Configure(Vector2 direction, float bulletSpeed, int bulletDamage, float homingTurnSpeedDegPerSec = 0f)
    {
        Vector2 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        speed = bulletSpeed;
        damage = bulletDamage;
        homingTurnSpeed = homingTurnSpeedDegPerSec;
        _velocity = dir * speed;
    }

    private void Update()
    {
        _age += Time.deltaTime;

        if (homingTurnSpeed > 0f && _playerCollider != null)
        {
            Vector2 toPlayer = (Vector2)_playerCollider.transform.position - (Vector2)transform.position;
            if (toPlayer.sqrMagnitude > 0.0001f)
            {
                Vector2 desired = toPlayer.normalized;
                Vector2 currentDir = _velocity.sqrMagnitude > 0.0001f ? _velocity.normalized : desired;

                if (!_reversing && Vector2.Angle(currentDir, desired) > reversalAngleThreshold)
                {
                    _reversing = true;
                }

                if (_reversing)
                {
                    // 回転ではなく、速度ベクトルの大きさを直線的に減速→0→反対向きへ加速させて向きを変える。
                    // 目標に達する（＝反転完了）まではこのモードを維持し、毎フレームなす角を測り直さない
                    // （大きさが0付近では「向き」が数値的に不安定になり、モード判定がガタつくため）。
                    Vector2 targetVelocity = desired * speed;
                    float baseRate = (2f * speed * homingTurnSpeed) / 180f; // homingTurnSpeedで180度回転するのと同じ時間で反転する速さ
                    float rate = baseRate * reversalRateMultiplier;
                    _velocity = Vector2.MoveTowards(_velocity, targetVelocity, rate * Time.deltaTime);
                    if (_velocity == targetVelocity) _reversing = false;
                }
                else
                {
                    Vector2 newDir = Vector3.RotateTowards(currentDir, desired, homingTurnSpeed * Mathf.Deg2Rad * Time.deltaTime, 0f);
                    _velocity = newDir * speed;
                }
            }
        }

        transform.position += (Vector3)(_velocity * Time.deltaTime);
    }

    private void FixedUpdate()
    {
        // Enemy と同じ：ダッシュ中だけ判定を無視してすり抜けさせる（トリガーでも IgnoreCollision は効く）。
        if (_playerCollider == null || _playerMainAction == null) return;

        bool shouldIgnore = _playerMainAction.IsDashing;
        if (shouldIgnore != _ignoringPlayer)
        {
            Physics2D.IgnoreCollision(_playerCollider, _col, shouldIgnore);
            _ignoringPlayer = shouldIgnore;
        }
    }

    private void OnTriggerEnter2D(Collider2D other) => HandleTrigger(other);
    private void OnTriggerStay2D(Collider2D other) => HandleTrigger(other);

    private void HandleTrigger(Collider2D other)
    {
        if (IsGround(other))
        {
            Destroy(gameObject);
            return;
        }

        // 敵キャラに当たった場合：発射直後の猶予時間内（発射元自身と重なっている間）は素通りする。
        // それ以降は、追尾弾をラスボスへ誘導してヒットさせられるよう、ダメージを与えて消滅する。
        var enemy = other.GetComponentInParent<Enemy>();
        if (enemy != null)
        {
            if (_age < selfHitGraceTime) return;
            enemy.TakeDamage(enemyDamage);
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
