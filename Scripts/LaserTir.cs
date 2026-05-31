using Godot;
using System;

public partial class LaserTir : Area3D
{
	[Export]
	public float Vitesse = 8.0f;

	[Export]
	public float LimiteY = 5.2f;

	[Export]
	public int SensVertical = 1;

	public override void _PhysicsProcess(double delta)
	{
		Position += Vector3.Up * SensVertical * Vitesse * (float)delta;
		if ((SensVertical >= 0 && Position.Y > LimiteY) || (SensVertical < 0 && Position.Y < LimiteY))
			QueueFree();
	}
}
