using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

public class CameraFollow : MonoBehaviour
{
    public Transform target;
    public float lookSensitivity = 0.3f;
    public float lookResetDelay = 1.5f;
    public float lookResetSpeedDegPerSec = 180f;

    [Tooltip("How much each scroll tick changes zoom. Tune to taste - the new Input System's raw scroll delta varies by OS/mouse.")]
    public float zoomSpeed = 0.5f;

    [Tooltip("Furthest the camera can zoom out, as a multiple of whatever orthographic size it starts with.")]
    public float maxZoomOutMultiplier = 2f;

    private float rotationOffset = 0f;
    private float idleTimer = 0f;
    private bool dragging = false;
    private bool wasPressedLastFrame = false;

    private Camera cam;
    private float baseOrthographicSize;
    // Множитель размера от колеса мыши (1 = базовый размер из сложности); поверх применяется отдаление при прицеливании.
    private float scrollMultiplier = 1f;
    private float aimZoomMultiplier = 1f;
    private TestScript player;
    private Vector2 lookOffsetSmoothed;

    [Tooltip("Как быстро камера смещается в режиме обзора (чем больше, тем резче).")]
    public float lookSmooth = 12f;

    [Tooltip("Как быстро камера следует за отдалением/приближением при прицеливании (чем больше, тем резче).")]
    public float aimZoomSmooth = 8f;

    // Плавный перенос камеры на другую цель (обучение): считается по нескалированному времени,
    // чтобы работать и в паузе.
    private Vector3 panStart;
    private float panElapsed;
    private float panDuration;

    // Переключает цель камеры; за seconds секунд камера плавно переезжает на неё (0 - сразу).
    public void PanTo(Transform newTarget, float seconds)
    {
        panStart = transform.position;
        target = newTarget;
        panElapsed = 0f;
        panDuration = Mathf.Max(0f, seconds);
    }

    void Awake()
    {
        cam = GetComponent<Camera>();
        baseOrthographicSize = cam.orthographicSize; // запасной вариант, пока сложность не применена
    }

    void LateUpdate()
    {
        UpdateZoom();
        UpdateAimZoom();

        if (target == null) return;

        Vector3 pos = transform.position;
        if (panElapsed < panDuration)
        {
            panElapsed += Time.unscaledDeltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(panElapsed / panDuration));
            pos.x = Mathf.Lerp(panStart.x, target.position.x, k);
            pos.y = Mathf.Lerp(panStart.y, target.position.y, k);
        }
        else
        {
            pos.x = target.position.x;
            pos.y = target.position.y;
        }
        // Режим обзора (эксперимент): камера смещается от игрока на LookOffset, плавно, по нескалированному времени.
        if (player == null) player = FindFirstObjectByType<TestScript>();
        Vector2 lookTarget = player != null && player.IsLooking ? player.LookOffset : Vector2.zero;
        lookOffsetSmoothed = Vector2.Lerp(lookOffsetSmoothed, lookTarget, 1f - Mathf.Exp(-lookSmooth * Time.unscaledDeltaTime));
        pos.x += lookOffsetSmoothed.x;
        pos.y += lookOffsetSmoothed.y;
        transform.position = pos;

        UpdateLook();
    }

    void UpdateZoom()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) < 0.01f) return;
        if (IsOverUI(mouse.position.ReadValue())) return;

        // zoomSpeed - в единицах размера камеры на тик колеса; переводим в множитель относительно базового размера.
        scrollMultiplier = Mathf.Clamp(scrollMultiplier - scroll * zoomSpeed / Mathf.Max(0.01f, baseOrthographicSize), 1f, maxZoomOutMultiplier);
    }

    // Отдаление при прицеливании (эксперимент из фидбэка): чем дальше отведён стик прицела, тем дальше камера,
    // до DifficultySettings.aimUnzoomMax. Итоговый размер = размер от колеса * множитель.
    void UpdateAimZoom()
    {
        // Базовый размер камеры - из сложности (Camera Size), применяется каждый кадр, поэтому правки видны сразу.
        baseOrthographicSize = Difficulty.Current.cameraSize;
        if (player == null) player = FindFirstObjectByType<TestScript>();
        float max = Mathf.Max(1f, Difficulty.Current.aimUnzoomMax);
        bool aiming = player != null && player.IsAiming && !PlayerHealth.IsDead;
        float target = aiming ? Mathf.Lerp(1f, max, player.AimDeflection) : 1f;
        // Unscaled: во время прицеливания время замедлено, а камера должна реагировать как обычно.
        aimZoomMultiplier = Mathf.Lerp(aimZoomMultiplier, target, 1f - Mathf.Exp(-aimZoomSmooth * Time.unscaledDeltaTime));
        cam.orthographicSize = baseOrthographicSize * scrollMultiplier * aimZoomMultiplier;
    }

    void UpdateLook()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        bool isPressed = mouse.leftButton.isPressed;
        if (isPressed && !wasPressedLastFrame)
        {
            dragging = !IsOverUI(mouse.position.ReadValue());
        }
        if (!isPressed)
        {
            dragging = false;
        }
        wasPressedLastFrame = isPressed;

        bool changed = false;
        if (dragging)
        {
            Vector2 delta = mouse.delta.ReadValue();
            if (Mathf.Abs(delta.x) > 0.01f)
            {
                rotationOffset -= delta.x * lookSensitivity;
                changed = true;
            }
        }

        if (changed)
        {
            idleTimer = 0f;
        }
        else
        {
            idleTimer += Time.deltaTime;
            if (idleTimer >= lookResetDelay)
            {
                rotationOffset = Mathf.MoveTowardsAngle(rotationOffset, 0f, lookResetSpeedDegPerSec * Time.deltaTime);
            }
        }

        transform.rotation = Quaternion.Euler(0f, 0f, rotationOffset);
    }

    private static bool IsOverUI(Vector2 screenPos)
    {
        if (EventSystem.current == null) return false;
        PointerEventData ped = new PointerEventData(EventSystem.current) { position = screenPos };
        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(ped, results);
        return results.Count > 0;
    }
}
