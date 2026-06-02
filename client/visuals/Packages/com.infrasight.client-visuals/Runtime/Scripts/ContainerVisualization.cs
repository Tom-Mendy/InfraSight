using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ContainerVisualization : MonoBehaviour
{
    private static readonly Color RunningColor = new(0.16f, 0.85f, 0.46f, 1f);
    private static readonly Color StoppedColor = new(0.84f, 0.18f, 0.18f, 1f);
    private static readonly Color UnknownColor = new(0.92f, 0.68f, 0.16f, 1f);
    private static readonly Color AggregateColor = new(0.25f, 0.64f, 1f, 1f);
    private static readonly Color SelectedPanelColor = new(0.03f, 0.05f, 0.08f, 0.88f);
    private static readonly Color DefaultPanelColor = new(0f, 0f, 0f, 0.7f);

    [SerializeField] private TMP_Text label;
    [SerializeField] private Image panel;
    [SerializeField] private Renderer targetRenderer;

    private ContainerDataPayload currentContainer;
    private string currentContainerId;
    private float peakRxBytesPerSecond;
    private float peakTxBytesPerSecond;
    private int hiddenAggregateCount;
    private bool isAggregate;
    private bool isSelected;
    private bool isPinned;

    private void Awake()
    {
        label ??= GetComponentInChildren<TMP_Text>(true);
        panel ??= GetComponentInChildren<Image>(true);
        targetRenderer ??= GetComponentInChildren<Renderer>(true);
    }

    private void OnMouseUpAsButton()
    {
        if (isAggregate)
        {
            isSelected = !isSelected;
        }
        else if (!isSelected)
        {
            isSelected = true;
            isPinned = false;
        }
        else
        {
            isPinned = !isPinned;
            isSelected = isPinned;
        }

        RefreshView();
    }

    public void SetContainerData(ContainerDataPayload containerData)
    {
        string nextContainerId = containerData?.id ?? string.Empty;
        if (!string.Equals(currentContainerId, nextContainerId, StringComparison.Ordinal))
        {
            peakRxBytesPerSecond = 0f;
            peakTxBytesPerSecond = 0f;
            currentContainerId = nextContainerId;
        }

        currentContainer = containerData;
        peakRxBytesPerSecond = Mathf.Max(peakRxBytesPerSecond, containerData?.rxBytesPerSecond ?? 0f);
        peakTxBytesPerSecond = Mathf.Max(peakTxBytesPerSecond, containerData?.txBytesPerSecond ?? 0f);
        hiddenAggregateCount = 0;
        isAggregate = false;
        RefreshView();
    }

    public void SetAggregateData(int hiddenCount)
    {
        currentContainer = null;
        hiddenAggregateCount = Mathf.Max(0, hiddenCount);
        isAggregate = true;
        RefreshView();
    }

    public void SetSelected(bool selected)
    {
        isSelected = selected;
        isPinned = selected && isPinned;
        RefreshView();
    }

    private void RefreshView()
    {
        if (isAggregate)
        {
            RefreshAggregateView();
            return;
        }

        if (currentContainer == null)
        {
            return;
        }

        Color statusColor = StatusColor(currentContainer.status);
        ApplyColor(statusColor);

        float normalizedCpu = Mathf.Clamp01(currentContainer.cpu / 100f);
        float scale = Mathf.Lerp(0.16f, 0.28f, normalizedCpu);
        transform.localScale = Vector3.one * (isSelected || isPinned ? scale * 1.2f : scale);

        if (label != null)
        {
            label.text = isSelected || isPinned ? DetailLabel(currentContainer) : CompactLabel(currentContainer);
            label.color = Color.white;
            label.fontSize = isSelected || isPinned ? 24f : 30f;
        }

        if (panel != null)
        {
            panel.color = isSelected || isPinned ? SelectedPanelColor : DefaultPanelColor;
        }
    }

    private void RefreshAggregateView()
    {
        ApplyColor(AggregateColor);
        transform.localScale = Vector3.one * (isSelected ? 0.26f : 0.2f);

        if (label != null)
        {
            label.text = isSelected
                ? $"+{hiddenAggregateCount} conteneurs\nMasques par densite"
                : $"+{hiddenAggregateCount}";
            label.color = Color.white;
            label.fontSize = isSelected ? 24f : 36f;
        }

        if (panel != null)
        {
            panel.color = isSelected ? SelectedPanelColor : DefaultPanelColor;
        }
    }

    private void ApplyColor(Color baseColor)
    {
        if (targetRenderer != null)
        {
            targetRenderer.material.color = baseColor;
        }
    }

    private static string CompactLabel(ContainerDataPayload containerData)
    {
        float trafficRate = Mathf.Max(containerData.rxBytesPerSecond, containerData.txBytesPerSecond);
        string traffic = trafficRate > 0f ? $"\n{FormatBytesPerSecond(trafficRate)}" : string.Empty;
        return $"{DisplayName(containerData)}\n{Mathf.Clamp(containerData.cpu, 0f, 100f):0.#}%{traffic}";
    }

    private string DetailLabel(ContainerDataPayload containerData)
    {
        string id = string.IsNullOrWhiteSpace(containerData.id) ? "unknown" : containerData.id;
        string shortId = id.Length > 12 ? id.Substring(0, 12) : id;
        string status = string.IsNullOrWhiteSpace(containerData.status) ? "unknown" : containerData.status;
        string networks = containerData.networkNames == null || containerData.networkNames.Length == 0
            ? "network unknown"
            : string.Join(", ", containerData.networkNames);
        return $"{DisplayName(containerData)}\n{status} | CPU {Mathf.Clamp(containerData.cpu, 0f, 100f):0.#}%\nRX {FormatBytesPerSecond(containerData.rxBytesPerSecond)} | TX {FormatBytesPerSecond(containerData.txBytesPerSecond)}\nPeak {FormatBytesPerSecond(peakRxBytesPerSecond)} / {FormatBytesPerSecond(peakTxBytesPerSecond)}\n{networks}\nID {shortId}\nTap: pin/unpin";
    }

    private static string DisplayName(ContainerDataPayload containerData)
    {
        return string.IsNullOrWhiteSpace(containerData.name) ? containerData.id : containerData.name;
    }

    private static Color StatusColor(string status)
    {
        if (string.Equals(status, "running", StringComparison.OrdinalIgnoreCase))
        {
            return RunningColor;
        }

        if (string.Equals(status, "stopped", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "exited", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "created", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "dead", StringComparison.OrdinalIgnoreCase))
        {
            return StoppedColor;
        }

        return UnknownColor;
    }

    private static string FormatBytesPerSecond(float bytesPerSecond)
    {
        float value = Mathf.Max(0f, bytesPerSecond);
        string[] units = { "B/s", "KB/s", "MB/s", "GB/s" };
        int unitIndex = 0;
        while (value >= 1024f && unitIndex < units.Length - 1)
        {
            value /= 1024f;
            unitIndex++;
        }

        return $"{value:0.#} {units[unitIndex]}";
    }
}
