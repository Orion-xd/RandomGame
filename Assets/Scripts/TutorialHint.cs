using UnityEngine;

/// <summary>
/// チュートリアルヒントの表示条件・達成条件を1つのオブジェクトにつき1種類だけ担当する（2026-09-22、全面刷新）。
///
/// 共通の考え方: 対象（敵/落とし穴/高台/段差）に近づいている間だけヒントを表示し、離れれば消える。
/// ただし、そのカテゴリを一度でも「達成」すれば（<see cref="TutorialProgress"/>側で管理）、以後は
/// 二度と表示されない（個々のオブジェクト単位ではなく、カテゴリ単位でグローバルに習得したかどうかを管理する）。
///
/// 「近づいている」の判定は、自分自身についている Collider2D（トリガー）とプレイヤーの重なりで行う。
/// 敵キャラ用（DashPastEnemy/AttackEnemy）は敵の子オブジェクトとして配置し、敵を覆う専用の Collider を
/// 持たせる（敵本体の当たり判定とは別物）。落とし穴/高台/段差用は、それらを覆う Collider を持つ専用の
/// マーカーオブジェクトとして配置する。
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
    [Tooltip("画面上部に表示するヒントテキスト")]
    [SerializeField] private string hintText;

    [Header("DashPastEnemy / AttackEnemy 用")]
    [Tooltip("対象の敵。未設定なら親から自動取得（敵の子オブジェクトとして配置する想定）")]
    [SerializeField] private Enemy targetEnemy;
    [Tooltip("DashPastEnemy用：プレイヤーのx座標が敵よりこれだけ大きくなったら「すり抜けた」と判定する")]
    [SerializeField] private float passThroughMargin = 1.5f;

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
    private Transform _playerTf;
    private Rigidbody2D _playerRb;
    private MainActionQueue _playerQueue;
    private TutorialProgress _progress;
    private TutorialHintUI _hintUI;

    private MainActionType RequiredAction => category switch
    {
        TutorialHintCategory.DashPastEnemy => MainActionType.Dash,
        TutorialHintCategory.DashOverPit => MainActionType.Dash,
        TutorialHintCategory.JumpOverPit => MainActionType.Jump,
        TutorialHintCategory.JumpHighGround => MainActionType.Jump,
        TutorialHintCategory.ClimbLedge => MainActionType.Jump,
        TutorialHintCategory.AttackEnemy => MainActionType.Attack,
        _ => MainActionType.Dash,
    };

    private void Awake()
    {
        _proximityCol = GetComponent<Collider2D>();
        _proximityCol.isTrigger = true;

        if (targetEnemy == null) targetEnemy = GetComponentInParent<Enemy>();

        _progress = FindAnyObjectByType<TutorialProgress>();
        _hintUI = FindAnyObjectByType<TutorialHintUI>();

        var p = GameObject.FindGameObjectWithTag("Player");
        if (p != null)
        {
            _playerTf = p.transform;
            _playerCollider = p.GetComponent<Collider2D>();
            _playerRb = p.GetComponent<Rigidbody2D>();
            _playerQueue = p.GetComponent<MainActionQueue>();
        }

        // AttackEnemy だけはイベント経由で判定する（Destroy されるより前、体力が0になった瞬間に
        // 判定を済ませる必要があるため。Update での存在チェックだと、このコンポーネント自身も敵の
        // 子として同時に破棄されてしまい、判定するチャンスが無いまま消えてしまう）。
        if (category == TutorialHintCategory.AttackEnemy && targetEnemy != null)
            targetEnemy.OnDamaged += HandleTargetDamaged;
    }

    private void OnDestroy()
    {
        if (targetEnemy != null) targetEnemy.OnDamaged -= HandleTargetDamaged;
    }

    private void HandleTargetDamaged()
    {
        if (targetEnemy.Health <= 0) _progress.MarkLearned(category);
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
            _hintUI.RequestShow(this, hintText, _proximityCol.Distance(_playerCollider).distance);
        else
            _hintUI.RequestHide(this);
    }

    private void CheckSuccess()
    {
        switch (category)
        {
            case TutorialHintCategory.DashPastEnemy:
                if (targetEnemy != null && _playerTf.position.x > targetEnemy.transform.position.x + passThroughMargin)
                    _progress.MarkLearned(category);
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
                // HandleTargetDamaged（Enemy.OnDamaged イベント経由）で判定するので、ここでは何もしない。
                break;
        }
    }

    private void OnDisable()
    {
        if (_hintUI != null) _hintUI.RequestHide(this);
    }
}
