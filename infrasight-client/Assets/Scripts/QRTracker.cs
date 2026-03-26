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
        if (qrID == "QR_Sphere")
        {
            label.text = "the Sphere";
            label.color = Color.red;
        }
        if (qrID == "QR_Cube")
        {
            label.text = "the Cube";
            label.color = Color.blue;
        }
    }

    // Update is called once per frame
    void Update()
    {

    }
}
