using UnityEngine;

// Зона-пробуждение: как только игрок заходит в неё, спящие враги из списка просыпаются (CombatAgent.Wake)
// и идут на игрока. Одноразовая. Враги должны быть помечены Start Dormant + Trigger Only, иначе их
// разбудит близость игрока или оклик бойца группы. Разовое решение для демо (4-й уровень), общей
// системы зон ответственности пока нет.
// Нужен триггер-коллайдер (BoxCollider2D с галкой Is Trigger) на этом же объекте.
[RequireComponent(typeof(Collider2D))]
public class EnemyWakeTrigger : MonoBehaviour
{
    [Tooltip("Кого будит зона.")]
    public CombatAgent[] targets;

    public bool Triggered { get; private set; }

    void Reset()
    {
        var col = GetComponent<Collider2D>();
        col.isTrigger = true;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (Triggered || other.GetComponentInParent<PlayerHealth>() == null) return;
        Triggered = true;
        foreach (var t in targets)
            if (t != null) t.Wake();
    }

    void OnDrawGizmos()
    {
        var box = GetComponent<BoxCollider2D>();
        if (box == null) return;
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.9f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(box.offset, box.size);
    }
}
