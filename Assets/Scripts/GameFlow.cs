using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ゲーム全体の画面遷移。シーン名で LoadScene するだけの最小構成。
/// 現在のステージ番号と「各ステージのクリア状況」を保持する。
///
/// ステージの並び・シーン名は `Resources/StageSet.asset`（StageSet, インスペクター編集）から読む。
/// ステージを増やすときはコード変更不要（StageSet と StageSelect シーンのボタンを足すだけ）。
///
/// 解放仕様：先頭ステージは常にプレイ可能。あるステージをクリアすると次のステージが解放される
///   （＝ステージ i は「i==0 もしくは ステージ i-1 がクリア済み」なら解放）。
///   クリア状況はステージごとの bool（PlayerPrefs にビットマスクで保存。アプリ再起動後も維持）。
///   テスト用に ResetProgress() で全消去。開発者はステージ選択画面の DevStageClearToggles で自由に変更可。
/// </summary>
public static class GameFlow
{
    public const string TitleScene = "Title";
    public const string StageSelectScene = "StageSelect";

    private const string StageSetResourcePath = "StageSet";
    private const string ClearedKey = "RandomGame.ClearedStagesMask";

    private static StageSet _stages;

    /// <summary>ステージ定義アセット（Resources/StageSet.asset）。</summary>
    public static StageSet Stages
    {
        get
        {
            if (_stages == null)
            {
                _stages = Resources.Load<StageSet>(StageSetResourcePath);
                if (_stages == null)
                    Debug.LogError($"GameFlow: Resources/{StageSetResourcePath}.asset (StageSet) が見つかりません。");
            }
            return _stages;
        }
    }

    public static int CurrentStageIndex { get; private set; }

    public static int StageCount => Stages != null ? Stages.Count : 0;
    public static bool HasNextStage => CurrentStageIndex + 1 < StageCount;

    private static int ClearedMask
    {
        get => PlayerPrefs.GetInt(ClearedKey, 0);
        set { PlayerPrefs.SetInt(ClearedKey, value); PlayerPrefs.Save(); }
    }

    /// <summary>そのステージをクリア済みか。</summary>
    public static bool IsStageCleared(int index)
        => index >= 0 && index < StageCount && (ClearedMask & (1 << index)) != 0;

    /// <summary>クリア状況を直接セットする（開発者用チェックボックス / クリア時の自動チェック）。</summary>
    public static void SetStageCleared(int index, bool cleared)
    {
        if (index < 0 || index >= StageCount) return;
        int m = ClearedMask;
        if (cleared) m |= (1 << index);
        else m &= ~(1 << index);
        ClearedMask = m;
    }

    /// <summary>そのステージがプレイ可能か（＝解放済みか）。先頭は常に true、以降は前ステージがクリア済みなら true。</summary>
    public static bool IsStageUnlocked(int index)
        => index >= 0 && index < StageCount && (index == 0 || IsStageCleared(index - 1));

    /// <summary>連続して解放されている最大のステージ index（表示・カーソル初期位置の目安）。</summary>
    public static int UnlockedStageIndex
    {
        get
        {
            int u = 0;
            for (int i = 1; i < StageCount; i++)
            {
                if (IsStageUnlocked(i)) u = i;
                else break;
            }
            return u;
        }
    }

    /// <summary>ステージクリア時に呼ぶ。そのステージを「クリア済み」にする（＝次が解放される）。</summary>
    public static void MarkStageCleared(int index) => SetStageCleared(index, true);

    /// <summary>クリア状況をリセット（全ステージ未クリア＝先頭のみ解放）。テスト用。</summary>
    public static void ResetProgress()
    {
        PlayerPrefs.DeleteKey(ClearedKey);
        PlayerPrefs.Save();
    }

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
        if (index < 0 || index >= StageCount) return;
        if (!IsStageUnlocked(index)) return; // ロック中は遷移しない

        string scene = Stages.SceneNameAt(index);
        if (string.IsNullOrEmpty(scene))
        {
            Debug.LogError($"GameFlow: StageSet の stage {index} に sceneName が設定されていません。");
            return;
        }

        CurrentStageIndex = index;
        Time.timeScale = 1f;
        SceneManager.LoadScene(scene);
    }

    public static void RetryStage() => LoadStage(CurrentStageIndex);

    public static void NextStage()
    {
        if (HasNextStage) LoadStage(CurrentStageIndex + 1);
        else GoStageSelect();
    }
}
