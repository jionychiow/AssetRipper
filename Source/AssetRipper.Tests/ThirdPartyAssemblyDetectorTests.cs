using AsmResolver.DotNet;
using AssetRipper.Export.UnityProjects.Scripts;

namespace AssetRipper.Tests;

public class ThirdPartyAssemblyDetectorTests
{
	[Test]
	public void IsThirdPartyByName_KnownPrefix_DOTween_ReturnsTrue()
	{
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("DOTween"), Is.True);
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("DOTweenPro"), Is.True);
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("DOTween.Modules"), Is.True);
	}

	[Test]
	public void IsThirdPartyByName_KnownPrefix_Naninovel_ReturnsTrue()
	{
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("Naninovel"), Is.True);
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("Naninovel.UI"), Is.True);
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("Naninovel.Commands"), Is.True);
	}

	[Test]
	public void IsThirdPartyByName_KnownPrefix_Newtonsoft_ReturnsTrue()
	{
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("Newtonsoft.Json"), Is.True);
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("Newtonsoft.Json.Bson"), Is.True);
	}

	[Test]
	public void IsThirdPartyByName_KnownPrefix_Others_ReturnsTrue()
	{
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("Zenject"), Is.True);
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("Zenject-usage"), Is.True);
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("Extenject"), Is.True);
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("Klak.Math"), Is.True);
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("ICSharpCode.SharpZipLib"), Is.True);
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("Google.Protobuf"), Is.True);
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("protobuf-net"), Is.True);
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("Ionic.Zip"), Is.True);
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("MessagePack"), Is.True);
	}

	[Test]
	public void IsThirdPartyByName_KnownExact_ReturnsTrue()
	{
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("websocket-sharp"), Is.True);
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("CsvHelper"), Is.True);
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("System.Memory"), Is.True);
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("System.Buffers"), Is.True);
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("System.Numerics.Vectors"), Is.True);
	}

	[Test]
	public void ShouldSkipAssembly_SystemUnsafe_ReturnsTrue()
	{
		Assert.That(ThirdPartyAssemblyDetector.ShouldSkipAssembly("System.Runtime.CompilerServices.Unsafe"), Is.True);
		Assert.That(ThirdPartyAssemblyDetector.ShouldSkipAssembly("System.Runtime.CompilerServices.Unsafe.dll"), Is.True);
		Assert.That(ThirdPartyAssemblyDetector.ShouldSkipAssembly("System.Memory"), Is.False);
	}

	[Test]
	public void IsThirdPartyByName_UniRxPrefix_ReturnsTrue()
	{
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("UniRx"), Is.True);
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("UniRx.Async"), Is.True);
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("UniRx.ObservableExtensions"), Is.True);
	}

	[Test]
	public void IsThirdPartyByName_ElringusPrefix_ReturnsTrue()
	{
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("Elringus.Naninovel.Runtime"), Is.True);
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("Elringus.NaninovelInventory.Runtime"), Is.True);
	}

	[Test]
	public void IsThirdPartyByName_NLayerPrefix_ReturnsTrue()
	{
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("NLayer"), Is.True);
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("NLayer.Audio"), Is.True);
	}

	[Test]
	public void IsThirdPartyByName_CaseInsensitive_ReturnsTrue()
	{
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("dotween"), Is.True);
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("NANINOVEL"), Is.True);
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("newtonsoft.json"), Is.True);
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("unirx.async"), Is.True);
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("elringus.naninovel.runtime"), Is.True);
	}

	[Test]
	public void IsThirdPartyByName_PredefinedGameScript_ReturnsFalse()
	{
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("Assembly-CSharp"), Is.False);
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("Assembly-CSharp-firstpass"), Is.False);
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("Assembly-CSharp-Editor"), Is.False);
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("Assembly-CSharp-Editor-firstpass"), Is.False);
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("Assembly-UnityScript"), Is.False);
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("Assembly-UnityScript-firstpass"), Is.False);
	}

	[Test]
	public void IsThirdPartyByName_UnityBuiltin_ReturnsFalse()
	{
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("Unity.Burst"), Is.False);
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("Unity.Timeline"), Is.False);
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("UnityEngine.UI"), Is.False);
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("UnityEngine.CoreModule"), Is.False);
	}

	[Test]
	public void IsThirdPartyByName_UnknownAssembly_ReturnsFalse()
	{
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("MyGame.Core"), Is.False);
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("GameScripts"), Is.False);
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("MyCompany.MyProduct"), Is.False);
	}

	[Test]
	public void IsThirdPartyByName_NullOrEmpty_ReturnsFalse()
	{
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName(null), Is.False);
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName(""), Is.False);
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyByName("   "), Is.False);
	}

	[Test]
	public void IsThirdPartyAssembly_Null_ReturnsFalse()
	{
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyAssembly(null), Is.False);
	}

	[Test]
	public void IsThirdPartyAssembly_KnownName_ReturnsTrue()
	{
		AssemblyDefinition assembly = new("DOTween", new Version(1, 0, 0, 0));
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyAssembly(assembly), Is.True);
	}

	[Test]
	public void IsThirdPartyAssembly_PredefinedName_ReturnsFalse()
	{
		AssemblyDefinition assembly = new("Assembly-CSharp", new Version(0, 0, 0, 0));
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyAssembly(assembly), Is.False);
	}

	[Test]
	public void IsThirdPartyAssembly_UnityBuiltinName_ReturnsFalse()
	{
		AssemblyDefinition assembly = new("Unity.Burst", new Version(0, 0, 0, 0));
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyAssembly(assembly), Is.False);
	}

	[Test]
	public void IsThirdPartyAssembly_WithStrongName_ReturnsTrue()
	{
		AssemblyDefinition assembly = new("SomeUnknownLib", new Version(1, 2, 0, 0));
		assembly.PublicKey = [0x80, 0x00, 0x01, 0x02, 0x03, 0x04];
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyAssembly(assembly), Is.True);
	}

	[Test]
	public void IsThirdPartyAssembly_WithAllZeroPublicKey_ReturnsFalse()
	{
		AssemblyDefinition assembly = new("SomeUnknownLib", new Version(0, 0, 0, 0));
		assembly.PublicKey = [0x00, 0x00, 0x00, 0x00];
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyAssembly(assembly), Is.False);
	}

	[Test]
	public void IsThirdPartyAssembly_WithEmptyPublicKey_ReturnsFalse()
	{
		AssemblyDefinition assembly = new("SomeUnknownLib", new Version(0, 0, 0, 0));
		assembly.PublicKey = [];
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyAssembly(assembly), Is.False);
	}

	[Test]
	public void IsThirdPartyAssembly_UnknownNoMetadata_ReturnsFalse()
	{
		AssemblyDefinition assembly = new("MyGame.Scripts", new Version(0, 0, 0, 0));
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyAssembly(assembly), Is.False);
	}

	[Test]
	public void IsThirdPartyAssembly_PredefinedWithStrongName_ReturnsFalse()
	{
		AssemblyDefinition assembly = new("Assembly-CSharp", new Version(1, 0, 0, 0));
		assembly.PublicKey = [0x80, 0x00, 0x01, 0x02];
		Assert.That(ThirdPartyAssemblyDetector.IsThirdPartyAssembly(assembly), Is.False);
	}
}