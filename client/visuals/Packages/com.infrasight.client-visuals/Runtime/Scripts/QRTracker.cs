using UnityEngine;
using TMPro;

public class QRTracker : MonoBehaviour
{
    public string qrID;
    [SerializeField] private TMP_Text label;

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
                label.text = "Connecting...";
                label.color = Color.white;
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
}
