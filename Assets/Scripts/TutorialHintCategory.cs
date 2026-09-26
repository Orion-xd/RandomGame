/// <summary>
/// チュートリアルヒントの種類。1オブジェクトにつき1つだけ割り当てる想定（<see cref="TutorialHint"/>参照）。
/// 達成状況（習得済みかどうか）はオブジェクト単位ではなく、このカテゴリ単位で
/// <see cref="TutorialProgress"/> がグローバルに管理する。
/// </summary>
public enum TutorialHintCategory
{
    DashPastEnemy,
    DashOverPit,
    JumpHighGround,
    ClimbLedge,
    AttackEnemy,
    // 新しい値は必ず末尾に追加すること。既存のシーン/プレハブは category を enumValueIndex
    // （＝宣言順のインデックス）でシリアライズしているため、途中に挿入すると既存の割り当てが
    // ずれて壊れる（追加時に一度この間違いをして直した）。
    JumpOverPit,
}
