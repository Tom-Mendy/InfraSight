using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

public sealed class AndroidMobileDebugConsole : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private Button openButton;
    [SerializeField] private Button copyButton;
    [SerializeField] private Button clearButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private Text logText;
    [SerializeField] private ScrollRect scrollRect;

    private void Awake()
    {
        openButton?.onClick.AddListener(Open);
        copyButton?.onClick.AddListener(Copy);
        clearButton?.onClick.AddListener(Clear);
        closeButton?.onClick.AddListener(Close);
        panel?.SetActive(false);
    }

    private void OnEnable()
    {
        AndroidMobileLogBuffer.Updated += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        AndroidMobileLogBuffer.Updated -= Refresh;
    }

    private void OnDestroy()
    {
        openButton?.onClick.RemoveListener(Open);
        copyButton?.onClick.RemoveListener(Copy);
        clearButton?.onClick.RemoveListener(Clear);
        closeButton?.onClick.RemoveListener(Close);
    }

    private void Open()
    {
        panel?.SetActive(true);
        Refresh();
    }

    private void Close()
    {
        panel?.SetActive(false);
    }

    private void Copy()
    {
        GUIUtility.systemCopyBuffer = AndroidMobileLogBuffer.Transcript;
    }

    private void Clear()
    {
        AndroidMobileLogBuffer.Clear();
    }

    private void Refresh()
    {
        if (logText == null)
        {
            return;
        }

        string transcript = AndroidMobileLogBuffer.Transcript;
        logText.text = string.IsNullOrEmpty(transcript) ? "Aucun log capture." : transcript;
        logText.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, logText.preferredHeight);

        if (scrollRect != null)
        {
            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0f;
        }
    }
}

internal static class AndroidMobileLogBuffer
{
    private const int Capacity = 200;
    private static readonly List<string> Messages = new List<string>(Capacity);

    internal static event Action Updated;

    internal static string Transcript
    {
        get
        {
            var builder = new StringBuilder();
            for (int index = 0; index < Messages.Count; index++)
            {
                if (index > 0)
                {
                    builder.AppendLine();
                }

                builder.Append(Messages[index]);
            }

            return builder.ToString();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        Messages.Clear();
        Application.logMessageReceived -= Capture;
        Application.logMessageReceived += Capture;
    }

    internal static void Clear()
    {
        Messages.Clear();
        Updated?.Invoke();
    }

    private static void Capture(string condition, string stackTrace, LogType type)
    {
        string message = $"[{DateTime.Now:HH:mm:ss}] [{type}] {condition}";
        if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            && !string.IsNullOrWhiteSpace(stackTrace))
        {
            message += Environment.NewLine + stackTrace.TrimEnd();
        }

        if (Messages.Count == Capacity)
        {
            Messages.RemoveAt(0);
        }

        Messages.Add(message);
        Updated?.Invoke();
    }
}
