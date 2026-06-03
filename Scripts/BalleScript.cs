using Godot;
using System;

public partial class BalleScript : RigidBody3D
{
	[Export]
	public float VitesseCible = 5.0f;

	public bool Percante { get; set; }
	public bool EstCollee { get; private set; }
	public string Proprietaire { get; set; } = "camp0";
	public string DestinataireBonus { get; set; } = "camp0";

	// Identifiant reseau (assigne par l'hote) et mode affichage (cote client) : une
	// balle en mode affichage ne simule rien, sa position vient des snapshots.
	public int IdReseau { get; set; }
	public bool ModeAffichage { get; private set; }

	// Cible reseau a rejoindre par interpolation (lissage du stutter 30 Hz cote client).
	private Vector3 _cibleAffichage;
	private bool _cibleDefinie;
	private const float LissageAffichage = 18.0f;

	// Axes (globaux) du couloir auquel la balle est rattachee : direction d'attaque
	// (vers le hub) et direction laterale (largeur de la barre). Remplacent l'ancien
	// SensAttaque entier pour gerer des couloirs pivotes (Y, croix).
	public Vector3 AxeAttaque { get; private set; } = Vector3.Up;
	public Vector3 AxeLateral { get; private set; } = Vector3.Right;

	private bool _enJeu = false;
	private BarScript _barCollee;
	private const float DecalageAimantY = 0.22f;
	// Rayon de collision de la balle (sphere 0.18, marge incluse) : sert a empecher une
	// balle collee au bord de la barre de depasser le mur lateral.
	private const float RayonBalle = 0.2f;
	private Vector3 _decalageColle = new Vector3(0.0f, DecalageAimantY, 0.0f);
	private Vector3 _derniereVitesseValide = Vector3.Up;

	// Cote client : la balle devient un objet purement affiche (pas de physique, pas
	// de collision), sa position est imposee chaque frame via GlobalPosition.
	public void ConfigurerAffichage()
	{
		ModeAffichage = true;
		Freeze = true;
		FreezeMode = FreezeModeEnum.Kinematic;
		GravityScale = 0.0f;
		CollisionLayer = 0;
		CollisionMask = 0;
		LinearVelocity = Vector3.Zero;
		AngularVelocity = Vector3.Zero;
	}

	// Cote client : definit la position cible recue d'un snapshot. La balle glisse vers
	// cette cible dans _PhysicsProcess (interpolation) au lieu de teleporter -> pas de stutter.
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
		if (ModeAffichage)
		{
			if (_cibleDefinie)
			{
				float f = 1.0f - Mathf.Exp(-(float)delta * LissageAffichage);
				GlobalPosition = GlobalPosition.Lerp(_cibleAffichage, f);
			}
			return;
		}

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

	// Definit les axes du couloir (vecteurs globaux, normalises en interne).
	public void DefinirAxes(Vector3 axeAttaque, Vector3 axeLateral)
	{
		if (axeAttaque.LengthSquared() > 0.0001f)
			AxeAttaque = axeAttaque.Normalized();
		if (axeLateral.LengthSquared() > 0.0001f)
			AxeLateral = axeLateral.Normalized();
	}

	public void Lancer()
	{
		EstCollee = false;
		_barCollee = null;
		Vector3 direction = (AxeAttaque + 0.1f * AxeLateral).Normalized();
		LinearVelocity = direction * VitesseCible;
		_derniereVitesseValide = direction;
		_enJeu = true;
	}

	public void LancerDepuisBarre(float offsetNormalise)
	{
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
		Vector3 versHub = bar.GlobalTransform.Basis.Y.Normalized();
		Vector3 lateral = bar.GlobalTransform.Basis.X.Normalized();
		// On garde le rayon de la balle a l'interieur du bord de la barre : une balle collee
		// au bord ne depasse alors jamais le mur lateral quand la barre est collee au bord du couloir.
		float demiLibre = Mathf.Max(0.0f, bar.DemiLargeur - RayonBalle);
		float offsetLat = Mathf.Clamp((GlobalPosition - bar.GlobalPosition).Dot(lateral), -demiLibre, demiLibre);
		AxeAttaque = versHub;
		AxeLateral = lateral;
		_decalageColle = lateral * offsetLat + versHub * DecalageAimantY;
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
		float x = Mathf.Clamp(offsetNormalise, -1.0f, 1.0f);
		Vector3 direction = (AxeAttaque + x * AxeLateral).Normalized();
		LinearVelocity = direction * VitesseCible;
		_derniereVitesseValide = direction;
		_enJeu = true;
		EstCollee = false;
		_barCollee = null;
	}
}
