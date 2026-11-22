using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Пауза на уровне: кнопка «||» в правом верхнем углу и панель с тремя кнопками (продолжить, рестарт,
// в меню с выбором уровня). Создаётся из LevelManager.BuildUI, интерфейс строится в коде (legacy Text).
// Время останавливается через Time.timeScale = 0; пока пауза, LateUpdate каждый кадр возвращает 0, чтобы
// замедление при прицеливании и прочие писатели timeScale (см. CLAUDE.md) паузу не сбивали.
public class PauseMenu : MonoBehaviour
{
    public static bool Paused { get; private set; }

    [Tooltip("Сцена меню, в которую ведёт кнопка «В меню» (откроется сразу на выборе уровня).")]
    public string menuSceneName = "MainMenu";
    public float buttonSize = 110f;
    public float margin = 30f;

    private GameObject panel;
    private CanvasGroup buttonGroup;
    private float savedScale = 1f;
    private float savedFixed = 0.02f;

    void Awake()
    {
        Paused = false;
        Build();
    }

    void OnDestroy()
    {
        Paused = false;
    }

    void Start()
    {
        transform.SetAsLastSibling(); // кнопка поверх остального интерфейса, созданного другими скриптами
    }

    void Build()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // Панель создаётся раньше кнопки: кнопка паузы остаётся над ней и повторным нажатием снимает паузу.
        panel = new GameObject("PausePanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panel.transform.SetParent(transform, false);
        var prt = (RectTransform)panel.transform;
        prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one; prt.offsetMin = prt.offsetMax = Vector2.zero;
        panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.8f);

        var title = NewText("Title", panel.transform, font, 120, new Vector2(0f, 300f), new Vector2(1000f, 180f));
        title.text = "Пауза";
        title.color = Color.white;
        NewButton("Resume", panel.transform, font, "Продолжить", new Vector2(0f, 90f), Resume);
        NewButton("Restart", panel.transform, font, "Рестарт", new Vector2(0f, -90f), Restart);
        NewButton("ToMenu", panel.transform, font, "В меню", new Vector2(0f, -270f), ToMenu);
        panel.SetActive(false);

        var go = new GameObject("PauseButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(CanvasGroup));
        go.transform.SetParent(transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-margin, -margin);
        rt.sizeDelta = new Vector2(buttonSize, buttonSize);
        var img = go.GetComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.55f);
        go.GetComponent<Button>().targetGraphic = img;
        go.GetComponent<Button>().onClick.AddListener(Toggle);
        buttonGroup = go.GetComponent<CanvasGroup>();
        for (int i = -1; i <= 1; i += 2)
        {
            var bar = new GameObject("Bar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            bar.transform.SetParent(go.transform, false);
            var brt = (RectTransform)bar.transform;
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0.5f);
            brt.anchoredPosition = new Vector2(i * buttonSize * 0.16f, 0f);
            brt.sizeDelta = new Vector2(buttonSize * 0.13f, buttonSize * 0.5f);
            var bi = bar.GetComponent<Image>();
            bi.color = Color.white;
            bi.raycastTarget = false;
        }
    }

    void Update()
    {
        if (Paused) return;
        // Паузу нельзя включить посреди мини-игры парирования, на экране смерти/победы и в паузе обучения:
        // там время ведут свои скрипты.
        bool allowed = !ParrySystem.Active && !PlayerHealth.IsDead && !LevelManager.Finished && !TutorialLevel1.Pausing;
        buttonGroup.alpha = allowed ? 1f : 0f;
        buttonGroup.interactable = allowed;
        buttonGroup.blocksRaycasts = allowed;
    }

    void LateUpdate()
    {
        if (Paused) Time.timeScale = 0f;
    }

    void Toggle()
    {
        if (Paused) Resume();
        else Pause();
    }

    void Pause()
    {
        savedScale = Time.timeScale;
        savedFixed = Time.fixedDeltaTime;
        Paused = true;
        Time.timeScale = 0f;
        panel.SetActive(true);
        transform.SetAsLastSibling();
    }

    void Resume()
    {
        Paused = false;
        Time.timeScale = savedScale;
        Time.fixedDeltaTime = savedFixed;
        panel.SetActive(false);
    }

    void Restart()
    {
        ResetTime();
        SceneManager.LoadScene(SceneManager.GetActiveScene().path);
    }

    void ToMenu()
    {
        ResetTime();
        MainMenu.OpenLevelSelect = true;
        SceneManager.LoadScene(menuSceneName);
    }

    static void ResetTime()
    {
        Paused = false;
        Time.timeScale = 1f;
        Time.fixedDeltaTime = 0.02f;
    }

    static Text NewText(string name, Transform parent, Font font, int size, Vector2 pos, Vector2 dim)
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
        t.alignment = TextAnchor.MiddleCenter;
        t.raycastTarget = false;
        return t;
    }

    static void NewButton(string name, Transform parent, Font font, string label, Vector2 pos, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(620f, 150f);
        var img = go.GetComponent<Image>();
        img.color = new Color(0.9f, 0.9f, 0.9f, 1f);
        go.GetComponent<Button>().targetGraphic = img;
        go.GetComponent<Button>().onClick.AddListener(onClick);
        var t = NewText("Label", go.transform, font, 72, Vector2.zero, rt.sizeDelta);
        t.text = label;
        t.color = Color.black;
    }
}
