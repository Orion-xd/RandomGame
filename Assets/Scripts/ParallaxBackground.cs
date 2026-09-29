using System;
using UnityEngine;

/// <summary>
/// 2Dカメラの移動量を、近景・中景・遠景へそれぞれ異なる割合で反映する。
/// Camera Followが0の場合はワールドに固定され、画面上では最も速く流れる。
/// 1の場合はカメラと同じ速度で移動し、画面上では停止しているように見える。
/// </summary>
public sealed class ParallaxBackground : MonoBehaviour
{
    [Serializable]
    private struct LayerSettings
    {
        [Tooltip("Hierarchyに生成されるレイヤー名")]
        public string name;

        [Tooltip("このレイヤーで使用する背景画像。森や洞窟など、任意のSpriteに変更できます。")]
        public Sprite sprite;

        [Range(0f, 1f)]
        [Tooltip("カメラの移動を反映する割合。0では画面上を速く流れ、1では画面に固定されます。")]
        public float cameraFollow;

        [Tooltip("有効にすると画像を左右に3枚配置し、無限に繰り返します。")]
        public bool repeatHorizontally;

        [Tooltip("画面中央を基準としたレイヤーの垂直位置補正")]
        public float verticalOffset;

        [Tooltip("Sprite Rendererの描画順。値が大きいほど手前に描画されます。")]
        public int sortingOrder;
    }

    [Header("追従対象")]
    [Tooltip("未設定の場合はMain Cameraを自動的に使用します。")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private bool followHorizontal = true;

    [Header("3段階パララックス")]
    [SerializeField] private LayerSettings foreground = new LayerSettings
    {
        name = "近景",
        cameraFollow = 0.15f,
        repeatHorizontally = true,
        sortingOrder = -10
    };

    [SerializeField] private LayerSettings midground = new LayerSettings
    {
        name = "中景",
        cameraFollow = 0.55f,
        repeatHorizontally = true,
        sortingOrder = -20
    };

    [SerializeField] private LayerSettings background = new LayerSettings
    {
        name = "遠景",
        cameraFollow = 0.9f,
        repeatHorizontally = true,
        sortingOrder = -30
    };

    private LayerRuntime[] _layers;
    private Vector3 _cameraStartPosition;

    private sealed class LayerRuntime
    {
        public Transform root;
        public float cameraFollow;
        public bool repeatHorizontally;
        public float tileWidth;
        public float verticalOffset;
        public Vector3 startPosition;
    }

    private void Awake()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera == null)
        {
            Debug.LogWarning("ParallaxBackground: Main Cameraが見つかりません。", this);
            enabled = false;
            return;
        }

        _cameraStartPosition = targetCamera.transform.position;
        _layers = new[]
        {
            CreateLayer(background),
            CreateLayer(midground),
            CreateLayer(foreground)
        };
    }

    private LayerRuntime CreateLayer(LayerSettings settings)
    {
        if (settings.sprite == null) return null;

        var root = new GameObject(string.IsNullOrWhiteSpace(settings.name) ? "パララックスレイヤー" : settings.name).transform;
        root.SetParent(transform, false);

        float cameraHeight = targetCamera.orthographicSize * 2f;
        float cameraWidth = cameraHeight * targetCamera.aspect;
        float spriteHeight = Mathf.Max(settings.sprite.bounds.size.y, 0.001f);
        float spriteWidth = Mathf.Max(settings.sprite.bounds.size.x, 0.001f);

        // 繰り返すレイヤーは高さを画面に合わせ、元画像の縦横比を維持する。
        // 繰り返さない設定では、画面全体を覆うように幅と高さの大きい方の倍率を使用する。
        float scaleByHeight = cameraHeight / spriteHeight;
        float scale = settings.repeatHorizontally
            ? scaleByHeight
            : Mathf.Max(scaleByHeight, cameraWidth / spriteWidth);
        float tileWidth = spriteWidth * scale;
        Vector3 pivotOffset = -settings.sprite.bounds.center * scale;

        int firstTile = settings.repeatHorizontally ? -1 : 0;
        int lastTile = settings.repeatHorizontally ? 1 : 0;
        for (int x = firstTile; x <= lastTile; x++)
        {
            var tile = new GameObject(settings.repeatHorizontally ? $"Tile_{x}" : "Image");
            tile.transform.SetParent(root, false);
            tile.transform.localScale = Vector3.one * scale;
            tile.transform.localPosition = new Vector3(x * tileWidth, 0f, 0f) + pivotOffset;

            var renderer = tile.AddComponent<SpriteRenderer>();
            renderer.sprite = settings.sprite;
            renderer.sortingOrder = settings.sortingOrder;
        }

        Vector3 cameraPosition = targetCamera.transform.position;
        float initialY = cameraPosition.y + settings.verticalOffset;
        root.position = new Vector3(cameraPosition.x, initialY, 0f);

        return new LayerRuntime
        {
            root = root,
            cameraFollow = settings.cameraFollow,
            repeatHorizontally = settings.repeatHorizontally,
            tileWidth = tileWidth,
            verticalOffset = settings.verticalOffset,
            startPosition = root.position
        };
    }

    private void LateUpdate()
    {
        if (targetCamera == null || _layers == null) return;

        Vector3 cameraPosition = targetCamera.transform.position;
        Vector3 cameraDelta = cameraPosition - _cameraStartPosition;

        foreach (LayerRuntime layer in _layers)
        {
            if (layer == null) continue;

            float x = followHorizontal
                ? layer.startPosition.x + cameraDelta.x * layer.cameraFollow
                : layer.startPosition.x;
            // 近景・中景・遠景の区別なく、Y座標は常にメインカメラへ1:1で追従する。
            float y = cameraPosition.y + layer.verticalOffset;

            // 同じ画像3枚をカメラ付近へ再配置し、左右の移動量に関係なく無限に繰り返す。
            if (followHorizontal && layer.repeatHorizontally && layer.tileWidth > 0f)
            {
                x += Mathf.Round((cameraPosition.x - x) / layer.tileWidth) * layer.tileWidth;
            }

            layer.root.position = new Vector3(x, y, 0f);
        }
    }
}
