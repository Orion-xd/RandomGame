using UnityEngine;

/// <summary>
/// Enemy3(ミニボス/ラスボス)の行動AI。
/// ①離脱移動 → (プレイヤーから十分離れたら)行動を抽選 → クールタイム → ①…の繰り返し。
/// 接触ダメージ・被ダメージ・ダッシュ中すり抜けなどは Enemy コンポーネント側が担当する
/// (このスクリプトは移動と発射のみを担当し、Enemy と併用する)。
///
/// ── 行動選択（重み付き抽選） ──
/// 放射弾・追尾弾・突進攻撃の3種類を <see cref="actions"/> に登録し、それぞれの weight（重み）で
/// 抽選する（<see cref="ChooseAction"/>）。重みの合計が1や100である必要はなく、相対的な比率だけが
/// 意味を持つ。突進攻撃のように edgeOnly を立てた行動は、画面端にいるとき（後述）だけ抽選対象に入る
/// （画面端にいるからといって必ず選ばれるわけではなく、他の行動と同じ抽選プールに加わるだけ）。
///
/// ── 画面端（スクロール無しの1画面ステージ用） ──
/// カメラがスクロールしないステージ（例: ラスボス戦）では、離脱移動で画面端まで来てしまうと
/// retreatDistance を満たせないままそれ以上下がれなくなることがある。これを避けるため、離脱移動中に
/// 画面端から edgeMargin 以内まで来てしまったら、retreatDistance 未達でも離脱を諦めて行動選択へ移る
/// （<see cref="IsNearScreenEdge"/>、<see cref="ScreenBounds2D"/>参照）。画面端脱出用の行動として
/// 突進攻撃（プレイヤーへ向かって突進する。結果的に画面端から離れる方向になる）を用意している。
///
/// ── アニメーション（任意） ──
/// 他の雑魚敵と違い、このボスはアニメーションさせる想定。<see cref="animator"/> を設定し、
/// 各行動（<see cref="WeightedAction.animationTrigger"/>）にトリガー名を指定すると、その行動を
/// 実行する瞬間に SetTrigger する。未設定（空文字）ならアニメーションなしでこれまで通り即座に発動する。
///
/// ── 向き ──
/// 常にプレイヤーのいる方向を向く（<see cref="FacingController"/>）。ただし突進攻撃中だけは
/// 向きを変えられない（突進の勢いの途中で向きが変わると不自然なため）。突進中に向きを変えたく
/// なった場合はその希望だけ覚えておき、突進が終わってもまだその希望が残っていれば、そこで向きを
/// 変える（FacingController の Lock/Unlock）。firePoint は facing.mirroredChildren に登録して
/// おくことで、向きに応じてローカルX座標が自動で反転され、常に正しい側から発射される。
/// </summary>
public class Enemy3AI : MonoBehaviour
{
    public enum AttackAction { Radial, Homing, DashAttack }

    [System.Serializable]
    public class WeightedAction
    {
        public AttackAction type;
        [Tooltip("抽選の重み。大きいほど選ばれやすい（相対比率）")]
        public float weight = 1f;
        [Tooltip("true にすると、画面端にいるとき（IsNearScreenEdge）だけ抽選対象に入る（例: 突進攻撃）")]
        public bool edgeOnly = false;
        [Tooltip("この行動を実行する瞬間に発火させる Animator のトリガー名（任意。空ならアニメーションなし）")]
        public string animationTrigger;
    }

    private enum State { Retreating, CoolingDown, WaitingForHomingBullet, FirstActionWait, DashingAttack }

    [Header("①離脱移動")]
    [Tooltip("プレイヤーから遠ざかる速度")]
    [SerializeField] private float moveSpeed = 2f;
    [Tooltip("プレイヤーとの距離がこれ以上になったら離脱移動を終了する")]
    [SerializeField] private float retreatDistance = 5f;

    [Header("画面端（スクロール無しの1画面ステージ用）")]
    [Tooltip("画面の左右端からこの距離以内まで来たら「画面端にいる」とみなす。" +
             "離脱移動中にこの状態になると、retreatDistance未達でも離脱を諦めて行動選択へ移る")]
    [SerializeField] private float edgeMargin = 2f;

    [Header("初回行動の遅延")]
    [Tooltip("ステージ開始後、一番最初の行動だけ、選択されてから" +
             "実際に発動するまでこの秒数だけ待つ。画面に映った瞬間にいきなり攻撃されると" +
             "難しすぎるための救済(2回目以降の行動には適用しない)")]
    [SerializeField] private float firstActionDelay = 3f;

    [Header("アニメーション（任意）")]
    [Tooltip("未設定なら自分自身から自動取得。他の雑魚敵と違いこのボスはアニメーションさせる想定")]
    [SerializeField] private Animator animator;

    [Header("向き")]
    [SerializeField] private FacingController facing;
    [Tooltip("弾の発射位置（銃口）。未設定ならこの GameObject の位置から発射する。" +
             "向きが変わったときにローカルX座標が自動で反転されるよう、facing.mirroredChildren にも登録すること")]
    [SerializeField] private Transform firePoint;

