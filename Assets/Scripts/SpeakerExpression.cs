/// <summary>
/// 会話（DialogueSequence）・チュートリアルヒント（TutorialHint）のセリフごとに選べる表情の種類。
/// 話者（SpeakerId）を選んだ後、続けてこれを選ぶことで、キャラクターごとに用意された表情アイコンの
/// 中からセリフに合ったものを表示できる（<see cref="SpeakerRegistry"/> 参照）。
/// 全キャラクター共通のリストなので、そのキャラクターがまだ用意していない表情を選んでも、
/// Normal相当のアイコンに自動でフォールバックする（未設定でもエラーにはならない）。
///
/// `_Kusari` は各表情の「鎖」バリエーション（無印の表情に対応する、鎖に繋がれた状態の絵）。
/// </summary>
public enum SpeakerExpression
{
    Normal_Kusari,
    Normal,
    Doya_Kusari,
    /// <summary>ドヤ顔</summary>
    Doya,
    Troubled_Kusari,
    /// <summary>困り顔（Trouble から改名。形容詞形で他の項目と表記を揃えた）</summary>
    Troubled,
    Angry_Kusari,
    Angry,
    Panicked_Kusari,
    /// <summary>焦り顔（Panic から改名。形容詞形で他の項目と表記を揃えた）</summary>
    Panicked,
    Serious_Kusari,
    /// <summary>真剣な顔</summary>
    Serious,
    Sleepy_Kusari,
    Sleepy,
    Surprised_Kusari,
    Surprised,
    // 新しい表情は必ず末尾に追加すること（enumValueIndexでシリアライズされるため、途中に挿入すると
    // 既存のセリフの割り当てが全部ずれて壊れる。SpeakerId/TutorialHintCategoryと同じ注意点）。
}
