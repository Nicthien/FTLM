using Godot;

public partial class CapsuleScript : Area3D
{
	// Effets possibles d'une capsule. Deux familles : les self-buffs (appliques
	// instantanement au camp qui ramasse) et les objets offensifs (ranges dans le stock
	// a 2 cases, lances vers un camp adverse). Voir EstObjetOffensif.
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
		// --- Objets offensifs (stock) ---
		Missile,
		Retrecisseur,
		Gel,
		Inverseur,
		Accelerateur,
	}

	// Vrai si le type est un objet offensif : il se range dans le stock du camp et se
	// lance vers un adversaire (au lieu de s'appliquer instantanement au ramassage).
	public static bool EstObjetOffensif(TypeBonus type) => type is
		TypeBonus.Missile or TypeBonus.Retrecisseur or TypeBonus.Gel
		or TypeBonus.Inverseur or TypeBonus.Accelerateur or TypeBonus.Laser;

	// Vitesse de chute.
	[Export]
	public float Vitesse = 2.0f;

	[Export]
	public int SensVertical = -1;

	public TypeBonus Type { get; private set; }

	// Identifiant reseau (assigne par l'hote) et mode affichage (cote client) : une
	// capsule en mode affichage ne tombe pas d'elle-meme, sa position vient des snapshots.
	public int IdReseau { get; set; }
	public bool ModeAffichage { get; set; }

	private Node3D _visuel;
	private float _temps;
	private Color _couleur = Colors.White;
	private Vector3 _cibleAffichage;
	private bool _cibleDefinie;
	private const float LissageAffichage = 18.0f;
	// Echelle d'affichage globale des capsules (bonus + objets) : < 1 pour les reduire.
	private const float EchelleVisuel = 0.72f;

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
		}
		else
		{
			Position += Vector3.Up * SensVertical * Vitesse * (float)delta;
		}

		if (_visuel != null)
		{
			_visuel.Rotation = new Vector3(
				0.18f * Mathf.Sin(_temps * 3.2f),
				0.16f * Mathf.Cos(_temps * 2.5f),
				_temps * 2.4f);
			_visuel.Scale = Vector3.One * EchelleVisuel * (1.0f + 0.055f * Mathf.Sin(_temps * 6.5f));
		}

		// Disparait si elle sort par le bas sans etre attrapee (cote hote/local seulement).
		if (!ModeAffichage && (Position.Y < -1.5f || Position.Y > 10.5f))
			QueueFree();
	}

	// Configure le type, la couleur et la silhouette de la capsule.
	public void Initialiser(TypeBonus type, Color couleur)
	{
		Type = type;
		_couleur = couleur;
		ConstruireVisuel();
	}

	private void ConstruireVisuel()
	{
		GetNodeOrNull<MeshInstance3D>("MeshInstance3D")?.QueueFree();
		GetNodeOrNull<Node3D>("Visuel")?.QueueFree();

		_visuel = new Node3D { Name = "Visuel" };
		AddChild(_visuel);
		AjouterHalo(_couleur);

		// Les objets offensifs partagent une silhouette commune : une bulle de verre
		// translucide englobant la plaque d'icone (ajoutee plus bas).
		if (EstObjetOffensif(Type))
		{
			ConstruireBulle();
			AjouterIconeBulle(Type, _couleur);
			return;
		}

		switch (Type)
		{
			case TypeBonus.BarreLarge:
				ConstruireBarreLarge();
				break;
			case TypeBonus.BarrePetite:
				ConstruireBarrePetite();
				break;
			case TypeBonus.MultiBalle:
				ConstruireMultiBalle();
				break;
			case TypeBonus.VieBonus:
				ConstruireVieBonus();
				break;
			case TypeBonus.BalleLente:
				ConstruireBalleLente();
				break;
			case TypeBonus.BalleRapide:
				ConstruireBalleRapide();
				break;
			case TypeBonus.Aimant:
				ConstruireAimant();
				break;
			case TypeBonus.Laser:
				ConstruireLaser();
				break;
			case TypeBonus.BouclierBas:
				ConstruireBouclier();
				break;
			case TypeBonus.ScoreDouble:
				ConstruireScoreDouble();
				break;
			case TypeBonus.BallePercante:
				ConstruireBallePercante();
				break;
		}

		AjouterPlaqueIcone(Type, _couleur);
		AjouterDetailsTechniques(_couleur);
	}

	private void ConstruireBarreLarge()
	{
		Material coeur = Materiau(_couleur, 1.8f, 0.25f);
		AjouterCapsuleHorizontale("BarreLarge", 0.56f, 0.075f, coeur);
		AjouterBoite("PlusH", new Vector3(0.25f, 0.045f, 0.035f), new Vector3(0.0f, 0.0f, 0.095f), Vector3.Zero, MateriauBlanc());
		AjouterBoite("PlusV", new Vector3(0.045f, 0.25f, 0.035f), new Vector3(0.0f, 0.0f, 0.096f), Vector3.Zero, MateriauBlanc());
	}

	private void ConstruireBarrePetite()
	{
		Material coeur = Materiau(_couleur, 1.6f, 0.25f);
		AjouterBoite("Losange", new Vector3(0.28f, 0.28f, 0.12f), Vector3.Zero, new Vector3(0.0f, 0.0f, Mathf.Pi / 4.0f), coeur);
		AjouterBoite("Moins", new Vector3(0.24f, 0.045f, 0.04f), new Vector3(0.0f, 0.0f, 0.105f), Vector3.Zero, MateriauBlanc());
		AjouterBoite("EclatBas", new Vector3(0.12f, 0.04f, 0.04f), new Vector3(0.0f, -0.21f, 0.03f), Vector3.Zero, Materiau(_couleur, 0.7f, 0.1f));
	}

	private void ConstruireMultiBalle()
	{
		Material coeur = Materiau(_couleur, 2.0f, 0.18f);
		AjouterSphere("BalleA", 0.105f, new Vector3(-0.13f, -0.08f, 0.04f), coeur);
		AjouterSphere("BalleB", 0.105f, new Vector3(0.13f, -0.08f, 0.04f), coeur);
		AjouterSphere("BalleC", 0.105f, new Vector3(0.0f, 0.14f, 0.04f), coeur);
		AjouterBoite("ConnecteurA", new Vector3(0.28f, 0.035f, 0.03f), new Vector3(0.0f, -0.03f, -0.02f), new Vector3(0.0f, 0.0f, -0.72f), MateriauSecondaire(_couleur));
		AjouterBoite("ConnecteurB", new Vector3(0.28f, 0.035f, 0.03f), new Vector3(0.0f, -0.03f, -0.02f), new Vector3(0.0f, 0.0f, 0.72f), MateriauSecondaire(_couleur));
	}

	private void ConstruireVieBonus()
	{
		Material coeur = Materiau(_couleur, 1.9f, 0.18f);
		AjouterSphere("CoeurGauche", 0.115f, new Vector3(-0.085f, 0.065f, 0.035f), coeur);
		AjouterSphere("CoeurDroit", 0.115f, new Vector3(0.085f, 0.065f, 0.035f), coeur);
		AjouterBoite("CoeurBas", new Vector3(0.22f, 0.22f, 0.11f), new Vector3(0.0f, -0.075f, 0.02f), new Vector3(0.0f, 0.0f, Mathf.Pi / 4.0f), coeur);
		AjouterBoite("CroixH", new Vector3(0.20f, 0.04f, 0.035f), new Vector3(0.0f, 0.01f, 0.13f), Vector3.Zero, MateriauBlanc());
		AjouterBoite("CroixV", new Vector3(0.04f, 0.20f, 0.035f), new Vector3(0.0f, 0.01f, 0.131f), Vector3.Zero, MateriauBlanc());
	}

	private void ConstruireBalleLente()
	{
		Material coeur = Materiau(_couleur, 1.7f, 0.22f);
		AjouterCone("SablierHaut", 0.13f, 0.18f, new Vector3(0.0f, 0.10f, 0.02f), new Vector3(Mathf.Pi, 0.0f, 0.0f), coeur);
		AjouterCone("SablierBas", 0.13f, 0.18f, new Vector3(0.0f, -0.10f, 0.02f), Vector3.Zero, coeur);
		AjouterBoite("TraitHaut", new Vector3(0.31f, 0.035f, 0.035f), new Vector3(0.0f, 0.22f, 0.03f), Vector3.Zero, MateriauBlanc());
		AjouterBoite("TraitBas", new Vector3(0.31f, 0.035f, 0.035f), new Vector3(0.0f, -0.22f, 0.03f), Vector3.Zero, MateriauBlanc());
	}

	private void ConstruireBalleRapide()
	{
		Material coeur = Materiau(_couleur, 2.2f, 0.16f);
		AjouterBoite("EclairHaut", new Vector3(0.13f, 0.30f, 0.09f), new Vector3(0.055f, 0.095f, 0.03f), new Vector3(0.0f, 0.0f, -0.48f), coeur);
		AjouterBoite("EclairMilieu", new Vector3(0.25f, 0.09f, 0.10f), new Vector3(0.0f, -0.015f, 0.035f), new Vector3(0.0f, 0.0f, -0.20f), coeur);
		AjouterBoite("EclairBas", new Vector3(0.13f, 0.30f, 0.09f), new Vector3(-0.055f, -0.135f, 0.03f), new Vector3(0.0f, 0.0f, -0.48f), coeur);
		AjouterSphere("NoyauRapide", 0.055f, new Vector3(0.0f, 0.0f, 0.13f), MateriauBlanc());
	}

	private void ConstruireAimant()
	{
		Material coeur = Materiau(_couleur, 1.8f, 0.20f);
		Material metal = Materiau(new Color(0.92f, 0.96f, 1.0f), 0.45f, 0.08f);
		AjouterBoite("AimantGauche", new Vector3(0.10f, 0.34f, 0.10f), new Vector3(-0.14f, 0.02f, 0.02f), Vector3.Zero, coeur);
		AjouterBoite("AimantDroit", new Vector3(0.10f, 0.34f, 0.10f), new Vector3(0.14f, 0.02f, 0.02f), Vector3.Zero, coeur);
		AjouterBoite("AimantDos", new Vector3(0.38f, 0.10f, 0.10f), new Vector3(0.0f, 0.18f, 0.02f), Vector3.Zero, coeur);
		AjouterBoite("PoleGauche", new Vector3(0.12f, 0.075f, 0.115f), new Vector3(-0.14f, -0.19f, 0.025f), Vector3.Zero, metal);
		AjouterBoite("PoleDroit", new Vector3(0.12f, 0.075f, 0.115f), new Vector3(0.14f, -0.19f, 0.025f), Vector3.Zero, metal);
	}

	private void ConstruireLaser()
	{
		Material coque = Materiau(_couleur, 2.1f, 0.12f);
		Material lentille = Materiau(new Color(1.0f, 0.20f, 0.10f), 2.8f, 0.08f);
		AjouterBoite("CanonBase", new Vector3(0.34f, 0.16f, 0.12f), new Vector3(0.0f, -0.02f, 0.02f), Vector3.Zero, coque);
		AjouterCylindre("CanonGauche", 0.045f, 0.26f, new Vector3(-0.09f, 0.13f, 0.045f), new Vector3(Mathf.Pi / 2.0f, 0.0f, 0.0f), lentille);
		AjouterCylindre("CanonDroit", 0.045f, 0.26f, new Vector3(0.09f, 0.13f, 0.045f), new Vector3(Mathf.Pi / 2.0f, 0.0f, 0.0f), lentille);
		AjouterBoite("Viseur", new Vector3(0.08f, 0.08f, 0.06f), new Vector3(0.0f, -0.16f, 0.09f), new Vector3(0.0f, 0.0f, Mathf.Pi / 4.0f), MateriauBlanc());
	}

	private void ConstruireBouclier()
	{
		Material coeur = Materiau(_couleur, 1.9f, 0.18f);
		AjouterBoite("BouclierCentre", new Vector3(0.24f, 0.36f, 0.10f), new Vector3(0.0f, 0.015f, 0.02f), Vector3.Zero, coeur);
		AjouterBoite("BouclierGauche", new Vector3(0.14f, 0.28f, 0.09f), new Vector3(-0.15f, 0.035f, 0.015f), new Vector3(0.0f, 0.0f, 0.34f), coeur);
		AjouterBoite("BouclierDroit", new Vector3(0.14f, 0.28f, 0.09f), new Vector3(0.15f, 0.035f, 0.015f), new Vector3(0.0f, 0.0f, -0.34f), coeur);
		AjouterBoite("BouclierPointe", new Vector3(0.18f, 0.18f, 0.10f), new Vector3(0.0f, -0.19f, 0.02f), new Vector3(0.0f, 0.0f, Mathf.Pi / 4.0f), coeur);
		AjouterBoite("Reflet", new Vector3(0.045f, 0.24f, 0.035f), new Vector3(-0.055f, 0.05f, 0.12f), new Vector3(0.0f, 0.0f, -0.25f), MateriauBlanc());
	}

	private void ConstruireScoreDouble()
	{
		Material or = Materiau(_couleur, 1.8f, 0.10f);
		AjouterCylindre("JetonA", 0.13f, 0.055f, new Vector3(-0.09f, 0.02f, 0.035f), new Vector3(Mathf.Pi / 2.0f, 0.0f, 0.0f), or);
		AjouterCylindre("JetonB", 0.13f, 0.055f, new Vector3(0.11f, -0.04f, 0.045f), new Vector3(Mathf.Pi / 2.0f, 0.0f, 0.0f), or);
		AjouterBoite("CroixJetonA", new Vector3(0.16f, 0.035f, 0.025f), new Vector3(-0.09f, 0.02f, 0.105f), Vector3.Zero, MateriauBlanc());
		AjouterBoite("CroixJetonB", new Vector3(0.035f, 0.16f, 0.025f), new Vector3(0.11f, -0.04f, 0.11f), Vector3.Zero, MateriauBlanc());
		AjouterBoite("Pile", new Vector3(0.34f, 0.045f, 0.05f), new Vector3(0.01f, -0.19f, 0.01f), Vector3.Zero, MateriauSecondaire(_couleur));
	}

	private void ConstruireBallePercante()
	{
		Material coeur = Materiau(_couleur, 2.1f, 0.16f);
		AjouterCone("Pointe", 0.13f, 0.24f, new Vector3(0.0f, -0.12f, 0.045f), Vector3.Zero, coeur);
		AjouterCylindre("Foret", 0.075f, 0.30f, new Vector3(0.0f, 0.08f, 0.035f), Vector3.Zero, coeur);
		AjouterBoite("AileronGauche", new Vector3(0.16f, 0.055f, 0.06f), new Vector3(-0.115f, 0.16f, 0.04f), new Vector3(0.0f, 0.0f, 0.55f), MateriauSecondaire(_couleur));
		AjouterBoite("AileronDroit", new Vector3(0.16f, 0.055f, 0.06f), new Vector3(0.115f, 0.16f, 0.04f), new Vector3(0.0f, 0.0f, -0.55f), MateriauSecondaire(_couleur));
		AjouterSphere("NoyauPercant", 0.052f, new Vector3(0.0f, 0.20f, 0.105f), MateriauBlanc());
	}

	// Bulle de verre (objets offensifs) : une orbe lumineuse entouree d'un anneau
	// energetique brillant — silhouette volontairement distincte des bonus classiques,
	// pour qu'on reconnaisse au premier coup d'oeil un objet a stocker.
	private void ConstruireBulle()
	{
		// Noyau lumineux marque.
		AjouterSphere("NoyauBulle", 0.14f, new Vector3(0.0f, 0.0f, -0.02f), Materiau(_couleur, 2.6f, 0.12f));

		// Coque de verre brillante (plus grosse et plus visible).
		var coque = new MeshInstance3D
		{
			Name = "CoqueBulle",
			Mesh = new SphereMesh { Radius = 0.34f, Height = 0.68f, RadialSegments = 32, Rings = 16 },
			MaterialOverride = MateriauBulle(_couleur),
		};
		_visuel.AddChild(coque);

		// Anneau energetique dans le plan de la camera (axe Z) : marqueur "objet special".
		var anneau = new MeshInstance3D
		{
			Name = "AnneauObjet",
			Mesh = new TorusMesh { InnerRadius = 0.38f, OuterRadius = 0.46f, RingSegments = 48 },
			Rotation = new Vector3(Mathf.Pi / 2.0f, 0.0f, 0.0f),
			MaterialOverride = MateriauAnneauObjet(_couleur),
		};
		_visuel.AddChild(anneau);

		// Quatre plots brillants sur l'anneau (rappellent une "boite a objet").
		Material plot = Materiau(Mix(_couleur, Colors.White, 0.5f), 2.4f, 0.0f);
		foreach (float a in new[] { 0.0f, Mathf.Pi / 2.0f, Mathf.Pi, 3.0f * Mathf.Pi / 2.0f })
			AjouterSphere($"Plot{a:0.0}", 0.045f, new Vector3(Mathf.Cos(a) * 0.42f, Mathf.Sin(a) * 0.42f, 0.0f), plot);
	}

	// Icone "medaillon" flottant au centre de l'orbe (sans le cadre des bonus classiques).
	private void AjouterIconeBulle(TypeBonus type, Color couleur)
	{
		var icone = new MeshInstance3D
		{
			Name = "TextureIcone",
			Mesh = new QuadMesh { Size = new Vector2(0.42f, 0.42f) },
			Position = new Vector3(0.0f, 0.0f, 0.05f),
			MaterialOverride = MateriauIcone(type, couleur),
		};
		_visuel.AddChild(icone);
	}

	private static StandardMaterial3D MateriauAnneauObjet(Color couleur)
	{
		return new StandardMaterial3D
		{
			AlbedoColor = Mix(couleur, Colors.White, 0.35f),
			EmissionEnabled = true,
			Emission = couleur,
			EmissionEnergyMultiplier = 3.0f,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		};
	}

	private static StandardMaterial3D MateriauBulle(Color couleur)
	{
		return new StandardMaterial3D
		{
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			AlbedoColor = new Color(couleur.R, couleur.G, couleur.B, 0.34f),
			EmissionEnabled = true,
			Emission = couleur,
			EmissionEnergyMultiplier = 0.7f,
			Metallic = 0.0f,
			Roughness = 0.04f,
			RimEnabled = true,
			Rim = 1.0f,
			RimTint = 0.3f,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
	}

	private void AjouterPlaqueIcone(TypeBonus type, Color couleur)
	{
		Material dos = Materiau(Mix(couleur, new Color(0.02f, 0.04f, 0.07f), 0.58f), 0.75f, 0.35f);
		AjouterBoite("PlaqueIconeDos", new Vector3(0.43f, 0.43f, 0.026f), new Vector3(0.0f, 0.0f, 0.132f), Vector3.Zero, dos);

		var icone = new MeshInstance3D
		{
			Name = "TextureIcone",
			Mesh = new QuadMesh { Size = new Vector2(0.36f, 0.36f) },
			Position = new Vector3(0.0f, 0.0f, 0.148f),
			MaterialOverride = MateriauIcone(type, couleur),
		};
		_visuel.AddChild(icone);

		Material bord = MateriauSecondaire(couleur);
		AjouterBoite("BordIconeHaut", new Vector3(0.46f, 0.025f, 0.035f), new Vector3(0.0f, 0.23f, 0.151f), Vector3.Zero, bord);
		AjouterBoite("BordIconeBas", new Vector3(0.46f, 0.025f, 0.035f), new Vector3(0.0f, -0.23f, 0.151f), Vector3.Zero, bord);
		AjouterBoite("BordIconeGauche", new Vector3(0.025f, 0.46f, 0.035f), new Vector3(-0.23f, 0.0f, 0.151f), Vector3.Zero, bord);
		AjouterBoite("BordIconeDroit", new Vector3(0.025f, 0.46f, 0.035f), new Vector3(0.23f, 0.0f, 0.151f), Vector3.Zero, bord);
	}

	private void AjouterDetailsTechniques(Color couleur)
	{
		Material diode = Materiau(Mix(couleur, Colors.White, 0.32f), 1.65f, 0.05f);
		AjouterSphere("DiodeNordOuest", 0.026f, new Vector3(-0.25f, 0.25f, 0.18f), diode);
		AjouterSphere("DiodeNordEst", 0.026f, new Vector3(0.25f, 0.25f, 0.18f), diode);
		AjouterSphere("DiodeSudOuest", 0.026f, new Vector3(-0.25f, -0.25f, 0.18f), diode);
		AjouterSphere("DiodeSudEst", 0.026f, new Vector3(0.25f, -0.25f, 0.18f), diode);

		Material rainure = Materiau(Mix(couleur, new Color(0.04f, 0.10f, 0.13f), 0.45f), 0.7f, 0.45f);
		AjouterBoite("RainureNord", new Vector3(0.20f, 0.018f, 0.032f), new Vector3(0.0f, 0.285f, 0.08f), Vector3.Zero, rainure);
		AjouterBoite("RainureSud", new Vector3(0.20f, 0.018f, 0.032f), new Vector3(0.0f, -0.285f, 0.08f), Vector3.Zero, rainure);
	}

	private void AjouterHalo(Color couleur)
	{
		var halo = new MeshInstance3D
		{
			Name = "Halo",
			Mesh = new QuadMesh { Size = new Vector2(0.72f, 0.72f) },
			Position = new Vector3(0.0f, 0.0f, -0.06f),
			MaterialOverride = MateriauHalo(couleur),
		};
		_visuel.AddChild(halo);
	}

	private void AjouterCapsuleHorizontale(string nom, float largeur, float rayon, Material materiau)
	{
		float corps = Mathf.Max(0.01f, largeur - rayon * 2.0f);
		AjouterCylindre($"{nom}Corps", rayon, corps, Vector3.Zero, new Vector3(0.0f, 0.0f, Mathf.Pi / 2.0f), materiau);
		AjouterSphere($"{nom}Gauche", rayon, new Vector3(-corps * 0.5f, 0.0f, 0.0f), materiau);
		AjouterSphere($"{nom}Droit", rayon, new Vector3(corps * 0.5f, 0.0f, 0.0f), materiau);
	}

	private MeshInstance3D AjouterBoite(string nom, Vector3 taille, Vector3 position, Vector3 rotation, Material materiau)
	{
		return AjouterMesh(nom, new BoxMesh { Size = taille }, position, rotation, materiau);
	}

	private MeshInstance3D AjouterSphere(string nom, float rayon, Vector3 position, Material materiau)
	{
		return AjouterMesh(nom, new SphereMesh
		{
			Radius = rayon,
			Height = rayon * 2.0f,
			RadialSegments = 24,
			Rings = 12,
		}, position, Vector3.Zero, materiau);
	}

	private MeshInstance3D AjouterCylindre(string nom, float rayon, float hauteur, Vector3 position, Vector3 rotation, Material materiau)
	{
		return AjouterMesh(nom, new CylinderMesh
		{
			TopRadius = rayon,
			BottomRadius = rayon,
			Height = hauteur,
			RadialSegments = 24,
		}, position, rotation, materiau);
	}

	private MeshInstance3D AjouterCone(string nom, float rayon, float hauteur, Vector3 position, Vector3 rotation, Material materiau)
	{
		return AjouterMesh(nom, new CylinderMesh
		{
			TopRadius = 0.0f,
			BottomRadius = rayon,
			Height = hauteur,
			RadialSegments = 28,
		}, position, rotation, materiau);
	}

	private MeshInstance3D AjouterMesh(string nom, Mesh mesh, Vector3 position, Vector3 rotation, Material materiau)
	{
		var instance = new MeshInstance3D
		{
			Name = nom,
			Mesh = mesh,
			Position = position,
			Rotation = rotation,
			MaterialOverride = materiau,
		};
		_visuel.AddChild(instance);
		return instance;
	}

	private static StandardMaterial3D Materiau(Color couleur, float emission, float metallic)
	{
		Color albedo = Mix(couleur, Colors.White, 0.16f);
		return new StandardMaterial3D
		{
			AlbedoColor = albedo,
			AlbedoTexture = CreerTextureCircuit(couleur),
			EmissionEnabled = true,
			Emission = couleur,
			EmissionEnergyMultiplier = emission,
			Metallic = metallic,
			Roughness = 0.18f,
		};
	}

	private static StandardMaterial3D MateriauSecondaire(Color couleur)
	{
		return Materiau(Mix(couleur, Colors.White, 0.38f), 0.85f, 0.18f);
	}

	private static StandardMaterial3D MateriauBlanc()
	{
		return Materiau(new Color(0.92f, 1.0f, 0.96f), 1.35f, 0.0f);
	}

	private static StandardMaterial3D MateriauHalo(Color couleur)
	{
		return new StandardMaterial3D
		{
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = new Color(couleur.R, couleur.G, couleur.B, 0.28f),
			EmissionEnabled = true,
			Emission = couleur,
			EmissionEnergyMultiplier = 0.65f,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
	}

	private static StandardMaterial3D MateriauIcone(TypeBonus type, Color couleur)
	{
		return new StandardMaterial3D
		{
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = Colors.White,
			AlbedoTexture = CreerTextureIcone(type, couleur),
			EmissionEnabled = true,
			Emission = Mix(couleur, Colors.White, 0.25f),
			EmissionEnergyMultiplier = 1.15f,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
	}

	private static ImageTexture CreerTextureCircuit(Color couleur)
	{
		const int taille = 32;
		Image image = Image.CreateEmpty(taille, taille, false, Image.Format.Rgba8);
		Color baseTex = Mix(couleur, new Color(0.08f, 0.10f, 0.13f), 0.34f);
		Color piste = Mix(couleur, Colors.White, 0.30f);

		for (int y = 0; y < taille; y++)
		{
			for (int x = 0; x < taille; x++)
			{
				float variation = ((x + y) % 9 == 0) ? 0.18f : 0.0f;
				Color pixel = Mix(baseTex, piste, variation);
				if (x == 4 || x == 21 || y == 7 || y == 24)
					pixel = Mix(pixel, piste, 0.36f);
				if ((x - y + taille) % 17 == 0)
					pixel = Mix(pixel, Colors.White, 0.22f);
				image.SetPixel(x, y, new Color(pixel.R, pixel.G, pixel.B, 1.0f));
			}
		}

		return ImageTexture.CreateFromImage(image);
	}

	// Expose l'icone d'un type pour l'afficher ailleurs (cases de stock du HUD).
	public static ImageTexture TextureIcone(TypeBonus type, Color couleur) => CreerTextureIcone(type, couleur);

	private static ImageTexture CreerTextureIcone(TypeBonus type, Color couleur)
	{
		const int taille = 96;
		Image image = Image.CreateEmpty(taille, taille, false, Image.Format.Rgba8);
		image.Fill(new Color(0.0f, 0.0f, 0.0f, 0.0f));

		Color fond = new Color(0.015f, 0.025f, 0.04f, 0.84f);
		Color accent = Mix(couleur, Colors.White, 0.22f);
		Color blanc = new Color(0.92f, 1.0f, 0.96f, 1.0f);

		DessinerDisque(image, 48, 48, 44, fond);
		DessinerAnneau(image, 48, 48, 41, 3, new Color(accent.R, accent.G, accent.B, 0.68f));
		DessinerAnneau(image, 48, 48, 31, 1, new Color(accent.R, accent.G, accent.B, 0.25f));
		DessinerLigne(image, 17, 73, 79, 21, 2, new Color(accent.R, accent.G, accent.B, 0.18f));
		DessinerLigne(image, 20, 18, 74, 70, 1, new Color(blanc.R, blanc.G, blanc.B, 0.14f));

		switch (type)
		{
			case TypeBonus.BarreLarge:
				DessinerRectangle(image, 24, 43, 72, 53, blanc);
				DessinerRectangle(image, 43, 24, 53, 72, blanc);
				break;
			case TypeBonus.BarrePetite:
				DessinerRectangle(image, 24, 43, 72, 53, blanc);
				DessinerLigne(image, 29, 67, 67, 29, 4, accent);
				break;
			case TypeBonus.MultiBalle:
				DessinerLigne(image, 31, 59, 65, 59, 3, accent);
				DessinerLigne(image, 31, 59, 48, 31, 3, accent);
				DessinerLigne(image, 65, 59, 48, 31, 3, accent);
				DessinerDisque(image, 31, 59, 10, blanc);
				DessinerDisque(image, 65, 59, 10, blanc);
				DessinerDisque(image, 48, 31, 10, blanc);
				break;
			case TypeBonus.VieBonus:
				DessinerDisque(image, 38, 37, 11, blanc);
				DessinerDisque(image, 58, 37, 11, blanc);
				DessinerTriangle(image, 27, 42, 69, 42, 48, 72, blanc);
				DessinerRectangle(image, 43, 42, 53, 62, accent);
				DessinerRectangle(image, 38, 47, 58, 57, accent);
				break;
			case TypeBonus.BalleLente:
				DessinerTriangle(image, 31, 25, 65, 25, 48, 47, blanc);
				DessinerTriangle(image, 31, 71, 65, 71, 48, 49, blanc);
				DessinerRectangle(image, 29, 21, 67, 26, accent);
				DessinerRectangle(image, 29, 70, 67, 75, accent);
				break;
			case TypeBonus.BalleRapide:
				DessinerLigne(image, 56, 17, 35, 49, 9, blanc);
				DessinerLigne(image, 35, 49, 54, 49, 9, blanc);
				DessinerLigne(image, 54, 49, 38, 78, 9, blanc);
				break;
			case TypeBonus.Aimant:
				DessinerRectangle(image, 25, 25, 36, 69, blanc);
				DessinerRectangle(image, 60, 25, 71, 69, blanc);
				DessinerRectangle(image, 25, 25, 71, 36, blanc);
				DessinerRectangle(image, 25, 62, 38, 75, accent);
				DessinerRectangle(image, 58, 62, 71, 75, accent);
				break;
			case TypeBonus.Laser:
				DessinerRectangle(image, 30, 45, 66, 57, blanc);
				DessinerRectangle(image, 36, 29, 43, 46, accent);
				DessinerRectangle(image, 53, 29, 60, 46, accent);
				DessinerDisque(image, 39, 27, 5, blanc);
				DessinerDisque(image, 56, 27, 5, blanc);
				break;
			case TypeBonus.BouclierBas:
				DessinerTriangle(image, 25, 30, 71, 30, 48, 78, blanc);
				DessinerRectangle(image, 29, 27, 67, 52, blanc);
				DessinerLigne(image, 48, 31, 48, 70, 5, accent);
				break;
			case TypeBonus.ScoreDouble:
				DessinerLigne(image, 29, 30, 49, 50, 7, blanc);
				DessinerLigne(image, 49, 30, 29, 50, 7, blanc);
				DessinerDisque(image, 61, 38, 8, blanc);
				DessinerDisque(image, 61, 61, 8, blanc);
				DessinerRectangle(image, 57, 38, 65, 61, blanc);
				break;
			case TypeBonus.BallePercante:
				DessinerTriangle(image, 48, 18, 30, 48, 66, 48, blanc);
				DessinerRectangle(image, 40, 47, 56, 73, blanc);
				DessinerLigne(image, 34, 60, 62, 60, 5, accent);
				break;
			case TypeBonus.Missile:
				DessinerTriangle(image, 48, 16, 38, 40, 58, 40, blanc);
				DessinerRectangle(image, 40, 40, 56, 70, blanc);
				DessinerTriangle(image, 40, 60, 40, 78, 30, 78, accent);
				DessinerTriangle(image, 56, 60, 56, 78, 66, 78, accent);
				DessinerDisque(image, 48, 52, 6, accent);
				break;
			case TypeBonus.Retrecisseur:
				DessinerTriangle(image, 22, 32, 22, 64, 44, 48, blanc);
				DessinerTriangle(image, 74, 32, 74, 64, 52, 48, blanc);
				DessinerRectangle(image, 45, 44, 51, 52, accent);
				break;
			case TypeBonus.Gel:
				DessinerLigne(image, 48, 20, 48, 76, 3, blanc);
				DessinerLigne(image, 24, 34, 72, 62, 3, blanc);
				DessinerLigne(image, 72, 34, 24, 62, 3, blanc);
				DessinerLigne(image, 48, 30, 40, 22, 2, accent);
				DessinerLigne(image, 48, 30, 56, 22, 2, accent);
				DessinerLigne(image, 48, 66, 40, 74, 2, accent);
				DessinerLigne(image, 48, 66, 56, 74, 2, accent);
				break;
			case TypeBonus.Inverseur:
				DessinerTriangle(image, 36, 20, 27, 38, 45, 38, blanc);
				DessinerRectangle(image, 33, 38, 39, 72, blanc);
				DessinerTriangle(image, 60, 76, 51, 58, 69, 58, accent);
				DessinerRectangle(image, 57, 24, 63, 58, accent);
				break;
			case TypeBonus.Accelerateur:
				DessinerLigne(image, 26, 28, 44, 48, 6, blanc);
				DessinerLigne(image, 44, 48, 26, 68, 6, blanc);
				DessinerLigne(image, 48, 28, 66, 48, 6, accent);
				DessinerLigne(image, 66, 48, 48, 68, 6, accent);
				break;
		}

		return ImageTexture.CreateFromImage(image);
	}

	private static void DessinerRectangle(Image image, int xMin, int yMin, int xMax, int yMax, Color couleur)
	{
		for (int y = Mathf.Max(0, yMin); y <= Mathf.Min(image.GetHeight() - 1, yMax); y++)
			for (int x = Mathf.Max(0, xMin); x <= Mathf.Min(image.GetWidth() - 1, xMax); x++)
				image.SetPixel(x, y, couleur);
	}

	private static void DessinerDisque(Image image, int cx, int cy, int rayon, Color couleur)
	{
		int r2 = rayon * rayon;
		for (int y = cy - rayon; y <= cy + rayon; y++)
		{
			for (int x = cx - rayon; x <= cx + rayon; x++)
			{
				if (x < 0 || y < 0 || x >= image.GetWidth() || y >= image.GetHeight())
					continue;
				int dx = x - cx;
				int dy = y - cy;
				if (dx * dx + dy * dy <= r2)
					image.SetPixel(x, y, couleur);
			}
		}
	}

	private static void DessinerAnneau(Image image, int cx, int cy, int rayon, int epaisseur, Color couleur)
	{
		int rMax = rayon * rayon;
		int rMin = Mathf.Max(0, rayon - epaisseur) * Mathf.Max(0, rayon - epaisseur);
		for (int y = cy - rayon; y <= cy + rayon; y++)
		{
			for (int x = cx - rayon; x <= cx + rayon; x++)
			{
				if (x < 0 || y < 0 || x >= image.GetWidth() || y >= image.GetHeight())
					continue;
				int dx = x - cx;
				int dy = y - cy;
				int distance = dx * dx + dy * dy;
				if (distance <= rMax && distance >= rMin)
					image.SetPixel(x, y, couleur);
			}
		}
	}

	private static void DessinerTriangle(Image image, int x1, int y1, int x2, int y2, int x3, int y3, Color couleur)
	{
		int minX = Mathf.Max(0, Mathf.Min(x1, Mathf.Min(x2, x3)));
		int maxX = Mathf.Min(image.GetWidth() - 1, Mathf.Max(x1, Mathf.Max(x2, x3)));
		int minY = Mathf.Max(0, Mathf.Min(y1, Mathf.Min(y2, y3)));
		int maxY = Mathf.Min(image.GetHeight() - 1, Mathf.Max(y1, Mathf.Max(y2, y3)));
		float aire = AireTriangle(x1, y1, x2, y2, x3, y3);

		if (Mathf.IsZeroApprox(aire))
			return;

		for (int y = minY; y <= maxY; y++)
		{
			for (int x = minX; x <= maxX; x++)
			{
				float a = AireTriangle(x, y, x2, y2, x3, y3) / aire;
				float b = AireTriangle(x1, y1, x, y, x3, y3) / aire;
				float c = AireTriangle(x1, y1, x2, y2, x, y) / aire;
				if (a >= 0.0f && b >= 0.0f && c >= 0.0f)
					image.SetPixel(x, y, couleur);
			}
		}
	}

	private static void DessinerLigne(Image image, int x1, int y1, int x2, int y2, int epaisseur, Color couleur)
	{
		int dx = Mathf.Abs(x2 - x1);
		int dy = Mathf.Abs(y2 - y1);
		int sx = x1 < x2 ? 1 : -1;
		int sy = y1 < y2 ? 1 : -1;
		int err = dx - dy;
		int x = x1;
		int y = y1;

		while (true)
		{
			DessinerDisque(image, x, y, epaisseur, couleur);
			if (x == x2 && y == y2)
				break;
			int e2 = 2 * err;
			if (e2 > -dy)
			{
				err -= dy;
				x += sx;
			}
			if (e2 < dx)
			{
				err += dx;
				y += sy;
			}
		}
	}

	private static float AireTriangle(float x1, float y1, float x2, float y2, float x3, float y3)
	{
		return ((y2 - y3) * (x1 - x3) + (x3 - x2) * (y1 - y3));
	}

	private static Color Mix(Color a, Color b, float t)
	{
		return new Color(
			Mathf.Lerp(a.R, b.R, t),
			Mathf.Lerp(a.G, b.G, t),
			Mathf.Lerp(a.B, b.B, t),
			Mathf.Lerp(a.A, b.A, t));
	}
}