    [Header("行動抽選")]
    [Tooltip("放射弾・追尾弾・突進攻撃、それぞれの重みと画面端限定かどうかをここで設定する")]
    [SerializeField] private WeightedAction[] actions =
    {
        new WeightedAction { type = AttackAction.Radial, weight = 1f, edgeOnly = false },
        new WeightedAction { type = AttackAction.Homing, weight = 1f, edgeOnly = false },
        new WeightedAction { type = AttackAction.DashAttack, weight = 1f, edgeOnly = true },
    };

    [Header("弾")]
    [SerializeField] private Bullet bulletPrefab;
    [Tooltip("弾の飛行速度(放射弾・追尾弾共通)")]
    [SerializeField] private float bulletSpeed = 5f;

    [Header("放射弾")]
    [Tooltip("放射状に同時発射する弾の数")]
    [SerializeField] private int radialBulletCount = 8;
    [Tooltip("発動後のクールタイム(秒)")]
    [SerializeField] private float radialCooldown = 3f;

    [Header("追尾弾")]
    [Tooltip("秒速あたりの旋回角度(度)。大きいほど正確に追尾する=ホーミングの強度")]
    [SerializeField] private float homingTurnSpeed = 90f;
    [Tooltip("発動後(弾が消えてから)のクールタイム(秒)")]
    [SerializeField] private float homingCooldown = 3f;

    [Header("突進攻撃（画面端にいるときだけ抽選対象になりうる）")]
    [Tooltip("突進の速さ")]
    [SerializeField] private float dashAttackSpeed = 10f;
    [Tooltip("突進の継続時間(秒)")]
    [SerializeField] private float dashAttackDuration = 0.6f;
    [Tooltip("突進後のクールタイム(秒)")]
    [SerializeField] private float dashAttackCooldown = 3f;

    private State _state = State.Retreating;
    private float _cooldownTimer;
    private Bullet _pendingHomingBullet;
    private Transform _player;
    private Transform _playerHomingTarget;
    private SpriteRenderer _sr;
    private Enemy _enemy;
    private int _dir = 1;
    private bool _firstActionDone;
    private WeightedAction _pendingAction;
    private float _dashDir;

    private void Awake()
    {
        _sr = GetComponent<SpriteRenderer>();
        _enemy = GetComponent<Enemy>();
        if (animator == null) animator = GetComponent<Animator>();
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
        if (p != null)
        {
            _player = p.transform;
            // 追尾弾の発射時の初期方向も、専用の目印（HomingTarget、心臓のあたりに置く想定）を狙う。
            // 無ければ今まで通りプレイヤー本体を狙う（フォールバック）。
            var homingTargetTf = p.transform.Find("HomingTarget");
            _playerHomingTarget = homingTargetTf != null ? homingTargetTf : _player;
        }
    }

