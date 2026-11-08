using Godot;

namespace Gamejam.Gameplay;

// A spot a Pickup can be placed into. Holds at most one Pickup and shows an outline while empty.
public partial class PickupSlot : Area3D
{
	[Export] public string Id { get; set; } = "";
	// Size of the object this slot fits. Used for the outline, and for the hit box when there's no CollisionShape3D child.
	[Export] public Vector3 Size { get; set; } = new Vector3(0.03f, 0.35f, 0.35f);
	// Extra hit box margin so an empty slot is easy to aim at.
	[Export] public float AimPadding { get; set; } = 0.03f;
	[Export] public Color OutlineColor { get; set; } = new Color(1f, 0.85f, 0.45f, 0.9f);
	// Only Pickups with this ItemName fit (e.g. "Key" for a keyhole). Empty = anything.
	[Export] public string Accepts { get; set; } = "";
	// Centre of the outline and hit box relative to the slot, for when the placed item's origin isn't where
	// the slot should show (a key's origin is mid-shaft, but its keyhole is on the door).
	[Export] public Vector3 ShapeCenter { get; set; } = Vector3.Zero;
	[Export] public float OutlineWidth { get; set; } = 0.012f;

	// itemName is "" when the slot is emptied. Lets puzzles check the arrangement later.
	[Signal] public delegate void OccupantChangedEventHandler(string slotId, string itemName);

	public Pickup Occupant { get; private set; }

	private Node3D _outline;

	public override void _Ready()
	{
		AddToGroup("pickup_slots");
		CollisionLayer = Pickup.InteractablesLayerBit;
		CollisionMask = 0;
		Monitoring = false;

		if (!HasCollisionShapeChild())
		{
			var box = new BoxShape3D { Size = Size + Vector3.One * (AimPadding * 2f) };
			AddChild(new CollisionShape3D { Shape = box, Position = ShapeCenter });
		}

		BuildOutline();
		_outline.Visible = Occupant == null;
	}

	public void ConfigureShape(Vector3 center, Vector3 size)
	{
		ShapeCenter = center;
		Size = size;
	}

	public bool CanHold(Pickup pickup) => pickup != null && (Accepts == "" || pickup.ItemName == Accepts);

	public void SetOccupant(Pickup pickup)
	{
		Occupant = pickup;
		if (_outline != null)
			_outline.Visible = pickup == null;
		EmitSignal(SignalName.OccupantChanged, Id, pickup == null ? "" : pickup.ItemName);
	}

	private bool HasCollisionShapeChild()
	{
		foreach (Node child in GetChildren())
		{
			if (child is CollisionShape3D)
				return true;
		}
		return false;
	}

	// A rectangle frame around the face of the object, drawn across the two largest axes of Size.
	private void BuildOutline()
	{
		_outline = new Node3D { Name = "Outline", Position = ShapeCenter };
		AddChild(_outline);

		int thin = 0;
		if (Size.Y < Size[thin]) thin = 1;
		if (Size.Z < Size[thin]) thin = 2;
		int a = (thin + 1) % 3;
		int b = (thin + 2) % 3;

		var material = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = OutlineColor,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		};

		for (int side = 0; side < 2; side++)
		{
			for (int sign = -1; sign <= 1; sign += 2)
			{
				int along = side == 0 ? a : b;
				int across = side == 0 ? b : a;

				Vector3 barSize = Vector3.Zero;
				barSize[thin] = OutlineWidth * 0.5f;
				barSize[along] = Size[along] + OutlineWidth;
				barSize[across] = OutlineWidth;

				Vector3 position = Vector3.Zero;
				position[across] = sign * Size[across] * 0.5f;

				_outline.AddChild(new MeshInstance3D
				{
					Mesh = new BoxMesh { Size = barSize, Material = material },
					Position = position,
					CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				});
			}
		}
	}
}
