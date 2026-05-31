using Godot;
using System;

public partial class BalleScript : RigidBody3D
{
	[Export]
	public float VitesseCible = 5.0f;

	public bool Percante { get; set; }
	public bool EstCollee { get; private set; }
	public string Proprietaire { get; set; } = "joueur";
	public int SensAttaque { get; set; } = 1;

	private bool _enJeu = false;
	private BarScript _barCollee;
	private const float DecalageAimantY = 0.22f;
	private Vector3 _decalageColle = new Vector3(0.0f, DecalageAimantY, 0.0f);
	private Vector3 _derniereVitesseValide = Vector3.Up;

	public override void _PhysicsProcess(double delta)
	{
		if (EstCollee)
		{
			LinearVelocity = Vector3.Zero;
			AngularVelocity = Vector3.Zero;
			if (IsInstanceValid(_barCollee))
				GlobalPosition = _barCollee.GlobalPosition + _decalageColle;
			return;
		}

		if (_enJeu && LinearVelocity.Length() > 0.01f)
		{
			_derniereVitesseValide = LinearVelocity.Normalized();
			LinearVelocity = _derniereVitesseValide * VitesseCible;
		}
	}

	public void Lancer()
	{
		EstCollee = false;
		_barCollee = null;
		LinearVelocity = new Vector3(0.1f, SensAttaque, 0.0f).Normalized() * VitesseCible;
		_derniereVitesseValide = LinearVelocity.Normalized();
		_enJeu = true;
	}

	public void LancerDepuisBarre(float offsetNormalise)
	{
		LancerDepuisBarre(offsetNormalise, SensAttaque);
	}

	public void LancerDepuisBarre(float offsetNormalise, int sensAttaque)
	{
		SensAttaque = sensAttaque >= 0 ? 1 : -1;
		EstCollee = false;
		_barCollee = null;
		RebondSurBarre(offsetNormalise);
	}

	public void Positionner(Vector3 position)
	{
		_enJeu = false;
		EstCollee = false;
		_barCollee = null;
		Percante = false;
		LinearVelocity = Vector3.Zero;
		AngularVelocity = Vector3.Zero;
		GlobalPosition = position;
	}

	public void Stopper()
	{
		_enJeu = false;
		EstCollee = false;
		_barCollee = null;
		LinearVelocity = Vector3.Zero;
		AngularVelocity = Vector3.Zero;
	}

	public void CollerA(BarScript bar)
	{
		if (!IsInstanceValid(bar))
			return;

		_enJeu = false;
		EstCollee = true;
		_barCollee = bar;
		float offsetX = Mathf.Clamp(GlobalPosition.X - bar.GlobalPosition.X, -bar.DemiLargeur, bar.DemiLargeur);
		SensAttaque = bar.SensAttaque;
		_decalageColle = new Vector3(offsetX, DecalageAimantY * bar.SensAttaque, 0.0f);
		LinearVelocity = Vector3.Zero;
		AngularVelocity = Vector3.Zero;
	}

	public void GarderDirectionPercante()
	{
		if (!Percante || _derniereVitesseValide.Length() <= 0.01f)
			return;

		LinearVelocity = _derniereVitesseValide.Normalized() * VitesseCible;
	}

	public void RebondSurBarre(float offsetNormalise)
	{
		RebondSurBarre(offsetNormalise, SensAttaque);
	}

	public void RebondSurBarre(float offsetNormalise, int sensAttaque)
	{
		SensAttaque = sensAttaque >= 0 ? 1 : -1;
		float x = Mathf.Clamp(offsetNormalise, -1.0f, 1.0f);
		Vector3 direction = new Vector3(x, SensAttaque, 0.0f).Normalized();
		LinearVelocity = direction * VitesseCible;
		_derniereVitesseValide = direction;
		_enJeu = true;
		EstCollee = false;
		_barCollee = null;
	}
}
