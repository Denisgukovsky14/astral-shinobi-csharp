using UnityEngine;
using UnityEngine.EventSystems;

// Показывает, лежит ли сейчас палец (или зажата кнопка мыши) на стике прицеливания, и где именно палец относительно
// стика. Joystick Pack сам этого не отдаёт (только Direction, зажатое в круг), а TestScript'у нужно:
//  - отличать «стик вернули в центр, не отпуская» от «палец убрали» (концентрация запускается заново только после
//    того, как палец был убран);
//  - знать, насколько палец вышел ЗА круг стика (режим обзора, DifficultySettings.aimLookEnabled).
// Вешается на объект стика прицеливания автоматически (TestScript.Awake).
public class AimStickHold : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
{
    public bool Held { get; private set; }

    // Расстояние пальца от центра стика / радиус стика: 1 = край круга, больше 1 = палец вышел за круг.
    public float RawRatio { get; private set; }
    // Направление от центра стика к пальцу в экранных координатах (нормализованное).
    public Vector2 RawDirection { get; private set; }

    private RectTransform rect;
    private Canvas canvas;

    public void OnPointerDown(PointerEventData eventData) { Held = true; Track(eventData); }

    public void OnDrag(PointerEventData eventData) { Track(eventData); }

    public void OnPointerUp(PointerEventData eventData)
    {
        Held = false;
        RawRatio = 0f;
        RawDirection = Vector2.zero;
    }

    void OnDisable()
    {
        Held = false;
        RawRatio = 0f;
    }

    void Track(PointerEventData e)
    {
        if (rect == null) rect = (RectTransform)transform;
        if (canvas == null) canvas = GetComponentInParent<Canvas>();
        // Тот же центр и радиус, что считает сам Joystick: центр фона в экранных координатах, радиус = половина размера * scaleFactor.
        Vector2 center = RectTransformUtility.WorldToScreenPoint(null, rect.position);
        float scale = canvas != null ? canvas.scaleFactor : 1f;
        float radius = Mathf.Max(1f, rect.rect.width * 0.5f * scale);
        Vector2 d = e.position - center;
        RawRatio = d.magnitude / radius;
        RawDirection = d.sqrMagnitude > 0.0001f ? d.normalized : Vector2.zero;
    }
}
