using Godot;

namespace Gamejam.Gameplay;

/// <summary>
/// Text painted onto a plastered panel, lit by the scene (unlike Label3D, which glows).
/// Set Weathered for the ruined era: stained plaster, chipped edges and flaked, faded paint.
/// The panel's front face sits at local z = 0, so place the node on the wall surface, facing +Z out.
/// </summary>
[Tool]
public partial class Inscription : Node3D
{
	private string _title = "";
	private string _body = "";
	private bool _weathered;
	private Vector2 _panelSize = new Vector2(1.14f, 0.46f);

	[Export] public string Title { get => _title; set { _title = value; Rebuild(); } }
	[Export(PropertyHint.MultilineText)] public string Body { get => _body; set { _body = value; Rebuild(); } }
	[Export] public bool Weathered { get => _weathered; set { _weathered = value; Rebuild(); } }
	[Export] public Vector2 PanelSize { get => _panelSize; set { _panelSize = value; Rebuild(); } }
	[Export] public int TitleFontSize { get; set; } = 26;
	[Export] public int BodyFontSize { get; set; } = 27;

	private const float PixelSize = 0.0016f;
	private const float PanelDepth = 0.012f;
	private const float BorderWidth = 0.02f;
	private const float TextDepth = 0.002f;

	private static readonly Shader PaintShader = GD.Load<Shader>("res://gameplay/weathered_paint.gdshader");

	private Node3D _built;

	public override void _Ready() => Rebuild();

	private void Rebuild()
	{
		if (!IsInsideTree())
			return;
		_built?.QueueFree();
		_built = new Node3D { Name = "Built" };
		// No owner, so these generated nodes are never saved into the scene.
		AddChild(_built, false, InternalMode.Front);

		var noise = MakeNoise(false);
		var bump = MakeNoise(true);
		var font = new SystemFont { FontNames = new[] { "Noto Serif", "Georgia", "Times New Roman", "DejaVu Serif", "serif" }, FontWeight = 500 };

		Vector2 half = PanelSize * 0.5f;
		Vector2 borderHalf = half + Vector2.One * BorderWidth;

		// Painted border (a slightly bigger slab behind the panel), then the plaster panel.
		AddBox(new Vector3(borderHalf.X * 2f, borderHalf.Y * 2f, PanelDepth * 0.8f), -PanelDepth * 0.6f,
			Paint(_weathered ? new Color(0.34f, 0.2f, 0.14f) : new Color(0.5f, 0.15f, 0.08f), noise, bump, borderHalf,
				wear: _weathered ? 0.25f : 0f, grime: _weathered ? 0.5f : 0.1f));
		AddBox(new Vector3(PanelSize.X, PanelSize.Y, PanelDepth), -PanelDepth * 0.5f,
			Paint(_weathered ? new Color(0.56f, 0.49f, 0.38f) : new Color(0.8f, 0.7f, 0.52f), noise, bump, half,
				wear: _weathered ? 0.08f : 0f, grime: _weathered ? 0.55f : 0.1f, chip: _weathered ? 0.06f : 0f,
				bumpiness: _weathered ? 0.9f : 0.25f));

		// Egyptian scribes wrote headings in red ochre and the rest in black.
		float bodyLine = BodyFontSize * PixelSize * 1.42f;
		int bodyLines = _body == "" ? 0 : _body.Split('\n').Length;
		float titleHeight = _title == "" ? 0f : TitleFontSize * PixelSize * 2.0f;
		float top = (titleHeight + bodyLines * bodyLine) * 0.5f;

		if (_title != "")
		{
			AddText(_title, font, TitleFontSize, top - TitleFontSize * PixelSize * 0.6f,
				Paint(_weathered ? new Color(0.42f, 0.17f, 0.1f) : new Color(0.58f, 0.12f, 0.06f), noise, bump, half,
					wear: _weathered ? 0.22f : 0f, grime: _weathered ? 0.4f : 0f, roughness: 0.8f));
		}
		if (_body != "")
		{
			AddText(_body, font, BodyFontSize, top - titleHeight - bodyLines * bodyLine * 0.5f,
				Paint(_weathered ? new Color(0.2f, 0.15f, 0.11f) : new Color(0.09f, 0.06f, 0.04f), noise, bump, half,
					wear: _weathered ? 0.26f : 0f, grime: _weathered ? 0.4f : 0f, roughness: 0.8f));
		}
	}

	private void AddBox(Vector3 size, float z, Material material)
	{
		_built.AddChild(new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = size, Material = material },
			Position = new Vector3(0, 0, z),
		});
	}

	private void AddText(string text, Font font, int fontSize, float y, Material material)
	{
		_built.AddChild(new MeshInstance3D
		{
			Mesh = new TextMesh
			{
				Text = text,
				Font = font,
				FontSize = fontSize,
				PixelSize = PixelSize,
				Depth = TextDepth,
				CurveStep = 0.5f,
				LineSpacing = fontSize * 0.06f,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				Material = material,
			},
			Position = new Vector3(0, y, TextDepth * 0.5f),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});
	}

	private static ShaderMaterial Paint(Color color, Texture2D noise, Texture2D bump, Vector2 halfSize,
		float wear = 0f, float grime = 0f, float chip = 0f, float bumpiness = 0.2f, float roughness = 0.92f)
	{
		var material = new ShaderMaterial { Shader = PaintShader };
		material.SetShaderParameter("paint_color", color);
		material.SetShaderParameter("noise", noise);
		material.SetShaderParameter("bump", bump);
		material.SetShaderParameter("wear", wear);
		material.SetShaderParameter("grime", grime);
		material.SetShaderParameter("edge_chip", chip);
		material.SetShaderParameter("half_size", halfSize);
		material.SetShaderParameter("bumpiness", bumpiness);
		material.SetShaderParameter("roughness_value", roughness);
		return material;
	}

	private static NoiseTexture2D MakeNoise(bool normalMap)
	{
		return new NoiseTexture2D
		{
			Width = 256,
			Height = 256,
			Seamless = true,
			AsNormalMap = normalMap,
			BumpStrength = 4f,
			GenerateMipmaps = true,
			Noise = new FastNoiseLite
			{
				NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
				Seed = 7,
				Frequency = 0.012f,
				FractalOctaves = 5,
			},
		};
	}
}
