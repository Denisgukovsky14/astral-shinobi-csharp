using UnityEngine;
using Pathfinding;

// Короткое замедление врага после попадания ножа (Difficulty.hitStunDuration): понятно, что нож попал.
// Добавляется на врага автоматически при попадании. Замедляет через ai.maxSpeed; во время рывка AntiTricker
// (он сам владеет maxSpeed) не вмешивается, а восстановление откладывается до конца рывка.
public class EnemyHitStun : MonoBehaviour
{
    private AIPath ai;
    private AntiTricker antiTricker;
    private float baseSpeed;
    private float factor;
    private float until;
    private bool slowed;

    public static void Apply(AIPath target, float duration, float speedFactor)
    {
        if (target == null || duration <= 0f) return;
        var stun = target.GetComponent<EnemyHitStun>();
        if (stun == null) stun = target.gameObject.AddComponent<EnemyHitStun>();
        stun.Begin(target, duration, speedFactor);
    }

    void Begin(AIPath path, float duration, float speedFactor)
    {
        ai = path;
        antiTricker = GetComponent<AntiTricker>();
        if (!slowed) baseSpeed = ai.maxSpeed;
        slowed = true;
        factor = speedFactor;
        until = Time.time + duration;
    }

    void LateUpdate()
    {
        if (!slowed) return;
        if (antiTricker != null && antiTricker.IsCountering) return; // рывок ведёт AntiTricker, потом доделаем
        if (Time.time >= until)
        {
            ai.maxSpeed = baseSpeed;
            slowed = false;
            return;
        }
        ai.maxSpeed = baseSpeed * factor;
    }

    void OnDisable()
    {
        // враг погиб или выключен: вернуть скорость, чтобы не остаться замедленным
        if (slowed && ai != null) ai.maxSpeed = baseSpeed;
        slowed = false;
    }
}
