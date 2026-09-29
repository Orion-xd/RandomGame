using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// UI ボタンの演出。
/// ・マウスを乗せている間は hoverSprite に差し替え（未設定なら差し替えなし）
/// ・Button が interactable=false（ステージ選択の未解放ボタンなど）の間は lockedSprite に差し替え、
///   ホバー・押下演出は行わない（lockedSprite 未設定なら暗くするだけ）
/// ・押している間は少し縮んで暗くなり、離すと元のサイズ・色に戻る
/// クリック自体は Button 標準どおり「離した時」に発火する。
/// Time.timeScale = 0 の結果画面でも動くよう unscaledDeltaTime で補間する。
/// Button の Transition は None にして使う（色を本コンポーネントが制御するため）。
/// </summary>
[RequireComponent(typeof(Button))]
public class UIButtonFX : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [SerializeField] private Sprite hoverSprite;
    [Tooltip("interactable=false（ロック中）のときのスプライト。未設定なら通常スプライトのまま暗くする")]
    [SerializeField] private Sprite lockedSprite;
    [SerializeField, Range(0.3f, 1f)] private float lockedBrightness = 0.55f;
    [SerializeField, Range(0.5f, 1f)] private float pressedScale = 0.92f;
    [SerializeField, Range(0.3f, 1f)] private float pressedBrightness = 0.75f;
    [SerializeField] private float speed = 18f;

    private Graphic graphic;
    private Image image;
    private Button button;
    private Sprite normalSprite;
    private Color normalColor;
    private Vector3 normalScale;
    private bool hovering, pressing;
    private float t; // 0=通常, 1=押下中
    private bool lastInteractable;

    private void Awake()
    {
        button = GetComponent<Button>();
        graphic = button.targetGraphic != null ? button.targetGraphic : GetComponent<Graphic>();
        image = graphic as Image;
        if (image != null) normalSprite = image.sprite;
        if (graphic != null) normalColor = graphic.color;
        normalScale = transform.localScale;

        // 子のラベルが大きく拡大されていると、ボタンの外側でも子がレイキャストを受けて
        // ホバー判定が広がってしまうので、子の Graphic は判定から外す（判定はボタン本体の画像のみ）。
        foreach (var g in GetComponentsInChildren<Graphic>(true))
            if (g.gameObject != gameObject) g.raycastTarget = false;
    }

    private void OnEnable()
    {
        hovering = pressing = false;
        t = 0f;
        lastInteractable = button.interactable;
        Apply();
    }

    private void OnDisable() => Apply();

    private void Update()
    {
        if (button.interactable != lastInteractable)
        {
            lastInteractable = button.interactable;
            pressing = false;
            t = 0f;
            Apply();
        }
        float target = pressing ? 1f : 0f;
        if (Mathf.Approximately(t, target)) return;
        t = Mathf.MoveTowards(t, target, speed * Time.unscaledDeltaTime);
        Apply();
    }

    private void Apply()
    {
        bool locked = !button.interactable;
        transform.localScale = normalScale * Mathf.Lerp(1f, pressedScale, t);
        if (graphic != null)
        {
            float b = locked ? (lockedSprite != null ? 1f : lockedBrightness) : Mathf.Lerp(1f, pressedBrightness, t);
            graphic.color = new Color(normalColor.r * b, normalColor.g * b, normalColor.b * b, normalColor.a);
        }
        if (image != null)
        {
            Sprite s = normalSprite;
            if (locked) { if (lockedSprite != null) s = lockedSprite; }
            else if (hovering && isActiveAndEnabled && hoverSprite != null) s = hoverSprite;
            image.sprite = s;
        }
    }

    public void OnPointerEnter(PointerEventData e) { hovering = true; Apply(); }

    public void OnPointerExit(PointerEventData e) { hovering = false; pressing = false; Apply(); }

    public void OnPointerDown(PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Left && button.interactable) pressing = true;
    }

    public void OnPointerUp(PointerEventData e) { pressing = false; }
}
