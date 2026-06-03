using Godot;
using System;

// Projectile homing tire depuis le stock d'un camp vers un camp adverse. Sur l'hote/en
// local il avance vers CiblePos et declenche Arrivee a l'impact ; cote client il est en
// mode affichage (sa position vient des snapshots, aucune logique). Modele : LaserTir.
public partial class ObjetProjectile : Area3D
{
	public CapsuleScript.TypeBonus Type;
	public int IdReseau;
	public bool ModeAffichage;
	public int CampSource = -1;
	public int CampCible = -1;
	public Vector3 CiblePos;
	public float Vitesse = 7.0f;

	// Rappel declenche a l'arrivee sur la cible (hote/local uniquement).
	public Action Arrivee;

	private Vector3 _cibleAffichage;
	private bool _cibleDefinie;
	private bool _termine;
	private float _temps;
	private const float LissageAffichage = 20.0f;
	private const float SeuilArrivee = 0.5f;

	// Cote client : position cible recue d'un snapshot, rejointe par interpolation.
	public void DefinirCibleAffichage(Vector3 cible)
	{
		_cibleAffichage = cible;
		if (!_cibleDefinie)
		{
			GlobalPosition = cible;
			_cibleDefinie = true;
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		_temps += (float)delta;

		if (ModeAffichage)
		{
			if (_cibleDefinie)
			{
				float f = 1.0f - Mathf.Exp(-(float)delta * LissageAffichage);
				GlobalPosition = GlobalPosition.Lerp(_cibleAffichage, f);
			}
			return;
		}

		if (_termine)
			return;

		Vector3 vers = CiblePos - GlobalPosition;
		float distance = vers.Length();
		if (distance <= SeuilArrivee)
		{
			_termine = true;
			Arrivee?.Invoke();
			QueueFree();
			return;
		}

		GlobalPosition += vers / distance * Vitesse * (float)delta;
	}
}
