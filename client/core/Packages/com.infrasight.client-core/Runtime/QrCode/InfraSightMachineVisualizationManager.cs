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
        feedbackObject.transform.SetPositionAndRotation(position, rotation);
        feedbackObject.SendMessage("SetConnectingMachine", machineName, SendMessageOptions.DontRequireReceiver);

        return feedbackObject;
    }

    public void UpdateFeedbackPose(GameObject feedbackObject, Vector3 position, Quaternion rotation)
    {
        if (feedbackObject != null)
        {
            feedbackObject.transform.SetPositionAndRotation(position, rotation);
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

        root.transform.SetPositionAndRotation(position, rotation);
        root.SendMessage("SetQrId", connection.MachineName, SendMessageOptions.DontRequireReceiver);

        GameObject containerGroup = new($"Docker Containers - {connection.MachineName}");
        containerGroup.transform.SetParent(rootTransform, false);
        containerGroup.transform.SetPositionAndRotation(position, rotation);

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

        context.AnchorPosition = position;
        context.AnchorRotation = rotation;

        if (context.Root != null)
        {
            context.Root.transform.SetPositionAndRotation(position, rotation);
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

        ApplyContainers(context, payload);
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
            ApplyContainers(context, context.LastPayload);
        }
    }

    private void RecenterContainerRing(MachineVisualizationContext context)
    {
        if (context.ContainerGroup != null)
        {
            context.ContainerGroup.transform.SetPositionAndRotation(context.AnchorPosition, context.AnchorRotation);
        }
    }

    private void ApplyContainers(MachineVisualizationContext context, ServerDataPayload payload)
    {
        ContainerDataPayload[] containers = payload?.containers;
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
        HashSet<string> visibleContainerIds = new();
        Dictionary<string, Vector3> visibleNodeLocalPositions = new();
        for (int i = 0; i < visibleContainerCount; i++)
        {
            ContainerDataPayload containerData = displayContainers[i];
            string nodeId = ContainerNodeId(containerData.id);
            activeNodeIds.Add(nodeId);
            visibleContainerIds.Add(containerData.id);

            GameObject containerObject = GetOrCreateContainerObject(context, nodeId);
            PoseContainerNode(containerObject, i, visibleContainerCount + (hiddenByCapCount > 0 ? 1 : 0), containerData.status);
            containerObject.SendMessage("SetContainerData", containerData, SendMessageOptions.DontRequireReceiver);
            visibleNodeLocalPositions[nodeId] = containerObject.transform.localPosition;
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
        ApplyNetworkEdges(context, payload?.networkEdges, visibleContainerIds, visibleNodeLocalPositions);

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

    private void ApplyNetworkEdges(
        MachineVisualizationContext context,
        DockerNetworkEdgePayload[] networkEdges,
        HashSet<string> visibleContainerIds,
        Dictionary<string, Vector3> visibleNodeLocalPositions)
    {
        HashSet<string> activeEdgeIds = new();
        List<DockerNetworkEdgePayload> visibleEdges = DockerContainerVisualizationLayout.FilterVisibleNetworkEdges(
            networkEdges,
            visibleContainerIds);
        foreach (DockerNetworkEdgePayload edgeData in visibleEdges)
        {
            string sourceNodeId = ContainerNodeId(edgeData.sourceId);
            string targetNodeId = ContainerNodeId(edgeData.targetId);
            if (!visibleNodeLocalPositions.TryGetValue(sourceNodeId, out Vector3 sourcePosition)
                || !visibleNodeLocalPositions.TryGetValue(targetNodeId, out Vector3 targetPosition))
            {
                continue;
            }

            string edgeId = DockerNetworkEdgeId(edgeData);
            activeEdgeIds.Add(edgeId);
            UpdateTrafficLink(context, edgeId, sourcePosition, targetPosition, edgeData);
        }

        RemoveStaleTrafficLinks(context, activeEdgeIds);
    }

    private void UpdateTrafficLink(
        MachineVisualizationContext context,
        string edgeId,
        Vector3 sourceLocalPosition,
        Vector3 targetLocalPosition,
        DockerNetworkEdgePayload edgeData)
    {
        if (context.ContainerGroup == null || edgeData == null)
        {
            return;
        }

        DockerNetworkTrafficLink trafficLink = GetOrCreateTrafficLink(context, edgeId);
        trafficLink.SetTrafficData(
            sourceLocalPosition,
            targetLocalPosition,
            edgeData.rxBytesPerSecond,
            edgeData.txBytesPerSecond);
    }

    private DockerNetworkTrafficLink GetOrCreateTrafficLink(MachineVisualizationContext context, string nodeId)
    {
        if (context.TrafficLinks.TryGetValue(nodeId, out DockerNetworkTrafficLink existingLink)
            && existingLink != null)
        {
            return existingLink;
        }

        GameObject linkObject = new($"Docker Traffic - {nodeId}");
        linkObject.transform.SetParent(context.ContainerGroup.transform, false);
        DockerNetworkTrafficLink trafficLink = linkObject.AddComponent<DockerNetworkTrafficLink>();
        context.TrafficLinks[nodeId] = trafficLink;
        return trafficLink;
    }

    private void RemoveStaleTrafficLinks(MachineVisualizationContext context, HashSet<string> activeNodeIds)
    {
        List<string> staleIds = null;
        foreach (string edgeId in context.TrafficLinks.Keys)
        {
            if (!activeNodeIds.Contains(edgeId))
            {
                staleIds ??= new List<string>();
                staleIds.Add(edgeId);
            }
        }

        if (staleIds == null)
        {
            return;
        }

        foreach (string staleId in staleIds)
        {
            DockerNetworkTrafficLink staleLink = context.TrafficLinks[staleId];
            if (staleLink != null)
            {
                UnityEngine.Object.Destroy(staleLink.gameObject);
            }

            context.TrafficLinks.Remove(staleId);
        }
    }

    private static string ContainerNodeId(string containerId)
    {
        return $"container:{containerId}";
    }

    private static string DockerNetworkEdgeId(DockerNetworkEdgePayload edgeData)
    {
        string protocol = string.IsNullOrWhiteSpace(edgeData.protocol) ? "unknown" : edgeData.protocol;
        return $"edge:{edgeData.sourceId}>{edgeData.targetId}:{protocol}";
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

    private sealed class MachineVisualizationContext
    {
        public GameObject Root { get; }
        public GameObject ContainerGroup { get; }
        public Dictionary<string, GameObject> ContainerVisualizations { get; } = new();
        public Dictionary<string, DockerNetworkTrafficLink> TrafficLinks { get; } = new();
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

public sealed class DockerNetworkTrafficLink : MonoBehaviour
{
    private static readonly Color IdleColor = new(0.05f, 0.95f, 1f, 0.92f);
    private static readonly Color ReceiveColor = new(0.05f, 0.75f, 1f, 1f);
    private static readonly Color TransmitColor = new(1f, 0.5f, 0.05f, 1f);

    private LineRenderer edgeLine;
    private float rxBytesPerSecond;
    private float txBytesPerSecond;
    private Vector3 fromLocalPosition;
    private Vector3 toLocalPosition;

    private void Awake()
    {
        EnsureInitialized();
    }

    private void Update()
    {
        if (edgeLine == null)
        {
            return;
        }

        SetDirectionalLine(edgeLine, fromLocalPosition, toLocalPosition, rxBytesPerSecond, txBytesPerSecond);
    }

    public void SetTrafficData(Vector3 fromLocalPosition, Vector3 toLocalPosition, float rxBps, float txBps)
    {
        EnsureInitialized();

        this.fromLocalPosition = fromLocalPosition;
        this.toLocalPosition = toLocalPosition;
        rxBytesPerSecond = Mathf.Max(0f, rxBps);
        txBytesPerSecond = Mathf.Max(0f, txBps);
        Update();
    }

    private void EnsureInitialized()
    {
        RemoveParentLineRenderers();
        edgeLine ??= CreateLine("Peer Traffic");
    }

    private LineRenderer CreateLine(string lineName)
    {
        var lineObject = new GameObject(lineName, typeof(LineRenderer));
        lineObject.transform.SetParent(transform, false);
        LineRenderer targetLine = lineObject.GetComponent<LineRenderer>();
        ConfigureLine(targetLine);
        return targetLine;
    }

    private static void ConfigureLine(LineRenderer targetLine)
    {
        targetLine.enabled = false;
        targetLine.positionCount = 3;
        targetLine.useWorldSpace = false;
        targetLine.material = new Material(Shader.Find("Sprites/Default"));
        targetLine.startWidth = 0.01f;
        targetLine.endWidth = 0.006f;
        targetLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        targetLine.receiveShadows = false;
        targetLine.numCapVertices = 6;
        targetLine.numCornerVertices = 4;
    }

    private void RemoveParentLineRenderers()
    {
        LineRenderer[] parentLines = GetComponents<LineRenderer>();
        foreach (LineRenderer parentLine in parentLines)
        {
            parentLine.enabled = false;
            if (Application.isPlaying)
            {
                Destroy(parentLine);
            }
            else
            {
                DestroyImmediate(parentLine);
            }
        }
    }

    private static void SetDirectionalLine(LineRenderer targetLine, Vector3 from, Vector3 to, float rxBps, float txBps)
    {
        float rate = Mathf.Max(0f, rxBps + txBps);
        float pulse = (Mathf.Sin(Time.time * PulseSpeed(rate)) + 1f) * 0.5f;
        float baseWidth = rate > 0f ? Mathf.Lerp(0.014f, 0.04f, Mathf.Clamp01(rate / (1024f * 1024f))) : 0.012f;
        float width = baseWidth * (1f + pulse * 0.35f);
        Color color = TrafficColor(rxBps, txBps);
        color.a = rate > 0f ? Mathf.Lerp(color.a * 0.72f, color.a, pulse) : color.a;
        Vector3 midpoint = CurvedMidpoint(from, to);

        targetLine.startWidth = width;
        targetLine.endWidth = width * 0.7f;
        targetLine.startColor = new Color(color.r, color.g, color.b, color.a * 0.72f);
        targetLine.endColor = color;
        targetLine.SetPosition(0, from);
        targetLine.SetPosition(1, midpoint);
        targetLine.SetPosition(2, to);
        targetLine.enabled = true;
    }

    private static float PulseSpeed(float totalRate)
    {
        return Mathf.Lerp(2.8f, 7f, Mathf.Clamp01(totalRate / (512f * 1024f)));
    }

    private static Vector3 CurvedMidpoint(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        if (delta.sqrMagnitude < 0.0001f)
        {
            return (from + to) * 0.5f + Vector3.up * 0.035f;
        }

        Vector3 side = Vector3.Cross(delta.normalized, Vector3.up);
        if (side.sqrMagnitude < 0.0001f)
        {
            side = Vector3.right;
        }

        return (from + to) * 0.5f + side.normalized * 0.045f + Vector3.up * 0.035f;
    }

    private static Color TrafficColor(float rxBps, float txBps)
    {
        float total = Mathf.Max(0f, rxBps + txBps);
        if (total <= 0f)
        {
            return IdleColor;
        }

        return Color.Lerp(ReceiveColor, TransmitColor, txBps / total);
    }
}
