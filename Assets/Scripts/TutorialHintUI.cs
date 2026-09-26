using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 画面上部にチュートリアルのヒントテキストを表示する。
/// 見た目（箱・話者アイコン・名前欄・本文の位置/色/フォント）は、ストーリー会話（`DialoguePlayer`）と
/// 完全に共有の `SpeakerTextBoxView`（`Assets/Prefabs/UI/SpeakerTextBox.prefab`）インスタンスが担当する。
/// 見た目を調整したい場合は、このスクリプトではなく必ずそのプレハブ自身を直接編集すること
/// （Prefab Mode でダブルクリックして開けば、ゲームを実行しなくてもそのまま実際のレイアウトを確認できる）。
/// このクラスは「いつ・何を表示するか」という振る舞いだけを担当する。
///
/// 複数の TutorialHint が同時に表示を要求した場合は、プレイヤーに一番近い（distanceが最小の）ものだけを
/// 表示する（1オブジェクト=1ヒントの制約のもとで複数の対象に同時に近づいてしまった場合の
/// 優先順位として、判定用コライダーがより近いものを優先する仕様）。
///
/// 【表示/非表示を GameObject.SetActive ではなく CanvasGroup.alpha で行う理由】
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
    [Tooltip("見た目本体（箱・話者アイコン・名前欄・本文）。ストーリー会話と共有の " +
             "Assets/Prefabs/UI/SpeakerTextBox.prefab のインスタンスを指す")]
    [SerializeField] private SpeakerTextBoxView textBox;

    private readonly List<(object requester, string text, float distance, SpeakerId speaker)> _requests = new();

    private void Awake()
    {
        SetVisible(false);

        if (textBox != null && textBox.Body != null)
        {
            // ヒントは短い一文を一気に表示するだけなので、箱の中央に据える（会話本文は左揃えで別途
            // DialoguePlayer 側が文字送り用に設定するため、ここでは触れない＝食い違わない）。
            textBox.Body.alignment = TextAnchor.MiddleCenter;

            // シーン上に直接置いた Text は DialoguePlayer の日本語フォント修正の対象外なので、
            // ここで明示的に設定する（過去のNoto Sans JP対応の教訓：シーン作成の Text は自動継承されない）。
            var font = Resources.Load<Font>("Fonts/NotoSansJP-Regular");
            if (font != null)
            {
                textBox.Body.font = font;
                if (textBox.SpeakerName != null) textBox.SpeakerName.font = font;
            }
        }
    }

    /// <summary>表示を要求する。distance はプレイヤーとの近さ（小さいほど優先表示される）。
    /// speaker は任意（ストーリー会話の TopTextbox と同じ話者アイコン欄の仕組み）。
    /// 省略（None）すればこれまで通りアイコン・名前欄とも非表示のまま。表示名・アイコンは
    /// SpeakerRegistry から解決する。</summary>
    public void RequestShow(object requester, string text, float distance, SpeakerId speaker = SpeakerId.None)
    {
        for (int i = 0; i < _requests.Count; i++)
        {
            if (Equals(_requests[i].requester, requester))
            {
                _requests[i] = (requester, text, distance, speaker);
                Refresh();
                return;
            }
        }
        _requests.Add((requester, text, distance, speaker));
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
        if (textBox == null) return;
        if (textBox.Body != null) textBox.Body.text = nearest.text;

        // 話者アイコン・名前欄・本文の余白は、共有ビュー（SpeakerTextBoxView）へ丸ごと委譲する
        // （見た目の計算はそちら側の責務。表示名・アイコンは SpeakerRegistry から解決する）。
        // 本文の左右マージンは話者の有無によらず常に同じ（ユーザー指定）。
        bool hasSpeaker = nearest.speaker != SpeakerId.None;
        var profile = hasSpeaker ? SpeakerRegistry.Get(nearest.speaker) : null;
        textBox.SetSpeaker(profile);
        textBox.ApplyBodyInset();
    }

    private void SetVisible(bool visible)
    {
        if (canvasGroup == null) return;
        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.blocksRaycasts = visible;
        canvasGroup.interactable = visible;
    }
}
