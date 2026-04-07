using UnityEngine;
using TMPro;

public class QRTracker : MonoBehaviour
{
    public string qrID;
    [SerializeField] private TMP_Text label;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        Debug.Log($"Qr Traker started for QR ID: {qrID}");
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
                label.text = "Unknow ID";
                label.color = Color.yellow;
                break;
        }
    }

    // Update is called once per frame
    void Update()
    {

    }
}
