using System;

public sealed class DockerContainerVisualizationActions
{
    public Action ToggleStoppedContainers { get; }
    public Action RecenterContainerRing { get; }
    public Action RefreshContainers { get; }

    public DockerContainerVisualizationActions(
        Action toggleStoppedContainers,
        Action recenterContainerRing,
        Action refreshContainers)
    {
        ToggleStoppedContainers = toggleStoppedContainers;
        RecenterContainerRing = recenterContainerRing;
        RefreshContainers = refreshContainers;
    }
}

public sealed class DockerContainerVisualizationSummary
{
    public int TotalCount { get; }
    public int RunningCount { get; }
    public int VisibleCount { get; }
    public int HiddenCount { get; }
    public bool ShowStoppedContainers { get; }

    public DockerContainerVisualizationSummary(
        int totalCount,
        int runningCount,
        int visibleCount,
        int hiddenCount,
        bool showStoppedContainers)
    {
        TotalCount = totalCount;
        RunningCount = runningCount;
        VisibleCount = visibleCount;
        HiddenCount = hiddenCount;
        ShowStoppedContainers = showStoppedContainers;
    }
}
