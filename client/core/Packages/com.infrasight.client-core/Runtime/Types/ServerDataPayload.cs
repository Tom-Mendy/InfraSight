using System;
using Newtonsoft.Json;

[System.Serializable]
public class ServerDataPayload
{
  public MachineDataPayload machine;
  public ContainerDataPayload[] containers;
  [JsonProperty("network_edges")]
  public DockerNetworkEdgePayload[] networkEdges;
  public DateTime timestamp;
}

[System.Serializable]
public class MachineDataPayload
{
  public string name;
  public float cpu;
  public float ram;
}

[System.Serializable]
public class ContainerDataPayload
{
  public string id;
  public string name;
  public string status;
  public float cpu;
  [JsonProperty("rx_bytes")]
  public long rxBytes;
  [JsonProperty("tx_bytes")]
  public long txBytes;
  [JsonProperty("rx_bps")]
  public float rxBytesPerSecond;
  [JsonProperty("tx_bps")]
  public float txBytesPerSecond;
  [JsonProperty("network_names")]
  public string[] networkNames;
}

[System.Serializable]
public class DockerNetworkEdgePayload
{
  [JsonProperty("source_id")]
  public string sourceId;
  [JsonProperty("target_id")]
  public string targetId;
  public string protocol;
  public string state;
  [JsonProperty("network_name")]
  public string networkName;
  [JsonProperty("rx_bps")]
  public float rxBytesPerSecond;
  [JsonProperty("tx_bps")]
  public float txBytesPerSecond;
}
