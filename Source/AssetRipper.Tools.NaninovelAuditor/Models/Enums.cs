namespace AssetRipper.Tools.NaninovelAuditor.Models;

public enum NaninovelAssetType
{
	NaniScript,
	ManagedText,
	Configuration,
	Localization,
	Audio,
	Voice,
	Unlockable,
	OtherNaninovelResource,
}

public enum DiffType
{
	QuantityMissing,
	ContentDifference,
	FieldValueDifference,
	DuplicateExport,
	PathChange,
	BrokenReference,
	EmptyFile,
}

public enum Severity
{
	Fatal,
	Severe,
	Moderate,
	Info,
}

public enum ExportDiffStatus
{
	ByteIdentical,
	NormalizedIdentical,
	SubstantiveDifference,
	EmptyFilePlaceholder,
	NotExported,
}

public enum DiagnosticLevel
{
	Error,
	Warning,
}

public enum DiagnosticCategory
{
	DuplicateResource,
	MissingReference,
	ImporterConfiguration,
	ScriptCompilation,
	AddressablesResidue,
	Other,
}

public enum Repairability
{
	FixableByExportLogic,
	ManualEditorOperation,
	UnfixableRequestExemption,
}

public enum ZeroTargetPhase
{
	PhaseOne,
	PhaseTwo,
	PhaseThreeExemption,
}

public enum ContentMatchLevel
{
	ByteIdentical,
	NormalizedIdentical,
	SubstantiveDifference,
}

public enum DuplicateOriginCause
{
	MultipleDataSourceWithoutDedup,
	SameBundleParsedMultipleTimes,
	LocalizationVariant,
	Other,
}

public enum AuditModule
{
	A,
	B,
	C,
	D,
	E,
	F,
}