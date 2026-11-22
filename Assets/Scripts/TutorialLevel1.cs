using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Обучение на первом уровне. Враг в сцене выключен; игрок заморожен и экран затемнён (как в паузе):
//   1) текст про нож (тап - дальше);
//   2) текст про прицеливание + стрелка на джойстик прицеливания; игрок тянет джойстик - пауза снимается;
//   3) игрок бросает нож и ждёт, пока он дорикошетит (исчезнет);
//   4) снова пауза: текст про перемещение, стрелка и рамка вокруг зоны динамического джойстика;
//   5) игрок тронулся - пауза снимается; через enemyDelay секунд враг включается на своём месте, игрок
//      замораживается, камера плавно переезжает на врага; тексты «первый враг» и «удары можно парировать»
//      (тап - дальше), затем мини-игра парирования в паузе, пока игрок не попадёт в квадратик;
//   6) камера возвращается к игроку, время идёт, враг бежит на игрока.
// Если игрок погиб и уровень перезапущен (Completed), обучение не повторяется: враг сразу на месте.
// Интерфейс строится в коде под Canvas (обычный Text, как и остальной UI).
public class TutorialLevel1 : MonoBehaviour
{
    [Tooltip("Враг, который появится после первого шага игрока. Должен стоять в сцене (выключенным или включённым - при старте он выключается).")]
    public GameObject enemy;

    [TextArea] public string weaponText = "Ваше оружие - это метательный нож\nОн становится тем смертоноснее, чем больше отскакивает от различных поверхностей";
    [TextArea] public string aimText = "Чтобы прицелиться, потяните ползунок прицеливания";
    [TextArea] public string moveText = "Чтобы перемещаться, используйте джойстик в левой части экрана";
    [TextArea] public string enemyText = "Вот и ваш первый враг - мечник";
    [TextArea] public string parryNoteText = "Не забывайте, что удары мечника можно спарировать, если на момент его удара вы стоите лицом к нему и не целитесь";
    [TextArea] public string parryGameText = "Попробуйте спарировать удар: нажмите по экрану, когда круг будет в зелёной зоне";
    public string continueHint = "Коснитесь экрана, чтобы продолжить";

    [Tooltip("Через сколько секунд после первого шага игрока появляется враг.")]
    public float enemyDelay = 1f;
    [Tooltip("За сколько секунд камера переезжает на врага и обратно к игроку.")]
    public float cameraPanTime = 0.6f;
    [Range(0f, 1f)] public float overlayAlpha = 0.7f;
    public Color accent = new Color(1f, 0.85f, 0.2f, 1f);
    [Tooltip("Отклонение джойстика, с которого считается, что игрок начал тянуть его.")]
    public float pullThreshold = 0.15f;
    [Tooltip("Высота текста над центром экрана во время мини-игры парирования (дуга лежит ниже центра).")]
    public float parryGameTextHeight = 170f;

    // Время стоит под затемнением (пауза обучения): игровая скорость не должна его размораживать.
    public static bool Pausing { get; private set; }
    // Игрок ничего не делает (тексты про нож, врага и парирование, сама мини-игра).
    public static bool FreezeInput { get; private set; }
    // Ходить нельзя (учим сначала бросать нож).
    public static bool LockMove { get; private set; }
    // Обучение пройдено в этой игре: после перезапуска уровня оно не повторяется. Сбрасывается из меню.
    public static bool Completed;

    private enum Step { Weapon, Aim, Throw, Move, Wait, EnemyIntro, ParryNote, ParryGame, ReturnCamera, Done }
    private Step step;

