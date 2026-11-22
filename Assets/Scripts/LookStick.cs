using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// ЭКСПЕРИМЕНТ «режим обзора»: отдельный независимый стик с глазиком слева от стика прицеливания, чуть меньше него.
// Пока палец на нём: время стоит, весь экран затемнён, траектория не рисуется, а камера смещается от игрока в сторону
// отклонения стика (на DifficultySettings.aimLookPanRadius при полном отклонении). Прицеливание от него не зависит.
//
// Объект стика лежит в сцене в Canvas (виден и двигается в редакторе, как Fixed_Joystick_Aim). Если в сцене его нет,
// LevelManager создаёт его из Resources/LookStick.prefab и ставит слева от стика прицеливания (autoPlace).
// Убрать эксперимент: снять галку DifficultySettings.aimLookEnabled (стик на старте удалится) или удалить этот файл,
// объект стика, TestScript.IsLooking/LookOffset и смещение в CameraFollow.
public class LookStick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public static LookStick Instance { get; private set; }

    [Tooltip("Размер стика обзора относительно стика прицеливания (0.6 = на 40% меньше). Используется только при автоматической расстановке.")]
    public float sizeRatio = 0.6f;
    [Tooltip("Где стоит центр стика обзора относительно центра стика прицеливания, в единицах канваса (X вправо, Y вверх). Только при автоматической расстановке. По умолчанию - раскладка Дениса с Level_4: правее и ниже прицела, у угла экрана.")]
    public Vector2 placeOffset = new Vector2(223f, -156f);
    [Tooltip("Ставить стик слева от стика прицеливания на старте уровня. Выключено у объекта, который стоит в сцене: тогда он остаётся там, куда его поставили руками.")]
    public bool autoPlace = true;
    [Tooltip("Ручка с глазиком (двигается внутри круга).")]
    public RectTransform knob;
    [Tooltip("Непрозрачность чёрного затемнения экрана в режиме обзора (0.3 = 30%).")]
    [Range(0f, 1f)] public float dimAlpha = 0.3f;
    [Tooltip("Как быстро затемнение появляется и пропадает, секунд (реальное время).")]
    public float dimFade = 0.12f;

    // Палец на стике и его отклонение: длина 0..1, направление в экранных осях.
    public bool Held { get; private set; }
    public Vector2 Value { get; private set; }

    private RectTransform rt;
    private Canvas canvas;
    private CanvasGroup dim;

    void Start()
    {
        var ts = FindFirstObjectByType<TestScript>();
        if (!Difficulty.Current.aimLookEnabled || ts == null || ts.aimJoystick == null) { Destroy(gameObject); return; }
        Instance = this;
        rt = (RectTransform)transform;
        canvas = GetComponentInParent<Canvas>();
        if (knob == null) knob = (RectTransform)transform.Find("Knob");
        if (autoPlace) PlaceNextTo((RectTransform)ts.aimJoystick.transform);

        // Затемнение на весь экран: самым нижним слоем Canvas, под HUD, не ловит касания.
        var dimGo = new GameObject("LookDim", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
        dimGo.transform.SetParent(transform.parent, false);
        var drt = (RectTransform)dimGo.transform;
        drt.anchorMin = Vector2.zero; drt.anchorMax = Vector2.one; drt.offsetMin = drt.offsetMax = Vector2.zero;
        var dimImg = dimGo.GetComponent<Image>();
        dimImg.color = new Color(0f, 0f, 0f, dimAlpha);
        dimImg.raycastTarget = false;
        dim = dimGo.GetComponent<CanvasGroup>();
        dim.alpha = 0f;
        dim.blocksRaycasts = false;
        dimGo.transform.SetAsFirstSibling();
    }

    // Ставит стик рядом с заданным (те же якоря, размер sizeRatio от его размера, смещение placeOffset). Зовётся на старте
    // (autoPlace) и из редакторского скрипта, который кладёт стик в сцену.
    public void PlaceNextTo(RectTransform joy)
    {
        var r = (RectTransform)transform;
        float size = joy.rect.width * sizeRatio;
        r.anchorMin = joy.anchorMin; r.anchorMax = joy.anchorMax; r.pivot = new Vector2(0.5f, 0.5f);
        r.sizeDelta = new Vector2(size, size);
        Vector2 joyCenter = joy.anchoredPosition + Vector2.Scale(new Vector2(0.5f, 0.5f) - joy.pivot, joy.sizeDelta);
        r.anchoredPosition = joyCenter + placeOffset;
        var k = knob != null ? knob : (RectTransform)transform.Find("Knob");
        if (k != null) k.sizeDelta = new Vector2(size * 0.6f, size * 0.6f);
    }

    void Update()
    {
        if (dim == null) return;
        float step = dimFade > 0f ? Time.unscaledDeltaTime / dimFade : 1f;
        dim.alpha = Mathf.MoveTowards(dim.alpha, Held ? 1f : 0f, step);
    }

    public void OnPointerDown(PointerEventData e) { Held = true; Track(e); }
    public void OnDrag(PointerEventData e) { Track(e); }

    public void OnPointerUp(PointerEventData e)
    {
        Held = false;
        Value = Vector2.zero;
        if (knob != null) knob.anchoredPosition = Vector2.zero;
    }

    void OnDisable()
    {
        Held = false;
        Value = Vector2.zero;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (dim != null) Destroy(dim.gameObject);
    }

    void Track(PointerEventData e)
    {
        float scale = canvas != null ? canvas.scaleFactor : 1f;
        float radiusPx = Mathf.Max(1f, rt.rect.width * 0.5f * scale);
        Vector2 center = RectTransformUtility.WorldToScreenPoint(null, rt.position);
        Value = Vector2.ClampMagnitude((e.position - center) / radiusPx, 1f);
        // Ручка двигается так, чтобы оставаться внутри круга.
        if (knob != null) knob.anchoredPosition = Value * (rt.rect.width * 0.5f - knob.rect.width * 0.5f);
    }

    // Белый круг; при thickness > 0 - только тонкое кольцо этой толщины (в пикселях текстуры).
    // public: картинки генерируются один раз редакторским скриптом и лежат в Assets/Sprites/UI.
    public static Texture2D DiscTexture(int size, float thickness)
    {
        return MakeTexture(size, (x, y) =>
        {
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(size * 0.5f, size * 0.5f));
            float outer = size * 0.5f - 1f;
            float a = Mathf.Clamp01(outer - d + 0.5f);
            if (thickness > 0f) a = Mathf.Min(a, Mathf.Clamp01(d - (outer - thickness) + 0.5f));
            return new Color(1f, 1f, 1f, a);
        });
    }

    // Глазик: серый овал со светлым ободком и белым круглым зрачком в центре.
    public static Texture2D EyeTexture(int size)
    {
        float c = size * 0.5f, a = size * 0.46f, b = size * 0.26f, line = size * 0.06f, iris = size * 0.15f;
        return MakeTexture(size, (x, y) =>
        {
            float dx = x - c, dy = y - c;
            // Расстояние до эллипса, приближённо: (норма - 1) * средняя полуось.
            float n = Mathf.Sqrt(dx * dx / (a * a) + dy * dy / (b * b));
            float edge = (n - 1f) * (a + b) * 0.5f;              // расстояние до края овала, пикселей (<0 внутри)
            float fill = Mathf.Clamp01(0.5f - edge);
            float rim = Mathf.Clamp01(line * 0.5f - Mathf.Abs(edge + line * 0.5f) + 0.5f);
            float disc = Mathf.Clamp01(iris - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
            Color col = Color.Lerp(new Color(0.5f, 0.5f, 0.5f), new Color(0.88f, 0.88f, 0.88f), rim);
            col = Color.Lerp(col, Color.white, disc);
            col.a = fill;
            return col;
        });
    }

    static Texture2D MakeTexture(int size, System.Func<float, float, Color> pixel)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                px[y * size + x] = pixel(x + 0.5f, y + 0.5f);
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }
}
