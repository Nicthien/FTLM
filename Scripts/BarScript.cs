using Godot;
using System;

public partial class BarScript : AnimatableBody3D
{
	[Export]
	public float Vitesse = 4.0f;

	private const float DemiLargeurBase = 0.5f;
	private const float ToleranceIA = 0.04f;

	// Bord interieur du couloir (distance du centre au mur lateral) ; depend du
	// nombre de joueurs, donc configurable plutot que constant.
	private float BordInterieur = 3.9f;

	public float DemiLargeur { get; private set; } = DemiLargeurBase;
	public double TempsRedimensionnementRestant => Math.Max(0.0, _tempsRedimRestant);
	public float FacteurRedimensionnement { get; private set; } = 1.0f;
	public int SensAttaque { get; private set; } = 1;

	// Source de mouvement de la barre. Local = clavier/souris de cette machine ;
	// IA = pilotee par l'ordinateur ; Distant = axe recu d'un client (cote hote) ;
	// Spectateur = position imposee par les snapshots reseau (cote client).
	public enum ModeControle
	{
		Local,
		IA,
		Distant,
		Spectateur,
	}

	private float LimiteX => BordInterieur - DemiLargeur;

	private MeshInstance3D _corpsMesh;
	private MeshInstance3D _faceAvant;
	private CollisionShape3D _collision;
	private BoxMesh _corpsBox;
	private QuadMesh _faceQuad;
	private BoxShape3D _collisionBox;
	private Vector3 _tailleCorpsBase = new Vector3(1.0f, 0.2f, 0.2f);
	private Vector2 _tailleFaceBase = new Vector2(1.0f, 0.2f);
	private Vector3 _tailleCollisionBase = new Vector3(1.0f, 0.2f, 0.2f);
	private double _tempsRedimRestant;
	private bool _controleSourisActif;
	private ModeControle _mode = ModeControle.Local;
	private float _axeDistant;
	private float? _cibleDistante;
	private float _cibleSpectateurX;
	private bool _cibleSpectateurDefinie;
	private const float LissageSpectateur = 18.0f;
	private bool _sourisAutorisee = true;
	private string _actionGauche = "ui_left";
	private string _actionDroite = "ui_right";
	private float? _cibleIA;
	private Node3D _canonsLaser;
	// Etats offensifs infliges par un adversaire (objets de stock). Gel = barre figee ;
	// Inversion = controles inverses (gauche/droite et souris miroir).
	private double _gelRestant;
	private double _inversionRestant;

	public override void _Ready()
	{
		_corpsMesh = GetNodeOrNull<MeshInstance3D>("MeshInstance3D");
		_faceAvant = GetNodeOrNull<MeshInstance3D>("TextureFaceAvant");
		_collision = GetNodeOrNull<CollisionShape3D>("CollisionShape3D");

		_corpsBox = _corpsMesh?.Mesh as BoxMesh;
		_faceQuad = _faceAvant?.Mesh as QuadMesh;
		_collisionBox = _collision?.Shape as BoxShape3D;

		if (_corpsBox != null)
		{
			_corpsBox = (BoxMesh)_corpsBox.Duplicate();
			_corpsMesh.Mesh = _corpsBox;
		}
		if (_faceQuad != null)
		{
			_faceQuad = (QuadMesh)_faceQuad.Duplicate();
			_faceAvant.Mesh = _faceQuad;
		}
		if (_collisionBox != null)
		{
			_collisionBox = (BoxShape3D)_collisionBox.Duplicate();
			_collision.Shape = _collisionBox;
		}

		if (_corpsBox != null)
			_tailleCorpsBase = _corpsBox.Size;
		if (_faceQuad != null)
			_tailleFaceBase = _faceQuad.Size;
		if (_collisionBox != null)
			_tailleCollisionBase = _collisionBox.Size;

		AppliquerFacteur(1.0f);
	}

	public override void _Input(InputEvent ev)
	{
		if (_mode != ModeControle.Local || !_sourisAutorisee)
			return;

		if (ev is InputEventMouseMotion || ev is InputEventMouseButton)
			_controleSourisActif = true;
	}

