namespace AssetRipper.Export.UnityProjects.AddressablesPathFix.Models;

public sealed record MoveSuccess(MoveItem Item, long DurationMs);
public sealed record MoveSkip(MoveItem Item, string Reason);
public sealed record MoveFailure(MoveItem Item, string Exception, MoveStage Stage);
public sealed record MoveConflict(MoveItem Item, ConflictStrategy Strategy, string Resolution);

public sealed class MoveResult
{
	public required IReadOnlyList<MoveSuccess> Successes { get; init; } = [];
	public required IReadOnlyList<MoveSkip> Skipped { get; init; } = [];
	public required IReadOnlyList<MoveFailure> Failures { get; init; } = [];
	public required IReadOnlyList<MoveConflict> Conflicts { get; init; } = [];
	public required IReadOnlyList<string> CreatedPlaceholders { get; init; } = [];
}