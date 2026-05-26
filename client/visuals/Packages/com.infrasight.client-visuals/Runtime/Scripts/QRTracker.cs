using UnityEngine;
using TMPro;

public class QRTracker : MonoBehaviour
{
    public string qrID;
    [SerializeField] private TMP_Text label;
    private string statusMachineName;

    private void Start()
    {
        RefreshLabel();
    }

    public void RefreshLabel()
    {
        if (label == null)
        {
            return;
        }

        switch (qrID)
        {
            case "CONNECTING":
                label.text = $"Connexion vers {FormatMachineName(statusMachineName)}...";
                label.color = Color.white;
                break;
            case "CONNECTION_FAILED":
                label.text = $"Connexion impossible vers {FormatMachineName(statusMachineName)}.";
                label.color = Color.red;
                break;
            case "QR_Sphere":
                label.text = "the Sphere";
                label.color = Color.red;
                break;
            case "QR_Cube":
                label.text = "the Cube";
                label.color = Color.blue;
                break;
            default:
                label.text = string.IsNullOrWhiteSpace(qrID) ? "Unknown ID" : qrID;
                label.color = Color.yellow;
                break;
        }
    }

    public void SetQrId(string value)
    {
        qrID = value;
        RefreshLabel();
    }

    public void SetConnectingMachine(string machineName)
    {
        qrID = "CONNECTING";
        statusMachineName = machineName;
        RefreshLabel();
    }

    public void SetConnectionFailedMachine(string machineName)
    {
        qrID = "CONNECTION_FAILED";
        statusMachineName = machineName;
        RefreshLabel();
    }

    private static string FormatMachineName(string machineName)
    {
        return string.IsNullOrWhiteSpace(machineName) ? "machine inconnue" : machineName;
    }
}
