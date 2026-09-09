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

        [Tooltip("このステージで抽選するアクション。空ならシーンの MainActionQueue.lottery をそのまま使う。" +
                 "例: ステージ1は Dash のみ、ステージ2は Dash と Attack、ステージ3以降は 3 つ全部")]
        public MainActionType[] allowedActions;

        [Tooltip("true にすると、このステージではコンボ（連続発動）を無効化する。" +
                 "1回使ったらコンボ猶予は無く、クールタイムが明けるまで次は出せない")]
        public bool disableCombos;
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

    /// <summary>そのステージで使えるアクション（＝抽選対象）。未設定（空 / null）なら null を返す。</summary>
    public MainActionType[] AllowedActionsAt(int index)
        => (index >= 0 && index < Count) ? stages[index].allowedActions : null;

    /// <summary>そのステージでコンボ（連続発動）を無効化するか。
    /// 明示フラグ、または抽選対象が実質1種類（X→X しか組めない）の場合に true。</summary>
    public bool DisableCombosAt(int index)
    {
        if (index < 0 || index >= Count) return false;
        if (stages[index].disableCombos) return true;

        var a = stages[index].allowedActions;
        if (a == null || a.Length == 0) return false;
        for (int i = 1; i < a.Length; i++) if (a[i] != a[0]) return false;
        return true; // 全部同じ = 実質1種類
    }

    /// <summary>シーン名から stages の index を引く。見つからなければ -1。</summary>
    public int IndexOfScene(string sceneName)
    {
        if (stages == null || string.IsNullOrEmpty(sceneName)) return -1;
        for (int i = 0; i < stages.Length; i++)
            if (stages[i] != null && stages[i].sceneName == sceneName) return i;
        return -1;
    }
}
