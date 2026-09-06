using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ゲーム全体の画面遷移。シーン名で LoadScene するだけの最小構成。
/// 現在のステージ番号だけ static で保持する（「次のステージへ」「もう一度」に使う）。
///
/// ※ 将来のためのメモ：ステージ開始演出などを足すなら、各ステージシーンの StageManager 側で
///    「Start 時に演出 → 終わったらゲームプレイ開始」のようにフックする想定。GameFlow は触らない。
/// </summary>
public static class GameFlow
{
    public const string TitleScene = "Title";
    public const string StageSelectScene = "StageSelect";

    /// <summary>ステージ番号 → シーン名。</summary>
    public static readonly string[] StageScenes = { "Stage1", "Stage2", "Stage3" };

    public static int CurrentStageIndex { get; private set; }

    public static int StageCount => StageScenes.Length;
    public static bool HasNextStage => CurrentStageIndex + 1 < StageScenes.Length;

    public static void GoTitle()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(TitleScene);
    }

    public static void GoStageSelect()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(StageSelectScene);
    }

    public static void LoadStage(int index)
    {
        if (index < 0 || index >= StageScenes.Length) return;
        CurrentStageIndex = index;
        Time.timeScale = 1f;
        SceneManager.LoadScene(StageScenes[index]);
    }

    public static void RetryStage() => LoadStage(CurrentStageIndex);

    public static void NextStage()
    {
        if (HasNextStage) LoadStage(CurrentStageIndex + 1);
        else GoStageSelect();
    }
}
