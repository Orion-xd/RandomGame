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
    public const string PrologueScene = "Prologue";
    public const string StageSelectScene = "StageSelect";

    private const string StageSetResourcePath = "StageSet";
    private const string ClearedKey = "RandomGame.ClearedStagesMask";
    private const string SeenIntroKey = "RandomGame.SeenIntroMask";
    private const string SeenPrologueKey = "RandomGame.SeenPrologue";

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

    // ── 会話の既読フラグ（初回だけ再生するため。PlayerPrefs に保存。開発者用トグルからも変更可） ──

    private static int SeenIntroMask
    {
        get => PlayerPrefs.GetInt(SeenIntroKey, 0);
        set { PlayerPrefs.SetInt(SeenIntroKey, value); PlayerPrefs.Save(); }
    }

    /// <summary>そのステージの開始時会話を既に見たか（初回判定）。</summary>
    public static bool HasSeenIntro(int index)
        => index >= 0 && index < StageCount && (SeenIntroMask & (1 << index)) != 0;

    /// <summary>そのステージの開始時会話の既読フラグを直接セットする（開発者用トグル / 再生後の自動セット）。</summary>
    public static void SetIntroSeen(int index, bool seen)
    {
        if (index < 0 || index >= StageCount) return;
        int m = SeenIntroMask;
        if (seen) m |= (1 << index);
        else m &= ~(1 << index);
        SeenIntroMask = m;
    }

    /// <summary>そのステージの開始時会話を「見た」ことにする。</summary>
    public static void MarkIntroSeen(int index) => SetIntroSeen(index, true);

    /// <summary>プロローグ会話を既に見たか。既読なら「ゲームスタート」でプロローグをスキップする。</summary>
    public static bool HasSeenPrologue => PlayerPrefs.GetInt(SeenPrologueKey, 0) != 0;

    /// <summary>プロローグ会話の既読フラグを直接セットする（開発者用トグル / 再生後の自動セット）。</summary>
    public static void SetPrologueSeen(bool seen)
    {
        PlayerPrefs.SetInt(SeenPrologueKey, seen ? 1 : 0);
        PlayerPrefs.Save();
    }

    /// <summary>プロローグ会話を「見た」ことにする。</summary>
    public static void MarkPrologueSeen() => SetPrologueSeen(true);

    /// <summary>クリア状況と会話既読フラグをリセット（先頭のみ解放・全会話が初回状態）。テスト用。</summary>
    public static void ResetProgress()
    {
        PlayerPrefs.DeleteKey(ClearedKey);
        PlayerPrefs.DeleteKey(SeenIntroKey);
        PlayerPrefs.DeleteKey(SeenPrologueKey);
        PlayerPrefs.Save();
    }

    public static void GoTitle()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(TitleScene);
    }

    /// <summary>タイトルの「ゲームスタート」から。未読ならプロローグシーンを経由し、既読ならステージ選択へ直行。
    /// プロローグシーンが Build Settings に無い場合もステージ選択へ直行する（未整備でも動く）。</summary>
    public static void StartGame()
    {
        Time.timeScale = 1f;
        if (!HasSeenPrologue && Application.CanStreamedLevelBeLoaded(PrologueScene))
            SceneManager.LoadScene(PrologueScene);
        else
            SceneManager.LoadScene(StageSelectScene);
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