	public override void _PhysicsProcess(double delta)
	{
		// En spectateur (client reseau) la position vient des snapshots : on interpole
		// vers la cible pour eviter le stutter du a la cadence reseau (~30 Hz).
		if (_mode == ModeControle.Spectateur)
		{
			if (_cibleSpectateurDefinie)
			{
				Vector3 p = Position;
				float f = 1.0f - Mathf.Exp(-(float)delta * LissageSpectateur);
				p.X = Mathf.Lerp(p.X, _cibleSpectateurX, f);
				Position = p;
			}
			return;
		}

		if (_tempsRedimRestant > 0.0)
		{
			_tempsRedimRestant -= delta;
			if (_tempsRedimRestant <= 0.0)
				AppliquerFacteur(1.0f);
		}

		if (_gelRestant > 0.0)
			_gelRestant -= delta;
		if (_inversionRestant > 0.0)
			_inversionRestant -= delta;

		// Gel : la barre est figee, aucun input n'est pris en compte.
		if (_gelRestant > 0.0)
			return;

		float direction = _mode switch
		{
			ModeControle.IA => DirectionIA(),
			ModeControle.Distant => _axeDistant,
			_ => Input.GetAxis(_actionGauche, _actionDroite),
		};
		bool inverse = _inversionRestant > 0.0;
		if (inverse)
			direction = -direction;

		Vector3 position = Position;
		if (!Mathf.IsZeroApprox(direction))
		{
			_controleSourisActif = false;
			position.X += direction * Vitesse * (float)delta;
		}
		else if (_mode == ModeControle.Distant && _cibleDistante.HasValue)
		{
			// Joueur distant a la souris : cible absolue (X local) recue de son client.
			position.X = inverse ? -_cibleDistante.Value : _cibleDistante.Value;
		}
		else if (_mode == ModeControle.Local && _sourisAutorisee && _controleSourisActif && EssayerLireXSouris(out float sourisX))
		{
			position.X = inverse ? -sourisX : sourisX;
		}

		position.X = Mathf.Clamp(position.X, -LimiteX, LimiteX);
		Position = position;
	}

	public void Configurer(bool controleIA, int sensAttaque, string actionGauche, string actionDroite, float bordInterieur)
	{
		_mode = controleIA ? ModeControle.IA : ModeControle.Local;
		SensAttaque = sensAttaque >= 0 ? 1 : -1;
		_actionGauche = actionGauche;
		_actionDroite = actionDroite;
		// Souris reservee au joueur 1 (couloir non pivote ou X local = X monde).
		_sourisAutorisee = !controleIA && actionGauche == "ui_left";
		BordInterieur = bordInterieur;
		_controleSourisActif = false;
		AppliquerFacteur(FacteurRedimensionnement);
	}

	// Force le mode de controle (override reseau applique apres Configurer).
	public void DefinirMode(ModeControle mode)
	{
		_mode = mode;
		_controleSourisActif = false;
		_sourisAutorisee = mode == ModeControle.Local && _actionGauche == "ui_left";
		if (mode != ModeControle.Distant)
		{
			_axeDistant = 0.0f;
			_cibleDistante = null;
		}
	}

	// Axe horizontal recu d'un client (cote hote, mode Distant).
	public void DefinirAxeDistant(float axe)
	{
		_axeDistant = Mathf.Clamp(axe, -1.0f, 1.0f);
	}

	// Cible absolue (X local) recue d'un client a la souris ; null = pas de souris active.
	public void DefinirCibleDistante(float? x)
	{
		_cibleDistante = x;
	}

	// Cote client (mode Spectateur) : X cible recu d'un snapshot, rejoint par interpolation.
	public void DefinirCibleSpectateur(float x)
	{
		_cibleSpectateurX = x;
		if (!_cibleSpectateurDefinie)
		{
			Vector3 p = Position;
			p.X = x;
			Position = p;
			_cibleSpectateurDefinie = true;
		}
	}

	public void DefinirCibleIA(float? x)
	{
		_cibleIA = x;
	}

	public void Redimensionner(float facteur, double duree)
	{
		AppliquerFacteur(facteur);
		_tempsRedimRestant = duree;
	}

	// Etats offensifs infliges par un objet adverse.
	public void Geler(double duree) => _gelRestant = Mathf.Max((float)_gelRestant, (float)duree);
	public void InverserControles(double duree) => _inversionRestant = Mathf.Max((float)_inversionRestant, (float)duree);

	public void ReinitialiserEtatsOffensifs()
	{
		_gelRestant = 0.0;
		_inversionRestant = 0.0;
	}

	private void AppliquerFacteur(float facteur)
	{
		facteur = Mathf.Clamp(facteur, 0.35f, 2.0f);
		Scale = Vector3.One;

		if (_corpsBox != null)
			_corpsBox.Size = new Vector3(_tailleCorpsBase.X * facteur, _tailleCorpsBase.Y, _tailleCorpsBase.Z);

		if (_faceQuad != null)
			_faceQuad.Size = new Vector2(_tailleFaceBase.X * facteur, _tailleFaceBase.Y);

		if (_collisionBox != null)
			_collisionBox.Size = new Vector3(_tailleCollisionBase.X * facteur, _tailleCollisionBase.Y, _tailleCollisionBase.Z);

		DemiLargeur = DemiLargeurBase * facteur;
		FacteurRedimensionnement = facteur;

		Vector3 position = Position;
		position.X = Mathf.Clamp(position.X, -LimiteX, LimiteX);
		Position = position;
	}

