namespace AssetRipper.Export.UnityProjects.Naninovel.Package;

/// <summary>
/// Represents a single entry extracted from a Unity .unitypackage (tar.gz) file.
/// </summary>
/// <param name="Guid">The 32-character hexadecimal GUID (directory name in the unitypackage).</param>
/// <param name="PathName">The target asset path (contents of the pathname file, starting with "Assets/").</param>
/// <param name="AssetData">The binary content of the asset file.</param>
/// <param name="AssetMeta">The text content of the asset.meta file (Unity .meta file).</param>
public sealed record UnityPackageEntry(string Guid, string PathName, byte[] AssetData, string AssetMeta);