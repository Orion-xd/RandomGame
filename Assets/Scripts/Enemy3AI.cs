using UnityEngine;

/// <summary>
/// Enemy3（3行動のミニボス）の行動AI。
/// ①離脱移動 → （距離が離れたら）②放射弾 or ③追尾弾を抽選 → クールタイム → ①…の繰り返し。
/// 接触ダメージ・被ダメージ・ダッシュ中すり抜けなどは Enemy コンポーネント側が担当する
/// （このスクリプトは移動と発射のみを担当し、Enemy と併用する）。
/// </summary>
public class Enemy3AI : MonoBehaviour
{
    private enum State { Retreating, CoolingDown, WaitingForHomingBullet, FirstActionWait }

    [Header("①離脱移動")]
    [Tooltip("プレイヤーから遠ざかる速度")]
    [SerializeField] private float moveSpeed = 2f;
    [Tooltip("プレイヤーとの距離がこれ以上になったら離脱移動を終了する")]
    [SerializeField] private float retreatDistance = 5f;

    [Header("初回行動の遅延")]
    [Tooltip("ステージ開始後、一番最初の行動（②か③のどちらか）だけ、選択されてから" +
             "実際に発射するまでこの秒数だけ待つ。画面に映った瞬間にいきなり攻撃されると" +
             "難しすぎるための救済（2回目以降の行動には適用しない）")]
    [SerializeField] private float firstActionDelay = 3f;

    [Header("弾")]
    [SerializeField] private Bullet bulletPrefab;
    [Tooltip("弾の飛行速度（②③共通）")]
    [SerializeField] private float bulletSpeed = 5f;

    [Header("行動選択の確率")]
    [Range(0f, 1f)]
    [Tooltip("①離脱移動が終わった後、②放射弾を選ぶ確率（0〜1）。残り(1-この値)が③追尾弾になる")]
    [SerializeField] private float radialChance = 0.5f;

    [Header("②放射弾")]
    [Tooltip("放射状に同時発射する弾の数")]
    [SerializeField] private int radialBulletCount = 8;
    [Tooltip("②発動後のクールタイム（秒）")]
    [SerializeField] private float radialCooldown = 3f;

    [Header("③追尾弾")]
    [Tooltip("秒速あたりの旋回角度（度）。大きいほど正確に追尾する＝ホーミングの強度")]
    [SerializeField] private float homingTurnSpeed = 90f;
    [Tooltip("③発動後（弾が消えてから）のクールタイム（秒）")]
    [SerializeField] private float homingCooldown = 3f;

    private State _state = State.Retreating;
    private float _cooldownTimer;
    private Bullet _pendingHomingBullet;
    private Transform _player;
    private SpriteRenderer _sr;
    private Enemy _enemy;
    private int _dir = 1;
    private bool _firstActionDone;
    private bool _pendingIsRadial;

    private void Awake()
    {
        _sr = GetComponent<SpriteRenderer>();
        _enemy = GetComponent<Enemy>();
    }

    private void OnEnable()
    {
        if (_enemy != null) _enemy.OnDamaged += HandleDamaged;
    }

    private void OnDisable()
    {
        if (_enemy != null) _enemy.OnDamaged -= HandleDamaged;
    }

    private void Start()
    {
        var p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) _player = p.transform;
    }

    /// <summary>プレイヤーの攻撃を受けた瞬間に呼ばれる。追尾弾を発射中なら、問答無用でその弾を消してクールタイムへ移行する。</summary>
    private void HandleDamaged()
    {
        Debug.Log("aaa");
        if (_state != State.WaitingForHomingBullet) return;
        Debug.Log("bbb");
        if (_pendingHomingBullet != null)
        {
            Destroy(_pendingHomingBullet.gameObject);
            _pendingHomingBullet = null;
        }

        _cooldownTimer = homingCooldown;
        _state = State.CoolingDown;
    }

    private void Update()
    {
        if (DialoguePlayer.IsPlaying || _player == null) return; // ストーリー再生中は敵を行動させない

        switch (_state)
        {
            case State.Retreating:
                UpdateRetreating();
                break;

            case State.CoolingDown:
                _cooldownTimer -= Time.deltaTime;
                if (_cooldownTimer <= 0f) _state = State.Retreating;
                break;

            case State.WaitingForHomingBullet:
                // 弾が消える（着弾・地面/壁接触・攻撃で破壊）までは何もできない。
                if (_pendingHomingBullet == null)
                {
                    _cooldownTimer = homingCooldown;
                    _state = State.CoolingDown;
                }
                break;

            case State.FirstActionWait:
                // 一番最初の行動だけ、選択されてから実際に発射するまで少し待つ。
                _cooldownTimer -= Time.deltaTime;
                if (_cooldownTimer <= 0f)
                {
                    if (_pendingIsRadial) FireRadial();
                    else FireHoming();
                }
                break;
        }
    }

    private void UpdateRetreating()
    {
        // 画面外にいるときは移動/発射しない。
        if (_sr != null && !_sr.isVisible) return;

        float dx = transform.position.x - _player.position.x;
        _dir = dx < 0f ? -1 : 1; // プレイヤーと反対方向へ

        Vector3 p = transform.position;
        p.x += _dir * moveSpeed * Time.deltaTime;
        transform.position = p;
        if (_sr != null) _sr.flipX = _dir < 0;

        // プレイヤーから十分離れていれば、次の攻撃を行う。そうでなければ、このフレームにおける行動を終了する
        float distance = Mathf.Abs(transform.position.x - _player.position.x);
        if (distance < retreatDistance) return;

        // 放射状に発射/追尾弾を発射のどちらかの攻撃を、radialChance の確率で抽選する。
        // ただしステージ開始後の一番最初の行動だけは、選択してすぐには実行せず firstActionDelay 秒待つ。
        bool isRadial = Random.value < radialChance;
        if (!_firstActionDone)
        {
            _firstActionDone = true;
            _pendingIsRadial = isRadial;
            _cooldownTimer = firstActionDelay;
            _state = State.FirstActionWait;
            return;
        }

        if (isRadial) FireRadial();
        else FireHoming();
    }

    private void FireRadial()
    {
        if (bulletPrefab != null)
        {
            for (int i = 0; i < radialBulletCount; i++)
            {
                float angle = i * (360f / radialBulletCount) * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var bullet = Instantiate(bulletPrefab, transform.position, Quaternion.identity);
                bullet.Configure(dir, bulletSpeed, 1);
            }
        }

        _cooldownTimer = radialCooldown;
        _state = State.CoolingDown;
    }

    private void FireHoming()
    {
        if (bulletPrefab != null)
        {
            Vector2 dir = ((Vector2)_player.position - (Vector2)transform.position).normalized;
            _pendingHomingBullet = Instantiate(bulletPrefab, transform.position, Quaternion.identity);
            _pendingHomingBullet.Configure(dir, bulletSpeed, 1, homingTurnSpeed);
        }

        _state = State.WaitingForHomingBullet;
    }
}
