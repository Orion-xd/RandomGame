using UnityEngine;

/// <summary>
/// ビルド版（エディタ外）で、条件に応じて起動時に一度だけ全進行状況をリセットする。
///
/// 目的: 開発中に貯まった PlayerPrefs（クリア状況・会話既読など）が、アップロードしたビルドに
/// 残っていて「新品の状態で遊べない」事故を防ぐ。
///
/// 「初回起動」の判定: OS レベルの初回起動フラグは無いので、リセットの基準になる文字列（スタンプ）を
/// PlayerPrefs に覚えておき、起動時に現在のスタンプと違えばリセット＋覚え直す。
/// スタンプの作り方は下の Policy で切り替える。
///
/// エディタ内では何もしない（開発中の進行状況は保持）。unityroom（WebGL）想定。
/// </summary>
public static class FreshBuildGuard
{
    // ═══ 設定：リリース前に必ず確認すること ══════════════════════════════════════
    //
    //  OnEveryBuild  … ビルドし直すたびにリセット（buildGUID が毎ビルド変わるため）。開発中の既定。
    //                  ★配布ビルドでは必ず OnTokenChange か Disabled にする★
    //                  （このまま配布すると、更新のたびに全プレイヤーの進行が消える）
    //                  → この設定のまま Development Build 以外をビルドしようとすると、
    //                    エディタが確認ダイアログを出して止める（FreshBuildGuardBuildCheck）。
    //
    //  OnTokenChange … 下の ResetToken を書き換えたときだけ、次回起動で1回だけリセット。
    //                  バージョン更新・ビルドし直しでは消えない。リリース後はこれ。
    //
    //  Disabled      … 自動リセットしない（手動 GameFlow.ResetProgress() のみ）。
    //
    private enum ResetPolicy { OnEveryBuild, OnTokenChange, Disabled }

    private const ResetPolicy Policy = ResetPolicy.OnEveryBuild;

    // Policy = OnTokenChange のときだけ意味を持つ。ここを書き換えた次の初回起動で1回リセットされる
    // （例: セーブ内容の互換性が切れたときに "1" → "2" のように上げる）。
    private const string ResetToken = "1";
    // ═══════════════════════════════════════════════════════════════════════════

    private const string BuildStampKey = "RandomGame.BuildStamp";

    /// <summary>ビルド前チェック用（エディタ）。true なら「配布すると毎ビルド全プレイヤーの進行が消える」設定。</summary>
    public static bool WipesAllProgressOnEveryBuild => Policy == ResetPolicy.OnEveryBuild;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ResetProgressIfNeeded()
    {
#if UNITY_EDITOR
        // エディタ内は対象外（開発中の進行状況を勝手に消さない）。
#else
        if (Policy == ResetPolicy.Disabled) return;

        string stamp;
        if (Policy == ResetPolicy.OnEveryBuild)
        {
            // buildGUID はビルドのたび Unity が自動採番。取れない環境向けに version をフォールバック。
            string build = string.IsNullOrEmpty(Application.buildGUID) ? Application.version : Application.buildGUID;
            stamp = "build:" + build;
        }
        else // OnTokenChange … バージョンやビルド回数では変えず、ResetToken だけを見る
        {
            stamp = "token:" + ResetToken;
        }

        if (PlayerPrefs.GetString(BuildStampKey, "") == stamp) return; // スタンプ一致 → 何もしない

        GameFlow.ResetProgress();
        PlayerPrefs.SetString(BuildStampKey, stamp);
        PlayerPrefs.Save();
        Debug.Log($"FreshBuildGuard: 進行状況をリセットしました (policy={Policy}, stamp={stamp})");
#endif
    }
}
