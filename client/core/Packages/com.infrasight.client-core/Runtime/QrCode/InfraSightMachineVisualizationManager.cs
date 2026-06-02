using System.Collections.Generic;
using UnityEngine;

public sealed class InfraSightMachineVisualizationManager
{
    private const int MaxVisibleContainers = 12;
    private const float ContainerRingHeight = 0.25f;
    private const float InnerRingRadius = 0.7f;
    private const float OuterRingRadius = 1.05f;

    private readonly Transform rootTransform;
    private readonly GameObject spawnCubePrefab;
    private readonly GameObject machineVisualizationPrefab;
    private readonly GameObject feedbackPrefab;
    private readonly Dictionary<string, MachineVisualizationContext> machineVisualizations = new();

    public InfraSightMachineVisualizationManager(
        Transform rootTransform,
        GameObject spawnCubePrefab,
        GameObject machineVisualizationPrefab,
        GameObject feedbackPrefab)
    {
        this.rootTransform = rootTransform;
        this.spawnCubePrefab = spawnCubePrefab;
        this.machineVisualizationPrefab = machineVisualizationPrefab;
        this.feedbackPrefab = feedbackPrefab;
    }

    public bool HasVisualization(string endpoint)
    {
        return machineVisualizations.ContainsKey(endpoint);
    }

    public GameObject CreateFeedback(Vector3 position, Quaternion rotation, string machineName)
    {
        if (feedbackPrefab == null)
        {
            return null;
        }

        GameObject feedbackObject = UnityEngine.Object.Instantiate(feedbackPrefab, rootTransform);
        feedbackObject.transform.SetPositionAndRotation(position, GetReadableRotation(rotation));
        feedbackObject.SendMessage("SetConnectingMachine", machineName, SendMessageOptions.DontRequireReceiver);

        return feedbackObject;
    }

    public void UpdateFeedbackPose(GameObject feedbackObject, Vector3 position, Quaternion rotation)
    {
        if (feedbackObject != null)
        {
            feedbackObject.transform.SetPositionAndRotation(position, GetReadableRotation(rotation));
        }
    }

    public void DestroyFeedback(GameObject feedbackObject)
    {
        DestroyFeedback(feedbackObject, 0f);
    }

    public void DestroyFeedback(GameObject feedbackObject, float delaySeconds)
    {
        if (feedbackObject != null)
        {
            UnityEngine.Object.Destroy(feedbackObject, Mathf.Max(0f, delaySeconds));
        }
    }

    public void SetFeedbackFailed(GameObject feedbackObject, string machineName)
    {
        if (feedbackObject != null)
        {
            feedbackObject.SendMessage("SetConnectionFailedMachine", machineName, SendMessageOptions.DontRequireReceiver);
        }
    }

    public void CreateMachineVisualization(ServerConnection connection, Vector3 position, Quaternion rotation)
    {
        if (connection == null || machineVisualizations.ContainsKey(connection.Endpoint))
        {
            return;
        }

        GameObject root = CreateMachineRoot(rootTransform);
        if (root == null)
        {
            Debug.LogWarning("Cannot create machine visualization: MachineInfo prefab is missing.");
            return;
        }

        Quaternion readableRotation = GetReadableRotation(rotation);
        root.transform.SetPositionAndRotation(position, readableRotation);
        root.SendMessage("SetQrId", connection.MachineName, SendMessageOptions.DontRequireReceiver);

        GameObject containerGroup = new($"Docker Containers - {connection.MachineName}");
        containerGroup.transform.SetParent(rootTransform, false);
        containerGroup.transform.SetPositionAndRotation(position, readableRotation);

        var context = new MachineVisualizationContext(root, containerGroup);
        machineVisualizations[connection.Endpoint] = context;

        root.SendMessage(
            "ConfigureDockerActions",
            new DockerContainerVisualizationActions(
                () => ToggleStoppedContainers(connection.Endpoint),
                () => RecenterContainerRing(connection.Endpoint),
                () => RefreshContainers(connection.Endpoint)),
            SendMessageOptions.DontRequireReceiver);
    }