	// Affiche/masque deux petits canons sur la barre (aux positions de tir du laser ±0.24), tournes vers le camp adverse.
	public void AfficherCanonsLaser(bool actif, Color couleur)
	{
		if (actif && _canonsLaser == null)
			ConstruireCanonsLaser(couleur);

		if (_canonsLaser != null)
			_canonsLaser.Visible = actif;
	}

	private void ConstruireCanonsLaser(Color couleur)
	{
		_canonsLaser = new Node3D { Name = "CanonsLaser" };
		AddChild(_canonsLaser);

		var metal = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.05f, 0.06f, 0.08f),
			Metallic = 0.95f,
			Roughness = 0.2f,
		};
		var metalClair = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.18f, 0.2f, 0.24f),
			Metallic = 0.9f,
			Roughness = 0.3f,
		};
		var lueur = new StandardMaterial3D
		{
			AlbedoColor = couleur,
			EmissionEnabled = true,
			Emission = couleur,
			EmissionEnergyMultiplier = 2.2f,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		};

		float s = SensAttaque;
		foreach (float x in new[] { -0.24f, 0.24f })
		{
			var canon = new Node3D
			{
				Name = $"Canon_{(x < 0.0f ? "Gauche" : "Droit")}",
				Position = new Vector3(x, s * 0.1f, 0.0f),
				Scale = Vector3.One * 0.6f,
			};
			_canonsLaser.AddChild(canon);

			// Socle : platine d'ancrage trapue sur la barre.
			canon.AddChild(new MeshInstance3D
			{
				Name = "Socle",
				Mesh = new BoxMesh { Size = new Vector3(0.12f, 0.05f, 0.13f) },
				MaterialOverride = metalClair,
				Position = new Vector3(0.0f, s * 0.015f, 0.0f),
			});

			// Corps : bloc evase qui abrite le mecanisme.
			canon.AddChild(new MeshInstance3D
			{
				Name = "Corps",
				Mesh = new CylinderMesh { TopRadius = 0.045f, BottomRadius = 0.07f, Height = 0.11f },
				MaterialOverride = metal,
				Position = new Vector3(0.0f, s * 0.095f, 0.0f),
			});

			// Bague d'energie : anneau lumineux a la jonction corps/fut.
			canon.AddChild(new MeshInstance3D
			{
				Name = "BagueEnergie",
				Mesh = new TorusMesh { InnerRadius = 0.045f, OuterRadius = 0.062f },
				MaterialOverride = lueur,
				Position = new Vector3(0.0f, s * 0.155f, 0.0f),
			});

			// Fut : long tube fin pointe vers l'adversaire.
			canon.AddChild(new MeshInstance3D
			{
				Name = "Fut",
				Mesh = new CylinderMesh { TopRadius = 0.022f, BottomRadius = 0.03f, Height = 0.26f },
				MaterialOverride = metal,
				Position = new Vector3(0.0f, s * 0.3f, 0.0f),
			});

			// Collier : renfort metallique au milieu du fut.
			canon.AddChild(new MeshInstance3D
			{
				Name = "Collier",
				Mesh = new CylinderMesh { TopRadius = 0.034f, BottomRadius = 0.034f, Height = 0.025f },
				MaterialOverride = metalClair,
				Position = new Vector3(0.0f, s * 0.28f, 0.0f),
			});

			// Emetteur : bouche lumineuse au bout du fut.
			canon.AddChild(new MeshInstance3D
			{
				Name = "Emetteur",
				Mesh = new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.024f, Height = 0.05f },
				MaterialOverride = lueur,
				Position = new Vector3(0.0f, s * 0.455f, 0.0f),
			});
		}
	}

	private float DirectionIA()
	{
		if (!_cibleIA.HasValue)
			return 0.0f;

		float cible = Mathf.Clamp(_cibleIA.Value, -LimiteX, LimiteX);
		float ecart = cible - Position.X;
		if (Mathf.Abs(ecart) <= ToleranceIA)
			return 0.0f;

		return Mathf.Sign(ecart);
	}

	private bool EssayerLireXSouris(out float x)
	{
		x = 0.0f;
		Camera3D camera = GetViewport().GetCamera3D();
		if (camera == null)
			return false;

		Vector2 souris = GetViewport().GetMousePosition();
		Vector3 origine = camera.ProjectRayOrigin(souris);
		Vector3 direction = camera.ProjectRayNormal(souris);
		if (Mathf.IsZeroApprox(direction.Z))
			return false;

		float t = (GlobalPosition.Z - origine.Z) / direction.Z;
		if (t < 0.0f)
			return false;

		x = origine.X + direction.X * t;
		return true;
	}
}
