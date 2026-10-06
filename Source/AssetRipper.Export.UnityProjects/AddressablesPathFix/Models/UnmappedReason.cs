namespace AssetRipper.Export.UnityProjects.AddressablesPathFix.Models;

public enum UnmappedReason
{
	NoPathIdAddressMatch,
	NoGuidMatch,
	NoPathIdMatch,
	NoNameMatch,
	PathPrefixOutOfScope,
	TypeMismatch,
	NotInTypeDirectory,
}