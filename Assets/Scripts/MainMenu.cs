using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Стартовое меню: чёрный фон, название сверху и кнопка-треугольник. Треугольник открывает выбор уровня:
// пять квадратиков 1-5, все открыты заранее (тестовый билд). Крестик возвращает к треугольнику.
// Из паузы уровня («В меню») меню открывается сразу на выборе уровня (флаг OpenLevelSelect).
// Интерфейс строится в коде (обычный Text, тот же шрифт, что и на уровнях).
public class MainMenu : MonoBehaviour
{
    // Ставит пауза уровня: меню сразу показывает выбор уровня, а не стартовый экран.
    public static bool OpenLevelSelect;

    [Tooltip("Сцены уровней по порядку (должны быть в Build Settings).")]
    public string[] levelScenes = { "Level_1", "Level_2", "Level_3", "Level_4", "Level_5" };

    private GameObject playButton;
    private GameObject levelsPanel;

    void Awake()
    {
        // Вернулись из уровня, где время могло стоять (победа/смерть/пауза).
        Time.timeScale = 1f;
        Time.fixedDeltaTime = 0.02f;
        TutorialLevel1.Completed = false; // из меню обучение на первом уровне проходится заново
        BuildUI();
        ShowLevels(OpenLevelSelect);
        OpenLevelSelect = false;
    }

    void BuildUI()
    {
        var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0f;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var titleGo = new GameObject("Title", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        titleGo.transform.SetParent(canvasGo.transform, false);
        var rt = (RectTransform)titleGo.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -140f);
        rt.sizeDelta = new Vector2(1600f, 200f);
        var title = titleGo.GetComponent<Text>();
        title.font = font;
        title.fontSize = 140;
        title.alignment = TextAnchor.MiddleCenter;
        title.color = Color.white;
        title.text = "Astral Shinobi";
        title.raycastTarget = false;

        playButton = UIShapes.IconButton("Play", canvasGo.transform, UIShapes.Triangle(), new Vector2(0f, -60f), 260f, () => ShowLevels(true)).gameObject;

        levelsPanel = new GameObject("Levels", typeof(RectTransform));
        levelsPanel.transform.SetParent(canvasGo.transform, false);
        var lrt = (RectTransform)levelsPanel.transform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = lrt.offsetMax = Vector2.zero;

        const float size = 230f, gap = 50f;
        for (int i = 0; i < levelScenes.Length; i++)
        {
            string scene = levelScenes[i];
            float x = (i - (levelScenes.Length - 1) * 0.5f) * (size + gap);
            var go = new GameObject("Level_" + (i + 1), typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(levelsPanel.transform, false);
            var brt = (RectTransform)go.transform;
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0.5f);
            brt.anchoredPosition = new Vector2(x, -60f);
            brt.sizeDelta = new Vector2(size, size);
            var img = go.GetComponent<Image>();
            img.color = new Color(0.9f, 0.9f, 0.9f, 1f);
            go.GetComponent<Button>().targetGraphic = img;
            go.GetComponent<Button>().onClick.AddListener(() => SceneManager.LoadScene(scene));

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            labelGo.transform.SetParent(go.transform, false);
            var lbrt = (RectTransform)labelGo.transform;
            lbrt.anchorMin = Vector2.zero; lbrt.anchorMax = Vector2.one; lbrt.offsetMin = lbrt.offsetMax = Vector2.zero;
            var text = labelGo.GetComponent<Text>();
            text.font = font;
            text.fontSize = 150;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.black;
            text.text = (i + 1).ToString();
            text.raycastTarget = false;
        }
        // Крестик «назад» в левом верхнем углу экрана.
        var back = UIShapes.IconButton("Back", levelsPanel.transform, UIShapes.Cross(), Vector2.zero, 130f, () => ShowLevels(false));
        var bkrt = (RectTransform)back.transform;
        bkrt.anchorMin = bkrt.anchorMax = bkrt.pivot = new Vector2(0f, 1f);
        bkrt.anchoredPosition = new Vector2(40f, -40f);
    }

    // true - выбор уровня, false - стартовый экран с треугольником.
    void ShowLevels(bool show)
    {
        playButton.SetActive(!show);
        levelsPanel.SetActive(show);
    }
}
