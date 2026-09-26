/// <summary>
/// 会話（DialogueSequence）・チュートリアルヒント（TutorialHint）で発言しうる話者の識別子。
/// 表示名・アイコンはこの enum の値そのものではなく、<see cref="SpeakerRegistry"/> で一括管理する
/// （システムの種類によらず共通の対応表を1箇所だけ持つ）。
/// </summary>
public enum SpeakerId
{
    /// <summary>特定のキャラクターではないナレーション。名前欄・アイコンとも非表示になる特別値。
    /// インスペクター上は「ナレーターって何？」とならないよう、あえて分かりやすい名前（None）にしている。</summary>
    None,
    Hero,
    LastBoss,
    // 新しい話者は必ず末尾に追加すること（DialogueSequence.Page.speaker / TutorialHint の speaker が
    // enumValueIndex＝宣言順インデックスでシリアライズされるため、途中に挿入すると既存のシーン/
    // アセットの割り当てが全部ずれて壊れる。TutorialHintCategoryと同じ注意点）。
}
