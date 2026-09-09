using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// すべてのシーン遷移で、暗転 → シーン読み込み → 明転 を行う。
/// <see cref="GameFlow"/> から <see cref="Go"/>(シーン名) で呼ぶ。実行時に自動生成（DontDestroyOnLoad）。
///
/// 演出中（暗転開始 〜 明転完了 ＋ postFadeInLock 秒のあいだ）は <see cref="Transitioning"/> が true で、
/// <see cref="InputLock"/>.InputAllowed が false になる（＝すべての入力を無効化）。
/// 真っ黒 Image が raycastTarget=true なので、その間はマウスの UI クリックも通らない。
///
/// ステージクリア / 失敗のパネル表示はシーン遷移ではないのでフェードしない（そちらは別の入力ロック）。
/// フェード時間などは `Assets/Resources/SceneTransitionSettings.asset` で調整。
/// </summary>
public class SceneTransition : MonoBehaviour
{
    /// <summary>フェード演出＋その後の入力ロック中か。</summary>
    public static bool Transitioning { get; private set; }

    private static SceneTransition _instance;

    private CanvasGroup _group;
    private GameObject _fadeRoot;

    /// <summary>暗転 → sceneName を読み込み → 明転。二重遷移は無視。</summary>
    public static void Go(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName) || Transitioning) return;
        EnsureInstance();
        _instance.StartCoroutine(_instance.Run(sceneName));
    }

    private static void EnsureInstance()
    {
        if (_instance != null) return;
        var go = new GameObject("SceneTransition");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<SceneTransition>();
    }

    private void Awake()
    {
        if (_instance == null) _instance = this;

        var canvasGo = new GameObject("FadeCanvas", typeof(Canvas), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32760; // 会話(200)・結果画面(100) など何よりも前面

        var imgGo = new GameObject("Black", typeof(RectTransform));
        imgGo.transform.SetParent(canvasGo.transform, false);
        var rt = (RectTransform)imgGo.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        var black = imgGo.AddComponent<Image>();
        black.color = Color.black;
        black.raycastTarget = true; // 演出中はクリックも遮る

        _group = canvasGo.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _fadeRoot = canvasGo;
        _fadeRoot.SetActive(false);
    }

    private IEnumerator Run(string sceneName)
    {
        Transitioning = true;

        var s = SceneTransitionSettings.Instance;
        float fadeOut = s != null ? s.fadeOutSeconds : 0.5f;
        float fadeIn  = s != null ? s.fadeInSeconds  : 0.5f;
        float postLock = s != null ? s.postFadeInLockSeconds : 0.5f;

        _fadeRoot.SetActive(true);
        // 暗転中は遷移元シーンを完全停止させる（クリア後にカメラ追従の余韻で画面が動く等を防ぐ）。
        // フェードは Time.unscaledDeltaTime で動かすので timeScale=0 でも進む。
        Time.timeScale = 0f;

        yield return Fade(0f, 1f, fadeOut); // 暗転

        var op = SceneManager.LoadSceneAsync(sceneName);
        while (op != null && !op.isDone) yield return null;
        Time.timeScale = 1f; // 遷移先で通常進行に戻す（開始会話があれば StageManager が再度 0 にする）
        // ロード直後の数フレームは delta が大きく荒れる（ヒッチ）。明転がそれで一瞬で終わらないよう、
        // ここで 2 フレーム捨ててから明転する。Fade 側でも 1 フレームの寄与に上限を設けている。
        yield return null;
        yield return null;

        yield return Fade(1f, 0f, fadeIn); // 明転

        InputLock.LockFor(postLock);        // 明転しきってから、さらに postLock 秒だけ入力を止める
        _fadeRoot.SetActive(false);
        Transitioning = false;
    }

    private IEnumerator Fade(float from, float to, float duration)
    {
        _group.alpha = from;
        if (duration <= 0f) { _group.alpha = to; yield break; }

        // 1 フレームの delta が大きくても（ロード直後のヒッチ等）フェードが飛ばないよう上限を設ける。
        float maxStep = Mathf.Max(1f / 60f, duration * 0.15f);
        float t = 0f;
        while (t < duration)
        {
            yield return null;
            t += Mathf.Min(Time.unscaledDeltaTime, maxStep);
            _group.alpha = Mathf.Lerp(from, to, t / duration);
        }
        _group.alpha = to;
    }
}
