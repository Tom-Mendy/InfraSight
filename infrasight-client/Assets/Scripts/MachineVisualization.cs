using UnityEngine;
using TMPro;
using XCharts.Runtime;

public class MachineVisualization : MonoBehaviour
{
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private LineChart cpuChart;
    [SerializeField] private LineChart ramChart;

    public void UpdateMachineChart(ServerDataPayload payload)
    {
        if (cpuChart == null || ramChart == null || payload?.machine == null)
        {
            return;
        }

        if (cpuChart.series == null || cpuChart.series.Count == 0)
        {
            cpuChart.AddSerie<Line>("CPU");
        }

        if (ramChart.series == null || ramChart.series.Count == 0)
        {
            ramChart.AddSerie<Line>("RAM");
        }

        string label = payload.timestamp != default
            ? payload.timestamp.ToLocalTime().ToString("HH:mm:ss")
            : System.DateTime.Now.ToString("HH:mm:ss");

        XAxis cpuXAxis = cpuChart.GetChartComponent<XAxis>(0);
        if (cpuXAxis != null && cpuXAxis.IsCategory())
        {
            cpuChart.AddXAxisData(label);
        }

        XAxis ramXAxis = ramChart.GetChartComponent<XAxis>(0);
        if (ramXAxis != null && ramXAxis.IsCategory())
        {
            ramChart.AddXAxisData(label);
        }

        cpuChart.AddData(0, Mathf.Clamp(payload.machine.cpu, 0f, 100f));
        ramChart.AddData(0, Mathf.Clamp(payload.machine.ram, 0f, 100f));
    }
}
