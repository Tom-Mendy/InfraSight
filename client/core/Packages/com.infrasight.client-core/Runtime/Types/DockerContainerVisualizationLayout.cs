using System;
using System.Collections.Generic;

public static class DockerContainerVisualizationLayout
{
    public static List<ContainerDataPayload> SortForDisplay(ContainerDataPayload[] containers)
    {
        List<ContainerDataPayload> sortedContainers = new();
        if (containers == null)
        {
            return sortedContainers;
        }

        HashSet<string> seenIds = new();
        foreach (ContainerDataPayload container in containers)
        {
            if (container == null || string.IsNullOrWhiteSpace(container.id) || !seenIds.Add(container.id))
            {
                continue;
            }

            sortedContainers.Add(container);
        }

        sortedContainers.Sort(CompareContainers);
        return sortedContainers;
    }

    public static List<ContainerDataPayload> FilterStoppedContainers(
        List<ContainerDataPayload> containers,
        bool showStoppedContainers)
    {
        if (showStoppedContainers)
        {
            return containers;
        }

        List<ContainerDataPayload> filteredContainers = new();
        foreach (ContainerDataPayload container in containers)
        {
            if (IsRunning(container.status))
            {
                filteredContainers.Add(container);
            }
        }

        return filteredContainers;
    }

    public static int CountRunning(List<ContainerDataPayload> containers)
    {
        int runningCount = 0;
        foreach (ContainerDataPayload container in containers)
        {
            if (IsRunning(container.status))
            {
                runningCount++;
            }
        }

        return runningCount;
    }

    public static int VisibleCountFor(int displayCount, int maxVisible)
    {
        return Math.Min(Math.Max(0, displayCount), Math.Max(0, maxVisible));
    }

    public static int HiddenByCapCountFor(int displayCount, int maxVisible)
    {
        return Math.Max(0, displayCount - Math.Max(0, maxVisible));
    }

    public static bool IsRunning(string status)
    {
        return string.Equals(status, "running", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsStopped(string status)
    {
        return string.Equals(status, "stopped", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "exited", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "created", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "dead", StringComparison.OrdinalIgnoreCase);
    }

    private static int CompareContainers(ContainerDataPayload left, ContainerDataPayload right)
    {
        int statusCompare = StatusPriority(left.status).CompareTo(StatusPriority(right.status));
        if (statusCompare != 0)
        {
            return statusCompare;
        }

        int cpuCompare = right.cpu.CompareTo(left.cpu);
        if (cpuCompare != 0)
        {
            return cpuCompare;
        }

        return string.Compare(DisplayName(left), DisplayName(right), StringComparison.OrdinalIgnoreCase);
    }

    private static int StatusPriority(string status)
    {
        if (IsRunning(status))
        {
            return 0;
        }

        if (IsStopped(status))
        {
            return 1;
        }

        return 2;
    }

    private static string DisplayName(ContainerDataPayload container)
    {
        return string.IsNullOrWhiteSpace(container.name) ? container.id : container.name;
    }
}
