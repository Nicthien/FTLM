using Godot;
using System;

public partial class LaserTir : Area3D
{
	[Export]
	public float Vitesse = 8.0f;

	[Export]
	public float LimiteY = 5.2f;

	public override void _PhysicsProcess(double delta)
	{
		Position += Vector3.Up * Vitesse * (float)delta;
		if (Position.Y > LimiteY)
			QueueFree();
	}
}
