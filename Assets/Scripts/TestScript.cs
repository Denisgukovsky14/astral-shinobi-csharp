using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[RequireComponent(typeof(Rigidbody2D))]
public class TestScript : MonoBehaviour
{
    public float moveSpeed = 4f;
    public Joystick moveJoystick;
    public Joystick aimJoystick;
    public GameObject knifePrefab;
    public float aimActivateThreshold = 0.15f;
    public float aimReleaseThreshold = 0.05f;
    public float holdDistance = 0.55f;
    // Замедление при прицеливании и радиус поимки ножа задаются сложностью (DifficultySettings), а не полями здесь.
    float aimTimeScale => Difficulty.Current.aimTimeScale;
    float aimSlowDuration => Difficulty.Current.aimSlowDuration;
    float aimRecoverDuration => Difficulty.Current.aimRecoverDuration;
    float catchZoneRadius => Difficulty.Current.catchRadius;

    public GameObject aimingVignette;
    public float vignetteFadeDuration = 0.15f;
    public Button catchButton;
    // Игрок сейчас целится и держит клинок - в это время парирование недоступно.
    public bool IsAiming => heldKnife != null;

    // 0..1: насколько отведён стик прицела от центра (камера отдаляется по этому значению); 0, если не целимся.
    public float AimDeflection { get; private set; }
    // 1..0: сколько осталось «сосредоточения» (замедления времени) с начала прицеливания; 1, когда не целимся.
    public float FocusFraction { get; private set; } = 1f;
    // ЭКСПЕРИМЕНТ «режим обзора»: палец вышел за круг стика, время стоит, камера смещается на LookOffset (мировые юниты).
    public bool IsLooking { get; private set; }
    public Vector2 LookOffset { get; private set; }
    private bool lookFroze; // время остановлено режимом обзора (нужно вернуть после)

    private const float NormalFixedDeltaTime = 0.02f;

