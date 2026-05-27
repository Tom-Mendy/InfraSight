using UnityEngine;
using TMPro;
using XCharts.Runtime;

public class MachineVisualization : MonoBehaviour
{
    private const int MaxHistorySeconds = 60;

    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text cpuText;
    [SerializeField] private TMP_Text ramText;
    [SerializeField] private LineChart cpuChart;
    [SerializeField] private LineChart ramChart;

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
}