    public void UpdateMachineVisualizationPose(string endpoint, Vector3 position, Quaternion rotation)
    {
        if (!machineVisualizations.TryGetValue(endpoint, out MachineVisualizationContext context))
        {
            return;
        }

        Quaternion readableRotation = GetReadableRotation(rotation);
        context.AnchorPosition = position;
        context.AnchorRotation = readableRotation;

        if (context.Root != null)
        {
            context.Root.transform.SetPositionAndRotation(position, readableRotation);
        }

        RecenterContainerRing(context);
    }

    public void ApplyPayload(string endpoint, ServerDataPayload payload)
    {
        if (!machineVisualizations.TryGetValue(endpoint, out MachineVisualizationContext context)
            || context.Root == null
            || payload?.machine == null)
        {
            return;
        }

        context.LastPayload = payload;

        float normalizedCpu = Mathf.Clamp01(payload.machine.cpu / 100f);
        float normalizedRam = Mathf.Clamp01(payload.machine.ram / 100f);

        context.Root.SendMessage("UpdateMachineChart", payload, SendMessageOptions.DontRequireReceiver);
        Renderer renderer = context.Root.GetComponentInChildren<Renderer>();
        if (renderer != null)
        {
            float scale = Mathf.Lerp(0.4f, 1.4f, normalizedCpu);
            context.Root.transform.localScale = new Vector3(scale, scale, scale);
            renderer.material.color = Color.Lerp(Color.green, Color.red, normalizedRam);
        }

        ApplyContainers(context, payload.containers);
    }

    private void ToggleStoppedContainers(string endpoint)
    {
        if (!machineVisualizations.TryGetValue(endpoint, out MachineVisualizationContext context))
        {
            return;
        }

        context.ShowStoppedContainers = !context.ShowStoppedContainers;
        RefreshContainers(context);
    }

    private void RecenterContainerRing(string endpoint)
    {
        if (machineVisualizations.TryGetValue(endpoint, out MachineVisualizationContext context))
        {
            RecenterContainerRing(context);
        }
    }

    private void RefreshContainers(string endpoint)
    {
        if (machineVisualizations.TryGetValue(endpoint, out MachineVisualizationContext context))
        {
            RefreshContainers(context);
        }
    }

    private void RefreshContainers(MachineVisualizationContext context)
    {
        if (context.LastPayload != null)
        {
            ApplyContainers(context, context.LastPayload.containers);
        }
    }

    private void RecenterContainerRing(MachineVisualizationContext context)
    {
        if (context.ContainerGroup != null)
        {
            context.ContainerGroup.transform.SetPositionAndRotation(context.AnchorPosition, context.AnchorRotation);
        }
    }

    private void ApplyContainers(MachineVisualizationContext context, ContainerDataPayload[] containers)
    {
        List<ContainerDataPayload> sortedContainers = DockerContainerVisualizationLayout.SortForDisplay(containers);
        List<ContainerDataPayload> displayContainers = DockerContainerVisualizationLayout.FilterStoppedContainers(
            sortedContainers,
            context.ShowStoppedContainers);
        int hiddenByFilterCount = sortedContainers.Count - displayContainers.Count;
        int hiddenByCapCount = DockerContainerVisualizationLayout.HiddenByCapCountFor(
            displayContainers.Count,
            MaxVisibleContainers);
        int visibleContainerCount = DockerContainerVisualizationLayout.VisibleCountFor(
            displayContainers.Count,
            MaxVisibleContainers);

        HashSet<string> activeNodeIds = new();
        for (int i = 0; i < visibleContainerCount; i++)
        {
            ContainerDataPayload containerData = displayContainers[i];
            string nodeId = ContainerNodeId(containerData.id);
            activeNodeIds.Add(nodeId);

            GameObject containerObject = GetOrCreateContainerObject(context, nodeId);
            PoseContainerNode(containerObject, i, visibleContainerCount + (hiddenByCapCount > 0 ? 1 : 0), containerData.status);
            containerObject.SendMessage("SetContainerData", containerData, SendMessageOptions.DontRequireReceiver);
        }

        if (hiddenByCapCount > 0)
        {
            const string aggregateNodeId = "aggregate";
            activeNodeIds.Add(aggregateNodeId);

            GameObject aggregateObject = GetOrCreateContainerObject(context, aggregateNodeId);
            PoseContainerNode(aggregateObject, visibleContainerCount, visibleContainerCount + 1, "aggregate");
            aggregateObject.SendMessage("SetAggregateData", hiddenByCapCount, SendMessageOptions.DontRequireReceiver);
        }

        RemoveStaleContainerNodes(context, activeNodeIds);

        int runningCount = DockerContainerVisualizationLayout.CountRunning(sortedContainers);

        context.Root.SendMessage(
            "UpdateDockerSummary",
            new DockerContainerVisualizationSummary(
                sortedContainers.Count,
                runningCount,
                visibleContainerCount,
                hiddenByFilterCount + hiddenByCapCount,
                context.ShowStoppedContainers),
            SendMessageOptions.DontRequireReceiver);
    }