    private Rigidbody2D rb;
    private FadeImage aimingVignetteFader;
    private GameObject heldKnife;
    private Knife heldKnifeScript;
    private GameObject flyingKnife;
    private Knife flyingKnifeScript;
    private Vector2 lastAimDir;
    private bool aimWasActive;
    private bool knifeWasFlying;
    private float cooldownRemaining;
    private float aimSlowTimer;
    // Сессия концентрации: начинается при первом отведении стика и длится, пока палец лежит на стике (даже если стик
    // вернули в центр). Замедление заново запускается только после того, как палец был убран и стик потянули снова.
    private bool concentrationSession;
    private float lastCatchableTime = -999f;
    private AimStickHold aimHold;
    private bool vignetteFadeOutStarted;
    private bool aimFacingActive;
    private float aimFacingAngle;
    private bool wasAimFacing;
    // Куда игрок смотрел по ходу движения в последний раз: после броска он разворачивается обратно туда
    // (на телефоне взгляд всегда совпадает с направлением движения, кроме момента прицеливания).
    private float moveFacingAngle;
    private bool mouseSeen;
    private bool mouseMoved;
    private Vector2 mouseStartPos;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        moveFacingAngle = rb.rotation;
        if (aimingVignette != null) aimingVignetteFader = aimingVignette.GetComponent<FadeImage>();
        if (catchButton != null)
        {
            catchButton.onClick.AddListener(TryCatchKnife);
            // Ловим в момент касания, а не отпускания пальца: на тапе нож успевает улететь (повторный вызов безвреден).
            var trigger = catchButton.GetComponent<UnityEngine.EventSystems.EventTrigger>();
            if (trigger == null) trigger = catchButton.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            var down = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerDown };
            down.callback.AddListener(_ => TryCatchKnife());
            trigger.triggers.Add(down);
        }
        if (aimJoystick != null)
        {
            aimHold = aimJoystick.GetComponent<AimStickHold>();
            if (aimHold == null) aimHold = aimJoystick.gameObject.AddComponent<AimStickHold>();
        }
    }

    void Update()
    {
        // Пока идёт мини-игра парирования, время стоит и игрок ничего не делает.
        if (ParrySystem.Active || PlayerHealth.IsDead || LevelManager.Finished || TutorialLevel1.FreezeInput) return;

        UpdateKnifeCooldown();
        aimFacingActive = false;
        HandleAim();
        UpdateCatchButton();

        UpdateMovementAndFacing();

        // Пока игрок целится (держит джойстик броска и нож в руке), он смотрит в сторону прицела, а не по
        // ходу движения или за мышью: это важно для парирования (оно зависит от того, куда смотрит игрок).
        // Применяется ПОСЛЕ движения, поэтому перекрывает поворот по ходу; после броска остаётся как есть.
        if (aimFacingActive)
        {
            rb.rotation = aimFacingAngle;
        }
        else if (wasAimFacing)
        {
            // Прицеливание закончилось (бросок или отпустили): герой остаётся смотреть туда, куда целился, и
            // это становится его «последним взглядом» - дальше его меняет только ходьба или новый прицел.
            // Если левый джойстик зажат, поворот по ходу применяется уже в следующем кадре.
            moveFacingAngle = aimFacingAngle;
            rb.rotation = moveFacingAngle;
        }
        wasAimFacing = aimFacingActive;
    }

    void UpdateMovementAndFacing()
    {
        // Обучение: пока учат бросать нож, ходить нельзя.
        if (TutorialLevel1.LockMove)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        // Две логики взгляда:
        //  - мышь (ПК и редактор): герой всегда смотрит на курсор, что бы он ни делал (ходит стиком/клавишами или стоит);
        //  - телефон (мыши нет или ей не пользуются): герой смотрит по ходу движения, а отпущенный стик/прицел
        //    оставляет его в последнем направлении.
        Mouse mouse = Mouse.current;
        bool mouseLook = MouseInUse(mouse) && Camera.main != null;

        Vector2 joystickDir = moveJoystick != null ? moveJoystick.Direction : Vector2.zero;
        bool useJoystick = joystickDir.sqrMagnitude > 0.01f;

        if (useJoystick)
        {
            rb.linearVelocity = joystickDir * moveSpeed;
            // Перед спрайта теперь смотрит в мировой +Y при повороте 0.
            float angle = Mathf.Atan2(joystickDir.y, joystickDir.x) * Mathf.Rad2Deg - 90f;
            moveFacingAngle = angle;
            if (!mouseLook) rb.rotation = angle;
        }
        else
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                Vector2 move = Vector2.zero;
                if (keyboard.leftArrowKey.isPressed) move.x -= 1f;
                if (keyboard.rightArrowKey.isPressed) move.x += 1f;
                if (keyboard.upArrowKey.isPressed) move.y += 1f;
                if (keyboard.downArrowKey.isPressed) move.y -= 1f;
                rb.linearVelocity = move.normalized * moveSpeed;
            }
        }

        if (mouseLook)
        {
            Vector3 screenPos = mouse.position.ReadValue();
            screenPos.z = -Camera.main.transform.position.z;
            Vector2 mouseWorld = Camera.main.ScreenToWorldPoint(screenPos);
            Vector2 lookDir = mouseWorld - rb.position;
            if (lookDir.sqrMagnitude > 0.0001f)
            {
                // поворачиваем спрайт на 90 градусов, так как математически 90 - это направление вверх.
                float angle = Mathf.Atan2(lookDir.y, lookDir.x) * Mathf.Rad2Deg - 90f;
                rb.rotation = angle;
                moveFacingAngle = angle;
            }
        }
    }

    // Мышью «пользуются», как только курсор сдвинулся с места после старта уровня. Так на ПК и в редакторе
    // герой сразу смотрит за мышью, а на телефоне (нет мыши) и в Unity Remote (мышь стоит) работает телефонная
    // логика. На мобильных платформах мышь не учитывается вообще.
    bool MouseInUse(Mouse mouse)
    {
        if (mouse == null || Application.isMobilePlatform) return false;
        Vector2 pos = mouse.position.ReadValue();
        if (!mouseSeen) { mouseSeen = true; mouseStartPos = pos; }
        else if (!mouseMoved && (pos - mouseStartPos).sqrMagnitude > 4f) mouseMoved = true;
        return mouseMoved;
    }

    void HandleAim()
    {
        if (aimJoystick == null || knifePrefab == null) return;

        // Джойстик отдаёт направление относительно экрана (перетаскивание «вверх» - всегда (0,1),
        // как бы ни была повёрнута камера). Свободный обзор CameraFollow вращает камеру вокруг Z,
        // из-за чего «вверх по экрану» перестаёт совпадать с «вверх в мире» - поворот на
        // собственный угол Z камеры переводит перетаскивание в правильное мировое направление,
        // так что бросок всё равно летит туда, куда визуально прицелились, а не по осям до поворота.
        Vector2 dir = aimJoystick.Direction;
        if (Camera.main != null) dir = RotateVector(dir, Camera.main.transform.eulerAngles.z);

        // Гистерезис: однажды начав прицеливание, отпусканием считается только реальный возврат
        // в центр. Без этого джойстик, на один кадр проседающий почти в ноль при проходе по дуге
        // (или обычный дрожащий ввод), читался бы как «отпущен», мгновенно бросал удерживаемый
        // нож, затем на следующем кадре снова активировался и создавал новый.
        float magnitude = dir.magnitude;
        bool active = aimWasActive ? magnitude > aimReleaseThreshold : magnitude > aimActivateThreshold;

        // Палец убран со стика - сессия концентрации закончена, следующее отведение запустит её заново.
        if (aimHold == null || !aimHold.Held) concentrationSession = false;

        // Одновременно существует только один нож - это один и тот же зачарованный клинок,
        // возвращающийся обратно, а не стопка метательных. Полностью игнорируем ввод прицеливания,
        // пока летящий нож не исчезнет. Сравнение с null работает, хотя сама ссылка не обнуляется:
        // Unity перегружает ==, так что уничтоженный объект сравнивается как равный null.
        if (flyingKnife != null || cooldownRemaining > 0f) active = false;

        // Режим обзора (эксперимент): отдельный стик с глазиком (LookStick), от прицеливания не зависит.
        var lookStick = LookStick.Instance;
        IsLooking = lookStick != null && lookStick.Held;
        if (IsLooking)
        {
            Vector2 lookDir = lookStick.Value;
            if (Camera.main != null) lookDir = RotateVector(lookDir, Camera.main.transform.eulerAngles.z);
            LookOffset = lookDir * Difficulty.Current.aimLookPanRadius;
        }
        else LookOffset = Vector2.zero;

        // Для камеры (отдаление по отклонению стика) и шкалы сосредоточения.
        AimDeflection = active ? Mathf.Clamp01((magnitude - aimActivateThreshold) / Mathf.Max(0.01f, 1f - aimActivateThreshold)) : 0f;

        if (active)
        {
            if (!aimWasActive)
            {
                if (!concentrationSession)
                {
                    // Новая сессия (палец только что лёг на стик): концентрация начинается с нуля.
                    aimSlowTimer = 0f;
                    concentrationSession = true;
                    vignetteFadeOutStarted = false;
                    if (aimingVignetteFader != null) aimingVignetteFader.FadeIn(vignetteFadeDuration);
                }
                else if (aimSlowTimer < aimSlowDuration)
                {
                    // Стик вернули в центр и потянули снова, не убирая палец: расписание продолжается, а не запускается заново.
                    vignetteFadeOutStarted = false;
                    if (aimingVignetteFader != null) aimingVignetteFader.FadeIn(vignetteFadeDuration);
                }
            }

            // Замедление при прицеливании («bullet-time»): замедляет физику/анимацию (всё, что
            // использует Time.deltaTime), тогда как чтение джойстика/указателя в Update() идёт
            // каждый реальный кадр, поэтому само прицеливание никогда не ощущается вялым. Работает
            // по фиксированному расписанию в реальном времени с момента начала прицеливания, а не
            // зависит от того, как долго игрок его держит - полное замедление на aimSlowDuration,
            // затем плавный возврат к норме за aimRecoverDuration, и после этого остаётся нормальной
            // скорость, даже если игрок всё ещё целится. Именно Time.unscaledDeltaTime, потому что
            // эта длительность должна означать одинаковое реальное время независимо от того,
            // какой timeScale действует в данный момент.
            if (!IsLooking) aimSlowTimer += Time.unscaledDeltaTime; // в режиме обзора время и концентрация стоят
            // Остаток сосредоточения: от 1 в начале прицела до 0, когда время снова идёт в обычном ритме.
            float targetTimeScale;
            if (aimSlowTimer < aimSlowDuration)
            {
                targetTimeScale = aimTimeScale;
            }
            else if (aimSlowTimer < aimSlowDuration + aimRecoverDuration)
            {
                // Виньетка гаснет синхронно с возвратом timeScale, начиная в тот же момент, что и
                // возврат (а не при отпускании) - становится полностью прозрачной ровно когда время
                // заканчивает нормализоваться, по тому же фиксированному расписанию, что и сам
                // возврат timeScale, даже если игрок всё ещё держит прицел.
                if (!vignetteFadeOutStarted)
                {
                    vignetteFadeOutStarted = true;
                    if (aimingVignetteFader != null) aimingVignetteFader.FadeOut(aimRecoverDuration);
                }
                float t = (aimSlowTimer - aimSlowDuration) / aimRecoverDuration;
                targetTimeScale = Mathf.Lerp(aimTimeScale, 1f, t);
            }
            else
            {
                targetTimeScale = 1f;
            }
            // Множитель скорости игры (кнопки 0.5/0.75/1) действует поверх замедления прицеливания.
            targetTimeScale *= GameSpeed.Multiplier;
            if (IsLooking) targetTimeScale = 0f; // режим обзора: время полностью стоит
            Time.timeScale = targetTimeScale;
            Time.fixedDeltaTime = NormalFixedDeltaTime * Mathf.Max(targetTimeScale, 0.01f);

            if (heldKnife == null)
            {
                heldKnife = Instantiate(knifePrefab);
                heldKnifeScript = heldKnife.GetComponent<Knife>();
            }
            // Держится на «сфере» вокруг игрока в направлении текущего прицеливания - выглядит как
            // выставленный вперёд и готовый к броску, а не приклеенный к телу. (Направление движения
            // здесь не используется, так как оно не зависит от прицела.) Rigidbody2D симулируется
            // в мировых координатах и игнорирует перемещения родительского transform, поэтому
            // позицию удержания нужно синхронизировать с игроком каждый кадр. Если обычная точка
            // удержания оказывается за стеной (игрок стоит вплотную к ней), нож подтягивается
            // обратно на сторону комнаты, а не создаётся за пределами уровня.
            Vector2 holdOffset = dir.normalized * holdDistance;
            heldKnifeScript.SetHoldPosition(rb.position, holdOffset);
            lastAimDir = dir;
            if (dir.sqrMagnitude > 0.0001f)
            {
                aimFacingActive = true;
                aimFacingAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
            }
            heldKnifeScript.SetAimDirection(dir);
            if (IsLooking) heldKnifeScript.HideTrajectory(); // режим обзора: траектории нет
        }
        else if (aimWasActive)
        {
            Time.timeScale = GameSpeed.Multiplier;
            Time.fixedDeltaTime = NormalFixedDeltaTime * GameSpeed.Multiplier;
            // Нужно только если затухание по расписанию выше так и не успело стартовать (отпустили
            // рано, ещё внутри aimSlowDuration) - как только оно идёт или завершено, виньетка уже
            // гаснет/исчезла, и FadeOut попытался бы запустить StartCoroutine на GameObject,
            // который сам FadeOut мог только что деактивировать.
            if (aimingVignetteFader != null && !vignetteFadeOutStarted)
            {
                aimingVignetteFader.FadeOut(vignetteFadeDuration);
                vignetteFadeOutStarted = true; // виньетка уже гаснет: повторный FadeOut на неактивном объекте даёт ошибку
            }

            if (heldKnife != null)
            {
                // То же правило, о котором уже предупреждает скрытая траектория: бросок прямо в
                // стену так близко отскочил бы обратно почти мгновенно, поэтому он не считается
                // настоящим броском - нож отменяется, а не запускается.
                if (lastAimDir.sqrMagnitude > 0.01f && !heldKnifeScript.IsThrowBlocked)
                {
                    heldKnifeScript.Throw(lastAimDir);
                    flyingKnife = heldKnife;
                    flyingKnifeScript = heldKnifeScript;
                }
                else
                {
                    Destroy(heldKnife);
                }
                heldKnife = null;
            }
        }

        // Пока палец на стике, концентрация тикает и когда стик в центре (иначе можно «сбрасывать» её возвратом в центр).
        if (!active && concentrationSession) aimSlowTimer += Time.unscaledDeltaTime;
        FocusFraction = concentrationSession
            ? Mathf.Clamp01(1f - aimSlowTimer / Mathf.Max(0.01f, aimSlowDuration + aimRecoverDuration))
            : 1f;

        // Режим обзора вне прицеливания: время стоит, пока палец на стике с глазиком, потом возвращается.
        // Когда цель прицеливания активна, время ведёт ветка выше.
        if (IsLooking)
        {
            if (!active && !ParrySystem.Active && !PlayerHealth.IsDead && !StartFreeze.Waiting && !PauseMenu.Paused)
            {
                Time.timeScale = 0f;
                lookFroze = true;
            }
            else if (active) lookFroze = true;
        }
        else if (lookFroze)
        {
            lookFroze = false;
            if (!active && !ParrySystem.Active && !PlayerHealth.IsDead && !StartFreeze.Waiting && !PauseMenu.Paused)
            {
                Time.timeScale = GameSpeed.Multiplier;
                Time.fixedDeltaTime = NormalFixedDeltaTime * GameSpeed.Multiplier;
            }
        }

        aimWasActive = active;
    }

    static Vector2 RotateVector(Vector2 v, float angleDeg)
    {
        float rad = angleDeg * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);
        return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
    }

    void UpdateKnifeCooldown()
    {
        // Ловит ситуацию, когда нож исчезает сам (попадание во врага, лимит отскоков, истечение
        // времени жизни) - ни одна из этих причин не сообщает TestScript напрямую, поэтому
        // отслеживается переход «летел -> исчез». Поимка (TryCatchKnife) уже сама обрабатывает
        // себя через EndKnifeFlight до этого, так что к этому моменту knifeWasFlying уже false,
        // и повторно читать уже по-настоящему null flyingKnifeScript не придётся.
        bool knifeFlyingNow = flyingKnife != null;
        if (knifeWasFlying && !knifeFlyingNow) EndKnifeFlight(flyingKnifeScript.ResultingCooldown);
        knifeWasFlying = knifeFlyingNow;

        if (cooldownRemaining > 0f) cooldownRemaining -= Time.unscaledDeltaTime;
    }

    void EndKnifeFlight(float cooldown)
    {
        cooldownRemaining = cooldown;
        flyingKnife = null;
        flyingKnifeScript = null;
        knifeWasFlying = false;
    }

    void UpdateCatchButton()
    {
        if (catchButton == null) return;
        catchButton.interactable = IsKnifeCatchable();
    }

    // Нож сейчас в радиусе поимки (после отскока).
    bool KnifeInCatchRange()
    {
        return flyingKnife != null && flyingKnifeScript.HasBounced
            && Vector2.Distance(rb.position, flyingKnifeScript.Position) <= catchZoneRadius;
    }

    // Можно ловить: нож в радиусе ИЛИ был в радиусе только что (окно прощения Difficulty.catchGraceTime). Нож летит
    // 18-25 юн/с и за время тапа по кнопке (~0.1 с) успевает выйти из радиуса, поэтому строгая проверка в момент
    // нажатия отказывала при активной кнопке. Окно считается в реальном времени, а не в замедленном.
    bool IsKnifeCatchable()
    {
        if (flyingKnife == null || !flyingKnifeScript.HasBounced) return false;
        if (KnifeInCatchRange()) { lastCatchableTime = Time.unscaledTime; return true; }
        return Time.unscaledTime - lastCatchableTime <= Difficulty.Current.catchGraceTime;
    }

    public void TryCatchKnife()
    {
        // Перепроверяем, а не доверяем состоянию interactable кнопки - клик и выход ножа из
        // радиуса поимки могут прийтись на один кадр, до обновления кнопки.
        if (!IsKnifeCatchable()) return;

        // Пойманный нож полностью пропускает кулдаун - поймать его обратно из воздуха уже
        // само по себе награда, а не промах.
        // Раньше было так -> float cooldown = flyingKnifeScript.ResultingCooldown;
        // Но, поскольку кд ножа при поимке должно быть чистый 0, сразу передаём ноль.
        Destroy(flyingKnife);
        
        EndKnifeFlight(0f);
    }
}
