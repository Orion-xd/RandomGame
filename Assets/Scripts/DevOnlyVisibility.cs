using UnityEngine;

/// <summary>
/// 【開発者用】このGameObjectを、開発者モード（<see cref="DeveloperSettings"/>.Active：エディタ内
/// かつ アセットの developerMode）のときだけ表示する。それ以外（developerModeがオフ、またはビルド）
/// では非表示にする。
/// 既存の開発者用ボタン・チェックボックス（DevProgressResetButton等）は実行時に自分で生成するが、
/// こちらは「シーンに元から置かれている、通常の見た目のオブジェクト」を対象にするためのもの。
/// </summary>
public class DevOnlyVisibility : MonoBehaviour
{
    private void Start()
    {
        if (!DeveloperSettings.Active) gameObject.SetActive(false);
    }
}
