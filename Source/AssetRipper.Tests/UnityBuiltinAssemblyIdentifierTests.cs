using AssetRipper.Export.UnityProjects.Scripts;

namespace AssetRipper.Tests;

internal class UnityBuiltinAssemblyIdentifierTests
{
	[TestCase("Unity.Timeline")]
	[TestCase("Unity.Mathematics")]
	[TestCase("Unity.TextMeshPro")]
	[TestCase("Unity.Addressables")]
	[TestCase("Unity.Burst")]
	[TestCase("unity.timeline")]
	[TestCase("UNITY.TIMELINE")]
	[TestCase("UnItY.TiMeLiNe")]
	[TestCase("UnityEngine.UI")]
	[TestCase("UnityEngine.UIElements")]
	[TestCase("UnityEngine.Rendering")]
	[TestCase("UnityEngine.U2D")]
	[TestCase("UnityEngine.Tilemap")]
	[TestCase("UnityEngine.CoreModule")]
	[TestCase("unityengine.ui")]
	[TestCase("UNITYENGINE.RENDERING")]
	public static void IdentifyUnityBuiltinAssemblies_ReturnsTrue(string assemblyName)
	{
		Assert.That(UnityBuiltinAssemblyIdentifier.IsUnityBuiltinAssembly(assemblyName), Is.True);
	}

	[TestCase("Assembly-CSharp")]
	[TestCase("Newtonsoft.Json")]
	[TestCase("Unity")]
	[TestCase("UnityEngine")]
	[TestCase("")]
	public static void IdentifyNonUnityBuiltinAssemblies_ReturnsFalse(string assemblyName)
	{
		Assert.That(UnityBuiltinAssemblyIdentifier.IsUnityBuiltinAssembly(assemblyName), Is.False);
	}

	[Test]
	public static void IdentifyNullAssembly_ReturnsFalse()
	{
		Assert.That(UnityBuiltinAssemblyIdentifier.IsUnityBuiltinAssembly(null), Is.False);
	}
}
