using UnityEngine;

// Доступ к текущей сложности: Difficulty.Current. По умолчанию - Resources/Difficulty/Normal. LevelManager
// на старте уровня переключает на набор, назначенный ему в поле Difficulty (если оно заполнено).
public static class Difficulty
{
    private static DifficultySettings current;

    public static DifficultySettings Current
    {
        get
        {
            if (current == null) current = Resources.Load<DifficultySettings>("Difficulty/Normal");
            if (current == null) current = ScriptableObject.CreateInstance<DifficultySettings>(); // нет ассета - значения по умолчанию
            return current;
        }
    }

    public static void Set(DifficultySettings settings)
    {
        if (settings != null) current = settings;
    }
}
