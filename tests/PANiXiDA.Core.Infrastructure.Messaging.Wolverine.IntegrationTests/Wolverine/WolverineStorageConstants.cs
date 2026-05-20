namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Wolverine;

public static class WolverineStorageConstants
{
    public const string Schema = "wolverine";
    public const string IncomingEnvelopesTable = $"{Schema}.wolverine_incoming_envelopes";
    public const string OutgoingEnvelopesTable = $"{Schema}.wolverine_outgoing_envelopes";
}
