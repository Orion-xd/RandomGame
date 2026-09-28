using UnityEngine;

/// <summary>
/// ビルド版（エディタ外）で、Development Build のときだけ起動時に一度だけ全進行状況をリセットする。
///
/// 目的: 開発中に貯まった PlayerPrefs（クリア状況・会話既読など）が、アップロードしたビルドに
/// 残っていて「新品の状態で遊べない」事故を防ぐ。
///
/// ── 「絶対にリセットしない」を人間の切り替え忘れに依存させないための設計 ──
/// 以前は`Policy`という独立した定数を手動でリリース前に切り替える方式だったが、切り替え忘れたまま
/// 配布すると既存プレイヤーの進行状況ごと全消去してしまう事故が起こり得た。それを構造的に防ぐため、
/// **`Debug.isDebugBuild`（Development Build なら true になる、ランタイムでも参照できる Unity 標準
/// プロパティ）に直結**させた。切り替える定数が存在しないので「切り替え忘れ」自体が原理的に起こらず、
/// ビルド前の確認ダイアログの類も不要（通常ビルドは常に無条件で安全なため）:
///   - Development Build（`Debug.isDebugBuild == true`）: 起動のたびにビルド固有スタンプ（buildGUID）
///     と前回保存したスタンプを比較し、違えばリセット。ビルドし直すたびに毎回リセットされる（開発用）。
///   - それ以外の通常ビルド（`Debug.isDebugBuild == false`）: 一切リセットしない。
///     **配布して上書きアップデートしても、既にプレイ済みのプレイヤーの進行状況は絶対に消えない。**
///
/// 「初回起動」の判定: OS レベルの初回起動フラグは無いので、リセットの基準になる文字列（スタンプ）を
/// PlayerPrefs に覚えておき、起動時に現在のスタンプと違えばリセット＋覚え直す。
///
/// エディタ内では何もしない（開発中の進行状況は保持）。エディタの PlayerPrefs は実際のビルド版とは
/// 別のストレージ（Windowsならレジストリキーが別、WebGLならそもそもブラウザ側の別ストレージ）に
/// 保存されるため、そもそも構造的に混ざりようがない（この関数の有無に関係なく常に成立する）。
/// unityroom（WebGL）想定。
/// </summary>
public static class FreshBuildGuard
{
    private const string BuildStampKey = "RandomGame.BuildStamp";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ResetProgressIfNeeded()
    {
#if UNITY_EDITOR
        // エディタ内は対象外（開発中の進行状況を勝手に消さない）。
#else
        if (!Debug.isDebugBuild) return; // Development Build 以外は絶対にリセットしない

        // buildGUID はビルドのたび Unity が自動採番。取れない環境向けに version をフォールバック。
        string build = string.IsNullOrEmpty(Application.buildGUID) ? Application.version : Application.buildGUID;
        string stamp = "build:" + build;

        if (PlayerPrefs.GetString(BuildStampKey, "") == stamp) return; // スタンプ一致 → 何もしない

        GameFlow.ResetProgress();
        PlayerPrefs.SetString(BuildStampKey, stamp);
        PlayerPrefs.Save();
        Debug.Log($"FreshBuildGuard: 進行状況をリセットしました (Development Build, stamp={stamp})");
#endif
    }
}
