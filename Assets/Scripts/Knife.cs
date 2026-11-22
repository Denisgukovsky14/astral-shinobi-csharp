using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class Knife : MonoBehaviour
{
    public float speed = 12f;
    public float previewMaxDistance = 20f;
    public LayerMask obstacleMask = ~0;
    public LayerMask enemyMask = ~0;
    public int maxBounces = 3;
    public float maxLifetime = 6f;
    public float minTrajectoryDistance = 0.8f; // один тайл при 100 пикс/юнит (~80 пикс)
    public Color normalTrajectoryColor = new Color(1f, 1f, 0f, 0.5f);
    public Color blockedTrajectoryColor = new Color(1f, 0f, 0f, 0.5f);

    [Tooltip("If the reflected leg would hit a second wall closer than cornerRiskLengthFraction of the knife's own physical length, the throw is blocked (same as being too close to throw at all) instead of just previewed - a corner, where the real thrown knife's own body (not a zero-width preview ray) doesn't have room to physically clear the first surface before the second one is already there. Measured from the sprite (Hero_Knife, ~0.67 units long), not the CircleCollider2D - that collider is a tiny ~0.033-diameter hit-detection point, nowhere near representative of the blade's real visual size.")]
    public float cornerRiskLengthFraction = 0.6f;
    public float missCooldown = 1.2f;
    public float hitEnemyCooldown = 0.5f;

    [Tooltip("Радиус «точки» на острие ножа для обнаружения стен и врагов (юниты). Нож в полёте - не физическое тело, а точка, которая летит и отражается от поверхностей; маленький радиус нужен, чтобы точка не проскакивала тонкие стены между кадрами.")]
    public float tipRadius = 0.03f;

    [Tooltip("Множитель толчка двери ножом: импульс = масса ножа * скорость * это число. 0 - нож двери не толкает.")]
    public float doorPushFactor = 1f;

    private Rigidbody2D rb;
    private Collider2D col;
    private ContactFilter2D obstacleFilter;
    private ContactFilter2D castFilter;
    private readonly RaycastHit2D[] castHits = new RaycastHit2D[8];
    private ContactFilter2D enemyFilter;
    private readonly RaycastHit2D[] padHits = new RaycastHit2D[8];
    private LineRenderer trajectoryLine;
    private SpriteRenderer bladeSprite;
    private int bounceCount = 0;
    private bool throwBlocked = false;

    // Полёт: считаем сами, без физических контактов. tip - точка на острие, flightDir - направление.
    private bool flying;
    private Vector2 tip;
    private Vector2 flightDir;
    private Vector2 sweepFrom;       // откуда начинается проверка в первом шаге (центр ножа), дальше - tip
    private bool firstStep;
    private Vector3 tipLocalOffset;  // положение острия в локальных координатах ножа
    private Vector2 centerPos;
    private Vector2 holdOrigin;       // позиция игрока, от которой держат нож (для проверок у стены)
    private bool hasHoldOrigin;
    private bool preBounced;
    // Собственная длина спрайта без поворота (а не AABB при текущем повороте, который меняется,
    // пока нож поворачивается в сторону прицела) - кэшируется один раз, так как ни спрайт, ни
    // масштаб в рантайме не меняются.
    private float knifeLength = 1f;

    public bool IsThrowBlocked => throwBlocked;
    public bool HasBounced => bounceCount > 0;
    public int BounceCount => bounceCount;
    public Vector2 TipPosition => tip;
    public Vector2 Position => flying ? centerPos : rb.position;
    public bool HitEnemy { get; private set; }
    // Тот, кто бросает этот нож, читает это, когда нож исчез - впустую потраченный бросок
    // (отскакал, истекло время, был пойман) стоит больше кулдауна, чем чистое попадание,
    // чтобы не поощрять спам.
    public float ResultingCooldown => HitEnemy ? hitEnemyCooldown : missCooldown;

    void Awake()
    {
        // Параметры ножа по сложности (Difficulty): скорость, размер, число отскоков.
        var diff = Difficulty.Current;
        speed *= diff.knifeSpeedMultiplier;
        maxBounces = diff.knifeMaxBounces;
        transform.localScale *= diff.knifeScaleMultiplier;

        rb = GetComponent<Rigidbody2D>();
        trajectoryLine = GetComponent<LineRenderer>();
        col = GetComponent<Collider2D>();
        // Коллайдер ножа (в руке и в полёте) нужен только если нож должен отражать вражеские кунаи и стрелы - это
        // выключенная по умолчанию возможность (DifficultySettings.knifeDeflectsEnemyProjectiles). Иначе снаряды
        // врагов пролетают сквозь нож: попадания считаются лучом, а не физическим контактом.
        if (col != null) col.enabled = diff.knifeDeflectsEnemyProjectiles;
        obstacleFilter = new ContactFilter2D();
        obstacleFilter.SetLayerMask(obstacleMask);
        obstacleFilter.useTriggers = false;
        // Полёт: стены и враги, триггеры (зоны, ключи) нож не трогают.
        castFilter = new ContactFilter2D();
        castFilter.SetLayerMask(obstacleMask | enemyMask);
        castFilter.useTriggers = false;
        enemyFilter = new ContactFilter2D();
        enemyFilter.SetLayerMask(enemyMask);
        enemyFilter.useTriggers = false;

        bladeSprite = GetComponentInChildren<SpriteRenderer>();
        if (bladeSprite != null && bladeSprite.sprite != null)
            knifeLength = bladeSprite.sprite.bounds.size.y * transform.lossyScale.y;
    }

    public void SetHoldPosition(Vector2 playerPos, Vector2 desiredOffset)
    {
        holdOrigin = playerPos;
        hasHoldOrigin = true;
        // Мгновенный «снап» вместо плавного скольжения - плавное перемещение ножа у стены/колонны
        // заставляло его заметно «уползать» в точку, откуда его ещё нельзя бросить, и это выглядело
        // как будто нож дрейфует сам по себе. Мгновенный снап делает переключение между доступными
        // и заблокированными точками чётким «монтажным склеем», а не медленным скольжением.
        rb.position = ResolveHoldPosition(playerPos, desiredOffset);
    }

    private Vector2 ResolveHoldPosition(Vector2 playerPos, Vector2 desiredOffset)
    {
        Vector2 desiredPos = playerPos + desiredOffset;
        RaycastHit2D straightHit = Physics2D.Linecast(playerPos, desiredPos, obstacleMask);
        if (straightHit.collider == null)
            return desiredPos;

        // Прямо вперёд заблокировано. Фиксированный поворот на 45 градусов всё равно уводит часть
        // длины в стену (cos(45) от исходной дистанции), так что при достаточной близости нож всё
        // равно оказывается в стене. Вместо этого строим касательную по реальной нормали стены -
        // направление, идущее строго параллельно поверхности, - у которого нет составляющей
        // в сторону стены, как бы близко мы ни стояли.
        Vector2 normal = straightHit.normal;
        Vector2 tangent = new Vector2(-normal.y, normal.x);
        float distance = desiredOffset.magnitude;
        if (Vector2.Dot(tangent, desiredOffset) < 0f) tangent = -tangent;

        Vector2 posA = playerPos + tangent * distance;
        if (Physics2D.Linecast(playerPos, posA, obstacleMask).collider == null)
            return posA;

        Vector2 posB = playerPos - tangent * distance;
        if (Physics2D.Linecast(playerPos, posB, obstacleMask).collider == null)
            return posB;

        // Оба направления вдоль стены тоже заблокированы (угол) - прижимаемся к ближайшей поверхности.
        return straightHit.point - desiredOffset.normalized * 0.1f;
    }

    // Режим обзора (эксперимент): траектория не рисуется.
    public void HideTrajectory()
    {
        if (trajectoryLine != null) trajectoryLine.enabled = false;
    }

    public void SetAimDirection(Vector2 direction)
    {
        if (direction.sqrMagnitude < 0.0001f) return;
        FaceDirection(direction);
        DrawTrajectoryPreview(direction.normalized);
    }

    public void Throw(Vector2 direction)
    {
        transform.SetParent(null);
        flightDir = direction.normalized;
        FaceDirection(flightDir);

        // Нож в полёте - не физическое тело, а точка на острие, которая летит прямо и отражается от стен
        // по лучу (CircleCast крошечного радиуса). Физические контакты не участвуют: нет скольжения по шву
        // стены, нет повторных касаний и самоуничтожения «застрявшего» ножа под острым углом.
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.linearVelocity = Vector2.zero;
        if (col != null) col.enabled = Difficulty.Current.knifeDeflectsEnemyProjectiles;

        // Острие: самая дальняя точка спрайта вдоль +Y ножа. Хранится в локальных координатах ножа, чтобы
        // после каждого отражения (поворота) тело ножа вставало за острием правильно.
        Vector3 tipWorld = bladeSprite != null && bladeSprite.sprite != null
            ? bladeSprite.transform.TransformPoint(new Vector3(bladeSprite.sprite.bounds.center.x, bladeSprite.sprite.bounds.max.y, 0f))
            : transform.position + (Vector3)(flightDir * knifeLength * 0.5f);
        tipLocalOffset = transform.InverseTransformPoint(tipWorld);
        tip = tipWorld;
        centerPos = rb.position;
        if (!hasHoldOrigin) holdOrigin = rb.position;
        if (ResolveTipThroughWall()) return;
        sweepFrom = centerPos;   // первый шаг проверяем от центра ножа, чтобы острие, оказавшееся в стене у самого броска, не пролетело сквозь неё
        firstStep = !preBounced;
        if (preBounced) sweepFrom = tip;
        flying = true;

        if (trajectoryLine != null) trajectoryLine.enabled = false;
        // Страховка: если нож почему-то не исчез сам, он всё равно будет убран, а не останется мусором навсегда.
        Destroy(gameObject, maxLifetime);
    }

    // Игрок стоит вплотную к стене и целится в неё: между игроком и острием ножа уже стена, и обычный полёт от
    // острия пролетел бы сквозь неё (острие стартует за стеной). Проверяем отрезок игрок -> острие: если на нём
    // стена, острие ставится на поверхность и нож сразу отражается (возвращается к игроку), если враг - попадание.
    // Возвращает true, если нож уже обработан (исчез).
    bool ResolveTipThroughWall()
    {
        Vector2 toTip = tip - holdOrigin;
        float dist = toTip.magnitude;
        if (dist < 0.01f) return false;

        bool prev = Physics2D.queriesStartInColliders;
        Physics2D.queriesStartInColliders = false;
        int n = Physics2D.CircleCast(holdOrigin, tipRadius, toTip / dist, castFilter, castHits, dist);
        Physics2D.queriesStartInColliders = prev;

        int best = -1;
        for (int i = 0; i < n; i++)
            if (best < 0 || castHits[i].distance < castHits[best].distance) best = i;
        if (best < 0) return false;

        RaycastHit2D h = castHits[best];
        if (DealDamage(h.collider))
        {
            HitEnemy = true;
            Destroy(gameObject);
            return true;
        }
        if (bounceCount >= maxBounces)
        {
            Destroy(gameObject);
            return true;
        }

        Vector2 normal = h.normal;
        Vector2 reflected = Vector2.Reflect(flightDir, normal);
        flightDir = reflected.sqrMagnitude < 0.0001f ? normal : reflected.normalized;
        bounceCount++;
        FaceDirection(flightDir);
        tip = h.centroid + normal * (tipRadius + 0.01f);
        centerPos = tip - (Vector2)transform.TransformVector(tipLocalOffset);
        rb.position = centerPos;
        preBounced = true; // острие уже на «чистой» стороне стены, дальше обычный полёт
        return false;
    }

    // Невидимый конус перед игроком: пять лучей от игрока в пределах ±flushConeHalfAngle от направления прицела.
    // Любой луч нашёл стену ближе длины ножа - игрок стоит вплотную к стене и целится в неё, бросок запрещён.
    bool IsFlushAgainstWall(Vector2 aimDir)
    {
        float reach = knifeLength; // длина (высота коллайдера) ножа с учётом масштаба
        float half = Difficulty.Current.flushConeHalfAngle;
        Vector2 origin = hasHoldOrigin ? holdOrigin : rb.position;
        const int rays = 5;
        for (int i = 0; i < rays; i++)
        {
            float angle = Mathf.Lerp(-half, half, i / (rays - 1f));
            Vector2 d = Quaternion.Euler(0f, 0f, angle) * aimDir;
            if (Physics2D.Raycast(origin, d, reach, obstacleMask).collider != null) return true;
        }
        return false;
    }

    void FixedUpdate()
    {
        if (!flying) return;

        float budget = knifeStepDistance();
        Vector2 from = firstStep ? sweepFrom : tip;
        if (firstStep) budget += Vector2.Distance(sweepFrom, tip);
        firstStep = false;

        // Сразу за поверхностью тоже может быть стена (угол): отражаем по цепочке в пределах одного шага.
        bool prevQueries = Physics2D.queriesStartInColliders;
        Physics2D.queriesStartInColliders = false;
        int guard = 0;
        while (budget > 1e-4f && guard++ < 6)
        {
            int n = Physics2D.CircleCast(from, tipRadius, flightDir, castFilter, castHits, budget);
            int best = -1;
            for (int i = 0; i < n; i++)
                if (best < 0 || castHits[i].distance < castHits[best].distance) best = i;

            // «Милость» сложности: невидимый запас вокруг врага. Проверяем только до ближайшей стены (или конца шага),
            // чтобы не засчитать попадание сквозь стену.
            float reach = best < 0 ? budget : castHits[best].distance;
            if (PaddedEnemyHit(from, flightDir, reach))
            {
                HitEnemy = true;
                Physics2D.queriesStartInColliders = prevQueries;
                Destroy(gameObject);
                return;
            }

            if (best < 0)
            {
                tip = from + flightDir * budget;
                budget = 0f;
                break;
            }

            RaycastHit2D h = castHits[best];
            tip = h.centroid; // центр круга в момент касания = острие на поверхности

            if (DealDamage(h.collider))
            {
                // Попадание во врага обрывает бросок на месте (исчезновение ножа читает TestScript).
                HitEnemy = true;
                Physics2D.queriesStartInColliders = prevQueries;
                Destroy(gameObject);
                return;
            }

            if (bounceCount >= maxBounces)
            {
                Physics2D.queriesStartInColliders = prevQueries;
                Destroy(gameObject);
                return;
            }

            // Отражение от поверхности: точка отскакивает, тело ножа (спрайт) просто разворачивается за ней.
            Vector2 normal = h.normal;
            Vector2 reflected = Vector2.Reflect(flightDir, normal);
            flightDir = reflected.sqrMagnitude < 0.0001f ? normal : reflected.normalized;
            bounceCount++;
            FaceDirection(flightDir);

            // Дверь на петле толкается ножом (раньше это делал физический контакт).
            if (doorPushFactor > 0f && h.rigidbody != null && h.rigidbody.bodyType == RigidbodyType2D.Dynamic)
                h.rigidbody.AddForceAtPosition(-normal * (rb.mass * speed * doorPushFactor), h.point, ForceMode2D.Impulse);

            // Отступ от поверхности больше радиуса точки: следующий луч не стартует внутри той же стены.
            tip = h.centroid + normal * (tipRadius + 0.01f);
            budget -= h.distance;
            from = tip;
        }
        Physics2D.queriesStartInColliders = prevQueries;

        // Тело ножа стоит за острием по его собственной оси.
        centerPos = tip - (Vector2)transform.TransformVector(tipLocalOffset);
        rb.MovePosition(centerPos);
    }

    float knifeStepDistance()
    {
        return speed * Time.fixedDeltaTime;
    }

    void FaceDirection(Vector2 direction)
    {
        // Остриё клинка смотрит в мировой +Y при повороте 0.
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);
        if (rb != null) rb.rotation = angle;
    }

    void DrawTrajectoryPreview(Vector2 dir)
    {
        if (trajectoryLine == null) return;

        // Сколько участков показываем и насколько каждый прозрачен - из сложности (Trajectory Leg Alphas):
        // первый элемент - прямой участок, дальше участки после каждого отскока, каждый бледнее предыдущего.
        var diff = Difficulty.Current;
        float[] alphas = diff.trajectoryLegAlphas;
        if (alphas == null || alphas.Length == 0) alphas = new[] { 0.5f, 0.5f };
        int legs = Mathf.Clamp(alphas.Length, 1, 4);
        bool anyAngle = diff.allowAnyThrowAngle;

        // Вплотную к стене и целимся в неё: траектория не рисуется, бросок запрещён (DifficultySettings.blockFlushThrows).
        if (diff.blockFlushThrows && IsFlushAgainstWall(dir))
        {
            throwBlocked = true;
            trajectoryLine.enabled = false;
            return;
        }

        Vector2 origin = transform.position;
        // Препятствия и враги находятся на разных слоях, поэтому здесь нужны обе маски - стена
        // отражает превью, хитбокс врага его просто обрывает (настоящий нож при контакте
        // исчезает, а не отскакивает, см. DealDamage/FixedUpdate).
        int mask = obstacleMask | enemyMask;

        var points = new List<Vector2> { origin };
        Vector2 legOrigin = origin;
        Vector2 legDir = dir;
        RaycastHit2D hit1 = default;
        RaycastHit2D hit2 = default;
        for (int leg = 0; leg < legs; leg++)
        {
            RaycastHit2D h = Physics2D.Raycast(legOrigin, legDir, previewMaxDistance, mask);
            if (leg == 0) hit1 = h;
            else if (leg == 1) hit2 = h;

            if (h.collider == null)
            {
                points.Add(legOrigin + legDir * previewMaxDistance);
                break;
            }
            points.Add(h.point);
            // Враг обрывает линию (нож в него попадёт и исчезнет), последний участок дальше не отражаем.
            if (h.collider.GetComponentInChildren<HealthBar>() != null || leg == legs - 1) break;

            legDir = Vector2.Reflect(legDir, h.normal);
            legOrigin = h.point + legDir * 0.01f;
        }

        // Блокировка должна использовать тот же самый луч, что и рисуется - проверка от позиции
        // игрока (другая точка отсчёта, чем у удерживаемого ножа, который находится на расстоянии
        // holdDistance) позволяла двум проверкам расходиться. Если встать вплотную к чему-то и бросить
        // нож прямо в это, он отскочит почти мгновенно - это эксплойт, а не настоящий бросок. Превью всё
        // равно рисуется, просто красным цветом, показывая, что бросок не сработает.
        // allowAnyThrowAngle (сложность): красных линий нет, бросок валиден под любым углом.
        throwBlocked = !anyAngle && hit1.collider != null && hit1.distance < minTrajectoryDistance;

        // Угол - отражённый участок тут же упирается в ДРУГУЮ стену ближе собственной физической длины ножа:
        // такая геометрия не отражает чисто, поэтому тоже блок (если красные линии включены).
        bool firstIsEnemy = hit1.collider != null && hit1.collider.GetComponentInChildren<HealthBar>() != null;
        bool cornerRisk = !firstIsEnemy && hit1.collider != null && hit2.collider != null
            && Vector2.Distance(hit1.point, hit2.point) < knifeLength * cornerRiskLengthFraction;
        if (cornerRisk && !anyAngle) throwBlocked = true;

        Color baseColor = throwBlocked ? blockedTrajectoryColor : normalTrajectoryColor;
        trajectoryLine.positionCount = points.Count;
        for (int i = 0; i < points.Count; i++) trajectoryLine.SetPosition(i, points[i]);
        trajectoryLine.colorGradient = LegGradient(points, baseColor, alphas);
        trajectoryLine.enabled = true;
    }

    // Градиент по длине линии: каждый участок (между соседними точками) - свой уровень прозрачности, резкими ступенями.
    static Gradient LegGradient(List<Vector2> points, Color baseColor, float[] alphas)
    {
        float total = 0f;
        var cumulative = new float[points.Count];
        for (int i = 1; i < points.Count; i++)
        {
            total += Vector2.Distance(points[i - 1], points[i]);
            cumulative[i] = total;
        }

        var alphaKeys = new List<GradientAlphaKey>();
        float lastTime = -1f;
        for (int leg = 0; leg < points.Count - 1; leg++)
        {
            float a = Mathf.Clamp01(alphas[Mathf.Min(leg, alphas.Length - 1)]);
            float t0 = total > 0.0001f ? cumulative[leg] / total : 0f;
            float t1 = total > 0.0001f ? cumulative[leg + 1] / total : 1f;
            t0 = Mathf.Max(t0, lastTime + 0.0005f);
            t1 = Mathf.Max(t1 - 0.0005f, t0 + 0.0005f);
            t0 = Mathf.Clamp01(t0); t1 = Mathf.Clamp01(t1);
            alphaKeys.Add(new GradientAlphaKey(a, t0));
            alphaKeys.Add(new GradientAlphaKey(a, t1));
            lastTime = t1;
        }
        if (alphaKeys.Count == 0) alphaKeys.Add(new GradientAlphaKey(baseColor.a, 0f));

        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(baseColor, 0f), new GradientColorKey(baseColor, 1f) },
            alphaKeys.ToArray());
        return g;
    }

    // Попадание «по запасу»: широкий луч (радиус острия + DifficultySettings.knifeEnemyHitPadding) только по врагам.
    // Нож, пролетевший в миллиметре от врага, засчитывается как попадание. true - урон нанесён.
    bool PaddedEnemyHit(Vector2 from, Vector2 dir, float dist)
    {
        float pad = Difficulty.Current.knifeEnemyHitPadding;
        if (pad <= 0f || dist <= 0f) return false;
        int n = Physics2D.CircleCast(from, tipRadius + pad, dir, enemyFilter, padHits, dist);
        int best = -1;
        for (int i = 0; i < n; i++)
            if (best < 0 || padHits[i].distance < padHits[best].distance) best = i;
        return best >= 0 && DealDamage(padHits[best].collider);
    }

    bool DealDamage(Collider2D other)
    {
        HealthBar healthBar = other.GetComponentInChildren<HealthBar>();
        if (healthBar == null) return false;

        // bounceCount - число отскоков, случившихся ДО этого попадания, поэтому свежий бросок
        // (0 отскоков) всё равно наносит один базовый удар: отскоки * база + база.
        var diff = Difficulty.Current;
        // Эксперимент: нож, сделавший все возможные отскоки, убивает с одного попадания.
        if (diff.oneShotAfterAllBounces && bounceCount >= maxBounces) healthBar.currentHealth = 0f;
        else healthBar.currentHealth -= diff.DamageForBounces(bounceCount);

        // Короткое замедление врага после попадания (сложность): понятно, что нож попал.
        EnemyHitStun.Apply(other.GetComponentInParent<Pathfinding.AIPath>(), diff.hitStunDuration, diff.hitStunSpeedFactor);
        return true;
    }
}
