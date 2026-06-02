using System.Collections.Generic;
using NUnit.Framework;

public sealed class DockerContainerVisualizationLayoutTests
{
    [Test]
    public void SortForDisplay_DropsInvalidAndDuplicateContainers()
    {
        ContainerDataPayload[] containers =
        {
            new() { id = "web", name = "web", status = "running", cpu = 8f },
            new() { id = "web", name = "duplicate", status = "running", cpu = 99f },
            new() { id = "", name = "invalid", status = "running", cpu = 1f },
            null
        };

        List<ContainerDataPayload> sorted = DockerContainerVisualizationLayout.SortForDisplay(containers);

        Assert.That(sorted, Has.Count.EqualTo(1));
        Assert.That(sorted[0].name, Is.EqualTo("web"));
    }

    [Test]
    public void SortForDisplay_OrdersByStatusCpuThenName()
    {
        ContainerDataPayload[] containers =
        {
            new() { id = "db", name = "db", status = "running", cpu = 20f },
            new() { id = "api", name = "api", status = "running", cpu = 80f },
            new() { id = "cache", name = "cache", status = "stopped", cpu = 95f },
            new() { id = "unknown", name = "unknown", status = "paused", cpu = 100f }
        };

        List<ContainerDataPayload> sorted = DockerContainerVisualizationLayout.SortForDisplay(containers);

        Assert.That(sorted[0].id, Is.EqualTo("api"));
        Assert.That(sorted[1].id, Is.EqualTo("db"));
        Assert.That(sorted[2].id, Is.EqualTo("cache"));
        Assert.That(sorted[3].id, Is.EqualTo("unknown"));
    }

    [Test]
    public void FilterStoppedContainers_HidesNonRunningContainersWhenDisabled()
    {
        List<ContainerDataPayload> containers = DockerContainerVisualizationLayout.SortForDisplay(new[]
        {
            new ContainerDataPayload { id = "api", status = "running" },
            new ContainerDataPayload { id = "worker", status = "exited" },
            new ContainerDataPayload { id = "pending", status = "created" },
            new ContainerDataPayload { id = "mystery", status = "paused" }
        });

        List<ContainerDataPayload> filtered = DockerContainerVisualizationLayout.FilterStoppedContainers(
            containers,
            showStoppedContainers: false);

        Assert.That(filtered, Has.Count.EqualTo(1));
        Assert.That(filtered[0].id, Is.EqualTo("api"));
    }

    [Test]
    public void VisibleAndHiddenCounts_CapDenseContainerLists()
    {
        Assert.That(DockerContainerVisualizationLayout.VisibleCountFor(displayCount: 20, maxVisible: 12), Is.EqualTo(12));
        Assert.That(DockerContainerVisualizationLayout.HiddenByCapCountFor(displayCount: 20, maxVisible: 12), Is.EqualTo(8));
    }
}
