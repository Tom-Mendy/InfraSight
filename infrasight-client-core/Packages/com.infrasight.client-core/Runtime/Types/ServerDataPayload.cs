using System;

[System.Serializable]
public class ServerDataPayload
{
  public MachineDataPayload machine;
  public ContainerDataPayload[] containers;
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
}
