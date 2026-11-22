using UnityEngine;
using UnityEngine.UI;

// Шкала сосредоточения: тонкая бледно-фиолетовая полоска под полоской хп. Пока игрок целится, она убывает вместе
// с замедлением времени и доходит до нуля, когда время снова идёт в обычном ритме (TestScript.FocusFraction).
// Вне прицеливания скрыта. Создаётся из LevelManager.BuildUI, строится в коде.
public class FocusBar : MonoBehaviour
{
    public Vector2 position = new Vector2(0f, -98f);
    public Vector2 size = new Vector2(600f, 14f);
    public Color backColor = new Color(0f, 0f, 0f, 0.45f);
    public Color fillColor = new Color(0.72f, 0.55f, 0.98f, 0.95f);
    [Tooltip("Как быстро шкала появляется и исчезает, секунд (реального времени).")]
    public float fadeTime = 0.15f;

    private CanvasGroup group;
    private RectTransform fill;
    private TestScript player;

    void Awake()
    {
        group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;

        var back = NewImage("FocusBarBack", transform, backColor);
        var rt = back.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = position;
        rt.sizeDelta = size;

        var f = NewImage("FocusBarFill", rt, fillColor);
        fill = f.rectTransform;
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = Vector2.one;
        fill.offsetMin = new Vector2(3f, 3f);
        fill.offsetMax = new Vector2(-3f, -3f);
    }

    void Update()
    {
        if (player == null) player = FindFirstObjectByType<TestScript>();
        bool show = player != null && player.IsAiming && !PlayerHealth.IsDead && !LevelManager.Finished;
        float step = fadeTime > 0f ? Time.unscaledDeltaTime / fadeTime : 1f;
        group.alpha = Mathf.MoveTowards(group.alpha, show ? 1f : 0f, step);
        if (player != null) fill.anchorMax = new Vector2(Mathf.Clamp01(player.FocusFraction), 1f);
    }

    static Image NewImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var im = go.GetComponent<Image>();
        im.color = color;
        im.raycastTarget = false;
        return im;
    }
}
