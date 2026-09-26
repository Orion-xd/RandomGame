using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 画面上部にチュートリアルのヒントテキストを表示する（ストーリー表示の DialoguePlayer とは別系統、使い回さない。
/// 表示位置だけはDialoguePlayerの会話ボックス「上部」レイアウトと一貫性を持たせて画面上部にしてある、2026-09-22）。
/// 複数の TutorialHint が同時に表示を要求した場合は、プレイヤーに一番近い（distanceが最小の）ものだけを
/// 表示する（2026-09-22、1オブジェクト=1ヒントの制約のもとで複数の対象に同時に近づいてしまった場合の
/// 優先順位として、判定用コライダーがより近いものを優先する仕様）。
///
/// 【表示/非表示を GameObject.SetActive ではなく CanvasGroup.alpha で行う理由（2026-09-22修正）】
/// 当初は自分自身（このスクリプトが付いている GameObject）を SetActive(false) で隠していたが、
/// それだと「自分自身が非アクティブな間、他のスクリプトの Awake() から
/// FindAnyObjectByType&lt;TutorialHintUI&gt;() で自分を見つけられなくなる」問題があった
/// （FindAnyObjectByType は既定で非アクティブなオブジェクトを対象外にするため）。GameObject をまたいだ
/// Awake() の実行順は Unity が保証しないため、参照解決に失敗することがある、実行順依存の不具合になっていた
/// （Editor Play では再現するのに、ビルド版では実行順がたまたま違って再現しない、ということが実際に起きた）。
/// CanvasGroup.alpha で見た目だけ隠す方式なら GameObject 自体は常にアクティブのままなので、
/// この問題が原理的に起こらない。
/// </summary>
public class TutorialHintUI : MonoBehaviour
{
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Text label;

    private readonly List<(object requester, string text, float distance)> _requests = new();

    private void Awake()
    {
        SetVisible(false);

        // シーン上に直接置いた Text は DialoguePlayer の日本語フォント修正の対象外なので、ここで明示的に設定する
        // （過去のNoto Sans JP対応の教訓：シーン作成の Text は自動継承されない）。
        var font = Resources.Load<Font>("Fonts/NotoSansJP-Regular");
        if (font != null && label != null) label.font = font;
    }

    /// <summary>表示を要求する。distance はプレイヤーとの近さ（小さいほど優先表示される）。</summary>
    public void RequestShow(object requester, string text, float distance)
    {
        for (int i = 0; i < _requests.Count; i++)
        {
            if (Equals(_requests[i].requester, requester))
            {
                _requests[i] = (requester, text, distance);
                Refresh();
                return;
            }
        }
        _requests.Add((requester, text, distance));
        Refresh();
    }

    public void RequestHide(object requester)
    {
        _requests.RemoveAll(r => Equals(r.requester, requester));
        Refresh();
    }

    private void Refresh()
    {
        if (_requests.Count == 0)
        {
            SetVisible(false);
            return;
        }

        var nearest = _requests[0];
        for (int i = 1; i < _requests.Count; i++)
            if (_requests[i].distance < nearest.distance) nearest = _requests[i];

        SetVisible(true);
        if (label != null) label.text = nearest.text;
    }

    private void SetVisible(bool visible)
    {
        if (canvasGroup == null) return;
        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.blocksRaycasts = visible;
        canvasGroup.interactable = visible;
    }
}
