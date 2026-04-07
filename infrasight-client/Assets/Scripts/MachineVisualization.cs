using UnityEngine;
using TMPro;
public class MachineVisualization : MonoBehaviour
{
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text cpuText;
    [SerializeField] private TMP_Text ramText;

    public void UpdateVisualization(MachineDataPayload machineData)
    {
        if (machineData != null)
        {
            nameText.text = machineData.name;
            cpuText.text = $"{machineData.cpu:F1}%";
            ramText.text = $"{machineData.ram:F1}%";
        }
        Debug.Log("Update Machine infos");
    }
}
