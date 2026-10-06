using AssetRipper.SourceGenerated.Extensions.Enums.Shader.SerializedShader;

namespace AssetRipper.Export.UnityProjects.Shaders;

public enum PropertyType
{
	Color = 0,
	Vector = 1,
	Single = 2,
	Range = 3,
	Texture = 4,
}

public static class PropertyTypeExtensions
{
	public static bool IsMatch(this PropertyType _this, SerializedPropertyType type)
	{
		int a = (int)_this;
		int b = (int)type;
		if (a == b)
		{
			return true;
		}

		// Color and Vector are both 4-component float types and are exported identically.
		if ((a == 0 || a == 1) && (b == 0 || b == 1))
		{
			return true;
		}

		return false;
	}
	public static bool IsMatch(this PropertyType _this, int type) => (int)_this == type;
}