    private TestScript player;
    private CameraFollow cameraFollow;
    private StartFreeze startFreeze;
    private RectTransform canvasRt;
    private Image overlay;
    private Text label, hint;
    private RectTransform arrow;
    private RectTransform frame;
    private int stepFrame;
    private float stepTime;
    private bool sawThrownKnife;
    private bool tapQueued;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Pausing = FreezeInput = LockMove = Completed = false;
    }

    void Start()
    {
        if (Completed)
        {
            if (enemy != null) enemy.SetActive(true);
            Destroy(this);
            return;
        }

        player = FindFirstObjectByType<TestScript>();
        var canvas = FindFirstObjectByType<Canvas>();
        if (player == null || canvas == null) { if (enemy != null) enemy.SetActive(true); Destroy(this); return; }
        canvasRt = (RectTransform)canvas.transform;
        if (Camera.main != null) cameraFollow = Camera.main.GetComponent<CameraFollow>();
        if (enemy != null) enemy.SetActive(false);

        // Время и заморозку ведёт обучение, а не StartFreeze (иначе первый же тап по тексту снял бы паузу).
        startFreeze = FindFirstObjectByType<StartFreeze>();
        if (startFreeze != null) startFreeze.enabled = false;
        Time.timeScale = 0f;

        BuildUI(canvas.transform);
        SetStep(Step.Weapon);
    }

    void OnDestroy()
    {
        Pausing = FreezeInput = LockMove = false;
    }

    // Программный тап для тестов (в редакторе через MCP ввод Input System не проходит).
    public void Advance() { tapQueued = true; }

    void Update()
    {
        if (step == Step.Done || player == null) return;
        bool fresh = Time.frameCount != stepFrame; // тап в кадр смены шага не считается
        stepTime += Time.unscaledDeltaTime;
        bool tap = fresh && (tapQueued || TapThisFrame());

        switch (step)
        {
            case Step.Weapon:
                if (tap) SetStep(Step.Aim);
                break;

            case Step.Aim:
                if (player.aimJoystick != null && player.aimJoystick.Direction.magnitude > pullThreshold)
                {
                    // Дальше прицеливание и замедление ведёт TestScript (он сам выставляет timeScale).
                    SetStep(Step.Throw);
                    if (!player.IsAiming) Resume();
                }
                break;

            case Step.Throw:
                bool thrown = false;
                foreach (var k in FindObjectsByType<Knife>(FindObjectsSortMode.None))
                    if (k.GetComponent<Rigidbody2D>().bodyType == RigidbodyType2D.Dynamic) thrown = true;
                if (thrown) sawThrownKnife = true;
                else if (sawThrownKnife && !player.IsAiming) SetStep(Step.Move);
                break;

            case Step.Move:
                if (player.moveJoystick != null && player.moveJoystick.Direction.magnitude > pullThreshold)
                {
                    Resume();
                    SetStep(Step.Wait);
                }
                break;

            case Step.Wait:
                // Игрок идёт, а враг появится через секунду.
                if (stepTime >= enemyDelay)
                {
                    if (enemy != null)
                    {
                        enemy.SetActive(true);
                        if (cameraFollow != null) cameraFollow.PanTo(enemy.transform, cameraPanTime);
                    }
                    SetStep(Step.EnemyIntro);
                }
                break;

            case Step.EnemyIntro:
                if (tap) SetStep(Step.ParryNote);
                break;

            case Step.ParryNote:
                if (tap) SetStep(Step.ParryGame);
                break;

            case Step.ParryGame:
                break; // ждём успеха мини-игры (ParrySystem вызовет OnParrySuccess)

            case Step.ReturnCamera:
                if (stepTime >= cameraPanTime)
                {
                    Resume();
                    SetStep(Step.Done);
                }
                break;
        }
        tapQueued = false;
    }

    void SetStep(Step next)
    {
        step = next;
        stepFrame = Time.frameCount;
        stepTime = 0f;
        sawThrownKnife = false;

        bool overlayOn = next == Step.Weapon || next == Step.Aim || next == Step.Move || next == Step.EnemyIntro || next == Step.ParryNote || next == Step.ParryGame;
        bool paused = overlayOn || next == Step.ReturnCamera;
        Pausing = paused;
        FreezeInput = next == Step.Weapon || next == Step.EnemyIntro || next == Step.ParryNote || next == Step.ParryGame || next == Step.ReturnCamera;
        LockMove = next == Step.Aim || next == Step.Throw;
        if (paused) Time.timeScale = 0f;

        overlay.enabled = overlayOn;
        label.enabled = overlayOn;
        hint.enabled = next == Step.Weapon || next == Step.EnemyIntro || next == Step.ParryNote;
        arrow.gameObject.SetActive(next == Step.Aim || next == Step.Move);
        frame.gameObject.SetActive(next == Step.Move);
        if (next == Step.Done) return;

        if (next == Step.ParryGame)
        {
            if (ParrySystem.Instance != null) ParrySystem.Instance.BeginTutorial(OnParrySuccess);
            else { OnParrySuccess(); return; }
        }
        if (next == Step.ReturnCamera && cameraFollow != null && player != null) cameraFollow.PanTo(player.transform, cameraPanTime);
        if (!overlayOn) return;

        label.rectTransform.anchoredPosition = new Vector2(0f, next == Step.ParryGame ? parryGameTextHeight : 0f);
        label.text = TextFor(next);
        label.transform.SetAsLastSibling();
        hint.transform.SetAsLastSibling();

        if (next == Step.Aim && player.aimJoystick != null)
        {
            // Стрелка сверху-слева вниз-вправо, остриём к краю джойстика прицеливания.
            Rect r = LocalRect((RectTransform)player.aimJoystick.transform);
            PlaceArrow(r.center - new Vector2(0.7071f, -0.7071f) * (r.width * 0.5f + 10f), -45f);
        }
        else if (next == Step.Move && player.moveJoystick != null)
        {
            Rect zone = LocalRect((RectTransform)player.moveJoystick.transform);
            BuildFrame(zone);
            // Ниже текста (он по центру экрана), чтобы стрелка его не перекрывала.
            PlaceArrow(new Vector2(zone.center.x - 80f, zone.yMin + 150f), 225f);
        }
        if (arrow.gameObject.activeSelf) arrow.SetAsLastSibling();
        if (frame.gameObject.activeSelf) frame.SetAsLastSibling();
    }

    string TextFor(Step s)
    {
        switch (s)
        {
            case Step.Weapon: return weaponText;
            case Step.Aim: return aimText;
            case Step.Move: return moveText;
            case Step.EnemyIntro: return enemyText;
            case Step.ParryNote: return parryNoteText;
            default: return parryGameText;
        }
    }

    // Мини-игра парирования завершена удачным попаданием: возвращаем камеру к игроку, потом идёт время.
    void OnParrySuccess()
    {
        Completed = true;
        SetStep(Step.ReturnCamera);
    }

    void Resume()
    {
        Pausing = false;
        FreezeInput = false;
        Time.timeScale = GameSpeed.Multiplier;
        Time.fixedDeltaTime = 0.02f * GameSpeed.Multiplier;
    }

    static bool TapThisFrame()
    {
        var screen = Touchscreen.current;
        if (screen != null)
            foreach (var t in screen.touches)
                if (t.press.wasPressedThisFrame) return true;
        var pointer = Pointer.current;
        return pointer != null && pointer.press.wasPressedThisFrame;
    }

    // --- Интерфейс ---

    // Прямоугольник элемента в координатах канваса (начало - центр канваса).
    Rect LocalRect(RectTransform rt)
    {
        var c = new Vector3[4];
        rt.GetWorldCorners(c);
        Vector2 a = canvasRt.InverseTransformPoint(c[0]);
        Vector2 b = canvasRt.InverseTransformPoint(c[2]);
        return Rect.MinMaxRect(a.x, a.y, b.x, b.y);
    }

    void PlaceArrow(Vector2 tip, float angleDeg)
    {
        float rad = angleDeg * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        arrow.anchoredPosition = tip - dir * (arrow.sizeDelta.x * 0.5f);
        arrow.localRotation = Quaternion.Euler(0f, 0f, angleDeg);
    }

    void BuildFrame(Rect zone)
    {
        const float t = 8f;
        SetEdge(frame.GetChild(0), new Vector2(zone.center.x, zone.yMax - t * 0.5f), new Vector2(zone.width, t));
        SetEdge(frame.GetChild(1), new Vector2(zone.center.x, zone.yMin + t * 0.5f), new Vector2(zone.width, t));
        SetEdge(frame.GetChild(2), new Vector2(zone.xMin + t * 0.5f, zone.center.y), new Vector2(t, zone.height));
        SetEdge(frame.GetChild(3), new Vector2(zone.xMax - t * 0.5f, zone.center.y), new Vector2(t, zone.height));
    }

    static void SetEdge(Transform edge, Vector2 pos, Vector2 size)
    {
        var rt = (RectTransform)edge;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    void BuildUI(Transform canvas)
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // Затемнение под всем остальным интерфейсом: джойстики остаются яркими и их видно.
        overlay = NewImage("TutorialOverlay", canvas, null, new Color(0f, 0f, 0f, overlayAlpha));
        var ort = overlay.rectTransform;
        ort.anchorMin = Vector2.zero; ort.anchorMax = Vector2.one; ort.offsetMin = ort.offsetMax = Vector2.zero;
        overlay.transform.SetAsFirstSibling();

        label = NewText("TutorialText", canvas, font, 54, new Vector2(0f, 0f), new Vector2(1500f, 420f), TextAnchor.MiddleCenter);
        label.color = Color.white;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.lineSpacing = 1.15f;

        hint = NewText("TutorialHint", canvas, font, 36, new Vector2(0f, -330f), new Vector2(1200f, 60f), TextAnchor.MiddleCenter);
        hint.text = continueHint;
        hint.color = new Color(1f, 1f, 1f, 0.65f);

        var arrowImg = NewImage("TutorialArrow", canvas, UIShapes.Arrow(), accent);
        arrow = arrowImg.rectTransform;
        arrow.sizeDelta = new Vector2(200f, 200f);

        var frameGo = new GameObject("TutorialFrame", typeof(RectTransform));
        frameGo.transform.SetParent(canvas, false);
        frame = (RectTransform)frameGo.transform;
        frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(0.5f, 0.5f);
        for (int i = 0; i < 4; i++) NewImage("Edge" + i, frame, null, accent);
    }

    static Image NewImage(string name, Transform parent, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false; // не перехватываем касания: игрок тянет джойстики сквозь подсказку
        return img;
    }

    static Text NewText(string name, Transform parent, Font font, int size, Vector2 pos, Vector2 dim, TextAnchor align)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = dim;
        var t = go.GetComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.alignment = align;
        t.raycastTarget = false;
        return t;
    }
}
