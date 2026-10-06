using AssetRipper.Import.Structure.Assembly.Serializable;

namespace AssetRipper.Tests;

public class SourceGeneratedLayoutTests
{
	[Test]
	public void IsEngineType_ForUnityEngineUIImage_ReturnsTrue()
	{
		Assert.That(SourceGeneratedLayoutResolver.IsEngineType("UnityEngine.UI", "UnityEngine.UI", "Image"), Is.True);
	}

	[Test]
	public void IsEngineType_ForUnityEngineUIText_ReturnsTrue()
	{
		Assert.That(SourceGeneratedLayoutResolver.IsEngineType("UnityEngine.UI", "UnityEngine.UI", "Text"), Is.True);
	}

	[Test]
	public void IsEngineType_ForUnityEngineUIButton_ReturnsTrue()
	{
		Assert.That(SourceGeneratedLayoutResolver.IsEngineType("UnityEngine.UI", "UnityEngine.UI", "Button"), Is.True);
	}

	[Test]
	public void IsEngineType_ForUnityEngineUIHorizontalLayoutGroup_ReturnsTrue()
	{
		Assert.That(SourceGeneratedLayoutResolver.IsEngineType("UnityEngine.UI", "UnityEngine.UI", "HorizontalLayoutGroup"), Is.True);
	}

	[Test]
	public void IsEngineType_ForTMProTextMeshPro_ReturnsTrue()
	{
		Assert.That(SourceGeneratedLayoutResolver.IsEngineType("Unity.TextMeshPro", "TMPro", "TextMeshPro"), Is.True);
	}

	[Test]
	public void IsEngineType_ForUnityPrefixAssembly_ReturnsTrue()
	{
		Assert.That(SourceGeneratedLayoutResolver.IsEngineType("Unity.Physics", "Unity.Physics", "PhysicsBody"), Is.True);
	}

	[Test]
	public void IsEngineType_ForUnityEnginePrefixAssembly_ReturnsTrue()
	{
		Assert.That(SourceGeneratedLayoutResolver.IsEngineType("UnityEngine.CoreModule", "UnityEngine", "Transform"), Is.True);
	}

	[Test]
	public void IsEngineType_ForGameScript_ReturnsFalse()
	{
		Assert.That(SourceGeneratedLayoutResolver.IsEngineType("Assembly-CSharp", "Tomie", "GameManager"), Is.False);
	}

	[Test]
	public void IsEngineType_ForNaninovel_ReturnsFalse()
	{
		Assert.That(SourceGeneratedLayoutResolver.IsEngineType("Naninovel", "Naninovel", "Command"), Is.False);
	}

	[Test]
	public void IsEngineType_ForNullAssemblyName_ReturnsFalse()
	{
		Assert.That(SourceGeneratedLayoutResolver.IsEngineType(null, "UnityEngine", "Transform"), Is.False);
	}

	[Test]
	public void IsEngineType_ForEmptyNamespace_ReturnsFalse()
	{
		Assert.That(SourceGeneratedLayoutResolver.IsEngineType("Assembly-CSharp", "", "SomeClass"), Is.False);
	}
}