using UnityEngine;

public class QRTracker : MonoBehaviour
{
    public string qrID;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        Debug.Log($"Qr Traker started for QR ID: {qrID}");
    }

    // Update is called once per frame
    void Update()
    {

    }
}
