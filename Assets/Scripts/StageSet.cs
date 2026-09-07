using UnityEngine;

/// <summary>
/// ステージの並びと、対応するシーン名を保持する設定アセット（インスペクターで編集）。
/// `Assets/Resources/StageSet.asset` に置き、GameFlow が `Resources.Load` で読む。
///
/// ステージを増やすとき（企画では全5ステージ予定）:
///   1. ステージシーンを作成し、Build Settings に追加する
///   2. このアセットの stages に要素を1つ足し、sceneName をそのシーン名に合わせる
///   3. StageSelect シーンにボタンを足し、StageSelectMenu.stageButtons に同じ index で割り当てる
/// </summary>
[CreateAssetMenu(fileName = "StageSet", menuName = "RandomGame/Stage Set")]
public class StageSet : ScriptableObject
{
    [System.Serializable]
    public class Stage
    {
        [Tooltip("UI に出す表示名（任意。空ならボタン既定のテキストのまま）")]
        public string displayName;

        [Tooltip("読み込むシーン名。Build Settings に追加したシーンと一致させること")]
        public string sceneName;

        [Tooltip("このステージに初めて入ったときに流す会話（任意。未設定なら会話なし）")]
        public DialogueSequence intro;
    }

    [Tooltip("ゲーム開始時（タイトル → ステージ選択の前）に流すプロローグ会話（任意。未設定ならスキップ）")]
    public DialogueSequence prologue;

    [Tooltip("ステージを順番に並べる。index 0 = 最初のステージ")]
    public Stage[] stages;

    public int Count => stages != null ? stages.Length : 0;

    public string SceneNameAt(int index)
        => (index >= 0 && index < Count) ? stages[index].sceneName : null;

    public string DisplayNameAt(int index)
        => (index >= 0 && index < Count) ? stages[index].displayName : null;

    public DialogueSequence IntroAt(int index)
        => (index >= 0 && index < Count) ? stages[index].intro : null;
}