    private GameObject GetOrCreateContainerObject(MachineVisualizationContext context, string nodeId)
    {
        if (context.ContainerVisualizations.TryGetValue(nodeId, out GameObject existingObject)
            && existingObject != null)
        {
            return existingObject;
        }

        GameObject containerObject = CreateContainerObject(context.ContainerGroup.transform);
        containerObject.name = $"Docker Container - {nodeId}";
        context.ContainerVisualizations[nodeId] = containerObject;
        return containerObject;
    }

    private void PoseContainerNode(GameObject containerObject, int index, int totalNodes, string status)
    {
        float angle = (360f / Mathf.Max(1, totalNodes)) * index;
        bool running = DockerContainerVisualizationLayout.IsRunning(status);
        float radius = running ? InnerRingRadius : OuterRingRadius;
        Vector3 offset = new(
            Mathf.Cos(angle * Mathf.Deg2Rad) * radius,
            ContainerRingHeight,
            Mathf.Sin(angle * Mathf.Deg2Rad) * radius);

        containerObject.transform.SetLocalPositionAndRotation(offset, Quaternion.identity);
    }

    private void RemoveStaleContainerNodes(MachineVisualizationContext context, HashSet<string> activeNodeIds)
    {
        List<string> staleIds = null;
        foreach (string containerId in context.ContainerVisualizations.Keys)
        {
            if (!activeNodeIds.Contains(containerId))
            {
                staleIds ??= new List<string>();
                staleIds.Add(containerId);
            }
        }

        if (staleIds == null)
        {
            return;
        }

        foreach (string staleId in staleIds)
        {
            GameObject staleObject = context.ContainerVisualizations[staleId];
            if (staleObject != null)
            {
                UnityEngine.Object.Destroy(staleObject);
            }

            context.ContainerVisualizations.Remove(staleId);
        }
    }

    private static string ContainerNodeId(string containerId)
    {
        return $"container:{containerId}";
    }

    private GameObject CreateMachineRoot(Transform parent)
    {
        if (machineVisualizationPrefab != null)
        {
            return UnityEngine.Object.Instantiate(machineVisualizationPrefab, parent);
        }

        return null;
    }

    private GameObject CreateContainerObject(Transform parent)
    {
        GameObject containerObject;
        if (spawnCubePrefab != null)
        {
            containerObject = UnityEngine.Object.Instantiate(spawnCubePrefab, parent);
        }
        else
        {
            containerObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            containerObject.transform.SetParent(parent, false);
        }

        containerObject.transform.localScale = Vector3.one * 0.18f;
        return containerObject;
    }

    private static Quaternion GetReadableRotation(Quaternion rotation)
    {
        return rotation * Quaternion.Euler(0f, 180f, 0f);
    }

    private sealed class MachineVisualizationContext
    {
        public GameObject Root { get; }
        public GameObject ContainerGroup { get; }
        public Dictionary<string, GameObject> ContainerVisualizations { get; } = new();
        public ServerDataPayload LastPayload { get; set; }
        public Vector3 AnchorPosition { get; set; }
        public Quaternion AnchorRotation { get; set; }
        public bool ShowStoppedContainers { get; set; } = true;

        public MachineVisualizationContext(GameObject root, GameObject containerGroup)
        {
            Root = root;
            ContainerGroup = containerGroup;
            AnchorPosition = root.transform.position;
            AnchorRotation = root.transform.rotation;
        }
    }
}
