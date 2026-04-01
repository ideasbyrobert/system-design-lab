namespace Lab.Shared.Configuration;

public sealed class DatabasePathOptions
{
    public string Primary { get; set; } = "data/primary.db";

    public string ReplicaEast { get; set; } = "data/replica-east.db";

    public string ReplicaWest { get; set; } = "data/replica-west.db";

    public string ReadModels { get; set; } = "data/readmodels.db";
}
