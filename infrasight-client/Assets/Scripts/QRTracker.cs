using TMPro;
using UnityEngine;

public class QRTracker : MonoBehaviour
{
    public string qrID;
    [SerializeField] private TMP_Text label;

    private void Start()
    {
        Debug.Log($"QRTracker started for QR ID: {qrID}");

        if (label == null)
        {
            return;
        }

        switch (qrID)
        {
            case "QR_Sphere":
                label.text = "the Sphere";
                label.color = Color.red;
                break;
            case "QR_Cube":
                label.text = "the Cube";
                label.color = Color.blue;
                break;
            default:
                label.text = "Unknown ID";
                label.color = Color.yellow;
                break;
        }
    }
}