    /// <summary>プレイヤーの攻撃を受けた瞬間に呼ばれる。追尾弾を発射中なら、問答無用でその弾を消してクールタイムへ移行する。</summary>
    private void HandleDamaged()
    {
        if (_state != State.WaitingForHomingBullet) return;

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

        UpdateFacing();

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
                // 一番最初の行動だけ、選択されてから実際に発動するまで少し待つ。
                _cooldownTimer -= Time.deltaTime;
                if (_cooldownTimer <= 0f) ExecuteAction(_pendingAction);
                break;

            case State.DashingAttack:
                UpdateDashAttack();
                break;
        }
    }

    /// <summary>常にプレイヤーのいる方向を向く（突進攻撃中はロックされるため、実際には
    /// 突進が終わるまで反映が保留される。FacingController 参照）。</summary>
    private void UpdateFacing()
    {
        float dx = _player.position.x - transform.position.x;
        if (Mathf.Abs(dx) < 0.01f) return; // ほぼ真上/真下などで左右不定のときは向きを変えない
        facing.SetDesiredSign(dx < 0f ? -1 : 1);
    }

    private void UpdateRetreating()
    {
        // 画面外にいるときは移動/発射しない。
        if (_sr != null && !_sr.isVisible) return;

        float dx = transform.position.x - _player.position.x;
        _dir = dx < 0f ? -1 : 1; // プレイヤーと反対方向へ（移動方向。向きはプレイヤー側を向いたままなので別）

        Vector3 p = transform.position;
        p.x += _dir * moveSpeed * Time.deltaTime;
        transform.position = p;
        ClampToScreenX();

        bool atEdge = IsNearScreenEdge();
        float distance = Mathf.Abs(transform.position.x - _player.position.x);
        bool retreatedEnough = distance >= retreatDistance;

        // 通常は十分離れるまで離脱を続ける。ただし、スクロール無しのステージで画面端まで来てしまうと
        // それ以上下がれず retreatDistance を一生満たせないことがあるため、そのときは離脱を諦めて
        // 行動選択へ移る（画面端にいる状態は行動抽選プール自体にも反映される。ChooseAction 参照）。
        if (!retreatedEnough && !atEdge) return;

        StartActionSelection(atEdge);
    }

    /// <summary>行動を抽選し、初回だけ firstActionDelay を挟んでから実行する。</summary>
    private void StartActionSelection(bool atEdge)
    {
        var chosen = ChooseAction(atEdge);
        if (chosen == null)
        {
            // actions が未設定など、抽選できるものが無い場合の保険。少し待って再挑戦する。
            _cooldownTimer = radialCooldown;
            _state = State.CoolingDown;
            return;
        }

        if (!_firstActionDone)
        {
            _firstActionDone = true;
            _pendingAction = chosen;
            _cooldownTimer = firstActionDelay;
            _state = State.FirstActionWait;
            return;
        }

        ExecuteAction(chosen);
    }

    /// <summary>重み付き抽選。edgeOnly の行動は atEdge が true のときだけ対象に入る。</summary>
    private WeightedAction ChooseAction(bool atEdge)
    {
        if (actions == null || actions.Length == 0) return null;

        float total = 0f;
        foreach (var a in actions)
        {
            if (a == null) continue;
            if (a.edgeOnly && !atEdge) continue;
            total += Mathf.Max(0f, a.weight);
        }
        if (total <= 0f) return null;

        float r = Random.value * total;
        foreach (var a in actions)
        {
            if (a == null) continue;
            if (a.edgeOnly && !atEdge) continue;
            float w = Mathf.Max(0f, a.weight);
            if (r < w) return a;
            r -= w;
        }
        return actions[actions.Length - 1]; // 浮動小数の誤差で漏れた場合の保険
    }

    private void ExecuteAction(WeightedAction action)
    {
        if (animator != null && !string.IsNullOrEmpty(action.animationTrigger))
            animator.SetTrigger(action.animationTrigger);

        switch (action.type)
        {
            case AttackAction.Radial: FireRadial(); break;
            case AttackAction.Homing: FireHoming(); break;
            case AttackAction.DashAttack: StartDashAttack(); break;
        }
    }

    private void FireRadial()
    {
        if (bulletPrefab != null)
        {
            for (int i = 0; i < radialBulletCount; i++)
            {
                float angle = i * (360f / radialBulletCount) * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var bullet = Instantiate(bulletPrefab, FirePointPosition, Quaternion.identity);
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
            Vector2 dir = ((Vector2)_playerHomingTarget.position - (Vector2)transform.position).normalized;
            _pendingHomingBullet = Instantiate(bulletPrefab, FirePointPosition, Quaternion.identity);
            _pendingHomingBullet.Configure(dir, bulletSpeed, 1, homingTurnSpeed);
        }

        _state = State.WaitingForHomingBullet;
    }

    private Vector3 FirePointPosition => firePoint != null ? firePoint.position : transform.position;

    /// <summary>突進攻撃。プレイヤーへ向かって高速移動する（結果的に画面端から離れる方向になるので、
    /// 離脱を諦めて画面端に張り付いた状態から抜け出す手段になる）。接触ダメージは Enemy 側の
    /// 通常の接触判定（OnCollisionEnter2D）がそのまま処理するので、ここでは移動だけを行う。
    /// 突進の勢いの途中で向きが変わると不自然なので、突進中は向きをロックする（FacingController）。</summary>
    private void StartDashAttack()
    {
        _dashDir = Mathf.Sign(_player.position.x - transform.position.x);
        if (_dashDir == 0f) _dashDir = 1f;
        facing.LockTo(_dashDir < 0f ? -1 : 1);

        _cooldownTimer = dashAttackDuration;
        _state = State.DashingAttack;
    }

    private void UpdateDashAttack()
    {
        Vector3 p = transform.position;
        p.x += _dashDir * dashAttackSpeed * Time.deltaTime;
        transform.position = p;
        ClampToScreenX();

        _cooldownTimer -= Time.deltaTime;
        if (_cooldownTimer <= 0f)
        {
            facing.Unlock(); // 突進中に向きを変えたい希望があれば、ここで初めて反映される
            _cooldownTimer = dashAttackCooldown;
            _state = State.CoolingDown;
        }
    }

    /// <summary>画面の左右端から edgeMargin 以内にいるか（スクロール無しの1画面ステージ前提）。</summary>
    private bool IsNearScreenEdge()
    {
        if (!ScreenBounds2D.TryGetWorldXRange(Camera.main, out float left, out float right)) return false;
        float x = transform.position.x;
        return (x - left) <= edgeMargin || (right - x) <= edgeMargin;
    }

    /// <summary>画面外へ出てしまわないよう、念のため位置を画面内へ収める
    /// （このボスに Rigidbody2D が無く、壁との物理衝突だけでは止まらないための保険）。</summary>
    private void ClampToScreenX()
    {
        if (!ScreenBounds2D.TryGetWorldXRange(Camera.main, out float left, out float right)) return;
        var p = transform.position;
        p.x = Mathf.Clamp(p.x, left, right);
        transform.position = p;
    }
}
