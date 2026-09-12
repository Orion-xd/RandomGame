using UnityEngine;

/// <summary>
/// 会話1本ぶんのデータ（ScriptableObject、インスペクターで編集）。
/// プロローグや各ステージ開始時の会話をこれで持つ。
///
/// 使い方:
///   - プロローグ:  Assets/Resources/StageSet.asset の prologue に割り当てる。
///   - ステージ会話: 同アセットの各 stage の intro に割り当てる（そのステージに初めて入ったとき再生）。
///
/// スペース / エンターで pages を1つずつ送る。分岐なしの一本道。
/// テキスト量が増えて表計算で管理したくなったら、CSV → この .asset へ焼き込むエディタ拡張を足せば
/// ランタイムは変えずに移行できる（今は .asset を直接編集）。
///
/// 各 Page の `useTypewriterEffect` で、1文字ずつ表示（タイプライター演出）するか本文を一括表示するかを
/// ページ単位で選べる（既定 true）。速さ（`typewriterCharsPerSecond`）もページ単位で調整できる。
/// </summary>
[CreateAssetMenu(fileName = "DialogueSequence", menuName = "RandomGame/Dialogue Sequence")]
public class DialogueSequence : ScriptableObject
{
    public enum Layout
    {
        /// <summary>真っ黒背景に中央テキスト。プロローグ前半（「ある日、ダンジョンが出現した。」など）。</summary>
        CenteredOnBlack,
        /// <summary>下部にテキストボックス。プロローグ後半（一枚絵つきの会話）。背景は暗転。</summary>
        BottomTextbox,
        /// <summary>上部にテキストボックス。ステージ開始時の会話。背景は暗転せずゲーム画面が見える。</summary>
        TopTextbox,
    }

    [System.Serializable]
    public class Page
    {
        [Tooltip("話者名。空ならナレーション（名前欄を隠す）")]
        public string speaker;

        [TextArea(2, 6)]
        [Tooltip("本文。改行可")]
        public string text;

        [Tooltip("このページで表示する一枚絵（任意）。未指定なら直前に指定された絵を継続。" +
                 "BottomTextbox で絵が未指定のうちは仮イラスト（主人公＝左 / ダンジョン＝右）を表示する")]
        public Sprite image;

        [Tooltip("表示レイアウト")]
        public Layout layout = Layout.BottomTextbox;

        [Tooltip("true: 本文を先頭から1文字ずつ徐々に表示する（タイプライター演出）。" +
                 "false: 本文を最初から丸ごと表示する")]
        public bool useTypewriterEffect = true;

        [Tooltip("1文字ずつ表示するときの速さ（1秒あたりに表示する文字数）。useTypewriterEffect が false のときは無視される")]
        public float typewriterCharsPerSecond = 30f;
    }

    [Tooltip("会話のページ。スペース / エンターで1ページずつ進む")]
    public Page[] pages;

    public int Count => pages != null ? pages.Length : 0;

    public Page PageAt(int index) => (index >= 0 && index < Count) ? pages[index] : null;
}
