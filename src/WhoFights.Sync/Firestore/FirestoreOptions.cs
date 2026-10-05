namespace WhoFights.Sync.Firestore;

public class FirestoreOptions
{
    public const string SectionName = "Firestore";

    public required string ProjectId { get; init; }
    // The events collection. Named before rankings existed; kept as-is so
    // existing configuration keeps working.
    public required string CollectionName { get; init; }

    public string RankingsCollectionName { get; init; } = "rankings";
}
