using UnityEngine;

// Метательный снаряд кунайщика. Как и нож игрока (Knife.cs), в полёте это не физическое тело, а ТОЧКА на острие:
// она летит прямо, каждый шаг проверяется лучом (CircleCast маленького радиуса) и отражается от стен и дверей
// по нормали попадания. Физические контакты не участвуют, поэтому нет скольжения по шву стены, повторных касаний
// и самоуничтожения «застрявшего» куная под острым углом; вплотную к стене (враг у стены) острие не вылетает
// за неё. Попадание в игрока - урон и исчезновение.
[RequireComponent(typeof(Rigidbody2D))]
public class Kunai : MonoBehaviour
{
    public float speed = 10f;
    public int maxBounces = 3;
    public float maxLifetime = 4f;

    [Tooltip("Слои, от которых кунай отражается (стены, двери). Игрок обнаруживается отдельно, по слою Player.")]
    public LayerMask obstacleMask;

    [Tooltip("Радиус «точки» на острие для обнаружения стен и игрока (юниты). Маленький радиус нужен, чтобы точка не проскакивала тонкие стены между кадрами.")]
    public float tipRadius = 0.06f;

    [Tooltip("Ширина «тела» куная для попадания в игрока (юниты): игрок получает урон, если тело куная (от острия до хвоста) пересекает его, а не только острие. Так кунай, задевший игрока вскользь, не «пролетает насквозь».")]
    public float bodyHitWidth = 0.12f;

    [Tooltip("Множитель толчка двери кунаем: импульс = масса * скорость * это число. 0 - дверь не толкается.")]
    public float doorPushFactor = 1f;

    // Счётчик реальных попаданий снарядов (в отличие от измерения «could've hit you» для ближнего
    // боя из EnemyAttackZone) - реальное попадание куная считается здесь для HUD-индикации
    // в левом верхнем углу (см. KunaiHitFeedbackUI).
    public static int TotalHits;

    [Tooltip("Урон по игроку в долях от полного хп (1 = всё хп).")]
    public float damage = 0.3f;

    private Rigidbody2D rb;
    private ContactFilter2D castFilter;
    private readonly RaycastHit2D[] castHits = new RaycastHit2D[8];
    private SpriteRenderer bladeSprite;
    private int playerLayer;
    private int bounceCount;

    private bool flying;
    private Vector2 tip;
    private Vector2 flightDir;
    private Vector3 tipLocalOffset;  // положение острия в локальных координатах куная
    private Vector2 launchOrigin;    // где кунай появился (рука врага): от неё проверяем, не за стеной ли острие
    private bool firstStep;
    private Vector2 sweepFrom;

    public Vector2 TipPosition => tip;
    public int BounceCount => bounceCount;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        bladeSprite = GetComponentInChildren<SpriteRenderer>();
        playerLayer = LayerMask.NameToLayer("Player");
        castFilter = new ContactFilter2D();
        castFilter.SetLayerMask(obstacleMask | (1 << playerLayer));
        castFilter.useTriggers = false;

