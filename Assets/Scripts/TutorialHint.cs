using UnityEngine;

/// <summary>
/// チュートリアルヒントの表示条件・達成条件を1つのオブジェクトにつき1種類だけ担当する（全面刷新）。
///
/// 共通の考え方: 対象（敵/落とし穴/高台/段差）に近づいている間だけヒントを表示し、離れれば消える。
/// ただし、そのカテゴリを一度でも「達成」すれば（<see cref="TutorialProgress"/>側で管理）、以後は
/// 二度と表示されない（個々のオブジェクト単位ではなく、カテゴリ単位でグローバルに習得したかどうかを管理する）。
///
/// 「近づいている」の判定は、自分自身についている Collider2D（トリガー）とプレイヤーの重なりで行う。
/// 落とし穴/高台/段差用は、それらを覆う Collider を持つ専用のマーカーオブジェクトとして配置する。
///
/// DashPastEnemy/AttackEnemy/AttackEnemyBullet は特定のオブジェクト（どの敵か、どの弾か）には一切
/// 紐付けない単体配置。表示用と同じ Collider（_proximityCol）がそのまま「この範囲内で正しく
/// すり抜け/撃破/弾破壊ができたか」の判定エリアを兼ねる（他の HintZone と同じ「範囲内で表示、
/// 範囲内で達成すれば非表示」という考え方に統一している。誰の・どれの、を問わないので配置場所は
/// 完全に自由。プランナー側で適切な範囲サイズに調整する）。
///
/// 複数のヒントが同時に「表示したい」状態になった場合は、<see cref="TutorialHintUI"/> 側でプレイヤーに
/// 一番近い（コライダー同士の距離が近い）ものだけが表示される。1オブジェクト=1ヒントの制約と合わせて、
/// レベルデザイン側でそもそも同時に複数が有効にならないよう間隔を空けることが望ましい。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class TutorialHint : MonoBehaviour
{
    [Tooltip("このヒントの種類。1オブジェクトにつき1種類だけ設定すること（複数割り当てない）")]
    [SerializeField] private TutorialHintCategory category;

    [Header("話者（任意）")]
    [Tooltip("このヒントを喋っているキャラクター。None ならナレーション扱い（アイコン・名前欄とも非表示）。" +
             "表示名・アイコンは SpeakerRegistry（Assets/Resources/SpeakerRegistry.asset）で一括管理している。" +
             "チュートリアルは基本的に勇者が話す想定のため既定値は Hero")]
    [SerializeField] private SpeakerId speaker = SpeakerId.Hero;
    [Header("内容")]
    [Tooltip("画面上部に表示するヒントテキスト")]
    [SerializeField] private string hintText;

    [Header("DashOverPit / JumpOverPit / ClimbLedge 用")]
    [Tooltip("対岸／段差の上に置く到達判定用コライダー")]
    [SerializeField] private Collider2D landingZone;

    [Header("JumpHighGround 用")]
    [Tooltip("この高台のコライダー。足元からの下向きレイキャストがこれに当たったら「上に乗れた」判定の対象にする")]
    [SerializeField] private Collider2D highGroundCollider;
    [Tooltip("足元からのレイキャスト距離")]
    [SerializeField] private float raycastDistance = 1.5f;
    [Tooltip("レイキャストの対象レイヤー（Ground）。指定しないとプレイヤー自身の当たり判定に当たってしまうため必須")]
    [SerializeField] private LayerMask groundLayer;
    [Tooltip("レイキャストで測定した地面までの距離がこれ以下なら「地面に着いている」とみなす")]
    [SerializeField] private float landedDistanceThreshold = 0.1f;

    private Collider2D _proximityCol;
    private Collider2D _playerCollider;
    private Rigidbody2D _playerRb;
    private MainActionQueue _playerQueue;
    private MainActionController _playerMainAction;
    private TutorialProgress _progress;
    private TutorialHintUI _hintUI;

    // DashPastEnemy用：「ダッシュ中に敵と接触し始めた」瞬間から「接触が解除された」瞬間までを
    // 1回の通過試行として追跡する（接触した敵と、接触し始めた時点の MainActionController.DashId）。
    private Enemy _dashPassEnemy;
    private int _dashPassDashId;

    private MainActionType RequiredAction => category switch
    {
        TutorialHintCategory.DashPastEnemy => MainActionType.Dash,
        TutorialHintCategory.DashOverPit => MainActionType.Dash,
        TutorialHintCategory.JumpOverPit => MainActionType.Jump,
        TutorialHintCategory.JumpHighGround => MainActionType.Jump,
        TutorialHintCategory.ClimbLedge => MainActionType.Jump,
        TutorialHintCategory.AttackEnemy => MainActionType.Attack,
        TutorialHintCategory.AttackEnemyBullet => MainActionType.Attack,
        _ => MainActionType.Dash,
    };

    private void Awake()
    {
        _proximityCol = GetComponent<Collider2D>();
        _proximityCol.isTrigger = true;

        _progress = FindAnyObjectByType<TutorialProgress>();
        _hintUI = FindAnyObjectByType<TutorialHintUI>();

        var p = GameObject.FindGameObjectWithTag("Player");
        if (p != null)
        {
            _playerCollider = p.GetComponent<Collider2D>();
            _playerRb = p.GetComponent<Rigidbody2D>();
            _playerQueue = p.GetComponent<MainActionQueue>();
            _playerMainAction = p.GetComponent<MainActionController>();
        }

        // AttackEnemy はイベント経由で判定する（Destroy されるより前、体力が0になった瞬間に
        // 判定を済ませる必要があるため。Update での存在チェックだと、倒れた敵自身も同時に
        // 破棄されてしまい、判定するチャンスが無いまま消えてしまう）。特定の敵には紐付けず、
        // 倒れた位置がこのヒントゾーンの範囲内かどうかだけで判定する。
        if (category == TutorialHintCategory.AttackEnemy)
            Enemy.OnAnyEnemyDied += HandleAnyEnemyDied;

        // AttackEnemyBullet も同じ理由（Bullet の Destroy より前に判定する必要がある）でイベント経由。
        // Bullet は動的に生成/破棄されるため、事前に特定のインスタンスを参照しておくことができない。
        // どの敵の弾かは問わず、壊れた位置がこのヒントゾーンの範囲内かどうかだけで判定する。
        if (category == TutorialHintCategory.AttackEnemyBullet)
            Bullet.OnDestroyedByAttack += HandleEnemyBulletDestroyedByAttack;
    }

    private void OnDestroy()
    {
        Enemy.OnAnyEnemyDied -= HandleAnyEnemyDied;
        Bullet.OnDestroyedByAttack -= HandleEnemyBulletDestroyedByAttack;
    }

    private void HandleAnyEnemyDied(Enemy enemy)
    {
        if (_proximityCol.OverlapPoint(enemy.transform.position)) _progress.MarkLearned(category);
    }

    private void HandleEnemyBulletDestroyedByAttack(Vector2 position)
    {
        if (_proximityCol.OverlapPoint(position)) _progress.MarkLearned(category);
    }

    /// <summary>DashPastEnemyの判定。単に「ダッシュ中に敵と接触した」だけでは成功にしない
    /// （すり抜けきれず、効果時間切れ後の接触ダメージのノックバックで反対側へ追い出されただけの
    /// 失敗ケースを、位置関係だけでは区別できないため）。接触した瞬間の MainActionController.DashId
    /// （ダッシュを発動するたびに増える通し番号）を覚えておき、接触が解除された瞬間にも
    /// 「まだダッシュ中」かつ「同じ DashId のまま」であって初めて「ダッシュ状態が途切れずに
    /// すり抜けられた」とみなす。DashId まで確認するのは、接触解除の瞬間にちょうど新しいダッシュを
    /// 発動し直していた場合（IsDashing 自体は true に戻ってしまう）を、同一のダッシュと区別するため。</summary>
    private void UpdateDashPastEnemy()
    {
        // 現在追跡中の敵がいれば、接触が解除されたかどうかをまず確認する。
        if (_dashPassEnemy != null)
        {
            var col = _dashPassEnemy.GetComponent<Collider2D>();
            bool stillOverlapping = col != null && _playerCollider.Distance(col).isOverlapped;
            if (!stillOverlapping)
            {
                bool stillSameDash = _playerMainAction != null
                    && _playerMainAction.IsDashing
                    && _playerMainAction.DashId == _dashPassDashId;
                if (stillSameDash) _progress.MarkLearned(category);
                _dashPassEnemy = null; // 成功・失敗いずれにせよ、この試行は解決した
            }
            return;
        }

        // 追跡中の敵がいなければ、新たに「ダッシュ中に、このヒントゾーンの範囲内で敵と接触し始めた」
        // 瞬間を検出する。ダッシュ中は Physics2D.IgnoreCollision で衝突を無視しているため
        // Collider2D.IsTouching では重なりを検出できず、幾何学的な距離判定（Distance）を使う。
        if (_playerMainAction == null || !_playerMainAction.IsDashing) return;
        if (!_proximityCol.IsTouching(_playerCollider)) return;

        var enemies = FindObjectsByType<Enemy>(FindObjectsSortMode.None);
        foreach (var e in enemies)
        {
            var col = e.GetComponent<Collider2D>();
            if (col == null || !_playerCollider.Distance(col).isOverlapped) continue;

            _dashPassEnemy = e;
            _dashPassDashId = _playerMainAction.DashId;
            break;
        }
    }

    private void Update()
    {
        if (_progress == null || _hintUI == null || _playerCollider == null) return;

        if (_progress.IsLearned(category))
        {
            _hintUI.RequestHide(this);
            return;
        }

        CheckSuccess();

        if (_progress.IsLearned(category)) // 今のフレームで達成した場合は即座に隠す
        {
            _hintUI.RequestHide(this);
            return;
        }

        bool near = _proximityCol.IsTouching(_playerCollider);
        bool actionReady = _playerQueue == null || _playerQueue.IsUnlocked(RequiredAction);

        if (near && actionReady)
            _hintUI.RequestShow(this, hintText, _proximityCol.Distance(_playerCollider).distance, speaker);
        else
            _hintUI.RequestHide(this);
    }

    private void CheckSuccess()
    {
        switch (category)
        {
            case TutorialHintCategory.DashPastEnemy:
                UpdateDashPastEnemy();
                break;

            case TutorialHintCategory.DashOverPit:
            case TutorialHintCategory.JumpOverPit:
            case TutorialHintCategory.ClimbLedge:
                if (landingZone != null && landingZone.IsTouching(_playerCollider))
                    _progress.MarkLearned(category);
                break;

            case TutorialHintCategory.JumpHighGround:
                if (highGroundCollider != null && _playerRb != null)
                {
                    // 足元ぎりぎりから始めるとプレイヤー自身の Collider に当たってしまうため、
                    // レイキャストは groundLayer だけを対象にする（PlayerController の接地判定と同じ考え方）。
                    var origin = new Vector2(_playerCollider.bounds.center.x, _playerCollider.bounds.min.y);
                    var hit = Physics2D.Raycast(origin, Vector2.down, raycastDistance, groundLayer);
                    // 「乗れた」＝地面までの距離がごく小さく、かつ上昇中でない（一方通行の高台を下から
                    // すり抜けている最中はレイキャストだけだと誤検知しうるため、上昇中でないことも見る）。
                    bool closeToGround = hit.collider == highGroundCollider && hit.distance <= landedDistanceThreshold;
                    bool notRising = _playerRb.linearVelocity.y <= 0f;
                    if (closeToGround && notRising) _progress.MarkLearned(category);
                }
                break;

            case TutorialHintCategory.AttackEnemy:
                // HandleAnyEnemyDied（Enemy.OnAnyEnemyDied イベント経由）で判定するので、ここでは何もしない。
                break;

            case TutorialHintCategory.AttackEnemyBullet:
                // HandleEnemyBulletDestroyedByAttack（Bullet.OnDestroyedByAttack イベント経由）で
                // 判定するので、ここでは何もしない。
                break;
        }
    }

    private void OnDisable()
    {
        if (_hintUI != null) _hintUI.RequestHide(this);
    }
}
