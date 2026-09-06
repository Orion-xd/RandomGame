using UnityEngine;

/// <summary>ステージ選択画面。各ボタンの OnClick から LoadStage(index) を呼ぶ。</summary>
public class StageSelectMenu : MonoBehaviour
{
    /// <summary>index 0 = Stage 1, 1 = Stage 2, ...</summary>
    public void LoadStage(int index) => GameFlow.LoadStage(index);
}
