using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Стрелки к живым врагам, которых игрок сейчас не видит: стрелка у края экрана в сторону врага, повёрнута к нему.
// Как только враг попадает в кадр камеры, стрелка исчезает. Создаётся из LevelManager.BuildUI под Canvas,
// строится в коде (спрайт стрелки - UIShapes.Arrow). Дремлющие враги тоже живые, поэтому показываются.
public class EnemyIndicators : MonoBehaviour
{
    public Color color = new Color(0.93f, 0.05f, 0.05f, 1f);
    [Tooltip("Толщина белой обводки в единицах Canvas.")]
    public float outlineWidth = 3f;
    [Tooltip("Размер стрелки в единицах Canvas.")]
    public float size = 90f;
    [Tooltip("Отступ центра стрелки от края экрана, доля высоты экрана.")]
    public float marginFraction = 0.07f;

    private readonly Dictionary<EnemyDeath, Image> arrows = new Dictionary<EnemyDeath, Image>();
    private readonly List<EnemyDeath> stale = new List<EnemyDeath>();
    private Camera cam;

    void LateUpdate()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null || Screen.width == 0) return;

        // Чистим стрелки погибших/уничтоженных врагов.
        stale.Clear();
        foreach (var kv in arrows)
            if (kv.Key == null || !EnemyDeath.Alive.Contains(kv.Key)) stale.Add(kv.Key);
        foreach (var e in stale)
        {
            if (arrows[e] != null) Destroy(arrows[e].gameObject);
            arrows.Remove(e);
        }

        bool hideAll = PlayerHealth.IsDead || LevelManager.Finished;
        float my = marginFraction;
        float mx = marginFraction * Screen.height / Screen.width;

        foreach (var e in EnemyDeath.Alive)
        {
            if (e == null) continue;
            if (!arrows.TryGetValue(e, out var img) || img == null)
            {
                img = NewArrow();
                arrows[e] = img;
            }

            Vector3 vp = cam.WorldToViewportPoint(e.transform.position);
            bool visible = vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f;
            if (hideAll || visible) { img.enabled = false; continue; }

            Vector2 v = new Vector2(vp.x - 0.5f, vp.y - 0.5f);
            // Точка на рамке (центр экрана -> враг), упирается в край с отступом.
            float t = Mathf.Min((0.5f - mx) / (Mathf.Abs(v.x) + 1e-5f), (0.5f - my) / (Mathf.Abs(v.y) + 1e-5f));
            Vector2 p = new Vector2(0.5f, 0.5f) + v * t;

            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = p;
            rt.anchoredPosition = Vector2.zero;
            // Направление считаем в пикселях, чтобы угол не зависел от пропорций экрана.
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(v.y * Screen.height, v.x * Screen.width) * Mathf.Rad2Deg);
            img.enabled = true;
        }
    }

    Image NewArrow()
    {
        var go = new GameObject("EnemyArrow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(transform, false);
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(size, size);
        var img = go.GetComponent<Image>();
        img.sprite = UIShapes.Arrow(); // остриё вправо (+X)
        img.color = color;
        img.raycastTarget = false;
        var outline = go.AddComponent<Outline>(); // тонкая белая обводка по контуру спрайта
        outline.effectColor = Color.white;
        outline.effectDistance = new Vector2(outlineWidth, outlineWidth);
        outline.useGraphicAlpha = false;
        img.enabled = false;
        return img;
    }
}
