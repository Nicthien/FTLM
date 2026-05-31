using Godot;
using System;

public partial class CapsuleScript : Area3D
{
	// Effets possibles d'une capsule (bonus ou malus).
	public enum TypeBonus
	{
		BarreLarge,
		BarrePetite,
		MultiBalle,
		VieBonus,
		BalleLente,
		BalleRapide,
		Aimant,
		Laser,
		BouclierBas,
		ScoreDouble,
		BallePercante,
	}

	// Vitesse de chute.
	[Export]
	public float Vitesse = 2.0f;

	public TypeBonus Type { get; private set; }

	public override void _PhysicsProcess(double delta)
	{
		Position += Vector3.Down * Vitesse * (float)delta;

		// Disparaît si elle sort par le bas sans être attrapée.
		if (Position.Y < -1.5f)
			QueueFree();
	}

	// Configure le type et la couleur de la capsule.
	public void Initialiser(TypeBonus type, Color couleur)
	{
		Type = type;
		var mesh = GetNodeOrNull<MeshInstance3D>("MeshInstance3D");
		if (mesh != null)
			mesh.MaterialOverride = new StandardMaterial3D { AlbedoColor = couleur };
	}
}
