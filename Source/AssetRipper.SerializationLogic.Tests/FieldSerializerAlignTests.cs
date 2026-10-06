using AssetRipper.Primitives;

namespace AssetRipper.SerializationLogic.Tests;

public class FieldSerializerAlignTests
{
	private class ClassWithIntField : UnityEngine.MonoBehaviour
	{
		public int intField;
	}

	[Test]
	public void ShouldAlign_ForIntType_ReturnsTrue()
	{
		SerializableType serializableType = SerializableTypes.Create<ClassWithIntField>();
		Assert.That(serializableType.Fields, Has.Count.EqualTo(1));
		Assert.That(serializableType.Fields[0].Align, Is.True, "int fields should require alignment (no-op for 4-byte types).");
	}

	private class ClassWithFloatField : UnityEngine.MonoBehaviour
	{
		public float floatField;
	}

	[Test]
	public void ShouldAlign_ForFloatType_ReturnsTrue()
	{
		SerializableType serializableType = SerializableTypes.Create<ClassWithFloatField>();
		Assert.That(serializableType.Fields, Has.Count.EqualTo(1));
		Assert.That(serializableType.Fields[0].Align, Is.True, "float fields should require alignment (no-op for 4-byte types).");
	}

	private class ClassWithBoolField : UnityEngine.MonoBehaviour
	{
		public bool boolField;
	}

	[Test]
	public void ShouldAlign_ForBoolType_ReturnsTrue()
	{
		SerializableType serializableType = SerializableTypes.Create<ClassWithBoolField>();
		Assert.That(serializableType.Fields, Has.Count.EqualTo(1));
		Assert.That(serializableType.Fields[0].Align, Is.True, "bool fields should require alignment (1-byte types need padding to 4 bytes).");
	}

	private class ClassWithStringField : UnityEngine.MonoBehaviour
	{
		public string? stringField;
	}

	[Test]
	public void ShouldAlign_ForStringType_ReturnsTrue()
	{
		SerializableType serializableType = SerializableTypes.Create<ClassWithStringField>();
		Assert.That(serializableType.Fields, Has.Count.EqualTo(1));
		Assert.That(serializableType.Fields[0].Align, Is.True, "string fields should require alignment.");
	}

	private class ClassWithIntArrayField : UnityEngine.MonoBehaviour
	{
		public int[]? intArrayField;
	}

	[Test]
	public void ShouldAlign_ForArrayType_ReturnsTrue()
	{
		SerializableType serializableType = SerializableTypes.Create<ClassWithIntArrayField>();
		Assert.That(serializableType.Fields, Has.Count.EqualTo(1));
		Assert.That(serializableType.Fields[0].Align, Is.True, "array fields should require alignment.");
	}

	private class ClassWithVector2Field : UnityEngine.MonoBehaviour
	{
		public UnityEngine.Vector2 vectorField;
	}

	[Test]
	public void ShouldAlign_ForComplexType_ReturnsFalse()
	{
		SerializableType serializableType = SerializableTypes.Create<ClassWithVector2Field>();
		Assert.That(serializableType.Fields, Has.Count.EqualTo(1));
		Assert.That(serializableType.Fields[0].Align, Is.False, "complex/struct fields should not require alignment.");
	}
}