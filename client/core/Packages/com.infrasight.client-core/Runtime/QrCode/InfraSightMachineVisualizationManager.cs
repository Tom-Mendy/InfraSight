using System.Collections.Generic;
using UnityEngine;

public sealed class InfraSightMachineVisualizationManager
{
    private readonly Transform rootTransform;
    private readonly GameObject spawnSpherePrefab;
    private readonly GameObject spawnCubePrefab;
    private readonly GameObject machineVisualizationPrefab;
    private readonly GameObject feedbackPrefab;
    private readonly Dictionary<string, MachineVisualizationContext> machineVisualizations = new();

    public InfraSightMachineVisualizationManager(
        Transform rootTransform,
        GameObject spawnSpherePrefab,
        GameObject spawnCubePrefab,
        GameObject machineVisualizationPrefab,
        GameObject feedbackPrefab)
    {
        this.rootTransform = rootTransform;
        this.spawnSpherePrefab = spawnSpherePrefab;
        this.spawnCubePrefab = spawnCubePrefab;
        this.machineVisualizationPrefab = machineVisualizationPrefab;
        this.feedbackPrefab = feedbackPrefab;
    }

    public bool HasVisualization(string endpoint)
    {
        return machineVisualizations.ContainsKey(endpoint);
    }

    public GameObject CreateFeedback(Vector3 position, Quaternion rotation)
    {
        if (feedbackPrefab == null)
        {
            return null;
        }

        GameObject feedbackObject = Object.Instantiate(feedbackPrefab, rootTransform);
        feedbackObject.transform.SetPositionAndRotation(position, rotation);
        feedbackObject.SendMessage("SetQrId", "CONNECTING", SendMessageOptions.DontRequireReceiver);

        return feedbackObject;
    }

    public void DestroyFeedback(GameObject feedbackObject)
    {
        DestroyFeedback(feedbackObject, 0f);
    }

    public void DestroyFeedback(GameObject feedbackObject, float delaySeconds)
    {
        if (feedbackObject != null)
        {
            Object.Destroy(feedbackObject, Mathf.Max(0f, delaySeconds));
        }
    }

    public void SetFeedbackState(GameObject feedbackObject, string state)
    {
        if (feedbackObject != null)
        {
            feedbackObject.SendMessage("SetQrId", state, SendMessageOptions.DontRequireReceiver);
        }
    }

    public void CreateMachineVisualization(ServerConnection connection, Vector3 position, Quaternion rotation)
    {
        if (connection == null || machineVisualizations.ContainsKey(connection.Endpoint))
        {
            return;
        }

        GameObject root = CreateMachineRoot(rootTransform);
        root.transform.SetPositionAndRotation(position, rotation * Quaternion.Euler(0f, 180f, 0f));
        root.SendMessage("SetQrId", connection.MachineName, SendMessageOptions.DontRequireReceiver);

        machineVisualizations[connection.Endpoint] = new MachineVisualizationContext(root);
    }

    public void ApplyPayload(string endpoint, ServerDataPayload payload)
    {
        if (!machineVisualizations.TryGetValue(endpoint, out MachineVisualizationContext context)
            || context.Root == null
            || payload?.machine == null)
        {
            return;
        }

        float normalizedCpu = Mathf.Clamp01(payload.machine.cpu / 100f);
        float normalizedRam = Mathf.Clamp01(payload.machine.ram / 100f);

        context.Root.SendMessage("UpdateMachineChart", payload, SendMessageOptions.DontRequireReceiver);
        if (context.Root.GetComponentInChildren<Renderer>() != null)
        {
            float scale = Mathf.Lerp(0.4f, 1.4f, normalizedCpu);
            context.Root.transform.localScale = new Vector3(scale, scale, scale);

            Renderer renderer = context.Root.GetComponentInChildren<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = Color.Lerp(Color.green, Color.red, normalizedRam);
            }
        }

        ApplyContainers(context, payload.containers);
    }

    private void ApplyContainers(MachineVisualizationContext context, ContainerDataPayload[] containers)
    {
        HashSet<string> activeContainerIds = new();
        if (containers != null)
        {
            for (int i = 0; i < containers.Length; i++)
            {
                ContainerDataPayload containerData = containers[i];
                if (containerData == null || string.IsNullOrWhiteSpace(containerData.id))
                {
                    continue;
                }

                activeContainerIds.Add(containerData.id);
                if (!context.ContainerVisualizations.TryGetValue(containerData.id, out GameObject containerGo) || containerGo == null)
                {
                    containerGo = CreateContainerObject(context.Root.transform.parent);
                    context.ContainerVisualizations[containerData.id] = containerGo;
                }

                float angle = (360f / Mathf.Max(1, containers.Length)) * i;
                float radius = 0.8f;
                Vector3 offset = new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), 0.2f, Mathf.Sin(angle * Mathf.Deg2Rad)) * radius;
                containerGo.transform.SetLocalPositionAndRotation(context.Root.transform.localPosition + offset, Quaternion.identity);
                containerGo.transform.localScale = Vector3.one * 0.2f;

                Renderer containerRenderer = containerGo.GetComponentInChildren<Renderer>();
                if (containerRenderer != null)
                {
                    containerRenderer.material.color = containerData.status == "running" ? Color.green : Color.red;
                }
            }
        }

        List<string> staleIds = new();
        foreach (string containerId in context.ContainerVisualizations.Keys)
        {
            if (!activeContainerIds.Contains(containerId))
            {
                staleIds.Add(containerId);
            }
        }

        foreach (string staleId in staleIds)
        {
            GameObject staleObject = context.ContainerVisualizations[staleId];
            if (staleObject != null)
            {
                Object.Destroy(staleObject);
            }

            context.ContainerVisualizations.Remove(staleId);
        }
    }

    private GameObject CreateMachineRoot(Transform parent)
    {
        GameObject prefabToSpawn = machineVisualizationPrefab != null ? machineVisualizationPrefab : spawnSpherePrefab;
        if (prefabToSpawn != null)
        {
            return Object.Instantiate(prefabToSpawn, parent);
        }

        GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        primitive.name = "InfraSight Machine";
        primitive.transform.SetParent(parent, false);
        return primitive;
    }

    private GameObject CreateContainerObject(Transform parent)
    {
        if (spawnCubePrefab != null)
        {
            return Object.Instantiate(spawnCubePrefab, parent);
        }

        GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
        primitive.name = "InfraSight Container";
        primitive.transform.SetParent(parent, false);
        return primitive;
    }

    private sealed class MachineVisualizationContext
    {
        public GameObject Root { get; }
        public Dictionary<string, GameObject> ContainerVisualizations { get; } = new();

        public MachineVisualizationContext(GameObject root)
        {
            Root = root;
        }
    }
}
