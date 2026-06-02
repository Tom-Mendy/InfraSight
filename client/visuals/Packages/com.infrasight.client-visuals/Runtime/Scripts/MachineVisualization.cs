using UnityEngine;
using TMPro;
using UnityEngine.UI;
using XCharts.Runtime;

public class MachineVisualization : MonoBehaviour
{
    private const int MaxHistorySeconds = 60;

    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text cpuText;
    [SerializeField] private TMP_Text ramText;
    [SerializeField] private LineChart cpuChart;
    [SerializeField] private LineChart ramChart;

    private DockerContainerVisualizationActions dockerActions;
    private DockerContainerVisualizationSummary dockerSummary;
    private TMP_Text dockerSummaryText;
    private TMP_Text toggleStoppedButtonText;
    private GameObject dockerControlsRoot;

    private void AddMachineMetricData(LineChart chart, string serieName, string label, float value)
    {
        if (chart == null)
        {
            return;
        }

        if (chart.series == null || chart.series.Count == 0)
        {
            chart.AddSerie<Line>(serieName);
        }

        float clampedValue = Mathf.Clamp(value, 0f, 100f);
        Serie serie = chart.GetSerie(0);
        if (serie == null)
        {
            return;
        }

        XAxis xAxis = chart.GetChartComponent<XAxis>(0);
        bool isCategoryAxis = xAxis != null && xAxis.IsCategory();
        bool hasSameSecondLabel = isCategoryAxis
            && xAxis.data != null
            && xAxis.data.Count > 0
            && xAxis.data[xAxis.data.Count - 1] == label;

        if (hasSameSecondLabel && serie.dataCount > 0)
        {
            chart.UpdateData(0, serie.dataCount - 1, clampedValue);
            return;
        }

        if (isCategoryAxis)
        {
            chart.AddXAxisData(label);
        }

        chart.AddData(0, clampedValue);

        while (serie.dataCount > MaxHistorySeconds)
        {
            serie.RemoveData(0);
            if (isCategoryAxis && xAxis.data != null && xAxis.data.Count > 0)
            {
                xAxis.RemoveData(0);
            }
        }
    }

    public void UpdateMachineChart(ServerDataPayload payload)
    {
        if (payload?.machine == null)
        {
            return;
        }

        if (nameText != null)
        {
            nameText.text = string.IsNullOrWhiteSpace(payload.machine.name) ? "Machine" : payload.machine.name;
        }

        if (cpuText != null)
        {
            cpuText.text = $"{Mathf.Clamp(payload.machine.cpu, 0f, 100f):0.#}% CPU";
        }

        if (ramText != null)
        {
            ramText.text = $"{Mathf.Clamp(payload.machine.ram, 0f, 100f):0.#}% RAM";
        }

        if (cpuChart == null || ramChart == null)
        {
            return;
        }

        string label = payload.timestamp != default
            ? payload.timestamp.ToLocalTime().ToString("HH:mm:ss")
            : System.DateTime.Now.ToString("HH:mm:ss");

        AddMachineMetricData(cpuChart, "CPU", label, payload.machine.cpu);
        AddMachineMetricData(ramChart, "RAM", label, payload.machine.ram);
    }

    public void ConfigureDockerActions(DockerContainerVisualizationActions actions)
    {
        dockerActions = actions;
        EnsureDockerControls();
        RefreshDockerControls();
    }

    public void UpdateDockerSummary(DockerContainerVisualizationSummary summary)
    {
        dockerSummary = summary;
        EnsureDockerControls();
        RefreshDockerControls();
    }

    private void EnsureDockerControls()
    {
        if (dockerControlsRoot != null)
        {
            return;
        }

        dockerControlsRoot = new GameObject("Docker Controls", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        dockerControlsRoot.transform.SetParent(transform, false);
        dockerControlsRoot.transform.localPosition = new Vector3(0f, 0.85f, -0.05f);
        dockerControlsRoot.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        dockerControlsRoot.transform.localScale = Vector3.one * 0.004f;

        RectTransform rootRect = dockerControlsRoot.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(360f, 160f);

        Canvas canvas = dockerControlsRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        GameObject panelObject = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        panelObject.transform.SetParent(dockerControlsRoot.transform, false);
        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        Image panelImage = panelObject.GetComponent<Image>();
        panelImage.color = new Color(0.02f, 0.04f, 0.06f, 0.78f);

        VerticalLayoutGroup layoutGroup = panelObject.GetComponent<VerticalLayoutGroup>();
        layoutGroup.padding = new RectOffset(12, 12, 10, 10);
        layoutGroup.spacing = 6f;
        layoutGroup.childAlignment = TextAnchor.UpperCenter;
        layoutGroup.childControlWidth = true;
        layoutGroup.childControlHeight = true;
        layoutGroup.childForceExpandWidth = true;
        layoutGroup.childForceExpandHeight = false;

        dockerSummaryText = CreateText(panelObject.transform, "Docker Summary", 24f);

        GameObject buttonRow = new GameObject("Actions", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        buttonRow.transform.SetParent(panelObject.transform, false);
        HorizontalLayoutGroup rowLayout = buttonRow.GetComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 8f;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = false;

        toggleStoppedButtonText = CreateButton(buttonRow.transform, "Stopped", () => dockerActions?.ToggleStoppedContainers?.Invoke());
        CreateButton(buttonRow.transform, "Recenter", () => dockerActions?.RecenterContainerRing?.Invoke());
        CreateButton(buttonRow.transform, "Refresh", () => dockerActions?.RefreshContainers?.Invoke());
    }

    private void RefreshDockerControls()
    {
        if (dockerSummaryText != null)
        {
            if (dockerSummary == null || dockerSummary.TotalCount == 0)
            {
                dockerSummaryText.text = "Docker: no containers";
            }
            else
            {
                dockerSummaryText.text = $"Docker: {dockerSummary.RunningCount}/{dockerSummary.TotalCount} running, {dockerSummary.VisibleCount} visible";
                if (dockerSummary.HiddenCount > 0)
                {
                    dockerSummaryText.text += $", {dockerSummary.HiddenCount} hidden";
                }
            }
        }

        if (toggleStoppedButtonText != null)
        {
            bool showStopped = dockerSummary?.ShowStoppedContainers ?? true;
            toggleStoppedButtonText.text = showStopped ? "Stopped on" : "Stopped off";
        }
    }

    private static TMP_Text CreateText(Transform parent, string name, float fontSize)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        TMP_Text text = textObject.GetComponent<TMP_Text>();
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.textWrappingMode = TextWrappingModes.Normal;

        RectTransform rectTransform = textObject.GetComponent<RectTransform>();
        rectTransform.sizeDelta = new Vector2(320f, 42f);

        return text;
    }

    private static TMP_Text CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction action)
    {
        GameObject buttonObject = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);

        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.12f, 0.2f, 0.28f, 0.95f);

        Button button = buttonObject.GetComponent<Button>();
        button.onClick.AddListener(action);

        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.sizeDelta = new Vector2(100f, 48f);

        TMP_Text text = CreateText(buttonObject.transform, label, 20f);
        RectTransform textRect = text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        text.text = label;

        return text;
    }
}
