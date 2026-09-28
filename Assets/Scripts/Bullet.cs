using UnityEngine;

/// <summary>
/// 敵が発射する弾。発射時に設定した方向へ直線的に飛び続ける（ホーミングは発射時の一度きり）。
/// ただし <see cref="homingTurnSpeed"/> が 0 より大きいときは、飛行中も継続してプレイヤーへ
/// 向きを補正し続ける「追尾弾」になる（Enemy3の③用。Enemy2はこれを使わず発射時一度きりのまま）。
/// 当たり判定はトリガー（プレイヤーを物理的に押し返す必要が無いため。接触時の処理は全てスクリプト側で行う）。
/// Enemy と同じ接触仕様：プレイヤー本体に触れるとダメージ、ダッシュ中はすり抜け、
/// プレイヤーの攻撃（AttackHitbox）に触れると一撃で消滅。地面に当たっても消滅するが、
/// 高台（一方通行、<see cref="OneWayPlatform"/>が付いているもの）には反応せず、
/// 進行方向に関わらず必ずすり抜ける（Enemy2/Enemy3共通の仕様。高台を配置すると
/// 弾が意図せず高台に吸われて消えてしまう問題への対策）。
/// 敵キャラに触れると enemyDamage を与えて消滅する（selfHitGraceTime の間だけは発射元自身と
/// 重なっているため無視する）。追尾弾をラスボスへ誘導してヒットさせる攻略に対応するための仕様。
///
/// 【基本の追尾】
/// 毎フレーム、プレイヤーの実際の位置へ向けて、最大 homingTurnSpeed 度/秒で向きを回転補正する。
/// あまり強くしすぎなければ、ジャンプやダッシュで簡単にかわせる程度の強さになる。
///
/// 【ジャンプ中はホーミング解除＋地面回避アシスト】
/// プレイヤーがジャンプ中（MainActionController.IsJumpCooldownActive）の間は、向きの補正を
/// 一切行わない（そのときの速度ベクトルのまま直進する）。ただしその間、進行方向の少し先に
/// 地面（高台を除く）を検知したら、地面に対してほぼ垂直に近い急角度で向かっている場合を除き
/// （そういうときまで無理に逸らすと不自然なため）、groundAvoidTurnSpeed でゆっくり向きを
/// 逸らして地面への着弾を避ける（プレイヤーへの追尾ではないので凍結の趣旨とは矛盾しない）。
///
/// 【反転（すれ違い）について】
/// 追尾中にダッシュで弾をすり抜けられる、またはジャンプで弾の反対側へ回り込まれると、
/// 「向かうべき方向」がほぼ真逆になる。これを通常の回転で処理すると、目標方向が毎フレーム
/// 僅かに動くたびに回転の向き（時計回り/反時計回り）自体が入れ替わってしまい、挙動が
/// 不安定になる問題があった。そこで、すれ違いを検出したときは回転を使わず、代わりに
/// 「速度ベクトルの大きさを直線的に減速→0→反対向きへ加速」させて向きを変える（反転モード）。
/// 減速・加速は常に元の進行方向の延長線上で起こるため、横方向（地面や壁の方向）を
/// 向いてしまうことがない。すれ違いの検出は2種類：
///  (a) ダッシュ中に弾と重なり始め、重なりが解消した瞬間にもまだ同じダッシュが継続中
///      （チュートリアルのDashPastEnemyと同じ、MainActionController.DashId による判定）
///  (b) ジャンプ離陸前とジャンプ着地後で、弾から見たプレイヤーの向き（進行方向に対して
///      前か後ろか）が反転している（＝ジャンプで弾の反対側へ回り込まれた）
///
/// 【上方向バイアス】
/// 弾がプレイヤーより高い位置へ行こうとすると誘導しにくくなるため、上昇角度（水平からの
/// 傾き）を、プレイヤーが弾より高い状態が続いている時間に応じて指数関数的なイーズインで
/// 緩和する（maxUpwardAngle が upwardRampTime 秒かけて到達する上限。最初はゆっくり、
/// 続けば続くほど加速して上限まで届く）。下方向・左右方向は特別な制限をせず、通常の
/// 回転補正だけで変化する。
///
/// 【ボスへ向かっているときはホーミング・反転を弱める】
/// 弾の現在の進行方向の延長線上（towardBossAngleThreshold度以内）にラスボスが「いるっぽい」
/// ときは、ホーミングの回転速度・反転の速度の両方を、ボスとの距離に応じて連続的に弱める
/// （距離が近いほど弱くなり、bossWeakenDistance以内でボスに極めて近いときはほぼ停止する）。
/// 誘導してボスにぶつける攻略のための仕様。
///
/// 【地面回避のための回転方向選択（通常の追尾補正のみ）】
/// 通常の毎フレームの回転補正（反転モード中を除く）で、弾を中心とする groundProximityRadius
/// の円内に地面（高台を除く）があるときは、回転方向（時計回り/反時計回り）を、地面へ向かう
/// 側を通らない方向になるよう選ぶ。地面を避けるためであれば、遠回りな回転方向になってもよい。
/// 反転モード中は回転を使わない（速度ベクトルの大きさの変化だけなので、この選択は不要）。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class Bullet : MonoBehaviour
{
    [SerializeField] private float speed = 8f;
    [SerializeField] private int damage = 1;
    [Tooltip("この時間が経過すると何にも当たらなくても自動的に消える（画面外に飛び続けるのを防ぐ）。" +
             "追尾弾（homingTurnSpeed > 0）には適用しない＝命中/地面接触/攻撃で消えるまで永久に飛び続ける")]
    [SerializeField] private float maxLifetime = 6f;
    [Tooltip("これに触れると弾が消滅する（地面など）。ただし同じレイヤーでも OneWayPlatform が" +
             "付いているもの（高台）には反応しない（必ずすり抜ける）")]
    [SerializeField] private LayerMask groundLayer;
    [Tooltip("秒速あたりの旋回角度（度）。0なら発射時の方向のまま直進（既定・Enemy2用）。" +
             "0より大きいと飛行中もプレイヤーへ継続して向きを補正し続ける追尾弾になる（Enemy3用）。" +
             "ジャンプやダッシュで簡単にかわせる程度の、ゆるめの値を想定")]
    [SerializeField] private float homingTurnSpeed = 0f;
    [Tooltip("【実験中の仕様。元に戻す可能性あり】trueにすると、プレイヤーがジャンプ中の間は" +
             "追尾の向き・狙い位置の更新を一切止める（着地するかジャンプが終わるまでの間だけ追尾を止める）")]
    [SerializeField] private bool freezeHomingWhilePlayerJumping = true;
    [Tooltip("ジャンプ中のホーミング凍結中だけ働く地面回避アシストの検知距離。" +
             "進行方向のこの距離以内に地面（高台を除く）があると回避を始める")]
    [SerializeField] private float groundAvoidLookahead = 1.5f;
    [Tooltip("ジャンプ中の地面回避アシストが働いているときの旋回速度（度/秒）")]
    [SerializeField] private float groundAvoidTurnSpeed = 180f;
    [Tooltip("ジャンプ中の地面回避アシストが働く最低角度（度）。進行方向と地面の法線のなす角がこれ" +
             "以上（＝かすめる程度の浅い角度）のときだけ回避する。これ未満（＝ほぼ垂直に近い急角度で" +
             "向かっている）のときは、無理に逸らさず素直に地面へ着弾させる")]
    [SerializeField] private float groundAvoidMinGrazeAngle = 55f;
    [Tooltip("反転（すれ違い判定）が働く、弾を中心とした検知半径。ダッシュのすり抜けはコライダーの" +
             "重なりで直接判定するのでこの半径は使わないが、ジャンプでの回り込み判定では、離陸時・" +
             "着地時ともにプレイヤーがこの距離以内にいた場合だけ「すれ違った」とみなす" +
             "（無関係な遠くでのジャンプで誤反応しないようにするため）")]
    [SerializeField] private float jumpPassMaxDistance = 3f;
    [Tooltip("反転（減速→反対向きへの加速）にかかる速さの倍率。既定の1なら" +
             "「homingTurnSpeedで180度回転するのと同じ時間」で反転が完了する。大きいほど反転が速く鋭くなる")]
    [SerializeField] private float reversalRateMultiplier = 1f;
    [Tooltip("上昇角度（水平からの傾き）の漸近的な上限（度）。プレイヤーが弾より高い状態が続くほど、" +
             "この角度まで指数関数的に緩和されていく（下方向・左右方向には制限を設けない）")]
    [SerializeField] private float maxUpwardAngle = 60f;
    [Tooltip("上昇角度が上限（maxUpwardAngle）まで完全に緩和されるのにかかる時間（秒）。" +
             "指数関数的なイーズインで変化する＝最初はゆっくり、続くほど加速して上限に近づく")]
    [SerializeField] private float upwardRampTime = 1.2f;
    [Tooltip("弾の進行方向とラスボスへ向かう方向のなす角がこれ（度）以内なら「ボスへ向かっている」と" +
             "みなし、ホーミング・反転の速度を弱める（誘導してボスに当てる攻略のため）")]
    [SerializeField] private float towardBossAngleThreshold = 30f;
    [Tooltip("ボスへ向かっているとき、弱める強さの基準距離。この距離以内でボスに近いほど、" +
             "ホーミング・反転の速度を強く弱める（距離0でほぼ完全停止）")]
    [SerializeField] private float bossWeakenDistance = 6f;
    [Tooltip("通常の追尾補正（反転モード中を除く）で、地面を避ける回転方向を選ぶ判定に使う、" +
             "弾を中心とした検知半径")]
    [SerializeField] private float groundProximityRadius = 2f;
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
    private float _aboveTimer;
    private bool _trackingDashPass;
    private int _dashPassDashId;
    private bool _jumpWasActive;
    private float _prevJumpSideSign;
    private float _prevJumpSideDist;
    private Enemy3AI _boss;
    private Collider2D _col;
    private Collider2D _playerCollider;
    private Transform _playerHomingTarget;
    private MainActionController _playerMainAction;
    private bool _ignoringPlayer;

    /// <summary>プレイヤーの攻撃で弾が破壊された瞬間に発火（壊れた位置を渡す。誰が撃ったかは問わない）。
    /// チュートリアル「Attack Enemy's Bullet」の達成判定用（TutorialHint がこれを購読し、その位置が
    /// 自分のヒントゾーンの範囲内かどうかで判定する）。</summary>
    public static event System.Action<Vector2> OnDestroyedByAttack;

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
            _boss = FindAnyObjectByType<Enemy3AI>(); // シーンに1体だけの想定。いなければ null のまま
            // 追尾のターゲットは当たり判定のTransformそのものではなく、専用の目印（HomingTarget、
            // 心臓のあたりに置く想定）を狙う。無ければ今まで通りプレイヤー本体を狙う（フォールバック）。
            var homingTargetTf = p.transform.Find("HomingTarget");
            _playerHomingTarget = homingTargetTf != null ? homingTargetTf : p.transform;
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

        if (homingTurnSpeed > 0f && _playerHomingTarget != null)
        {
            UpdateHoming();
        }

        transform.position += (Vector3)(_velocity * Time.deltaTime);
    }

    private void UpdateHoming()
    {
        bool playerJumping = freezeHomingWhilePlayerJumping && _playerMainAction != null && _playerMainAction.IsJumpCooldownActive;

        // ジャンプでの回り込み検出は、凍結の有無に関わらず離陸/着地のエッジで判定する必要があるため先に行う。
        UpdateJumpPassDetection(playerJumping);

        if (playerJumping)
        {
            ApplyGroundAvoidance();
            return;
        }

        UpdateDashPassDetection();

        Vector2 pos = transform.position;
        Vector2 currentDir = _velocity.sqrMagnitude > 0.0001f ? _velocity.normalized : Vector2.right;

        Vector2 realTargetPos = _playerHomingTarget.position;
        Vector2 toReal = realTargetPos - pos;
        if (toReal.sqrMagnitude <= 0.0001f) return;
        Vector2 desired = ApplyUpwardBias(toReal.normalized);

        float bossWeaken = ComputeBossWeaken(pos, currentDir);

        if (_reversing)
        {
            // 回転ではなく、速度ベクトルの大きさを直線的に減速→0→反対向きへ加速させて向きを変える。
            Vector2 targetVelocity = desired * speed;
            float baseRate = (2f * speed * homingTurnSpeed) / 180f; // homingTurnSpeedで180度回転するのと同じ時間で反転する速さ
            float rate = baseRate * reversalRateMultiplier * (1f - bossWeaken);
            _velocity = Vector2.MoveTowards(_velocity, targetVelocity, rate * Time.deltaTime);
            if (_velocity == targetVelocity) _reversing = false;
            return;
        }

        float normalRate = homingTurnSpeed * (1f - bossWeaken);
        float sign = ChooseRotationSign(currentDir, desired, pos);
        Vector2 newDir = RotateTowardSigned(currentDir, desired, normalRate * Time.deltaTime, sign, out _);
        _velocity = newDir * speed;
    }

    /// <summary>ダッシュ中に弾と重なり始め、重なりが解消した瞬間にもまだ同じダッシュが継続中なら、
    /// 「ダッシュですり抜けられた」とみなして反転モードに入る（チュートリアルのDashPastEnemy判定と同じ
    /// 方式。IgnoreCollisionで衝突を無視しているため IsTouching では検出できず、幾何学的な距離判定を使う）。</summary>
    private void UpdateDashPassDetection()
    {
        if (_trackingDashPass)
        {
            bool stillOverlapping = _playerCollider != null && _playerCollider.Distance(_col).isOverlapped;
            if (!stillOverlapping)
            {
                bool stillSameDash = _playerMainAction != null
                    && _playerMainAction.IsDashing
                    && _playerMainAction.DashId == _dashPassDashId;
                _trackingDashPass = false;
                if (stillSameDash) _reversing = true;
            }
            return;
        }

        if (_playerMainAction == null || !_playerMainAction.IsDashing || _playerCollider == null) return;
        if (!_playerCollider.Distance(_col).isOverlapped) return;

        _trackingDashPass = true;
        _dashPassDashId = _playerMainAction.DashId;
    }

    /// <summary>ジャンプの離陸前と着地後で、弾から見たプレイヤーの向き（弾の進行方向に対して前か後ろか）
    /// が反転していたら、「ジャンプで弾の反対側へ回り込まれた」とみなして反転モードに入る。
    /// 離陸時・着地時ともにプレイヤーが jumpPassMaxDistance 以内にいた場合だけ判定する
    /// （弾と無関係な、遠くでのジャンプに反応しないようにするため）。</summary>
    private void UpdateJumpPassDetection(bool playerJumping)
    {
        if (playerJumping && !_jumpWasActive)
        {
            _prevJumpSideSign = ComputeSideSign(out _prevJumpSideDist);
        }
        else if (!playerJumping && _jumpWasActive)
        {
            float newSign = ComputeSideSign(out float newDist);
            bool bothClose = _prevJumpSideDist <= jumpPassMaxDistance && newDist <= jumpPassMaxDistance;
            if (bothClose && _prevJumpSideSign != 0f && newSign != 0f && Mathf.Sign(_prevJumpSideSign) != Mathf.Sign(newSign))
            {
                _reversing = true;
            }
        }
        _jumpWasActive = playerJumping;
    }

    /// <summary>弾の進行方向を基準に、プレイヤーが「前」か「後ろ」かを符号で返す（正なら前、負なら後ろ）。
    /// distance にはプレイヤーまでの距離も返す。</summary>
    private float ComputeSideSign(out float distance)
    {
        distance = float.MaxValue;
        if (_playerHomingTarget == null) return 0f;
        Vector2 currentDir = _velocity.sqrMagnitude > 0.0001f ? _velocity.normalized : Vector2.right;
        Vector2 toPlayer = (Vector2)_playerHomingTarget.position - (Vector2)transform.position;
        distance = toPlayer.magnitude;
        if (distance < 0.0001f) return 0f;
        return Vector2.Dot(currentDir, toPlayer / distance);
    }

    /// <summary>上昇角度（水平からの傾き）を、プレイヤーが弾より高い状態が続いている時間に応じて
    /// 指数関数的に緩和しながら制限する（最初はゆっくり、続くほど加速して上限に近づく）。
    /// 下方向・左右方向はそのまま返す（制限なし）。</summary>
    private Vector2 ApplyUpwardBias(Vector2 desired)
    {
        bool above = desired.y > 0f;
        _aboveTimer = above ? _aboveTimer + Time.deltaTime : 0f;
        if (!above) return desired;

        // e^x 型のイーズイン：t=0付近では傾きが緩やかで、upwardRampTimeに近づくにつれ一気に
        // 上限（maxUpwardAngle）まで加速する。UpwardRampSteepnessが大きいほどその傾向が強まる。
        float t = Mathf.Clamp01(_aboveTimer / Mathf.Max(0.01f, upwardRampTime));
        float eased = (Mathf.Exp(UpwardRampSteepness * t) - 1f) / (Mathf.Exp(UpwardRampSteepness) - 1f);
        float capAngle = maxUpwardAngle * eased;

        float maxSin = Mathf.Sin(capAngle * Mathf.Deg2Rad);
        if (desired.y <= maxSin) return desired;

        // 左右どちらへ傾けるかは、今の速度の向き（_velocity.x）を優先して決める。プレイヤーがほぼ
        // 真上にいるとき、狙いの左右のごく僅かな符号差（ノイズ同然）で決めると激しく反転するため。
        float xSign = Mathf.Abs(_velocity.x) > 0.0001f ? Mathf.Sign(_velocity.x)
                    : (Mathf.Abs(desired.x) > 0.0001f ? Mathf.Sign(desired.x) : 1f);
        float maxCos = Mathf.Cos(capAngle * Mathf.Deg2Rad);
        return new Vector2(xSign * maxCos, maxSin);
    }

    private const float UpwardRampSteepness = 4f;

    /// <summary>ボスへ向かっているとき、ホーミング・反転の速度を弱める倍率（0=弱めない、1=ほぼ完全停止）
    /// を返す。ボスが進行方向の先にいないときは0。</summary>
    private float ComputeBossWeaken(Vector2 pos, Vector2 currentDir)
    {
        if (_boss == null || _velocity.sqrMagnitude < 0.0001f || bossWeakenDistance <= 0f) return 0f;
        Vector2 toBoss = (Vector2)_boss.transform.position - pos;
        float dist = toBoss.magnitude;
        if (dist < 0.0001f) return 1f;
        if (Vector2.Angle(currentDir, toBoss / dist) > towardBossAngleThreshold) return 0f;
        return Mathf.Clamp01(1f - dist / bossWeakenDistance);
    }

    /// <summary>通常の追尾補正（反転モード中を除く）で使う回転方向（+1=反時計回り、-1=時計回り）を選ぶ。
    /// 弾の近くに地面があるときは、地面へ向かう側を通らない方向を優先する（遠回りになってもよい）。</summary>
    private float ChooseRotationSign(Vector2 currentDir, Vector2 desired, Vector2 pos)
    {
        float signedTotal = Vector2.SignedAngle(currentDir, desired);
        float defaultSign = signedTotal >= 0f ? 1f : -1f;

        if (TryGetNearbyGroundDirection(pos, out Vector2 towardGround))
        {
            float angleToGround = Vector2.SignedAngle(currentDir, towardGround);
            bool dangerous = defaultSign > 0f
                ? (angleToGround >= 0f && angleToGround <= signedTotal)
                : (angleToGround <= 0f && angleToGround >= signedTotal);
            if (dangerous) return -defaultSign;
        }
        return defaultSign;
    }

    /// <summary>弾を中心とした groundProximityRadius の円内に、最も近い地面（高台を除く）があれば、
    /// そこへ向かう方向を返す。</summary>
    private bool TryGetNearbyGroundDirection(Vector2 pos, out Vector2 towardGround)
    {
        towardGround = default;
        var hits = Physics2D.OverlapCircleAll(pos, groundProximityRadius, groundLayer);
        Collider2D nearest = null;
        float nearestSqrDist = float.MaxValue;
        foreach (var h in hits)
        {
            if (!IsGround(h)) continue;
            Vector2 cp = h.ClosestPoint(pos);
            float d = (cp - pos).sqrMagnitude;
            if (d < nearestSqrDist)
            {
                nearestSqrDist = d;
                nearest = h;
            }
        }
        if (nearest == null) return false;

        Vector2 diff = nearest.ClosestPoint(pos) - pos;
        if (diff.sqrMagnitude < 0.0001f) return false;
        towardGround = diff.normalized;
        return true;
    }

    /// <summary>指定した回転方向（sign）に固定したまま、currentDir を desired へ向けて
    /// 最大 maxDegrees だけ回転させる。desired へ到達しきった場合は reached が true になる。</summary>
    private static Vector2 RotateTowardSigned(Vector2 currentDir, Vector2 desired, float maxDegrees, float sign, out bool reached)
    {
        float signedTotal = Vector2.SignedAngle(currentDir, desired);
        float remaining = sign > 0f
            ? (signedTotal >= 0f ? signedTotal : signedTotal + 360f)
            : (signedTotal <= 0f ? signedTotal : signedTotal - 360f);
        float stepMag = Mathf.Min(Mathf.Abs(remaining), maxDegrees);
        reached = stepMag >= Mathf.Abs(remaining) - 0.001f;
        return RotateVector(currentDir, sign * stepMag);
    }

    private static Vector2 RotateVector(Vector2 v, float degrees)
    {
        float rad = degrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);
        return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
    }

    /// <summary>ジャンプ中のホーミング凍結中だけ呼ばれる。進行方向の少し先に、浅い角度でかすめる
    /// 地面（高台を除く）があれば、ゆっくり向きを逸らして刺さらないようにする。地面に対してほぼ垂直に
    /// 近い急角度で向かっている場合は回避しない（見た目が不自然になるため）。</summary>
    private void ApplyGroundAvoidance()
    {
        if (_velocity.sqrMagnitude < 0.0001f) return;
        Vector2 pos = transform.position;
        Vector2 dir = _velocity.normalized;

        var hits = Physics2D.RaycastAll(pos, dir, groundAvoidLookahead, groundLayer);
        bool found = false;
        RaycastHit2D nearestHit = default;
        foreach (var hit in hits)
        {
            if (hit.collider == _col) continue;
            if (!IsGround(hit.collider)) continue;
            if (!found || hit.distance < nearestHit.distance)
            {
                nearestHit = hit;
                found = true;
            }
        }
        if (!found) return;

        Vector2 normal = nearestHit.normal;
        float incidenceAngle = Vector2.Angle(dir, -normal);
        if (incidenceAngle < groundAvoidMinGrazeAngle) return;

        Vector2 tangent = new Vector2(-normal.y, normal.x);
        if (Vector2.Dot(tangent, dir) < 0f) tangent = -tangent;
        Vector2 avoidDir = (tangent + normal * 0.5f).normalized;

        Vector2 newDir = Vector3.RotateTowards(dir, avoidDir, groundAvoidTurnSpeed * Mathf.Deg2Rad * Time.deltaTime, 0f);
        _velocity = newDir * speed;
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

    /// <summary>地面（Ground）かどうか。高台（OneWayPlatform が付いているもの）は同じレイヤーでも
    /// 除外し、必ずすり抜けさせる（Enemy2/Enemy3の弾に共通の仕様）。</summary>
    private bool IsGround(Collider2D other)
    {
        if ((groundLayer.value & (1 << other.gameObject.layer)) == 0) return false;
        if (other.GetComponent<OneWayPlatform>() != null) return false;
        return true;
    }

    /// <summary>プレイヤーの攻撃判定（AttackHitbox）から呼ばれる。一撃で消滅する。</summary>
    public void DestroyByAttack()
    {
        OnDestroyedByAttack?.Invoke(transform.position);
        Destroy(gameObject);
    }
}
