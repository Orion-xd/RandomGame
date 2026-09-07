using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ステージ選択画面。各ボタンの OnClick から LoadStage(index) を呼ぶ。
///
/// stageButtons[i] が「ステージ i のボタン」。ステージ i のシーン名・表示名は
/// GameFlow.Stages（Resources/StageSet.asset, インスペクター編集）の stages[i] で決まる。
/// ステージを増やすときは、StageSet に要素を足し、ここに同じ index でボタンを足すだけ。
///
/// Start / RefreshLocks で未解放ステージのボタンを無効化し、StageSet に displayName があれば
/// ボタンのラベルへ反映する。
/// </summary>
public class StageSelectMenu : MonoBehaviour
{
    [Tooltip("index 0 = 最初のステージのボタン、1 = 次のステージ … の順で割り当てる（GameFlow.Stages と対応）")]
    [SerializeField] private Button[] stageButtons;

    /// <summary>index 順のステージボタン（開発者用トグルなどから参照）。</summary>
    public Button[] StageButtons => stageButtons;

    private void Start()
    {
        ApplyDisplayNames();
        RefreshLocks();
    }

    /// <summary>未解放ステージのボタンを無効化する。</summary>
    public void RefreshLocks()
    {
        if (stageButtons == null) return;
        for (int i = 0; i < stageButtons.Length; i++)
            if (stageButtons[i] != null)
                stageButtons[i].interactable = GameFlow.IsStageUnlocked(i);
    }

    /// <summary>StageSet に displayName があれば、対応するボタンのラベル（子の Text）へ反映する。</summary>
    private void ApplyDisplayNames()
    {
        if (stageButtons == null || GameFlow.Stages == null) return;
        for (int i = 0; i < stageButtons.Length; i++)
        {
            if (stageButtons[i] == null) continue;
            string name = GameFlow.Stages.DisplayNameAt(i);
            if (string.IsNullOrEmpty(name)) continue;
            var label = stageButtons[i].GetComponentInChildren<Text>(true);
            if (label != null) label.text = name;
        }
    }

    /// <summary>index 0 = 最初のステージ, 1 = 次のステージ, ...</summary>
    public void LoadStage(int index) => GameFlow.LoadStage(index);

    /// <summary>タイトル画面へ戻る（右下の戻るボタンの OnClick から）。</summary>
    public void BackToTitle() => GameFlow.GoTitle();
}
