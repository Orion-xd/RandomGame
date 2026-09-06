using UnityEngine;

/// <summary>タイトル画面。「ゲームスタート」ボタンの OnClick からこのメソッドを呼ぶ。</summary>
public class TitleMenu : MonoBehaviour
{
    public void OnStartClicked() => GameFlow.GoStageSelect();
}
