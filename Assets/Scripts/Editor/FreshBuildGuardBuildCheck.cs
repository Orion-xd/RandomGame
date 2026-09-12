using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// リリースビルドを作るとき、<see cref="FreshBuildGuard"/> が「毎ビルド全プレイヤーの進行を消す」
/// 設定（Policy = OnEveryBuild）のままだと、ビルドを止めて確認ダイアログを出す。
/// 「その仕様を忘れたまま配布して全プレイヤーのセーブを消してしまう」事故の保険。
///
///  - Development Build のときは確認しない（開発用なので消えて当然）。
///  - Policy が OnEveryBuild 以外なら確認しない（安全側なので通す）。
///  - バッチモード（CI 等）では確認できないので、警告を出したうえで中断する。
/// </summary>
internal sealed class FreshBuildGuardBuildCheck : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (!FreshBuildGuard.WipesAllProgressOnEveryBuild) return;   // 安全な設定
        if (EditorUserBuildSettings.development) return;             // 開発ビルドは対象外

        const string title = "FreshBuildGuard の確認";
        const string body =
            "FreshBuildGuard.Policy が『OnEveryBuild』のままです。\n\n" +
            "この設定でビルドを配布すると、更新後に起動した\n" +
            "全プレイヤーの進行状況（クリア状況・会話既読）が消えます。\n\n" +
            "リリース向けなら FreshBuildGuard.cs の Policy を\n" +
            "OnTokenChange（または Disabled）に変更してからビルドしてください。";

        if (Application.isBatchMode)
        {
            Debug.LogError(title + " / " + body);
            throw new BuildFailedException(
                "FreshBuildGuard: Policy=OnEveryBuild のまま非開発ビルドを作成しようとしました。中断します。");
        }

        bool proceed = EditorUtility.DisplayDialog(title,
            body + "\n\nこのままビルドを続けますか？", "このままビルドする", "中止");
        if (!proceed)
            throw new BuildFailedException("FreshBuildGuard: ユーザーがビルドを中止しました（Policy を確認してください）。");
    }
}
