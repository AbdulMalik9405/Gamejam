using Godot;

namespace Gamejam.Gameplay;

// A physics object the player can carry in their inventory and put into a PickupSlot.
public partial class Pickup : RigidBody3D
{
	// Layer 3 "interactables". Swap for Layers.Interactables once core/Layers.cs exists.
	public const uint InteractablesLayerBit = 1u << 2;

	[Export] public string ItemName { get; set; } = "";
	// Inventory icon. Left empty, it's cut from the textured face of the mesh's last surface.
	[Export] public Texture2D Icon { get; set; }
	// Turns the spot the object starts in into a slot, so it can be put back or rearranged.
	[Export] public bool CreateSlotAtStart { get; set; } = true;

	public PickupSlot Slot { get; private set; }
	public bool IsHeld { get; private set; }

	private uint _layer;
	private uint _mask;

	public override void _Ready()
	{
		AddToGroup("pickups");
		if (ItemName == "")
			ItemName = Name;

		CollisionLayer |= InteractablesLayerBit;
		_layer = CollisionLayer;
		_mask = CollisionMask;
		// Static, not Kinematic: with Kinematic, Jolt treats each transform change as a move it finishes on the
		// next physics step, so a block placed right after being carried snapped back to its old in-hand position.
		FreezeMode = FreezeModeEnum.Static;

		MeshInstance3D mesh = FindMesh();
		if (Icon == null && mesh != null)
			Icon = BuildIconFromMesh(mesh);

		if (CreateSlotAtStart)
			CreateStartSlot(mesh);
	}

	public void Hold()
	{
		LeaveSlot();
		IsHeld = true;
		Freeze = true;
		// No collision while carried, so it can't shove the player or block the interact ray.
		CollisionLayer = 0;
		CollisionMask = 0;
	}

	public void PlaceIn(PickupSlot slot)
	{
		LeaveSlot();
		IsHeld = false;
		Visible = true;
		Slot = slot;
		slot.SetOccupant(this);
		GlobalTransform = slot.GlobalTransform;
		Freeze = true;
		RestoreCollision();
	}

	public void Drop(Vector3 velocity)
	{
		LeaveSlot();
		IsHeld = false;
		Visible = true;
		RestoreCollision();
		Freeze = false;
		LinearVelocity = velocity;
		AngularVelocity = Vector3.Zero;
	}

	private void LeaveSlot()
	{
		if (Slot == null)
			return;
		Slot.SetOccupant(null);
		Slot = null;
	}

	private void RestoreCollision()
	{
		CollisionLayer = _layer;
		CollisionMask = _mask;
	}

	private MeshInstance3D FindMesh()
	{
		foreach (Node child in GetChildren())
		{
			if (child is MeshInstance3D mesh && mesh.Mesh != null)
				return mesh;
		}
		return null;
	}

	// Picture surfaces (like the glyph face) usually come last and use a small region of an atlas texture.
	private static Texture2D BuildIconFromMesh(MeshInstance3D meshInstance)
	{
		Mesh mesh = meshInstance.Mesh;
		for (int surface = mesh.GetSurfaceCount() - 1; surface >= 0; surface--)
		{
			if (mesh.SurfaceGetMaterial(surface) is not BaseMaterial3D material || material.AlbedoTexture == null)
				continue;

			Vector2[] uvs = mesh.SurfaceGetArrays(surface)[(int)Mesh.ArrayType.TexUV].AsVector2Array();
			if (uvs.Length == 0)
				continue;

			Vector2 min = uvs[0];
			Vector2 max = uvs[0];
			foreach (Vector2 uv in uvs)
			{
				min = min.Min(uv);
				max = max.Max(uv);
			}

			Vector2 size = material.AlbedoTexture.GetSize();
			return new AtlasTexture
			{
				Atlas = material.AlbedoTexture,
				Region = new Rect2(min * size, (max - min) * size),
			};
		}
		return null;
	}

	private void CreateStartSlot(MeshInstance3D mesh)
	{
		Aabb bounds = mesh != null
			? mesh.Transform * mesh.GetAabb()
			: new Aabb(new Vector3(-0.15f, -0.15f, -0.15f), new Vector3(0.3f, 0.3f, 0.3f));

		var slot = new PickupSlot { Name = Name + "_Slot", Id = Name + "_slot", Transform = Transform };
		slot.ConfigureShape(bounds.GetCenter(), bounds.Size);

		Slot = slot;
		slot.SetOccupant(this);
		Freeze = true;
		// Parent is still setting up its children during _Ready.
		GetParent().CallDeferred(Node.MethodName.AddChild, slot);
	}
}
