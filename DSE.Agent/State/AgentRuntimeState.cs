namespace DSE.Agent.State;

public class AgentRuntimeState
{
    public DateTime StartedAt { get; set; } = DateTime.Now;
    public DateTime LastHeartbeat { get; set; } = DateTime.Now;
}