        // Физические коллайдеры куная больше не нужны: стены и игрока находит луч от острия.
        foreach (var c in GetComponentsInChildren<Collider2D>(true)) c.enabled = false;
    }

    public void Launch(Vector2 direction)
    {
        if (direction.sqrMagnitude < 0.0001f) direction = Vector2.up;
        flightDir = direction.normalized;
        FaceDirection(flightDir);

        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.linearVelocity = Vector2.zero;

        // Острие: самая дальняя точка спрайта вдоль +Y, в локальных координатах (после поворотов тело встаёт за ним).
        Vector3 tipWorld = bladeSprite != null && bladeSprite.sprite != null
            ? bladeSprite.transform.TransformPoint(new Vector3(bladeSprite.sprite.bounds.center.x, bladeSprite.sprite.bounds.max.y, 0f))
            : transform.position + (Vector3)(flightDir * 0.2f);
        tipLocalOffset = transform.InverseTransformPoint(tipWorld);
        tip = tipWorld;
        launchOrigin = rb.position;
        sweepFrom = launchOrigin;
        firstStep = true;

        // Враг стоит вплотную к стене: между рукой и острием уже стена - острие ставится на её поверхность.
        if (ResolveTipThroughWall()) return;
        var touchedAtLaunch = BodyTouchingPlayer();
        if (touchedAtLaunch != null)
        {
            ApplyPlayerHit(touchedAtLaunch);
            Destroy(gameObject);
            return;
        }
        flying = true;
        Destroy(gameObject, maxLifetime);
    }

    // Возвращает true, если кунай уже обработан (исчез).
    bool ResolveTipThroughWall()
    {
        Vector2 toTip = tip - launchOrigin;
        float dist = toTip.magnitude;
        if (dist < 0.01f) return false;

        bool prev = Physics2D.queriesStartInColliders;
        Physics2D.queriesStartInColliders = false;
        int n = Physics2D.CircleCast(launchOrigin, tipRadius, toTip / dist, castFilter, castHits, dist);
        Physics2D.queriesStartInColliders = prev;
        int best = Nearest(n);
        if (best < 0) return false;

        RaycastHit2D h = castHits[best];
        if (HitPlayer(h) || bounceCount >= maxBounces)
        {
            Destroy(gameObject);
            return true;
        }
        Reflect(h);
        centerFromTip();
        firstStep = false;
        sweepFrom = tip;
        flying = true;
        Destroy(gameObject, maxLifetime);
        return true; // дальше летим из FixedUpdate; Launch завершён
    }

    void FixedUpdate()
    {
        if (!flying) return;

        float budget = speed * Time.fixedDeltaTime;
        Vector2 from = firstStep ? sweepFrom : tip;
        if (firstStep) budget += Vector2.Distance(sweepFrom, tip);
        firstStep = false;

        bool prevQueries = Physics2D.queriesStartInColliders;
        Physics2D.queriesStartInColliders = false;
        int guard = 0;
        while (budget > 1e-4f && guard++ < 6)
        {
            int n = Physics2D.CircleCast(from, tipRadius, flightDir, castFilter, castHits, budget);
            int best = Nearest(n);
            if (best < 0)
            {
                tip = from + flightDir * budget;
                budget = 0f;
                break;
            }

            RaycastHit2D h = castHits[best];
            tip = h.centroid;
            if (HitPlayer(h) || bounceCount >= maxBounces)
            {
                Physics2D.queriesStartInColliders = prevQueries;
                Destroy(gameObject);
                return;
            }

            Reflect(h);
            budget -= h.distance;
            from = tip;
        }
        Physics2D.queriesStartInColliders = prevQueries;
        centerFromTip();
        var touched = BodyTouchingPlayer();
        if (touched != null)
        {
            ApplyPlayerHit(touched);
            Destroy(gameObject);
        }
    }

    int Nearest(int count)
    {
        int best = -1;
        for (int i = 0; i < count; i++)
            if (best < 0 || castHits[i].distance < castHits[best].distance) best = i;
        return best;
    }

    // Попадание в игрока: урон и исчезновение (true). Любое другое попадание - стена/дверь (false).
    bool HitPlayer(RaycastHit2D h)
    {
        if (h.collider.gameObject.layer != playerLayer) return false;
        ApplyPlayerHit(h.collider);
        return true;
    }

    void ApplyPlayerHit(Collider2D playerCollider)
    {
        // Реального нанесения урона игроку в проекте пока нет нигде (подтверждено: EnemyAttackZone - просто
        // счётчик попаданий) - считаем попадание и наносим урон через PlayerHealth.
        TotalHits++;
        var health = playerCollider.GetComponentInParent<PlayerHealth>();
        if (health != null) health.TakeDamage(damage);
    }

    // Тело куная (от острия до хвоста) пересекает игрока: игрок задет вскользь или тело оказалось в нём после отскока.
    Collider2D BodyTouchingPlayer()
    {
        float length = 2f * tipLocalOffset.magnitude;
        if (length < 0.05f) return null;
        return Physics2D.OverlapCapsule(rb.position, new Vector2(bodyHitWidth, length), CapsuleDirection2D.Vertical, transform.eulerAngles.z, 1 << playerLayer);
    }

    // Отражение от поверхности: точка отскакивает, тело куная просто разворачивается за ней.
    void Reflect(RaycastHit2D h)
    {
        Vector2 normal = h.normal;
        Vector2 reflected = Vector2.Reflect(flightDir, normal);
        flightDir = reflected.sqrMagnitude < 0.0001f ? normal : reflected.normalized;
        bounceCount++;
        FaceDirection(flightDir);

        if (doorPushFactor > 0f && h.rigidbody != null && h.rigidbody.bodyType == RigidbodyType2D.Dynamic)
            h.rigidbody.AddForceAtPosition(-normal * (rb.mass * speed * doorPushFactor), h.point, ForceMode2D.Impulse);

        // Отступ от поверхности больше радиуса точки: следующий луч не стартует внутри той же стены.
        tip = h.centroid + normal * (tipRadius + 0.01f);
    }

    void centerFromTip()
    {
        rb.position = tip - (Vector2)transform.TransformVector(tipLocalOffset);
    }

    void FaceDirection(Vector2 direction)
    {
        // Та же конвенция «+Y при повороте 0», что и во всём проекте.
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);
        if (rb != null) rb.rotation = angle;
    }
}
