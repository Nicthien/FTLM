using Godot;
using System;
using System.Collections.Generic;

public partial class GameManager : Node3D
{
	private enum Etat
	{
		AttenteLancement,
		EnJeu,
		Pause,
		GameOver,
		Victoire,
	}

	private readonly struct TirageCapsule
	{
		public TirageCapsule(CapsuleScript.TypeBonus type, float poids)
		{
			Type = type;
			Poids = poids;
		}

		public CapsuleScript.TypeBonus Type { get; }
		public float Poids { get; }
	}

	private sealed class BatimentVille
	{
		public BatimentVille(
			StaticBody3D racine,
			MeshInstance3D corps,
			MeshInstance3D lumiere,
			MeshInstance3D antenne,
			CollisionShape3D collision,
			StandardMaterial3D materiauCorps,
			StandardMaterial3D materiauLumiere,
			Vector3 positionBase,
			float largeur,
			float hauteur,
			Color couleurBase,
			Color couleurNeon,
			int ordreDegats)
		{
			Racine = racine;
			Corps = corps;
			Lumiere = lumiere;
			Antenne = antenne;
			Collision = collision;
			MateriauCorps = materiauCorps;
			MateriauLumiere = materiauLumiere;
			PositionBase = positionBase;
			Largeur = largeur;
			Hauteur = hauteur;
			CouleurBase = couleurBase;
			CouleurNeon = couleurNeon;
			OrdreDegats = ordreDegats;
		}

		public StaticBody3D Racine { get; }
		public MeshInstance3D Corps { get; }
		public MeshInstance3D Lumiere { get; }
		public MeshInstance3D Antenne { get; }
		public CollisionShape3D Collision { get; }
		public StandardMaterial3D MateriauCorps { get; }
		public StandardMaterial3D MateriauLumiere { get; }
		public Vector3 PositionBase { get; }
		public float Largeur { get; }
		public float Hauteur { get; }
		public Color CouleurBase { get; }
		public Color CouleurNeon { get; }
		public int OrdreDegats { get; }
		public bool Detruit { get; set; }
	}

	private sealed class Camp
	{
		public Camp(int index, string nom, bool controleIA)
		{
			Index = index;
			Nom = nom;
			ControleIA = controleIA;
		}

		public int Index { get; }
		public string Nom { get; }
		public bool ControleIA { get; set; }
		// Chaque couloir est pivote autour du hub : dans son repere local la barre est
		// toujours "en bas" et attaque vers le hub (sens +1). La rotation gere l'orientation.
		public int SensAttaque => 1;
		public int SensCapsuleVersBarre => -1;
		public string Cle => $"camp{Index}";
		public string ActionGauche = "ui_left";
		public string ActionDroite = "ui_right";
		public string ActionLancement = "lancer_balle";
		public string ActionCapacite = "tirer_capacite";
		// Cyclage de cible : "" = molette souris (J1), sinon touche dediee (J2-J4).
		public string ActionCible = "";
		public bool Elimine;
		public Node3D Arm;
		public Color Couleur = new Color(0.15f, 0.95f, 1.0f);
		public BarScript Bar;
		public Area3D ZoneDegats;
		public Node3D Balles;
		public Node3D Capsules;
		public Node3D Ville;
		public Vector3 PositionBalleRepos;
		public Vector3 PositionVilleLocale;
		public readonly List<BatimentVille> BatimentsVille = new();
		public int Score;
		public int Niveau;
		public int Combo;
		public int MeilleurCombo;
		public int BriquesDetruites;
		public int CapsulesRamassees;
		public float VitesseBalle;
		public double MessageBonusRestant;
		public double AimantRestant;
		public double LaserRestant;
		public double BouclierRestant;
		public double ScoreDoubleRestant;
		public double BallePercanteRestant;
		public double VitesseTemporaireRestante;
		public double LaserCooldownRestant;
		// Temps restant avant le lancement automatique d'une balle qui respawn collee a la
		// barre. > 0 : la balle suit la barre ; 0 : elle est partie (lancee ou auto-lancee).
		public double CollageAutoRestant;
		public StaticBody3D Bouclier;

		// Stock d'objets offensifs (file FIFO de 2 cases) + camp adverse vise.
		public readonly CapsuleScript.TypeBonus?[] Stock = new CapsuleScript.TypeBonus?[2];
		public int IndexCible = -1;
		// Delai avant le prochain lancement d'objet par l'IA.
		public double ObjetCooldownIA;
		// Fleche flottant au-dessus de la barre, pointant vers le camp vise.
		public Node3D FlecheCible;
	}

	private sealed class HudCamp
	{
		public PanelContainer Panel;
		public Label Titre;
		public Label Message;
		public Label Score;
		public Label Vies;
		public Label Briques;
		public Label Balles;
		public Label Combo;
		public Label Etat;
		public readonly Label[] NomsBonus = new Label[NombreLignesBonusHud];
		public readonly Label[] TempsBonus = new Label[NombreLignesBonusHud];
		public readonly ProgressBar[] JaugesBonus = new ProgressBar[NombreLignesBonusHud];
		// Cases du stock d'objets offensifs (2) : cadre + icone.
		public readonly Panel[] CasesObjets = new Panel[2];
		public readonly TextureRect[] CasesIcones = new TextureRect[2];
	}

	private readonly Dictionary<string, AudioStreamPlayer> _sons = new();
	private readonly RandomNumberGenerator _rng = new();
	private readonly List<Camp> _camps = new();
	// Raccourcis vers les deux premiers camps (utilises par le harnais de test, qui
	// suppose la config par defaut a 2 joueurs Joueur/IA).
	private Camp _joueur;
	private Camp _ia;
	private readonly List<HudCamp> _huds = new();

	private Etat _etat;
	private Etat _etatAvantPause;
	private bool _modeTest;
	private Camera3D CameraJeu;
	private Label ScoreLabel;
	private Label ViesLabel;
	private Label NiveauLabel;
	private Label MeilleurLabel;
	private Label MessageLabel;
	private float _echelleHud = 1.0f;
	private PauseMenu PauseMenu;
	private PackedScene BriqueScene;
	private PackedScene BalleScene;
	private PackedScene CapsuleScene;
	private PackedScene ExplosionScene;
	private PackedScene ArmScene;
	private Node3D _bras;
	private Node3D _murs;
	private Node3D _ballesRoot;
	// Amas de briques unique partage au centre (non pivote ; chaque brique porte sa
	// propre rotation). Remplace les anciens conteneurs Briques par bras.
	private Node3D _briquesCentrales;
	private int _briquesRestantes;
	// Index du camp gagnant a la fin (-1 = match nul / aucun survivant) ; sert au panneau
	// de fin de partie. Synchronise vers les clients via le HUD reseau.
	private int _indexGagnant = -1;
	// Overlay de fin de partie (classement stylise) construit a la demande.
	private Control _panneauFin;
	private VBoxContainer _panneauFinPile;
	private bool _finAffichee;
	private PhysicsMaterial _matMurPhysique;
	private StandardMaterial3D _matMurVisuel;

	// Geometrie de l'arene (depend du nombre de joueurs), calculee dans ConstruireArene.
	private static readonly Vector3 Hub = new Vector3(0.0f, 5.0f, 0.0f);
	private const float LongueurBras = 5.5f;
	private float _demiLargeurTerrain = 4.0f;
	private float _facteurLargeur = 1.0f;
	private int _colonnesBriques = 12;
	private float _premierYBriques = 4.5f;

	private static readonly Color[] CouleursCamp =
	{
		new Color(0.15f, 0.95f, 1.0f),
		new Color(1.0f, 0.45f, 0.18f),
		new Color(0.45f, 1.0f, 0.40f),
		new Color(0.95f, 0.45f, 0.95f),
	};

	[Export] public float VitesseBalleBase = 5.0f;
	[Export] public float VitesseBalleMax = 8.0f;
	[Export] public int NombreMaxBallesParCamp = 5;
	[Export] public float ProbaCapsule = 0.3f;
	[Export] public double DureeBonus = 8.0;
	[Export] public double DureeMessageBonus = 1.6;
	[Export] public double CadenceLaser = 0.22;
	[Export] public double DelaiLancementIA = 0.75;
	// Duree max pendant laquelle une balle qui respawn reste collee a la barre du joueur
	// (qui peut la lancer avant) avant de partir toute seule.
	[Export] public double DelaiCollageBalle = 5.0;

	// Bordure d'une case de stock vide (HUD).
	private static readonly Color CouleurCaseVide = new Color(0.22f, 0.28f, 0.34f);

	private const float LargeurTableauScore = 258.0f;
	private const float HauteurTableauScore = 474.0f;
	private const int NombreLignesBonusHud = 5;
	private const int NombreNiveauxParMode = 5;
	private const float VilleExplosionZ = 0.12f;
	private const float VilleBaseY = -0.36f;
	private const float EpaisseurMur = 0.2f;

	private static readonly string[][] Niveaux2Joueurs =
	{
		new[]
		{
			"111111111111",
			"1C11BB11C111",
			"222222222222",
			"1X11XX11X11X",
		},
		new[]
		{
			"1111M11M1111",
			"22C222222C22",
			"11BB1XX1BB11",
			"333333333333",
			"1X111C111X11",
		},
		new[]
		{
			"2S22222222S2",
			"11C1M11M1C11",
			"3333XX333333",
			"1B11B11B11B1",
			"222222222222",
		},
		new[]
		{
			"3X33333333X3",
			"22M22C22M222",
			"111SS11SS111",
			"33BB3333BB33",
			"2C222XX222C2",
			"111111111111",
		},
		new[]
		{
			"3M33X33X33M3",
			"2222C22C2222",
			"33SS3333SS33",
			"1B1X1BB1X1B1",
			"333333333333",
			"2C22M22M22C2",
		},
	};

	private static readonly string[][] Niveaux3Joueurs =
	{
		new[]
		{
			"11111",
			"1C1C1",
			"22222",
			"1X1X1",
		},
		new[]
		{
			"11M11",
			"2C2C2",
			"1BXB1",
			"33333",
			"11111",
		},
		new[]
		{
			"2S1S2",
			"1CXC1",
			"33333",
			"1M1M1",
			"22222",
		},
		new[]
		{
			"3X3X3",
			"2MCM2",
			"11S11",
			"3BBB3",
			"22222",
			"1C1C1",
		},
		new[]
		{
			"3M3M3",
			"2CXC2",
			"33S33",
			"1BXB1",
			"33333",
			"2MCM2",
		},
	};

	private static readonly string[][] Niveaux4Joueurs =
	{
		new[]
		{
			"111",
			"1C1",
			"222",
			"1X1",
		},
		new[]
		{
			"1M1",
			"2C2",
			"BXB",
			"333",
			"111",
		},
		new[]
		{
			"2S2",
			"CXC",
			"333",
			"1M1",
			"222",
		},
		new[]
		{
			"3X3",
			"MCM",
			"1S1",
			"BBB",
			"222",
			"1C1",
		},
		new[]
		{
			"3M3",
			"CXC",
			"3S3",
			"BXB",
			"333",
			"MCM",
		},
	};

	private static readonly Color[] CouleursResistance =
	{
		new Color(0.5f, 0.5f, 0.5f),
		new Color(0.40f, 0.85f, 0.45f),
		new Color(0.95f, 0.65f, 0.25f),
		new Color(0.90f, 0.30f, 0.30f),
	};

	private static readonly TirageCapsule[] TableCapsules =
	{
		new(CapsuleScript.TypeBonus.BarreLarge, 18.0f),
		new(CapsuleScript.TypeBonus.BalleLente, 16.0f),
		new(CapsuleScript.TypeBonus.Aimant, 10.0f),
		new(CapsuleScript.TypeBonus.MultiBalle, 9.0f),
		new(CapsuleScript.TypeBonus.BouclierBas, 8.0f),
		new(CapsuleScript.TypeBonus.ScoreDouble, 8.0f),
		new(CapsuleScript.TypeBonus.VieBonus, 7.0f),
		new(CapsuleScript.TypeBonus.BallePercante, 7.0f),
		new(CapsuleScript.TypeBonus.BarrePetite, 4.0f),
		new(CapsuleScript.TypeBonus.BalleRapide, 4.0f),
		// --- Objets offensifs (stock) ---
		new(CapsuleScript.TypeBonus.Missile, 10.0f),
		new(CapsuleScript.TypeBonus.Laser, 8.0f),
		new(CapsuleScript.TypeBonus.Retrecisseur, 7.0f),
		new(CapsuleScript.TypeBonus.Gel, 6.0f),
		new(CapsuleScript.TypeBonus.Inverseur, 5.0f),
		new(CapsuleScript.TypeBonus.Accelerateur, 5.0f),
	};

	public override void _Ready()
	{
		_modeTest = Array.IndexOf(OS.GetCmdlineArgs(), "--test") >= 0
			|| Array.IndexOf(OS.GetCmdlineUserArgs(), "--test") >= 0;
		ConfigurerSmokeSiDemande();
		InitReseau();

		_rng.Randomize();
		SettingsManager.Charger();
		SettingsManager.Appliquer(GetTree());
		PartieConfig.EnregistrerActions();
		ProcessMode = Node.ProcessModeEnum.Always;

		CameraJeu = GetNode<Camera3D>("Camera3D");
		ScoreLabel = GetNode<Label>("HUD/Control/ScoreLabel");
		ViesLabel = GetNode<Label>("HUD/Control/ViesLabel");
		NiveauLabel = GetNode<Label>("HUD/Control/NiveauLabel");
		MeilleurLabel = GetNode<Label>("HUD/Control/MeilleurLabel");
		MessageLabel = GetNode<Label>("HUD/Control/MessageLabel");
		PauseMenu = GetNode<PauseMenu>("PauseMenu");
		BriqueScene = GD.Load<PackedScene>("res://Brique.tscn");
		BalleScene = GD.Load<PackedScene>("res://Balle.tscn");
		CapsuleScene = GD.Load<PackedScene>("res://Capsule.tscn");
		ExplosionScene = GD.Load<PackedScene>("res://Explosion.tscn");
		ArmScene = GD.Load<PackedScene>("res://Arm.tscn");
		_bras = GetNode<Node3D>("Bras");
		_murs = GetNode<Node3D>("Murs");
		_ballesRoot = new Node3D { Name = "BallesRoot" };
		AddChild(_ballesRoot);
		_briquesCentrales = new Node3D { Name = "BriquesCentrales" };
		AddChild(_briquesCentrales);
		_matMurPhysique = GD.Load<PhysicsMaterial>("res://Physics_Material/Mur.tres");
		_matMurVisuel = CreerMateriauMur();

		ConstruireArene();

		foreach (Camp camp in _camps)
		{
			ChargerVille(camp);
			// Cote client la zone de mort ne doit pas consommer les balles affichees.
			if (!_estClient)
			{
				Camp campLocal = camp;
				camp.ZoneDegats.BodyEntered += (body) => OnZoneDegats(campLocal, body);
			}
		}

		CreerLabelsSupplementaires();
		ChargerSons();
		if (_estClient)
			DemarrerClient();
		else
			NouvellePartie();

		GD.Print($"[FTLM] Partie demarree : {_camps.Count} joueurs.");
		if (_modeTest)
			GD.Print($"[TEST] Mode {_camps.Count} joueurs actif.");
	}

	// Instancie un couloir (Arm.tscn) par joueur, pivote autour du hub, configure son
	// camp, puis genere les murs du contour et place la camera. Le couloir du joueur 1
	// reste a l'identite (= ancienne moitie joueur) ; les autres sont des copies pivotees.
	private void ConstruireArene()
	{
		int nb = Mathf.Clamp(PartieConfig.NombreJoueurs, PartieConfig.MinJoueurs, PartieConfig.MaxJoueurs);
		AppliquerGeometrie(nb);

		_camps.Clear();
		float[] angles = AnglesBras(nb);
		for (int i = 0; i < nb; i++)
		{
			bool ia = PartieConfig.ControleDe(i) == PartieConfig.TypeControle.IA;
			var camp = new Camp(i, $"Joueur {i + 1}", ia)
			{
				Couleur = CouleursCamp[i % CouleursCamp.Length],
			};
			(camp.ActionGauche, camp.ActionDroite, camp.ActionLancement) = PartieConfig.ActionsDe(i);
			camp.ActionCapacite = PartieConfig.ActionCapaciteDe(i);
			camp.ActionCible = PartieConfig.ActionCibleDe(i);

			var arm = ArmScene.Instantiate<Node3D>();
			arm.Name = $"Arm{i}";
			// Pivoter le couloir AVANT de l'ajouter a l'arbre : la raquette est un
			// AnimatableBody3D (sync_to_physics) qui fige sa transform globale a l'entree
			// dans l'arbre ; si on tourne apres, la barre reste a sa position d'origine.
			Basis basis = new Basis(new Vector3(0.0f, 0.0f, 1.0f), Mathf.DegToRad(angles[i]));
			arm.Transform = new Transform3D(basis, Hub - basis * Hub);
			_bras.AddChild(arm);

			camp.Arm = arm;
			camp.Bar = arm.GetNode<BarScript>("Bar");
			camp.ZoneDegats = arm.GetNode<Area3D>("ZoneMort");
			camp.Capsules = arm.GetNode<Node3D>("Capsules");
			// Conteneur des balles hors du bras pivote : un RigidBody3D ne doit pas etre
			// enfant d'un noeud tourne (l'integration physique se fait en espace global).
			camp.Balles = new Node3D { Name = $"Balles{i}" };
			_ballesRoot.AddChild(camp.Balles);
			camp.Ville = arm.GetNode<Node3D>("Ville");
			camp.PositionVilleLocale = camp.Ville.Position;
			camp.PositionBalleRepos = arm.ToGlobal(new Vector3(0.0f, 0.8f, 0.0f));
			camp.Bar.Configurer(camp.ControleIA, camp.SensAttaque, camp.ActionGauche, camp.ActionDroite, _demiLargeurTerrain - 0.1f);
			ConfigurerModeBarreReseau(camp);
			RedimensionnerZoneMort(camp);
			_camps.Add(camp);
		}

		_joueur = _camps[0];
		_ia = _camps.Count > 1 ? _camps[1] : null;

		// Cible initiale = premier adversaire, + fleche de visee au-dessus de la barre.
		foreach (Camp camp in _camps)
		{
			camp.IndexCible = PremierAdversaire(camp);
			if (!camp.ControleIA)
				ConstruireFlecheCible(camp);
		}

		ConstruireMurs(angles);
		PlacerCamera(nb);
	}

	private void AppliquerGeometrie(int nb)
	{
		// _premierYBriques / _colonnesBriques cadrent desormais l'amas CENTRAL partage :
		// la premiere rangee est proche du hub (y=5) et l'amas s'etend vers chaque joueur.
		// En N>2 on garde peu de colonnes pour que les secteurs voisins ne se chevauchent
		// pas pres du hub.
		switch (nb)
		{
			case 2:
				_demiLargeurTerrain = 4.0f;
				_colonnesBriques = 12;
				_premierYBriques = 4.5f;
				break;
			case 3:
				// Couloirs plus etroits pour que les bras (Y) ne se chevauchent pas.
				_demiLargeurTerrain = 2.6f;
				_colonnesBriques = 5;
				_premierYBriques = 4.0f;
				break;
			default:
				// Croix : secteurs a 90 degres. On reduit les colonnes pour que les coins
				// internes des blocs ne franchissent pas la diagonale a 45 degres (sinon
				// chevauchement avec le secteur voisin pres du hub).
				_demiLargeurTerrain = 2.6f;
				_colonnesBriques = 3;
				_premierYBriques = 4.0f;
				break;
		}

		_facteurLargeur = _demiLargeurTerrain / 4.0f;
	}

	private static float[] AnglesBras(int nb) => nb switch
	{
		2 => new[] { 0.0f, 180.0f },
		3 => new[] { 0.0f, 120.0f, 240.0f },
		_ => new[] { 0.0f, 90.0f, 180.0f, 270.0f },
	};

	private void PlacerCamera(int nb)
	{
		Vector3 position;
		Vector2 tailleFond;
		switch (nb)
		{
			case 2:
				position = new Vector3(0.0f, 5.0f, 8.2f);
				tailleFond = new Vector2(8.7f, 11.8f);
				break;
			case 3:
				position = new Vector3(0.0f, 4.0f, 10.8f);
				tailleFond = new Vector2(15.0f, 15.0f);
				break;
			default:
				position = new Vector3(0.0f, 5.0f, 10.8f);
				tailleFond = new Vector2(15.0f, 15.0f);
				break;
		}

		CameraJeu.Position = position;

		MeshInstance3D fond = GetNodeOrNull<MeshInstance3D>("Fond");
		if (fond != null && fond.Mesh is QuadMesh quad)
		{
			var copie = (QuadMesh)quad.Duplicate();
			copie.Size = tailleFond;
			fond.Mesh = copie;
			fond.Position = new Vector3(position.X, position.Y, -0.13f);
		}

		// Oriente la vue pour que le joueur LOCAL (celui qui lance l'appli) soit toujours
		// en bas : on roule la camera (et le fond) autour du hub de l'angle de son bras.
		// No-op en local / cote hote (slot 0, angle 0).
		int slotLocal = Mathf.Clamp(PartieConfig.SlotLocal, 0, nb - 1);
		float angleLocal = AnglesBras(nb)[slotLocal];
		if (!Mathf.IsZeroApprox(angleLocal))
		{
			Basis basis = new Basis(new Vector3(0.0f, 0.0f, 1.0f), Mathf.DegToRad(angleLocal));
			Transform3D pivot = new Transform3D(basis, Hub - basis * Hub);
			CameraJeu.Transform = pivot * CameraJeu.Transform;
			if (fond != null)
				fond.Transform = pivot * fond.Transform;
		}
	}

	private void RedimensionnerZoneMort(Camp camp)
	{
		var collision = camp.ZoneDegats.GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
		if (collision?.Shape is BoxShape3D box)
		{
			var copie = (BoxShape3D)box.Duplicate();
			copie.Size = new Vector3(_demiLargeurTerrain * 2.0f + 0.4f, 0.2f, 0.2f);
			collision.Shape = copie;
		}
	}

	private static StandardMaterial3D CreerMateriauMur()
	{
		var texture = GD.Load<Texture2D>("res://Textures/mur_futuriste.png");
		return new StandardMaterial3D
		{
			AlbedoTexture = texture,
			Metallic = 0.55f,
			Roughness = 0.28f,
		};
	}

	// Construit le contour de l'arene : pour chaque bras, deux murs lateraux reliant le
	// bout exterieur (zone de mort) au coin rentrant partage avec le bras voisin.
	private void ConstruireMurs(float[] angles)
	{
		ViderConteneur(_murs);
		int n = angles.Length;
		Vector2 h = new Vector2(Hub.X, Hub.Y);
		var d = new Vector2[n];
		var l = new Vector2[n];
		for (int i = 0; i < n; i++)
		{
			float a = Mathf.DegToRad(angles[i]);
			d[i] = new Vector2(Mathf.Sin(a), -Mathf.Cos(a)); // direction sortante depuis le hub
			l[i] = new Vector2(-d[i].Y, d[i].X);             // laterale (90 deg CCW)
		}

		var coin = new Vector2[n]; // coin[i] = coin rentrant entre le bras i et le bras i+1
		for (int i = 0; i < n; i++)
		{
			int j = (i + 1) % n;
			coin[i] = IntersectionLignes(h + l[i] * _demiLargeurTerrain, d[i], h - l[j] * _demiLargeurTerrain, d[j]);
		}

		for (int i = 0; i < n; i++)
		{
			Vector2 exterieurGauche = h + d[i] * LongueurBras + l[i] * _demiLargeurTerrain;
			Vector2 exterieurDroit = h + d[i] * LongueurBras - l[i] * _demiLargeurTerrain;
			ConstruireMurSegment(exterieurGauche, coin[i]);
			ConstruireMurSegment(exterieurDroit, coin[(i - 1 + n) % n]);
		}
	}

	private static Vector2 IntersectionLignes(Vector2 p, Vector2 dp, Vector2 q, Vector2 dq)
	{
		float denom = dp.X * dq.Y - dp.Y * dq.X;
		if (Mathf.Abs(denom) < 1e-4f)
			return p; // bras opposes (2 joueurs) : murs colineaires, ils se rejoignent au hub
		float t = ((q.X - p.X) * dq.Y - (q.Y - p.Y) * dq.X) / denom;
		return p + dp * t;
	}

	private void ConstruireMurSegment(Vector2 a, Vector2 b)
	{
		Vector2 milieu = (a + b) * 0.5f;
		Vector2 v = b - a;
		float longueur = v.Length();
		if (longueur < 0.05f)
			return;

		var corps = new StaticBody3D
		{
			Name = "Mur",
			PhysicsMaterialOverride = _matMurPhysique,
			Position = new Vector3(milieu.X, milieu.Y, 0.0f),
			RotationDegrees = new Vector3(0.0f, 0.0f, Mathf.RadToDeg(Mathf.Atan2(v.Y, v.X))),
		};
		var taille = new Vector3(longueur + 0.2f, 0.2f, 0.2f);
		corps.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = taille }, MaterialOverride = _matMurVisuel });
		corps.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = taille } });
		_murs.AddChild(corps);
	}

	public override void _Process(double delta)
	{
		// Client reseau : aucune simulation, on envoie nos entrees et on affiche les snapshots.
		if (_estClient)
		{
			ProcessClient(delta);
			return;
		}

		// Hote : diffuser l'etat tot pour que ce soit independant des retours anticipes plus bas.
		if (_estHote)
		{
			DiffuserSnapshot(delta);
			if (_netTest)
				ProcessNetTest();
		}

		MettreAJourPositionTableauScore();
		MettreAJourIA(delta);

		if (_modeTest)
			ProcessTest();

		if (_modeSmoke)
			ProcessSmoke();

		if (_etat != Etat.Pause)
			foreach (Camp camp in _camps)
				MettreAJourBonusTemporaires(camp, delta);

		if (_etat is Etat.EnJeu or Etat.AttenteLancement)
		{
			MettreAJourCollageAuto(delta);
			SurveillerBallesEchappees();
			MettreAJourFleches();
		}

		if (_etat == Etat.Pause && PauseMenu.OptionsOuvertes)
			return;

		if (Input.IsActionJustPressed("ui_cancel")
			&& _etat is Etat.EnJeu or Etat.AttenteLancement or Etat.Pause)
		{
			BasculerPause();
			return;
		}

		// Redemarrage apres une fin de partie : n'importe quelle action de lancement humaine.
		if (_etat is Etat.GameOver or Etat.Victoire)
		{
			if (UnHumainALanceDemande())
				NouvellePartie();
			return;
		}

		if (_etat is not (Etat.EnJeu or Etat.AttenteLancement))
			return;

		// Chaque humain local lance/relance avec SA propre action (J1 = Espace, J2-J4 = leurs touches).
		foreach (Camp camp in _camps)
		{
			if (camp.ControleIA || camp.Elimine || !EstControleLocalement(camp))
				continue;

			if (Input.IsActionJustPressed(camp.ActionLancement))
				GererLancementCamp(camp);

			if (Input.IsActionJustPressed(camp.ActionCapacite))
				GererCapaciteCamp(camp);

			// Cyclage de cible : J2-J4 via leur touche dediee (J1 = molette, gere dans _UnhandledInput).
			if (!string.IsNullOrEmpty(camp.ActionCible) && Input.IsActionJustPressed(camp.ActionCible))
				CyclerCible(camp, 1);
		}
	}

	// Molette souris : cycle la cible du joueur a la souris (Joueur 1 local). En reseau,
	// le client envoie la demande a l'hote (qui est autoritaire sur la cible).
	public override void _UnhandledInput(InputEvent ev)
	{
		if (_etat is not (Etat.EnJeu or Etat.AttenteLancement))
			return;
		if (ev is not InputEventMouseButton { Pressed: true } mb)
			return;

		int sens = mb.ButtonIndex switch
		{
			MouseButton.WheelUp => 1,
			MouseButton.WheelDown => -1,
			_ => 0,
		};
		if (sens == 0)
			return;

		if (_estClient)
		{
			RpcId(1, MethodName.HoteRecevoirCible, sens);
			return;
		}

		Camp local = CampSouris();
		if (local != null)
			CyclerCible(local, sens);
	}

	// Action de lancement (clic gauche / Espace) : envoie ou relance la balle, rien d'autre.
	private void GererLancementCamp(Camp camp)
	{
		if (_etat == Etat.AttenteLancement)
		{
			LancerCamp(camp);
			_etat = Etat.EnJeu;
			MessageLabel.Visible = false;
			return;
		}

		if (LancerBallesPretes(camp))
		{
			MessageLabel.Visible = false;
			AfficherBonus(camp, "Balle relancee");
		}
	}

	// Action de capacite (clic droit / touche dediee) : lance l'objet en tete de stock
	// vers le camp vise.
	private void GererCapaciteCamp(Camp camp)
	{
		if (_etat != Etat.EnJeu)
			return;

		LancerObjetStock(camp);
	}

	// ----------------------------------------------- Stock d'objets offensifs & ciblage

	private static bool StockPlein(Camp camp) => camp.Stock[0].HasValue && camp.Stock[1].HasValue;

	// Vide le stock d'un camp et rafraichit ses cases HUD.
	private void ViderStock(Camp camp)
	{
		for (int i = 0; i < camp.Stock.Length; i++)
			camp.Stock[i] = null;
		camp.ObjetCooldownIA = 0.0;
		camp.IndexCible = PremierAdversaire(camp);
		MettreAJourCasesObjets(camp);
	}

	// Retire les projectiles d'objets encore en vol (entre deux parties).
	private void SupprimerProjectiles()
	{
		if (_ballesRoot == null)
			return;
		foreach (Node n in _ballesRoot.GetChildren())
			if (n is ObjetProjectile proj && !proj.IsQueuedForDeletion())
				proj.QueueFree();
	}

	// Range un objet dans la premiere case libre. Renvoie false si le stock est plein.
	private bool AjouterObjet(Camp camp, CapsuleScript.TypeBonus type)
	{
		for (int i = 0; i < camp.Stock.Length; i++)
		{
			if (!camp.Stock[i].HasValue)
			{
				camp.Stock[i] = type;
				MettreAJourCasesObjets(camp);
				return true;
			}
		}
		return false;
	}

	// Retire l'objet en tete de file (les suivants avancent d'un cran).
	private CapsuleScript.TypeBonus? RetirerPremierObjet(Camp camp)
	{
		CapsuleScript.TypeBonus? premier = camp.Stock[0];
		if (!premier.HasValue)
			return null;
		for (int i = 1; i < camp.Stock.Length; i++)
			camp.Stock[i - 1] = camp.Stock[i];
		camp.Stock[camp.Stock.Length - 1] = null;
		MettreAJourCasesObjets(camp);
		return premier;
	}

	private static string NomObjet(CapsuleScript.TypeBonus type) => type switch
	{
		CapsuleScript.TypeBonus.Missile => "Missile",
		CapsuleScript.TypeBonus.Laser => "Laser",
		CapsuleScript.TypeBonus.Retrecisseur => "Retrecisseur",
		CapsuleScript.TypeBonus.Gel => "Gel",
		CapsuleScript.TypeBonus.Inverseur => "Inverseur",
		CapsuleScript.TypeBonus.Accelerateur => "Accelerateur",
		_ => type.ToString(),
	};

	// Index du premier camp adverse encore en jeu (cible par defaut), ou -1.
	private int PremierAdversaire(Camp camp)
	{
		foreach (Camp autre in _camps)
			if (autre != camp && !autre.Elimine)
				return autre.Index;
		return -1;
	}

	// Camp vise, revalide : si la cible est eliminee/invalide, on reprend le 1er adversaire.
	private Camp CampCibleDe(Camp camp)
	{
		if (camp.IndexCible >= 0 && camp.IndexCible < _camps.Count)
		{
			Camp c = _camps[camp.IndexCible];
			if (c != camp && !c.Elimine)
				return c;
		}
		camp.IndexCible = PremierAdversaire(camp);
		return camp.IndexCible >= 0 ? _camps[camp.IndexCible] : null;
	}

	// Fait defiler la cible parmi les camps adverses encore en jeu.
	private void CyclerCible(Camp camp, int sens)
	{
		int n = _camps.Count;
		if (n <= 1)
			return;
		int depart = camp.IndexCible >= 0 ? camp.IndexCible : camp.Index;
		for (int pas = 1; pas <= n; pas++)
		{
			int idx = (((depart + sens * pas) % n) + n) % n;
			Camp c = _camps[idx];
			if (c != camp && !c.Elimine)
			{
				camp.IndexCible = idx;
				return;
			}
		}
	}

	// Camp pilote a la souris sur cette machine (Joueur 1 = action ui_left).
	private Camp CampSouris()
	{
		foreach (Camp camp in _camps)
			if (!camp.ControleIA && !camp.Elimine && EstControleLocalement(camp) && camp.ActionGauche == "ui_left")
				return camp;
		return null;
	}

	private static Vector3 PositionVilleMonde(Camp camp)
	{
		if (camp.Ville != null && IsInstanceValid(camp.Ville))
			return camp.Ville.GlobalPosition;
		return camp.Bar != null && IsInstanceValid(camp.Bar) ? camp.Bar.GlobalPosition : Vector3.Zero;
	}

	// Lance l'objet en tete de stock vers le camp vise (projectile homing).
	private void LancerObjetStock(Camp camp)
	{
		if (camp.Elimine)
			return;

		Camp cible = CampCibleDe(camp);
		if (cible == null)
			return;

		CapsuleScript.TypeBonus? type = RetirerPremierObjet(camp);
		if (!type.HasValue)
			return;

		Vector3 depart = camp.Bar.GlobalPosition;
		Vector3 arrivee = PositionVilleMonde(cible);
		CreerProjectile(camp, cible, type.Value, depart, arrivee, true);
		Jouer("bonus");
		AfficherBonus(camp, $"{NomObjet(type.Value)} -> {cible.Nom}");
		if (_modeTest)
			GD.Print($"[TEST] Objet lance {camp.Nom} -> {cible.Nom} : {type.Value}");
	}

	private ObjetProjectile CreerProjectile(Camp source, Camp cible, CapsuleScript.TypeBonus type, Vector3 depart, Vector3 arrivee, bool avecEffet)
	{
		var proj = new ObjetProjectile
		{
			Name = $"Objet_{source.Nom}",
			Type = type,
			CampSource = source.Index,
			CampCible = cible?.Index ?? -1,
			CiblePos = arrivee,
			ModeAffichage = !avecEffet,
			Vitesse = 7.5f,
		};
		_ballesRoot.AddChild(proj);
		proj.GlobalPosition = depart;
		proj.IdReseau = ++_compteurIdMobile;
		ConstruireVisuelProjectile(proj, source.Couleur);

		if (avecEffet)
			proj.Arrivee = () => AppliquerEffetObjet(source, cible, type);
		return proj;
	}

	private static void ConstruireVisuelProjectile(ObjetProjectile proj, Color couleur)
	{
		proj.AddChild(new MeshInstance3D
		{
			Name = "Coeur",
			Mesh = new SphereMesh { Radius = 0.16f, Height = 0.32f, RadialSegments = 16, Rings = 8 },
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = couleur.Lerp(Colors.White, 0.2f),
				EmissionEnabled = true,
				Emission = couleur,
				EmissionEnergyMultiplier = 2.4f,
			},
		});
		proj.AddChild(new MeshInstance3D
		{
			Name = "Halo",
			Mesh = new SphereMesh { Radius = 0.26f, Height = 0.52f, RadialSegments = 16, Rings = 8 },
			MaterialOverride = new StandardMaterial3D
			{
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				AlbedoColor = new Color(couleur.R, couleur.G, couleur.B, 0.22f),
				EmissionEnabled = true,
				Emission = couleur,
				EmissionEnergyMultiplier = 0.8f,
				CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			},
		});
	}

	// Effet d'un objet a l'arrivee sur le camp adverse.
	private void AppliquerEffetObjet(Camp source, Camp cible, CapsuleScript.TypeBonus type)
	{
		if (cible == null || cible.Elimine || _etat != Etat.EnJeu)
			return;

		switch (type)
		{
			case CapsuleScript.TypeBonus.Missile:
			case CapsuleScript.TypeBonus.Laser:
				FrapperVilleAdverse(source, cible);
				break;
			case CapsuleScript.TypeBonus.Retrecisseur:
				cible.Bar?.Redimensionner(0.55f, DureeBonus);
				AfficherBonus(cible, "Barre retrecie !");
				break;
			case CapsuleScript.TypeBonus.Gel:
				cible.Bar?.Geler(1.6);
				AfficherBonus(cible, "Gele !");
				break;
			case CapsuleScript.TypeBonus.Inverseur:
				cible.Bar?.InverserControles(3.0);
				AfficherBonus(cible, "Controles inverses !");
				break;
			case CapsuleScript.TypeBonus.Accelerateur:
				ChangerVitesseBalles(cible, Mathf.Min(VitesseBalleMax, VitesseBalleBase * 1.4f), DureeBonus);
				AfficherBonus(cible, "Balles accelerees !");
				break;
		}
		Jouer("casse");
	}

	// Detruit un batiment de la ville adverse et credite l'attaquant.
	private void FrapperVilleAdverse(Camp source, Camp cible)
	{
		BatimentVille batiment = PremierBatimentIntact(cible);
		if (batiment == null)
			return;

		DetruireBatiment(cible, batiment, null);
		if (!source.Elimine)
		{
			source.Score += 200 * CalculerMultiplicateurScore(source);
			source.Combo++;
			MettreAJourHud();
		}
	}

	private static BatimentVille PremierBatimentIntact(Camp camp)
	{
		foreach (BatimentVille b in camp.BatimentsVille)
			if (!b.Detruit)
				return b;
		return null;
	}

	private void ConstruireFlecheCible(Camp camp)
	{
		var fleche = new Node3D { Name = $"FlecheCible{camp.Index}", Visible = false };
		_ballesRoot.AddChild(fleche);

		var mat = new StandardMaterial3D
		{
			AlbedoColor = new Color(camp.Couleur.R, camp.Couleur.G, camp.Couleur.B, 0.92f),
			EmissionEnabled = true,
			Emission = camp.Couleur,
			EmissionEnergyMultiplier = 2.2f,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		};

		// Hampe + pointe orientees vers +X local (la fleche tourne autour de Z).
		fleche.AddChild(new MeshInstance3D
		{
			Name = "Hampe",
			Mesh = new BoxMesh { Size = new Vector3(0.32f, 0.05f, 0.02f) },
			MaterialOverride = mat,
		});
		fleche.AddChild(new MeshInstance3D
		{
			Name = "Pointe",
			Mesh = new CylinderMesh { TopRadius = 0.0f, BottomRadius = 0.11f, Height = 0.18f, RadialSegments = 4 },
			Position = new Vector3(0.25f, 0.0f, 0.0f),
			Rotation = new Vector3(0.0f, 0.0f, -Mathf.Pi / 2.0f),
			MaterialOverride = mat,
		});
		camp.FlecheCible = fleche;
	}

	// Oriente chaque fleche vers le camp vise, un peu en avant de la barre.
	private void MettreAJourFleches()
	{
		foreach (Camp camp in _camps)
		{
			Node3D fleche = camp.FlecheCible;
			if (fleche == null || !IsInstanceValid(fleche))
				continue;

			if (camp.Elimine || camp.Bar == null || !IsInstanceValid(camp.Bar))
			{
				fleche.Visible = false;
				continue;
			}

			Camp cible = CampCibleDe(camp);
			if (cible == null)
			{
				fleche.Visible = false;
				continue;
			}

			Vector3 origine = camp.Bar.GlobalPosition;
			Vector3 vers = PositionVilleMonde(cible) - origine;
			float ang = Mathf.Atan2(vers.Y, vers.X);
			var dir2 = new Vector2(vers.X, vers.Y);
			if (dir2.LengthSquared() > 0.0001f)
				dir2 = dir2.Normalized();
			Vector3 pos = origine + new Vector3(dir2.X, dir2.Y, 0.0f) * 0.55f;
			pos.Z = origine.Z - 0.25f;
			fleche.GlobalPosition = pos;
			fleche.Rotation = new Vector3(0.0f, 0.0f, ang);
			fleche.Visible = true;
		}
	}

	// Vrai si au moins un humain local demande un redemarrage (sa touche de lancement).
	private bool UnHumainALanceDemande()
	{
		foreach (Camp camp in _camps)
			if (!camp.ControleIA && EstControleLocalement(camp) && Input.IsActionJustPressed(camp.ActionLancement))
				return true;
		return Input.IsActionJustPressed("lancer_balle");
	}

	// Vrai si ce camp est pilote par le clavier/souris de CETTE machine. En local,
	// tous les humains le sont ; en reseau seul le slot local de cette machine l'est.
	private bool EstControleLocalement(Camp camp)
	{
		if (PartieConfig.Mode == PartieConfig.ModePartie.Local)
			return true;
		return camp.Index == PartieConfig.SlotLocal;
	}

	private void BasculerPause()
	{
		if (_etat == Etat.Pause)
		{
			ReprendrePartie();
			return;
		}

		_etatAvantPause = _etat;
		_etat = Etat.Pause;
		MessageLabel.Visible = false;
		PauseMenu.Ouvrir();
		GetTree().Paused = true;
	}

	public void ReprendrePartie()
	{
		GetTree().Paused = false;
		PauseMenu.Fermer();
		_etat = _etatAvantPause;
		MessageLabel.Visible = _etat == Etat.AttenteLancement;
		if (_etat == Etat.AttenteLancement)
			MessageLabel.Text = "Espace pour lancer";
	}

	public void NouvellePartieDepuisMenu()
	{
		GetTree().Paused = false;
		PauseMenu.Fermer();
		NouvellePartie();
	}

	public void RetourAccueil()
	{
		GetTree().Paused = false;
		PauseMenu.Fermer();
		NetworkSession.Instance?.Fermer();
		GetTree().ChangeSceneToFile("res://MainMenu.tscn");
	}

	private void NouvellePartie()
	{
		foreach (Camp camp in _camps)
		{
			camp.Score = 0;
			camp.Niveau = 1;
			camp.Combo = 0;
			camp.MeilleurCombo = 0;
			camp.BriquesDetruites = 0;
			camp.CapsulesRamassees = 0;
			camp.Elimine = false;
			camp.VitesseBalle = VitesseBalleBase;
			camp.Bar.Position = new Vector3(0.0f, camp.Bar.Position.Y, camp.Bar.Position.Z);
			camp.Bar.Visible = true;
			ActiverCollisionBarre(camp, true);
			ReinitialiserBonusTemporaires(camp);
			ReparerTousLesBatiments(camp);
			ViderStock(camp);
			PreparerLancement(camp);
		}

		SupprimerProjectiles();
		_indexGagnant = -1;
		GenererBriquesCentrales();
		DiffuserChargerNiveau(_joueur?.Niveau ?? 1, true);
		_etat = Etat.AttenteLancement;
		MessageLabel.Text = "Espace pour lancer";
		MessageLabel.Visible = true;
		MettreAJourHud();
	}

	private void PreparerLancement(Camp camp)
	{
		ViderConteneur(camp.Balles);
		ViderConteneur(camp.Capsules);
		camp.Bar.Redimensionner(1.0f, 0.0);
		camp.Combo = 0;
		camp.VitesseBalle = VitesseBalleBase;
		ReinitialiserBonusTemporaires(camp);
		CreerBalleAuRepos(camp);
	}

	private bool LancerCamp(Camp camp)
	{
		camp.CollageAutoRestant = 0.0;
		bool lance = false;
		foreach (Node n in camp.Balles.GetChildren())
		{
			if (n is BalleScript b)
			{
				DefinirContactBalle(b, camp);
				b.Lancer();
				lance = true;
			}
		}

		return lance;
	}

	private bool LancerBallesPretes(Camp camp)
	{
		camp.CollageAutoRestant = 0.0;
		bool relance = false;
		foreach (Node n in camp.Balles.GetChildren())
		{
			if (n is not BalleScript b || b.IsQueuedForDeletion())
				continue;

			DefinirContactBalle(b, camp);
			if (b.EstCollee)
			{
				b.LancerDepuisBarre(_rng.RandfRange(-0.45f, 0.45f));
				relance = true;
			}
			else if (b.LinearVelocity.Length() <= 0.01f)
			{
				b.Lancer();
				relance = true;
			}
		}
		return relance;
	}

	// Construit l'amas central partage : le motif est replique une fois par direction de
	// joueur, tourne autour du hub (symetrie radiale). Toutes les briques vivent dans le
	// conteneur non pivote _briquesCentrales et portent leur propre rotation, si bien
	// qu'elles font face a leur joueur et que les briques Mobile oscillent sur le bon axe.
	private void GenererBriquesCentrales()
	{
		ViderConteneur(_briquesCentrales);
		_briquesParId.Clear();
		_briquesRestantes = 0;
		int idBrique = 0;

		string[] motif = ChoisirMotifNiveau();
		int rangees = motif.Length;
		int colonnesMotif = 0;
		foreach (string ligne in motif)
			colonnesMotif = Mathf.Max(colonnesMotif, ligne.Length);

		// On garde une tranche centree du motif selon la largeur disponible pres du hub.
		int colonnes = Mathf.Clamp(_colonnesBriques, 1, colonnesMotif);
		int debutColonne = (colonnesMotif - colonnes) / 2;

		const float pasX = 0.6f;
		const float pasY = 0.32f;
		float debutX = -(colonnes - 1) * pasX / 2.0f;
		float premierY = _premierYBriques;

		int nb = Mathf.Clamp(PartieConfig.NombreJoueurs, PartieConfig.MinJoueurs, PartieConfig.MaxJoueurs);
		foreach (float angle in AnglesBras(nb))
		{
			Basis basis = new Basis(new Vector3(0.0f, 0.0f, 1.0f), Mathf.DegToRad(angle));
			// Meme rotation que les bras : un point "local bras" p donne en global
			// Hub + basis * (p - Hub).
			var pivot = new Transform3D(basis, Hub - basis * Hub);

			for (int rang = 0; rang < rangees; rang++)
			{
				string ligne = motif[rang];
				float y = premierY - rang * pasY;
				for (int c = 0; c < colonnes; c++)
				{
					int col = debutColonne + c;
					if (col >= ligne.Length)
						continue;

					if (!LireBrique(ligne[col], out int resistance, out BriqueScript.TypeBrique type, out int points, out Color couleur))
						continue;

					var brique = BriqueScene.Instantiate<BriqueScript>();
					_briquesCentrales.AddChild(brique);
					brique.Transform = pivot * new Transform3D(Basis.Identity, new Vector3(debutX + c * pasX, y, 0.0f));
					brique.Points = points;
					brique.Initialiser(resistance, couleur, type);
					// Identifiant deterministe (meme ordre de generation hote/client) pour le reseau.
					brique.IdReseau = idBrique;
					_briquesParId[idBrique] = brique;
					idBrique++;
					if (brique.EstDestructible)
						_briquesRestantes++;
				}
			}
		}
	}

	private string[] ChoisirMotifNiveau()
	{
		string[][] banque = BanqueNiveauxPourMode();
		int niveau = Math.Max(1, _joueur?.Niveau ?? 1);
		int index = Math.Clamp(niveau - 1, 0, banque.Length - 1);
		return banque[index];
	}

	private static string[][] BanqueNiveauxPourMode()
	{
		return Mathf.Clamp(PartieConfig.NombreJoueurs, PartieConfig.MinJoueurs, PartieConfig.MaxJoueurs) switch
		{
			2 => Niveaux2Joueurs,
			3 => Niveaux3Joueurs,
			_ => Niveaux4Joueurs,
		};
	}

	private static bool LireBrique(char c, out int resistance, out BriqueScript.TypeBrique type, out int points, out Color couleur)
	{
		resistance = 1;
		type = BriqueScript.TypeBrique.Normale;
		points = 10;
		couleur = CouleursResistance[1];

		if (c >= '1' && c <= '9')
		{
			resistance = c - '0';
			points = resistance * 10;
			couleur = CouleursResistance[Mathf.Min(resistance, CouleursResistance.Length - 1)];
			return true;
		}

		switch (c)
		{
			case 'S':
				type = BriqueScript.TypeBrique.Solide;
				points = 0;
				couleur = new Color(0.42f, 0.45f, 0.50f);
				return true;
			case 'X':
				type = BriqueScript.TypeBrique.Explosive;
				points = 40;
				couleur = new Color(1.0f, 0.32f, 0.22f);
				return true;
			case 'C':
				type = BriqueScript.TypeBrique.CapsuleGarantie;
				points = 25;
				couleur = new Color(0.30f, 0.85f, 1.0f);
				return true;
			case 'M':
				type = BriqueScript.TypeBrique.Mobile;
				points = 35;
				couleur = new Color(0.92f, 0.42f, 1.0f);
				return true;
			case 'B':
				type = BriqueScript.TypeBrique.BonusScore;
				resistance = 2;
				points = 100;
				couleur = new Color(1.0f, 0.88f, 0.28f);
				return true;
			default:
				return false;
		}
	}

	// Cree une balle au repos collee a la barre du joueur : elle suit la barre pendant
	// DelaiCollageBalle secondes (le joueur peut la lancer avant avec sa touche), puis elle
	// part toute seule. Utilisee a chaque respawn (debut de partie, changement de niveau,
	// relance apres une balle perdue).
	private BalleScript CreerBalleAuRepos(Camp camp)
	{
		BalleScript balle = CreerBalle(camp, camp.PositionBalleRepos);
		if (IsInstanceValid(camp.Bar))
			balle.CollerA(camp.Bar);
		camp.CollageAutoRestant = DelaiCollageBalle;
		return balle;
	}

	private BalleScript CreerBalle(Camp camp, Vector3 position)
	{
		var balle = BalleScene.Instantiate<BalleScript>();
		camp.Balles.AddChild(balle);
		balle.IdReseau = ++_compteurIdMobile;
		balle.VitesseCible = camp.VitesseBalle;
		balle.Percante = camp.BallePercanteRestant > 0.0;
		balle.Positionner(position);
		DefinirContactBalle(balle, camp);
		balle.BodyEntered += (body) => OnBalleCollision(balle, body);
		return balle;
	}

	private int CompterBalles(Camp camp)
	{
		int n = 0;
		foreach (Node enfant in camp.Balles.GetChildren())
			if (enfant is BalleScript b && !b.IsQueuedForDeletion())
				n++;
		return n;
	}

	private Camp CampParCle(string cle)
	{
		foreach (Camp camp in _camps)
			if (camp.Cle == cle)
				return camp;
		return _joueur;
	}

	private Camp CampParBarre(BarScript bar)
	{
		foreach (Camp camp in _camps)
			if (camp.Bar == bar)
				return camp;
		return _joueur;
	}

	private void DefinirContactBalle(BalleScript balle, Camp camp)
	{
		if (balle == null || camp == null || !IsInstanceValid(balle))
			return;

		balle.Proprietaire = camp.Cle;
		balle.DestinataireBonus = camp.Cle;
		balle.DefinirAxes(camp.Bar.GlobalTransform.Basis.Y, camp.Bar.GlobalTransform.Basis.X);
		if (balle.GetParent() != camp.Balles)
			CallDeferred(nameof(RattacherBalleAuConteneur), balle, camp.Balles);
	}

	private void RattacherBalleAuConteneur(Node balleNode, Node nouveauParent)
	{
		if (balleNode is not BalleScript balle
			|| !IsInstanceValid(balle)
			|| balle.IsQueuedForDeletion()
			|| !IsInstanceValid(nouveauParent)
			|| balle.GetParent() == nouveauParent)
			return;

		balle.Reparent(nouveauParent, true);
	}

	private void OnBalleCollision(BalleScript balle, Node body)
	{
		if (TrouverBatiment(body, out Camp campVille, out BatimentVille batiment))
		{
			if (BouclierActif(campVille))
			{
				RepousserBalleParBouclier(campVille, balle);
				Jouer("rebond");
				return;
			}

			DetruireBatiment(campVille, batiment, balle);
			return;
		}

		if (body.IsInGroup("briques") && body is BriqueScript brique && !brique.IsQueuedForDeletion())
		{
			Camp proprietaire = balle != null ? CampParCle(balle.DestinataireBonus) : _joueur;
			FrapperBrique(proprietaire, brique, balle);
			if (balle != null && balle.Percante)
				GetTree().CreateTimer(0.01).Timeout += () =>
				{
					if (IsInstanceValid(balle))
						balle.GarderDirectionPercante();
				};
			return;
		}

		if (body is BarScript bar)
		{
			Camp campBarre = CampParBarre(bar);
			if (balle != null)
				DefinirContactBalle(balle, campBarre);

			if (campBarre.AimantRestant > 0.0 && balle != null)
			{
				balle.CollerA(bar);
				AfficherBonus(campBarre, "Aimant pret");
			}
			else if (balle != null)
			{
				Vector3 lateral = bar.GlobalTransform.Basis.X.Normalized();
				float offset = (balle.GlobalPosition - bar.GlobalPosition).Dot(lateral) / bar.DemiLargeur;
				balle.RebondSurBarre(offset);
			}
			Jouer("rebond");
		}
		else if (body is StaticBody3D)
		{
			Jouer("rebond");
		}
	}

	private bool TrouverBatiment(Node body, out Camp camp, out BatimentVille batiment)
	{
		foreach (Camp c in _camps)
		{
			foreach (BatimentVille b in c.BatimentsVille)
			{
				if (body == b.Racine)
				{
					camp = c;
					batiment = b;
					return true;
				}
			}
		}

		camp = null;
		batiment = null;
		return false;
	}

	private void DetruireBatiment(Camp campVille, BatimentVille batiment, BalleScript balle)
	{
		if (batiment.Detruit)
			return;

		batiment.Detruit = true;
		batiment.Collision.Disabled = true;
		AppliquerEtatBatiment(batiment, 3);
		DiffuserBatimentDetruit(campVille.Index, campVille.BatimentsVille.IndexOf(batiment));

		// L'attaquant est le proprietaire de la balle (avec N>2 il n'y a plus d'oppose unique).
		// Une ville ne marque pas en se detruisant elle-meme.
		Camp attaquant = balle != null ? CampParCle(balle.Proprietaire) : campVille;
		if (attaquant != null && attaquant != campVille)
		{
			attaquant.Score += 250 * CalculerMultiplicateurScore(attaquant);
			attaquant.Combo++;
		}
		Jouer("casse");
		Vector3 position = ChoisirPositionExplosionBatiment(batiment);
		ExploserVille(position, new Color(1.0f, 0.35f, 0.08f), 1.05f, true);

		if (balle != null && IsInstanceValid(balle))
			balle.QueueFree();

		MettreAJourHud();

		if (CompterBatimentsRestants(campVille) <= 0)
		{
			EliminerCamp(campVille);
			return;
		}

		AssurerBallesMinimumPlateau();
		MettreAJourHud();
	}

	// Marque un camp comme elimine (ville rasee), retire ses balles et masque sa barre.
	// Si un seul camp survit, la partie se termine.
	private void EliminerCamp(Camp camp)
	{
		if (camp.Elimine)
			return;

		camp.Elimine = true;
		ViderConteneur(camp.Balles);
		ViderConteneur(camp.Capsules);
		ReinitialiserBonusTemporaires(camp);
		if (IsInstanceValid(camp.Bar))
		{
			camp.Bar.Visible = false;
			ActiverCollisionBarre(camp, false);
		}

		var survivants = new List<Camp>();
		foreach (Camp c in _camps)
			if (!c.Elimine)
				survivants.Add(c);

		if (survivants.Count <= 1)
		{
			FinPartie(survivants.Count == 1 ? survivants[0] : null);
			return;
		}

		AssurerBallesMinimumPlateau();
		MettreAJourHud();
	}

	private void FrapperBrique(Camp proprietaire, BriqueScript brique, BalleScript balle = null, bool destructionDirecte = false, bool verifierOuverture = true)
	{
		if (brique.IsQueuedForDeletion())
			return;

		if (!brique.EstDestructible)
		{
			Jouer("rebond");
			return;
		}

		Vector3 pos = brique.GlobalPosition;
		Color couleur = brique.CouleurBase;
		BriqueScript.TypeBrique type = brique.TypeSpecial;
		int points = brique.Points;
		int idBrique = brique.IdReseau;
		bool detruite = destructionDirecte;

		if (destructionDirecte)
			brique.QueueFree();
		else
			detruite = brique.Frapper();

		if (!detruite)
		{
			DiffuserBriqueFrappee(idBrique, brique.Resistance);
			Jouer("rebond");
			return;
		}

		_briquesParId.Remove(idBrique);
		DiffuserDespawnBrique(idBrique);
		EnregistrerBriqueDetruite(proprietaire, points);
		Jouer("casse");
		float tailleExplosion = type == BriqueScript.TypeBrique.Explosive ? 1.35f : 1.0f;
		Color couleurExplosion = type == BriqueScript.TypeBrique.Explosive ? new Color(1.0f, 0.24f, 0.08f) : couleur;
		Exploser(pos, couleurExplosion, tailleExplosion);

		if (type == BriqueScript.TypeBrique.CapsuleGarantie)
			LacherCapsule(proprietaire, pos, TirerTypeCapsule(proprietaire));
		else
			PeutEtreLacherCapsule(proprietaire, pos);

		if (type == BriqueScript.TypeBrique.Explosive)
			DetruireBriquesVoisines(proprietaire, pos);

		if (verifierOuverture && _briquesRestantes <= 0)
		{
			AfficherBonus(proprietaire, $"{proprietaire.Nom} a vide l'amas central");
			NiveauTermine(proprietaire);
			MettreAJourHud();
		}
	}

	private static void ActiverCollisionBarre(Camp camp, bool actif)
	{
		var collision = camp.Bar?.GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
		if (collision != null)
			collision.Disabled = !actif;
	}

	private void EnregistrerBriqueDetruite(Camp proprietaire, int points)
	{
		proprietaire.Combo++;
		proprietaire.MeilleurCombo = Mathf.Max(proprietaire.MeilleurCombo, proprietaire.Combo);
		proprietaire.BriquesDetruites++;
		_briquesRestantes--;
		proprietaire.Score += points * CalculerMultiplicateurScore(proprietaire);
		MettreAJourHud();
	}

	private void NiveauTermine(Camp vainqueurNiveau = null)
	{
		if ((vainqueurNiveau?.Niveau ?? _joueur?.Niveau ?? 1) >= NombreNiveauxParMode)
		{
			FinPartie(vainqueurNiveau ?? _joueur);
			return;
		}

		foreach (Camp camp in _camps)
		{
			if (camp.Elimine)
				continue;

			camp.Niveau++;
			camp.Combo = 0;
			camp.VitesseBalle = CalculerVitesseNiveau(camp);
			ReinitialiserBonusTemporaires(camp);
			ViderConteneur(camp.Capsules);
			ViderConteneur(camp.Balles);
			CreerBalleAuRepos(camp);
		}

		GenererBriquesCentrales();
		DiffuserChargerNiveau(_joueur?.Niveau ?? 1, false);
		Jouer("niveau");
		_etat = Etat.AttenteLancement;
		MessageLabel.Text = $"Niveau {_joueur.Niveau}/{NombreNiveauxParMode} - Espace pour lancer";
		MessageLabel.Visible = true;
		MettreAJourHud();
	}

	private float CalculerVitesseNiveau(Camp camp)
	{
		return Mathf.Min(VitesseBalleMax, VitesseBalleBase + Math.Max(0, camp.Niveau - 1) * 0.22f);
	}

	private void DetruireBriquesVoisines(Camp proprietaire, Vector3 centre)
	{
		List<BriqueScript> voisines = new();
		foreach (Node n in _briquesCentrales.GetChildren())
		{
			if (n is BriqueScript b
				&& !b.IsQueuedForDeletion()
				&& b.EstDestructible
				&& b.GlobalPosition.DistanceTo(centre) <= 0.75f)
			{
				voisines.Add(b);
			}
		}

		foreach (BriqueScript b in voisines)
			FrapperBrique(proprietaire, b, null, true, false);
	}

	private void OnZoneDegats(Camp campTouche, Node body)
	{
		if (body is not BalleScript balle || !balle.IsInGroup("balle"))
			return;

		balle.QueueFree();
		if (_etat == Etat.EnJeu || _etat == Etat.AttenteLancement)
			PenaliserBalleRatee(campTouche);
	}

	private void PenaliserBalleRatee(Camp campTouche)
	{
		campTouche.Combo = 0;
		campTouche.Score = Math.Max(0, campTouche.Score - 250);

		Jouer("perte");
		AssurerBallesMinimumPlateau();
		MettreAJourHud();
		if (!campTouche.ControleIA && !campTouche.Elimine)
		{
			MessageLabel.Text = "Balle ratee ! Espace pour relancer";
			MessageLabel.Visible = true;
		}
	}

	private void AssurerBallesMinimumPlateau()
	{
		foreach (Camp camp in _camps)
			AssurerBalleRelance(camp);
	}

	// Rayon au-dela duquel une balle est consideree comme sortie de l'arene (tunneling
	// rare a travers un mur lateral). Le coin le plus eloigne d'un couloir est a ~6.8 du
	// hub (2 joueurs, demi-largeur 4) ; au-dela de 8 on est forcement hors-terrain.
	private const float RayonEvasionBalle = 8.0f;

	// Filet de securite : si une balle a malgre tout franchi un mur, elle ne croisera
	// jamais de ZoneMort et partirait a l'infini. On la recupere et on en regarantit une.
	private void SurveillerBallesEchappees()
	{
		bool recyclage = false;
		foreach (Camp camp in _camps)
		{
			foreach (Node n in camp.Balles.GetChildren())
			{
				if (n is not BalleScript balle || balle.IsQueuedForDeletion())
					continue;
				if ((balle.GlobalPosition - Hub).Length() <= RayonEvasionBalle)
					continue;

				GD.PushWarning($"[FTLM] Balle echappee a {balle.GlobalPosition} (camp {camp.Cle}) - recuperee.");
				balle.QueueFree();
				recyclage = true;
			}
		}

		if (recyclage)
			AssurerBallesMinimumPlateau();
	}

	private void AssurerBalleRelance(Camp camp)
	{
		if (!camp.Elimine && CompterBalles(camp) == 0)
			CreerBalleAuRepos(camp);
	}

	private void FinPartie(Camp gagnant)
	{
		if (_etat is Etat.GameOver or Etat.Victoire)
			return;

		ArreterBalles();
		bool victoireHumain = gagnant != null && !gagnant.ControleIA;
		_etat = victoireHumain ? Etat.Victoire : Etat.GameOver;
		Jouer(victoireHumain ? "niveau" : "gameover");
		_indexGagnant = gagnant?.Index ?? -1;
		MessageLabel.Visible = false;
		MettreAJourHud();
	}

	private void PeutEtreLacherCapsule(Camp destinataire, Vector3 position)
	{
		if (_rng.Randf() <= ProbaCapsule)
			LacherCapsule(destinataire, position, TirerTypeCapsule(destinataire));
	}

	private CapsuleScript LacherCapsule(Camp destinataire, Vector3 position, CapsuleScript.TypeBonus type)
	{
		var capsule = CapsuleScene.Instantiate<CapsuleScript>();
		destinataire.Capsules.AddChild(capsule);
		capsule.IdReseau = ++_compteurIdMobile;
		capsule.GlobalPosition = position;
		capsule.SensVertical = destinataire.SensCapsuleVersBarre;
		capsule.Initialiser(type, CouleurBonus(type));
		capsule.BodyEntered += (body) => OnCapsule(destinataire, capsule, body);
		return capsule;
	}

	private CapsuleScript.TypeBonus TirerTypeCapsule(Camp camp)
	{
		float total = 0.0f;
		foreach (TirageCapsule entree in TableCapsules)
			if (BonusAutorise(camp, entree.Type))
				total += PoidsAjuste(camp, entree);

		if (total <= 0.0f)
			return CapsuleScript.TypeBonus.BarreLarge;

		float tirage = _rng.RandfRange(0.0f, total);
		foreach (TirageCapsule entree in TableCapsules)
		{
			if (!BonusAutorise(camp, entree.Type))
				continue;

			tirage -= PoidsAjuste(camp, entree);
			if (tirage <= 0.0f)
				return entree.Type;
		}

		return CapsuleScript.TypeBonus.BarreLarge;
	}

	private static float PoidsAjuste(Camp camp, TirageCapsule entree)
	{
		float retard = CompterBatimentsRestants(camp) <= 3 ? 0.12f : 0.0f;
		if (EstMalus(entree.Type))
			return entree.Poids * (1.0f + retard);

		return entree.Poids;
	}

	private bool BonusAutorise(Camp camp, CapsuleScript.TypeBonus type)
	{
		// Un objet offensif n'a d'interet que si une case de stock est libre.
		if (CapsuleScript.EstObjetOffensif(type))
			return !StockPlein(camp);

		return type switch
		{
			CapsuleScript.TypeBonus.BarrePetite => camp.Bar.DemiLargeur > 0.32f,
			CapsuleScript.TypeBonus.BalleRapide => camp.VitesseBalle < VitesseBalleMax * 0.90f,
			CapsuleScript.TypeBonus.MultiBalle => CompterBalles(camp) < NombreMaxBallesParCamp,
			CapsuleScript.TypeBonus.VieBonus => CompterBatimentsRestants(camp) < camp.BatimentsVille.Count,
			CapsuleScript.TypeBonus.Aimant => camp.AimantRestant <= 0.0,
			CapsuleScript.TypeBonus.BouclierBas => camp.BouclierRestant <= 0.0,
			CapsuleScript.TypeBonus.ScoreDouble => camp.ScoreDoubleRestant <= DureeBonus * 0.5,
			CapsuleScript.TypeBonus.BallePercante => camp.BallePercanteRestant <= DureeBonus * 0.5,
			_ => true,
		};
	}

	private static bool EstMalus(CapsuleScript.TypeBonus type)
	{
		return type is CapsuleScript.TypeBonus.BarrePetite or CapsuleScript.TypeBonus.BalleRapide;
	}

	private void OnCapsule(Camp camp, CapsuleScript capsule, Node body)
	{
		if (capsule.IsQueuedForDeletion() || body != camp.Bar)
			return;

		// Objet offensif : il va dans le stock. Si les 2 cases sont pleines, la capsule
		// n'est pas consommee (elle continue de tomber) ; sinon elle est rangee.
		if (CapsuleScript.EstObjetOffensif(capsule.Type))
		{
			if (!AjouterObjet(camp, capsule.Type))
				return;

			camp.CapsulesRamassees++;
			AfficherBonus(camp, $"Objet : {NomObjet(capsule.Type)}");
			Jouer("bonus");
			if (_modeTest)
				GD.Print($"[TEST] Objet stocke {camp.Nom} : {capsule.Type}");
			capsule.QueueFree();
			return;
		}

		camp.CapsulesRamassees++;
		AppliquerBonus(camp, capsule.Type);
		Jouer("bonus");
		if (_modeTest)
			GD.Print($"[TEST] Bonus attrape {camp.Nom} : {capsule.Type}");
		capsule.QueueFree();
	}

	private void AppliquerBonus(Camp camp, CapsuleScript.TypeBonus type)
	{
		switch (type)
		{
			case CapsuleScript.TypeBonus.BarreLarge:
				camp.Bar.Redimensionner(1.6f, DureeBonus);
				AfficherBonus(camp, "+ Barre large");
				break;
			case CapsuleScript.TypeBonus.BarrePetite:
				if (BonusAutorise(camp, type))
					camp.Bar.Redimensionner(0.6f, DureeBonus);
				AfficherBonus(camp, "- Barre petite");
				break;
			case CapsuleScript.TypeBonus.MultiBalle:
				AjouterBalles(camp, Mathf.Min(2, NombreMaxBallesParCamp - CompterBalles(camp)));
				AfficherBonus(camp, "Multi-balle");
				break;
			case CapsuleScript.TypeBonus.VieBonus:
				if (ReparerBatiment(camp))
					AfficherBonus(camp, "Batiment repare");
				else
					AfficherBonus(camp, "Ville intacte");
				MettreAJourHud();
				break;
			case CapsuleScript.TypeBonus.BalleLente:
				ChangerVitesseBalles(camp, Mathf.Max(3.5f, VitesseBalleBase * 0.72f), DureeBonus);
				AfficherBonus(camp, "Balle lente");
				break;
			case CapsuleScript.TypeBonus.BalleRapide:
				if (BonusAutorise(camp, type))
					ChangerVitesseBalles(camp, Mathf.Min(VitesseBalleMax, VitesseBalleBase * 1.35f), DureeBonus);
				AfficherBonus(camp, "Balle rapide");
				break;
			case CapsuleScript.TypeBonus.Aimant:
				camp.AimantRestant = DureeBonus;
				AfficherBonus(camp, "Aimant");
				break;
			case CapsuleScript.TypeBonus.Laser:
				camp.LaserRestant = DureeBonus;
				camp.LaserCooldownRestant = 0.0;
				camp.Bar.AfficherCanonsLaser(true, CouleurCanonLaser(camp));
				AfficherBonus(camp, "Laser");
				break;
			case CapsuleScript.TypeBonus.BouclierBas:
				camp.BouclierRestant = DureeBonus;
				ActiverBouclier(camp);
				AfficherBonus(camp, "Bouclier");
				break;
			case CapsuleScript.TypeBonus.ScoreDouble:
				camp.ScoreDoubleRestant = DureeBonus;
				AfficherBonus(camp, "Score x2");
				MettreAJourHud();
				break;
			case CapsuleScript.TypeBonus.BallePercante:
				camp.BallePercanteRestant = DureeBonus;
				AppliquerPercanteAuxBalles(camp, true);
				AfficherBonus(camp, "Balle percante");
				break;
		}
	}

	private void AjouterBalles(Camp camp, int nombre)
	{
		if (nombre <= 0)
			return;

		Vector3 origine = camp.PositionBalleRepos;
		foreach (Node n in camp.Balles.GetChildren())
			if (n is BalleScript b && !b.IsQueuedForDeletion())
			{
				origine = b.GlobalPosition;
				break;
			}

		for (int i = 0; i < nombre; i++)
		{
			var balle = CreerBalle(camp, origine);
			balle.RebondSurBarre(_rng.RandfRange(-0.85f, 0.85f));
		}
	}

	private void ChangerVitesseBalles(Camp camp, float vitesse, double duree = 0.0)
	{
		camp.VitesseBalle = Mathf.Clamp(vitesse, 3.0f, VitesseBalleMax);
		camp.VitesseTemporaireRestante = duree;
		foreach (Node n in camp.Balles.GetChildren())
			if (n is BalleScript b)
				b.VitesseCible = camp.VitesseBalle;
	}

	// Decompte du collage automatique : une balle qui respawn reste collee a la barre (et la
	// suit) tant que le joueur ne l'a pas lancee ; passe DelaiCollageBalle, elle part seule.
	private void MettreAJourCollageAuto(double delta)
	{
		foreach (Camp camp in _camps)
		{
			if (camp.CollageAutoRestant <= 0.0)
				continue;

			camp.CollageAutoRestant -= delta;
			if (camp.CollageAutoRestant > 0.0)
				continue;

			camp.CollageAutoRestant = 0.0;
			if (LancerBallesPretes(camp) && _etat == Etat.AttenteLancement)
			{
				_etat = Etat.EnJeu;
				MessageLabel.Visible = false;
			}
		}
	}

	private void MettreAJourBonusTemporaires(Camp camp, double delta)
	{
		Decompter(ref camp.MessageBonusRestant, delta, () => MasquerMessageBonus(camp));
		Decompter(ref camp.AimantRestant, delta);
		Decompter(ref camp.LaserRestant, delta, () => camp.Bar.AfficherCanonsLaser(false, CouleurCanonLaser(camp)));
		Decompter(ref camp.LaserCooldownRestant, delta);
		Decompter(ref camp.ScoreDoubleRestant, delta, MettreAJourHud);
		Decompter(ref camp.BouclierRestant, delta, () => DesactiverBouclier(camp));
		Decompter(ref camp.BallePercanteRestant, delta, () => AppliquerPercanteAuxBalles(camp, false));
		Decompter(ref camp.VitesseTemporaireRestante, delta, () => ChangerVitesseBalles(camp, VitesseBalleBase));
		MettreAJourTimersBonusHud();
	}

	private static void Decompter(ref double valeur, double delta, Action expiration = null)
	{
		if (valeur <= 0.0)
			return;

		valeur -= delta;
		if (valeur <= 0.0)
		{
			valeur = 0.0;
			expiration?.Invoke();
		}
	}

	private void ReinitialiserBonusTemporaires(Camp camp)
	{
		camp.MessageBonusRestant = 0.0;
		camp.AimantRestant = 0.0;
		camp.LaserRestant = 0.0;
		camp.Bar.AfficherCanonsLaser(false, CouleurCanonLaser(camp));
		camp.BouclierRestant = 0.0;
		camp.ScoreDoubleRestant = 0.0;
		camp.BallePercanteRestant = 0.0;
		camp.VitesseTemporaireRestante = 0.0;
		camp.LaserCooldownRestant = 0.0;
		camp.Bar?.ReinitialiserEtatsOffensifs();
		MasquerMessageBonus(camp);
		DesactiverBouclier(camp);
		AppliquerPercanteAuxBalles(camp, false);
		MettreAJourTimersBonusHud();
	}

	private void AppliquerPercanteAuxBalles(Camp camp, bool actif)
	{
		foreach (Node n in camp.Balles.GetChildren())
			if (n is BalleScript b)
				b.Percante = actif;
	}

	private static Color CouleurCanonLaser(Camp camp)
	{
		return camp.Couleur;
	}

	private void TirerLaser(Camp camp)
	{
		if (camp.LaserCooldownRestant > 0.0)
			return;

		camp.LaserCooldownRestant = CadenceLaser;
		Vector3 lateral = camp.Bar.GlobalTransform.Basis.X.Normalized();
		Vector3 versHub = camp.Bar.GlobalTransform.Basis.Y.Normalized();
		Vector3 baseP = camp.Bar.GlobalPosition + versHub * 0.18f;
		Vector3 gauche = baseP - lateral * 0.24f;
		Vector3 droite = baseP + lateral * 0.24f;
		CreerTirLaser(camp, gauche, true);
		CreerTirLaser(camp, droite, true);
		// Repliquer le faisceau (visuel seul) chez les clients.
		if (_estHote)
			Rpc(MethodName.ClientTirLaser, camp.Index, gauche.X, gauche.Y, droite.X, droite.Y);
	}

	// Cree un tir laser. avecCollision = false pour la version client (visuel seul, pas
	// de degats ni de signal : la logique reste autoritaire sur l'hote).
	private void CreerTirLaser(Camp camp, Vector3 position, bool avecCollision)
	{
		// Parente au couloir : le tir avance le long du +Y local (vers le hub), quel
		// que soit l'angle du bras.
		var tir = new LaserTir
		{
			Name = $"LaserTir_{camp.Nom}",
			SensVertical = 1,
			LimiteY = 9.8f,
		};
		camp.Arm.AddChild(tir);
		tir.GlobalPosition = position;

		var mesh = new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = new Vector3(0.06f, 0.32f, 0.06f) },
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = camp.Couleur,
				EmissionEnabled = true,
				Emission = camp.Couleur,
				EmissionEnergyMultiplier = 1.5f,
			},
		};
		tir.AddChild(mesh);

		if (avecCollision)
		{
			tir.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.08f, 0.34f, 0.08f) } });
			tir.BodyEntered += (body) => OnLaserCollision(camp, tir, body);
		}
	}

	private void OnLaserCollision(Camp camp, LaserTir tir, Node body)
	{
		if (!IsInstanceValid(tir) || tir.IsQueuedForDeletion())
			return;

		if (body is BriqueScript brique)
			FrapperBrique(camp, brique);

		tir.QueueFree();
	}

	private void ActiverBouclier(Camp camp)
	{
		if (IsInstanceValid(camp.Bouclier))
			return;

		camp.Bouclier = new StaticBody3D
		{
			Name = $"Bouclier_{camp.Nom}",
			PhysicsMaterialOverride = new PhysicsMaterial
			{
				Friction = 0.0f,
				Bounce = 1.0f,
			},
		};
		// Parente au couloir : tout est exprime dans le repere local du bras (pivote).
		camp.Arm.AddChild(camp.Bouclier);
		float largeur = CalculerLargeurBouclier();
		// Decalage > hauteur du dome (0.78) pour que tout le dome reste sous la barre (cote ville).
		float baseY = camp.Bar.Position.Y - camp.SensAttaque * 0.95f;
		camp.Bouclier.Position = new Vector3(0.0f, baseY, camp.Bar.Position.Z);

		Color couleur = camp.Couleur;
		couleur.A = 0.75f;
		var materiau = new StandardMaterial3D
		{
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = couleur,
			EmissionEnabled = true,
			Emission = couleur,
			EmissionEnergyMultiplier = 1.1f,
		};

		CreerDomeBouclier(camp, largeur, materiau);
	}

	private void CreerDomeBouclier(Camp camp, float largeur, StandardMaterial3D materiau)
	{
		const int segments = 36;
		const float hauteur = 0.78f;
		const float epaisseur = 0.16f;
		const float profondeur = 0.14f;
		const float chevauchement = 1.08f;
		float demiLargeur = largeur * 0.5f;

		for (int i = 0; i < segments; i++)
		{
			float t0 = (float)i / segments;
			float t1 = (float)(i + 1) / segments;
			Vector2 p0 = PositionDomeBouclier(t0, demiLargeur, hauteur, camp.SensAttaque);
			Vector2 p1 = PositionDomeBouclier(t1, demiLargeur, hauteur, camp.SensAttaque);
			Vector2 milieu = (p0 + p1) * 0.5f;
			Vector2 delta = p1 - p0;
			float longueur = delta.Length() * chevauchement;
			float angle = Mathf.Atan2(delta.Y, delta.X);

			var segment = new Node3D
			{
				Name = $"BouclierSegment{i + 1}",
				Position = new Vector3(milieu.X, milieu.Y, 0.0f),
				Rotation = new Vector3(0.0f, 0.0f, angle),
			};
			camp.Bouclier.AddChild(segment);

			var mesh = new MeshInstance3D
			{
				Mesh = new BoxMesh { Size = new Vector3(longueur, epaisseur, profondeur) },
				MaterialOverride = materiau,
			};
			segment.AddChild(mesh);

			camp.Bouclier.AddChild(new CollisionShape3D
			{
				Name = $"CollisionBouclierSegment{i + 1}",
				Position = new Vector3(milieu.X, milieu.Y, 0.0f),
				Rotation = new Vector3(0.0f, 0.0f, angle),
				Shape = new BoxShape3D { Size = new Vector3(longueur, epaisseur, profondeur) },
			});
		}
	}

	private static Vector2 PositionDomeBouclier(float t, float demiLargeur, float hauteur, int sensAttaque)
	{
		float x = Mathf.Lerp(-demiLargeur, demiLargeur, t);
		float y = sensAttaque * hauteur * Mathf.Sin(Mathf.Pi * t);
		return new Vector2(x, y);
	}

	private static bool BouclierActif(Camp camp)
	{
		return camp.BouclierRestant > 0.0 && IsInstanceValid(camp.Bouclier);
	}

	private void RepousserBalleParBouclier(Camp camp, BalleScript balle)
	{
		if (balle == null || !IsInstanceValid(balle))
			return;

		Vector3 lateral = camp.Bar.GlobalTransform.Basis.X.Normalized();
		float offset = Mathf.Clamp((balle.GlobalPosition - camp.Bar.GlobalPosition).Dot(lateral) / Math.Max(0.1f, camp.Bar.DemiLargeur), -1.0f, 1.0f);
		balle.RebondSurBarre(offset);
	}

	private float CalculerLargeurBouclier()
	{
		// Largeur interieure du couloir (entre les faces internes des murs lateraux).
		return Mathf.Max(1.0f, _demiLargeurTerrain * 2.0f - EpaisseurMur);
	}

	private void DesactiverBouclier(Camp camp)
	{
		if (IsInstanceValid(camp.Bouclier))
			camp.Bouclier.QueueFree();
		camp.Bouclier = null;
	}

	private void MettreAJourIA(double delta)
	{
		foreach (Camp camp in _camps)
		{
			if (!camp.ControleIA || camp.Elimine || camp.Bar == null)
				continue;

			MettreAJourCampIA(camp, delta);
		}
	}

	private void MettreAJourCampIA(Camp camp, double delta)
	{
		BalleScript cible = ChoisirBalleMenacanteIA(camp);
		// Cible exprimee en X local du couloir (la barre se deplace sur son X local).
		float? cibleX = cible != null ? camp.Arm.ToLocal(cible.GlobalPosition).X : (float?)null;
		camp.Bar.DefinirCibleIA(cibleX);

		if (_etat == Etat.AttenteLancement)
		{
			camp.LaserCooldownRestant += delta;
			if (camp.LaserCooldownRestant >= DelaiLancementIA)
			{
				LancerCamp(camp);
				camp.LaserCooldownRestant = 0.0;
			}
		}
		else if (_etat == Etat.EnJeu)
		{
			LancerBallesPretes(camp);
			if (camp.LaserRestant > 0.0)
				TirerLaser(camp);
			GererObjetIA(camp, delta);
		}
	}

	// L'IA lance periodiquement l'objet en tete de stock vers un adversaire.
	private void GererObjetIA(Camp camp, double delta)
	{
		if (!camp.Stock[0].HasValue)
			return;

		camp.ObjetCooldownIA -= delta;
		if (camp.ObjetCooldownIA > 0.0)
			return;

		camp.ObjetCooldownIA = _rng.RandfRange(2.5f, 5.0f);
		LancerObjetStock(camp);
	}

	private BalleScript ChoisirBalleMenacanteIA(Camp campIA)
	{
		BalleScript meilleure = null;
		float meilleureDistance = float.MaxValue;
		float barreYLocal = campIA.Bar.Position.Y;
		Basis baseInverse = campIA.Arm.GlobalTransform.Basis.Inverse();
		foreach (Camp camp in _camps)
		{
			foreach (Node n in camp.Balles.GetChildren())
			{
				if (n is not BalleScript balle || balle.IsQueuedForDeletion())
					continue;

				// Tout dans le repere local du couloir de l'IA : une balle menace si elle
				// se dirige vers la barre (vitesse locale Y < 0, vers la zone de mort).
				Vector3 posLocale = campIA.Arm.ToLocal(balle.GlobalPosition);
				Vector3 vitLocale = baseInverse * balle.LinearVelocity;
				bool menace = vitLocale.Y < 0.05f && posLocale.Y < 5.0f;
				float distance = Math.Abs(posLocale.Y - barreYLocal);
				if (menace && distance < meilleureDistance)
				{
					meilleure = balle;
					meilleureDistance = distance;
				}
			}
		}

		return meilleure;
	}

	private void ArreterBalles()
	{
		foreach (Camp camp in _camps)
			foreach (Node n in camp.Balles.GetChildren())
				if (n is BalleScript b)
					b.Stopper();
	}

	private void ChargerSons()
	{
		foreach (string nom in new[] { "rebond", "casse", "bonus", "perte", "niveau", "gameover" })
		{
			var lecteur = new AudioStreamPlayer { Stream = GD.Load<AudioStream>($"res://Audio/{nom}.wav") };
			lecteur.Bus = "SFX";
			AddChild(lecteur);
			_sons[nom] = lecteur;
		}
	}

	private void Jouer(string nom)
	{
		if (_sons.TryGetValue(nom, out AudioStreamPlayer lecteur))
			lecteur.Play();
	}

	private void ChargerVille(Camp camp)
	{
		foreach (Node enfant in camp.Ville.GetChildren())
			enfant.QueueFree();
		camp.BatimentsVille.Clear();

		// Ville comprimee horizontalement pour tenir dans les couloirs etroits (3/4 joueurs).
		camp.Ville.Scale = new Vector3(_facteurLargeur, 1.0f, 1.0f);

		var plans = new[]
		{
			(X: -1.58f, Largeur: 0.24f, Hauteur: 0.42f, Profondeur: 0.15f, Couleur: new Color(0.08f, 0.13f, 0.18f), Neon: new Color(0.10f, 0.86f, 1.00f), Antenne: false, Ordre: 8),
			(X: -1.31f, Largeur: 0.28f, Hauteur: 0.64f, Profondeur: 0.17f, Couleur: new Color(0.10f, 0.12f, 0.22f), Neon: new Color(0.92f, 0.28f, 1.00f), Antenne: true, Ordre: 3),
			(X: -1.02f, Largeur: 0.22f, Hauteur: 0.48f, Profondeur: 0.14f, Couleur: new Color(0.07f, 0.16f, 0.20f), Neon: new Color(0.25f, 1.00f, 0.64f), Antenne: false, Ordre: 10),
			(X: -0.74f, Largeur: 0.32f, Hauteur: 0.72f, Profondeur: 0.18f, Couleur: new Color(0.11f, 0.13f, 0.19f), Neon: new Color(0.12f, 0.64f, 1.00f), Antenne: true, Ordre: 1),
			(X: -0.42f, Largeur: 0.24f, Hauteur: 0.52f, Profondeur: 0.14f, Couleur: new Color(0.09f, 0.17f, 0.16f), Neon: new Color(0.95f, 0.68f, 0.18f), Antenne: false, Ordre: 6),
			(X: -0.13f, Largeur: 0.34f, Hauteur: 0.78f, Profondeur: 0.19f, Couleur: new Color(0.12f, 0.14f, 0.23f), Neon: new Color(0.08f, 0.92f, 1.00f), Antenne: true, Ordre: 0),
			(X: 0.18f, Largeur: 0.20f, Hauteur: 0.44f, Profondeur: 0.13f, Couleur: new Color(0.07f, 0.15f, 0.18f), Neon: new Color(0.80f, 0.35f, 1.00f), Antenne: false, Ordre: 11),
			(X: 0.45f, Largeur: 0.30f, Hauteur: 0.68f, Profondeur: 0.17f, Couleur: new Color(0.11f, 0.16f, 0.20f), Neon: new Color(0.16f, 1.00f, 0.78f), Antenne: true, Ordre: 4),
			(X: 0.74f, Largeur: 0.23f, Hauteur: 0.50f, Profondeur: 0.14f, Couleur: new Color(0.10f, 0.12f, 0.18f), Neon: new Color(1.00f, 0.36f, 0.18f), Antenne: false, Ordre: 7),
			(X: 1.03f, Largeur: 0.31f, Hauteur: 0.70f, Profondeur: 0.18f, Couleur: new Color(0.08f, 0.14f, 0.22f), Neon: new Color(0.24f, 0.78f, 1.00f), Antenne: true, Ordre: 2),
			(X: 1.34f, Largeur: 0.23f, Hauteur: 0.46f, Profondeur: 0.14f, Couleur: new Color(0.09f, 0.16f, 0.18f), Neon: new Color(0.80f, 1.00f, 0.28f), Antenne: false, Ordre: 9),
			(X: 1.59f, Largeur: 0.26f, Hauteur: 0.58f, Profondeur: 0.16f, Couleur: new Color(0.12f, 0.13f, 0.20f), Neon: new Color(0.98f, 0.30f, 0.88f), Antenne: true, Ordre: 5),
		};

		foreach (var plan in plans)
		{
			StaticBody3D racine = new StaticBody3D
			{
				Name = "BatimentVille",
				Position = new Vector3(plan.X, VilleBaseY, 0.0f),
				Scale = new Vector3(1.0f, camp.SensAttaque, 1.0f),
			};
			racine.AddToGroup("batiments_ville");
			camp.Ville.AddChild(racine);

			StandardMaterial3D materiauCorps = CreerMateriauVille(plan.Couleur, plan.Neon, 0.18f, false);
			MeshInstance3D corps = new MeshInstance3D
			{
				Name = "Corps",
				Mesh = new BoxMesh { Size = new Vector3(plan.Largeur, plan.Hauteur, plan.Profondeur) },
				MaterialOverride = materiauCorps,
				Position = new Vector3(0.0f, plan.Hauteur * 0.5f, 0.0f),
			};
			racine.AddChild(corps);

			var collision = new CollisionShape3D
			{
				Name = "CollisionBatiment",
				Shape = new BoxShape3D { Size = new Vector3(plan.Largeur, plan.Hauteur, plan.Profondeur) },
				Position = new Vector3(0.0f, plan.Hauteur * 0.5f, 0.0f),
			};
			racine.AddChild(collision);

			StandardMaterial3D materiauLumiere = CreerMateriauVille(plan.Neon, plan.Neon, 1.35f, true);
			MeshInstance3D lumiere = new MeshInstance3D
			{
				Name = "Lumiere",
				Mesh = new QuadMesh { Size = new Vector2(plan.Largeur * 0.62f, 0.035f) },
				MaterialOverride = materiauLumiere,
				Position = new Vector3(0.0f, plan.Hauteur * 0.58f, plan.Profondeur * 0.5f + 0.004f),
			};
			racine.AddChild(lumiere);

			MeshInstance3D antenne = null;
			if (plan.Antenne)
			{
				antenne = new MeshInstance3D
				{
					Name = "Antenne",
					Mesh = new CylinderMesh { TopRadius = 0.01f, BottomRadius = 0.015f, Height = 0.18f },
					MaterialOverride = CreerMateriauVille(plan.Neon, plan.Neon, 1.7f, true),
					Position = new Vector3(plan.Largeur * 0.25f, plan.Hauteur + 0.09f, 0.0f),
				};
				racine.AddChild(antenne);
			}

			camp.BatimentsVille.Add(new BatimentVille(
				racine,
				corps,
				lumiere,
				antenne,
				collision,
				materiauCorps,
				materiauLumiere,
				racine.Position,
				plan.Largeur,
				plan.Hauteur,
				plan.Couleur,
				plan.Neon,
				plan.Ordre));
		}
	}

	private void MettreAJourVille(Camp camp)
	{
		if (camp.BatimentsVille.Count == 0)
			return;

		foreach (BatimentVille batiment in camp.BatimentsVille)
			AppliquerEtatBatiment(batiment, batiment.Detruit ? 3 : 0);
	}

	private void ReparerTousLesBatiments(Camp camp)
	{
		foreach (BatimentVille batiment in camp.BatimentsVille)
		{
			ReparerBatiment(batiment);
			AppliquerEtatBatiment(batiment, 0);
		}
	}

	private bool ReparerBatiment(Camp camp)
	{
		foreach (BatimentVille batiment in camp.BatimentsVille)
		{
			if (!batiment.Detruit)
				continue;

			ReparerBatiment(batiment);
			AppliquerEtatBatiment(batiment, 0);
			return true;
		}

		return false;
	}

	private static void ReparerBatiment(BatimentVille batiment)
	{
		batiment.Detruit = false;
		if (batiment.Collision != null)
			batiment.Collision.Disabled = false;
	}

	private static int CompterBatimentsRestants(Camp camp)
	{
		int total = 0;
		foreach (BatimentVille batiment in camp.BatimentsVille)
			if (!batiment.Detruit)
				total++;
		return total;
	}

	private void DeclencherExplosionsVille(Camp camp, bool destructionTotale)
	{
		int nombre = destructionTotale ? 9 : 4;
		float tailleBase = destructionTotale ? 1.15f : 0.78f;

		for (int i = 0; i < nombre; i++)
		{
			Vector3 position = ChoisirPositionExplosionVille(camp);
			Color couleur = _rng.Randf() < 0.35f
				? new Color(0.15f, 0.9f, 1.0f)
				: new Color(1.0f, 0.35f, 0.08f);
			float taille = tailleBase * _rng.RandfRange(0.8f, 1.25f);
			double delai = i * (destructionTotale ? 0.045 : 0.075);

			if (delai <= 0.0)
				ExploserVille(position, couleur, taille, destructionTotale);
			else
				GetTree().CreateTimer(delai).Timeout += () => ExploserVille(position, couleur, taille, destructionTotale);
		}
	}

	private static StandardMaterial3D CreerMateriauVille(Color albedo, Color emission, float energieEmission, bool translucide)
	{
		return new StandardMaterial3D
		{
			Transparency = translucide ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
			ShadingMode = translucide ? BaseMaterial3D.ShadingModeEnum.Unshaded : BaseMaterial3D.ShadingModeEnum.PerPixel,
			AlbedoColor = translucide ? new Color(albedo.R, albedo.G, albedo.B, 0.82f) : albedo,
			Metallic = translucide ? 0.0f : 0.68f,
			Roughness = translucide ? 0.18f : 0.32f,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			EmissionEnabled = true,
			Emission = emission,
			EmissionEnergyMultiplier = energieEmission,
		};
	}

	private static int CalculerDegatsBatiment(int ordreDegats, int etatDegats)
	{
		return etatDegats switch
		{
			0 => 0,
			1 => ordreDegats < 4 ? 1 : 0,
			2 => ordreDegats < 4 ? 3 : ordreDegats < 8 ? 2 : ordreDegats < 10 ? 1 : 0,
			_ => ordreDegats < 8 ? 3 : 2,
		};
	}

	private void AppliquerEtatBatiment(BatimentVille batiment, int degats)
	{
		float signe = batiment.OrdreDegats % 2 == 0 ? -1.0f : 1.0f;
		batiment.Racine.Visible = degats < 3 || batiment.OrdreDegats % 4 != 1;
		batiment.Racine.Position = batiment.PositionBase;
		batiment.Racine.Rotation = Vector3.Zero;
		batiment.Racine.Scale = new Vector3(1.0f, batiment.Racine.Scale.Y < 0.0f ? -1.0f : 1.0f, 1.0f);
		batiment.Lumiere.Visible = degats < 3;
		if (batiment.Antenne != null)
			batiment.Antenne.Visible = degats <= 1;

		switch (degats)
		{
			case 0:
				ColorierBatiment(batiment, batiment.CouleurBase, batiment.CouleurNeon, 0.18f, 1.35f, 0.82f);
				break;
			case 1:
				batiment.Racine.Rotation = new Vector3(0.0f, 0.0f, signe * 0.035f);
				batiment.Racine.Scale *= new Vector3(0.98f, 0.86f, 1.0f);
				ColorierBatiment(batiment, batiment.CouleurBase * 0.62f, batiment.CouleurNeon, 0.08f, 0.62f, 0.58f);
				break;
			case 2:
				batiment.Racine.Rotation = new Vector3(0.0f, 0.0f, signe * 0.09f);
				batiment.Racine.Scale *= new Vector3(1.02f, 0.58f, 1.0f);
				ColorierBatiment(batiment, new Color(0.055f, 0.045f, 0.05f), new Color(1.0f, 0.23f, 0.08f), 0.03f, 0.18f, 0.32f);
				break;
			default:
				batiment.Racine.Rotation = new Vector3(0.0f, 0.0f, signe * 0.16f);
				batiment.Racine.Scale *= new Vector3(1.12f, 0.24f, 1.0f);
				batiment.Racine.Position = batiment.PositionBase + new Vector3(signe * 0.025f, -0.02f, 0.0f);
				ColorierBatiment(batiment, new Color(0.035f, 0.032f, 0.035f), new Color(1.0f, 0.18f, 0.06f), 0.0f, 0.0f, 0.0f);
				break;
		}
	}

	private static void ColorierBatiment(BatimentVille batiment, Color couleurCorps, Color couleurNeon, float emissionCorps, float emissionNeon, float alphaNeon)
	{
		couleurCorps.A = 1.0f;
		batiment.MateriauCorps.AlbedoColor = couleurCorps;
		batiment.MateriauCorps.Emission = couleurNeon;
		batiment.MateriauCorps.EmissionEnergyMultiplier = emissionCorps;
		batiment.MateriauLumiere.AlbedoColor = new Color(couleurNeon.R, couleurNeon.G, couleurNeon.B, alphaNeon);
		batiment.MateriauLumiere.Emission = couleurNeon;
		batiment.MateriauLumiere.EmissionEnergyMultiplier = emissionNeon;
	}

	private Vector3 ChoisirPositionExplosionVille(Camp camp)
	{
		if (camp.BatimentsVille.Count == 0)
			return camp.Ville.GlobalPosition + new Vector3(_rng.RandfRange(-1.65f, 1.65f), _rng.RandfRange(-0.38f, 0.16f), VilleExplosionZ);

		BatimentVille batiment = camp.BatimentsVille[_rng.RandiRange(0, camp.BatimentsVille.Count - 1)];
		Vector3 locale = new Vector3(
			_rng.RandfRange(-batiment.Largeur * 0.32f, batiment.Largeur * 0.32f),
			_rng.RandfRange(batiment.Hauteur * 0.18f, batiment.Hauteur * 0.90f),
			0.0f);
		Vector3 globale = batiment.Racine.ToGlobal(locale);
		return new Vector3(globale.X, globale.Y, VilleExplosionZ);
	}

	private Vector3 ChoisirPositionExplosionBatiment(BatimentVille batiment)
	{
		Vector3 locale = new Vector3(0.0f, batiment.Hauteur * 0.55f, 0.0f);
		Vector3 globale = batiment.Racine.ToGlobal(locale);
		return new Vector3(globale.X, globale.Y, VilleExplosionZ);
	}

	private void ExploserVille(Vector3 position, Color couleur, float taille, bool destructionTotale)
	{
		CreerImpactVille(position, couleur, taille);
		CreerFumeeVille(position, taille, destructionTotale);
	}

	private void Exploser(Vector3 position, Color couleur, float taille = 1.0f, bool avecFlash = true)
	{
		var explosion = ExplosionScene.Instantiate<CpuParticles3D>();
		AddChild(explosion);
		explosion.GlobalPosition = position;
		explosion.Scale = Vector3.One * taille;
		explosion.Color = couleur;
		explosion.Amount = Mathf.Clamp(Mathf.RoundToInt(28.0f * taille), 12, 96);
		explosion.InitialVelocityMin = 1.7f * taille;
		explosion.InitialVelocityMax = 3.7f * taille;
		explosion.ScaleAmountMin = 0.45f * taille;
		explosion.ScaleAmountMax = 1.2f * taille;
		explosion.Emitting = true;
		if (avecFlash)
			CreerFlashExplosion(position, couleur, taille);
		GetTree().CreateTimer(1.1).Timeout += () =>
		{
			if (IsInstanceValid(explosion))
				explosion.QueueFree();
		};
	}

	private void CreerFlashExplosion(Vector3 position, Color couleur, float taille)
	{
		var couleurFlash = new Color(couleur.R, couleur.G, couleur.B, 0.58f);
		var materiau = new StandardMaterial3D
		{
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = couleurFlash,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			EmissionEnabled = true,
			Emission = couleur,
			EmissionEnergyMultiplier = 1.8f,
		};
		var flash = new MeshInstance3D
		{
			Name = "FlashExplosion",
			Mesh = new QuadMesh { Size = new Vector2(0.28f * taille, 0.28f * taille) },
			MaterialOverride = materiau,
		};

		AddChild(flash);
		flash.GlobalPosition = position + new Vector3(0.0f, 0.0f, 0.04f);

		Tween tween = CreateTween();
		tween.SetParallel(true);
		tween.TweenProperty(flash, "scale", Vector3.One * 2.8f, 0.24).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
		tween.TweenProperty(materiau, "albedo_color", new Color(couleur.R, couleur.G, couleur.B, 0.0f), 0.24);
		tween.TweenProperty(materiau, "emission_energy_multiplier", 0.0f, 0.24);
		tween.Finished += () =>
		{
			if (IsInstanceValid(flash))
				flash.QueueFree();
		};
	}

	private void CreerImpactVille(Vector3 position, Color couleur, float taille)
	{
		var materiau = new StandardMaterial3D
		{
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = new Color(couleur.R, couleur.G, couleur.B, 0.48f),
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			EmissionEnabled = true,
			Emission = couleur,
			EmissionEnergyMultiplier = 2.0f,
		};
		var impact = new MeshInstance3D
		{
			Name = "ImpactVille",
			Mesh = new QuadMesh { Size = new Vector2(0.18f * taille, 0.34f * taille) },
			MaterialOverride = materiau,
		};

		AddChild(impact);
		impact.GlobalPosition = position + new Vector3(0.0f, 0.08f * taille, 0.05f);

		Tween tween = CreateTween();
		tween.SetParallel(true);
		tween.TweenProperty(impact, "scale", new Vector3(1.8f, 3.2f, 1.0f), 0.38).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
		tween.TweenProperty(impact, "position", impact.Position + new Vector3(0.0f, 0.12f * taille, 0.0f), 0.38);
		tween.TweenProperty(materiau, "albedo_color", new Color(couleur.R, couleur.G, couleur.B, 0.0f), 0.38);
		tween.TweenProperty(materiau, "emission_energy_multiplier", 0.0f, 0.38);
		tween.Finished += () =>
		{
			if (IsInstanceValid(impact))
				impact.QueueFree();
		};
	}

	private void CreerFumeeVille(Vector3 position, float taille, bool destructionTotale)
	{
		var materiau = new StandardMaterial3D
		{
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = new Color(0.04f, 0.045f, 0.05f, destructionTotale ? 0.46f : 0.34f),
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
		var fumee = new MeshInstance3D
		{
			Name = "FumeeVille",
			Mesh = new QuadMesh { Size = new Vector2(0.22f * taille, 0.46f * taille) },
			MaterialOverride = materiau,
		};

		AddChild(fumee);
		fumee.GlobalPosition = position + new Vector3(_rng.RandfRange(-0.04f, 0.04f), 0.10f * taille, 0.045f);

		float hauteur = destructionTotale ? 0.52f : 0.34f;
		Tween tween = CreateTween();
		tween.SetParallel(true);
		tween.TweenProperty(fumee, "scale", new Vector3(2.4f, destructionTotale ? 3.8f : 2.6f, 1.0f), 0.95).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
		tween.TweenProperty(fumee, "position", fumee.Position + new Vector3(_rng.RandfRange(-0.06f, 0.06f), hauteur, 0.0f), 0.95);
		tween.TweenProperty(materiau, "albedo_color", new Color(0.04f, 0.045f, 0.05f, 0.0f), 0.95);
		tween.Finished += () =>
		{
			if (IsInstanceValid(fumee))
				fumee.QueueFree();
		};
	}

	private static Color CouleurBonus(CapsuleScript.TypeBonus type) => type switch
	{
		CapsuleScript.TypeBonus.BarreLarge => new Color(0.40f, 0.85f, 0.45f),
		CapsuleScript.TypeBonus.BarrePetite => new Color(0.90f, 0.30f, 0.30f),
		CapsuleScript.TypeBonus.MultiBalle => new Color(0.35f, 0.85f, 0.95f),
		CapsuleScript.TypeBonus.VieBonus => new Color(0.95f, 0.45f, 0.85f),
		CapsuleScript.TypeBonus.BalleLente => new Color(0.45f, 0.55f, 0.95f),
		CapsuleScript.TypeBonus.BalleRapide => new Color(0.95f, 0.65f, 0.25f),
		CapsuleScript.TypeBonus.Aimant => new Color(0.75f, 0.50f, 1.0f),
		CapsuleScript.TypeBonus.Laser => new Color(0.20f, 0.95f, 1.0f),
		CapsuleScript.TypeBonus.BouclierBas => new Color(0.25f, 0.65f, 1.0f),
		CapsuleScript.TypeBonus.ScoreDouble => new Color(1.0f, 0.85f, 0.25f),
		CapsuleScript.TypeBonus.BallePercante => new Color(1.0f, 0.35f, 0.75f),
		CapsuleScript.TypeBonus.Missile => new Color(1.0f, 0.45f, 0.20f),
		CapsuleScript.TypeBonus.Retrecisseur => new Color(0.95f, 0.30f, 0.35f),
		CapsuleScript.TypeBonus.Gel => new Color(0.45f, 0.85f, 1.0f),
		CapsuleScript.TypeBonus.Inverseur => new Color(0.75f, 0.45f, 1.0f),
		CapsuleScript.TypeBonus.Accelerateur => new Color(1.0f, 0.75f, 0.20f),
		_ => Colors.White,
	};

	private int CalculerMultiplicateurScore(Camp camp)
	{
		int comboMul = Mathf.Clamp(1 + camp.Combo / 5, 1, 4);
		int bonusMul = camp.ScoreDoubleRestant > 0.0 ? 2 : 1;
		return comboMul * bonusMul;
	}

	private void CreerLabelsSupplementaires()
	{
		var control = GetNode<Control>("HUD/Control");
		CacherLabelScene(ScoreLabel);
		CacherLabelScene(ViesLabel);
		CacherLabelScene(NiveauLabel);
		CacherLabelScene(MeilleurLabel);

		CreerTableauxScore(control);
	}

	private static void CacherLabelScene(Label label)
	{
		label.Visible = false;
		label.MouseFilter = Control.MouseFilterEnum.Ignore;
	}

	private void CreerTableauxScore(Control control)
	{
		// Echelle des encarts : reduits quand il y a 3-4 panneaux a caser dans les coins.
		_echelleHud = _camps.Count switch
		{
			<= 2 => 1.0f,
			3 => 0.82f,
			_ => 0.72f,
		};

		_huds.Clear();
		foreach (Camp camp in _camps)
		{
			var hud = new HudCamp();
			CreerTableauScoreCamp(control, hud, camp.Nom.ToUpper());
			hud.Titre.AddThemeColorOverride("font_color", camp.Couleur);
			_huds.Add(hud);
		}

		MettreAJourPositionTableauScore();
	}

	private void CreerTableauScoreCamp(Control control, HudCamp hud, string titre)
	{
		hud.Panel = new PanelContainer
		{
			Name = $"TableauScore_{titre}",
			CustomMinimumSize = new Vector2(LargeurTableauScore, HauteurTableauScore),
			Size = new Vector2(LargeurTableauScore, HauteurTableauScore),
			Scale = new Vector2(_echelleHud, _echelleHud),
			PivotOffset = Vector2.Zero,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			ZIndex = 4,
		};
		hud.Panel.AddThemeStyleboxOverride("panel", CreerStyleTableau());
		control.AddChild(hud.Panel);

		var marge = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		marge.AddThemeConstantOverride("margin_left", 14);
		marge.AddThemeConstantOverride("margin_top", 12);
		marge.AddThemeConstantOverride("margin_right", 14);
		marge.AddThemeConstantOverride("margin_bottom", 12);
		hud.Panel.AddChild(marge);

		var pile = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		pile.AddThemeConstantOverride("separation", 7);
		marge.AddChild(pile);

		hud.Titre = CreerTitreHud(titre);
		pile.AddChild(hud.Titre);
		hud.Message = CreerMessageBonusHud();
		pile.AddChild(hud.Message);
		hud.Score = CreerLigneInfoHud("Score");
		hud.Vies = CreerLigneInfoHud("Batiments");
		hud.Briques = CreerLigneInfoHud("Briques");
		hud.Balles = CreerLigneInfoHud("Balles");
		hud.Combo = CreerLigneInfoHud("Combo");
		hud.Etat = CreerLigneInfoHud("Etat");
		pile.AddChild(hud.Score);
		pile.AddChild(hud.Vies);
		pile.AddChild(hud.Briques);
		pile.AddChild(hud.Balles);
		pile.AddChild(hud.Combo);
		pile.AddChild(hud.Etat);
		pile.AddChild(CreerSeparateurHud());
		pile.AddChild(CreerTitreHud("STOCK"));
		pile.AddChild(CreerCasesObjetsHud(hud));

		pile.AddChild(CreerSeparateurHud());
		pile.AddChild(CreerTitreHud("BONUS"));

		for (int i = 0; i < NombreLignesBonusHud; i++)
			pile.AddChild(CreerLigneBonusHud(hud, i));
	}

	// Deux cases d'objets offensifs (cadre + icone) cote a cote.
	private Control CreerCasesObjetsHud(HudCamp hud)
	{
		var ligne = new HBoxContainer
		{
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Alignment = BoxContainer.AlignmentMode.Center,
		};
		ligne.AddThemeConstantOverride("separation", 12);

		for (int i = 0; i < hud.CasesObjets.Length; i++)
		{
			var caseObjet = new Panel
			{
				CustomMinimumSize = new Vector2(48.0f, 48.0f),
				MouseFilter = Control.MouseFilterEnum.Ignore,
				ClipContents = true,
			};
			caseObjet.AddThemeStyleboxOverride("panel", CreerStyleCaseObjet(CouleurCaseVide));

			var icone = new TextureRect
			{
				// IgnoreSize : le contrôle ne prend pas la taille native de la texture
				// (96 px) ; la texture est mise à l'échelle de la case (48 px) sans déborder.
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
				MouseFilter = Control.MouseFilterEnum.Ignore,
				Visible = false,
			};
			icone.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			// Petit retrait pour ne pas chevaucher la bordure de la case.
			icone.OffsetLeft = 4;
			icone.OffsetTop = 4;
			icone.OffsetRight = -4;
			icone.OffsetBottom = -4;
			caseObjet.AddChild(icone);

			ligne.AddChild(caseObjet);
			hud.CasesObjets[i] = caseObjet;
			hud.CasesIcones[i] = icone;
		}

		return ligne;
	}

	private static StyleBoxFlat CreerStyleCaseObjet(Color bordure)
	{
		var style = new StyleBoxFlat
		{
			BgColor = new Color(0.04f, 0.07f, 0.10f, 0.85f),
			BorderColor = bordure,
			CornerRadiusTopLeft = 6,
			CornerRadiusTopRight = 6,
			CornerRadiusBottomLeft = 6,
			CornerRadiusBottomRight = 6,
		};
		style.SetBorderWidthAll(2);
		return style;
	}

	// Met a jour l'affichage des 2 cases de stock d'un camp.
	private void MettreAJourCasesObjets(Camp camp)
	{
		if (camp.Index < 0 || camp.Index >= _huds.Count)
			return;

		HudCamp hud = _huds[camp.Index];
		for (int i = 0; i < hud.CasesObjets.Length; i++)
		{
			if (hud.CasesIcones[i] == null || hud.CasesObjets[i] == null)
				continue;

			CapsuleScript.TypeBonus? type = i < camp.Stock.Length ? camp.Stock[i] : null;
			if (type.HasValue)
			{
				Color couleur = CouleurBonus(type.Value);
				hud.CasesIcones[i].Texture = CapsuleScript.TextureIcone(type.Value, couleur);
				hud.CasesIcones[i].Visible = true;
				hud.CasesObjets[i].AddThemeStyleboxOverride("panel", CreerStyleCaseObjet(couleur));
			}
			else
			{
				hud.CasesIcones[i].Texture = null;
				hud.CasesIcones[i].Visible = false;
				hud.CasesObjets[i].AddThemeStyleboxOverride("panel", CreerStyleCaseObjet(CouleurCaseVide));
			}
		}
	}

	private static Label CreerTitreHud(string texte)
	{
		var label = new Label
		{
			Text = texte,
			HorizontalAlignment = HorizontalAlignment.Center,
			CustomMinimumSize = new Vector2(0.0f, 20.0f),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		label.AddThemeFontSizeOverride("font_size", 13);
		label.AddThemeColorOverride("font_color", new Color(0.50f, 0.95f, 1.0f));
		return label;
	}

	private static Label CreerMessageBonusHud()
	{
		var label = new Label
		{
			Name = "MessageBonusHud",
			Text = "",
			Visible = false,
			HorizontalAlignment = HorizontalAlignment.Center,
			CustomMinimumSize = new Vector2(0.0f, 22.0f),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		label.AddThemeFontSizeOverride("font_size", 16);
		label.AddThemeColorOverride("font_color", new Color(0.84f, 1.0f, 0.96f));
		label.AddThemeColorOverride("font_outline_color", new Color(0.0f, 0.05f, 0.08f, 0.95f));
		label.AddThemeConstantOverride("outline_size", 4);
		return label;
	}

	private static Label CreerLigneInfoHud(string nom)
	{
		var label = new Label
		{
			Name = $"{nom}LabelHud",
			Text = $"{nom} : -",
			CustomMinimumSize = new Vector2(0.0f, 24.0f),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		label.AddThemeFontSizeOverride("font_size", 18);
		label.AddThemeColorOverride("font_color", new Color(0.86f, 0.94f, 0.98f));
		label.AddThemeColorOverride("font_outline_color", new Color(0.0f, 0.02f, 0.05f, 0.9f));
		label.AddThemeConstantOverride("outline_size", 2);
		return label;
	}

	private static Control CreerSeparateurHud()
	{
		return new ColorRect
		{
			Color = new Color(0.18f, 0.90f, 1.0f, 0.45f),
			CustomMinimumSize = new Vector2(0.0f, 2.0f),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
	}

	private HBoxContainer CreerLigneBonusHud(HudCamp hud, int index)
	{
		var ligne = new HBoxContainer
		{
			Name = $"BonusTimer{index + 1}",
			CustomMinimumSize = new Vector2(0.0f, 22.0f),
			Visible = false,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		ligne.AddThemeConstantOverride("separation", 5);

		hud.NomsBonus[index] = new Label
		{
			CustomMinimumSize = new Vector2(82.0f, 20.0f),
			ClipText = true,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		hud.NomsBonus[index].AddThemeFontSizeOverride("font_size", 14);

		hud.JaugesBonus[index] = new ProgressBar
		{
			CustomMinimumSize = new Vector2(84.0f, 12.0f),
			SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
			ShowPercentage = false,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};

		hud.TempsBonus[index] = new Label
		{
			CustomMinimumSize = new Vector2(42.0f, 20.0f),
			HorizontalAlignment = HorizontalAlignment.Right,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		hud.TempsBonus[index].AddThemeFontSizeOverride("font_size", 14);

		ligne.AddChild(hud.NomsBonus[index]);
		ligne.AddChild(hud.JaugesBonus[index]);
		ligne.AddChild(hud.TempsBonus[index]);
		return ligne;
	}

	private static StyleBoxFlat CreerStyleTableau()
	{
		return new StyleBoxFlat
		{
			BgColor = new Color(0.015f, 0.025f, 0.035f, 0.86f),
			BorderColor = new Color(0.10f, 0.92f, 1.0f, 0.78f),
			BorderWidthLeft = 2,
			BorderWidthTop = 2,
			BorderWidthRight = 2,
			BorderWidthBottom = 2,
			CornerRadiusTopLeft = 8,
			CornerRadiusTopRight = 8,
			CornerRadiusBottomRight = 8,
			CornerRadiusBottomLeft = 8,
			ShadowColor = new Color(0.0f, 0.9f, 1.0f, 0.22f),
			ShadowSize = 12,
			ShadowOffset = new Vector2(0.0f, 3.0f),
		};
	}

	private static StyleBoxFlat CreerStyleJauge(Color couleur, bool fond)
	{
		return new StyleBoxFlat
		{
			BgColor = fond ? new Color(0.03f, 0.08f, 0.10f, 0.92f) : couleur,
			CornerRadiusTopLeft = 5,
			CornerRadiusTopRight = 5,
			CornerRadiusBottomRight = 5,
			CornerRadiusBottomLeft = 5,
		};
	}

	// ----------------------------------------------- Panneau de fin de partie

	// Affiche / masque l'overlay de classement selon l'etat courant. Construit une seule
	// fois par fin de partie (garde _finAffichee). Appele a chaque MettreAJourHud, donc
	// fonctionne aussi cote client (qui recoit l'etat + le gagnant via le HUD reseau).
	private void MettreAJourPanneauFin()
	{
		bool terminal = _etat is Etat.GameOver or Etat.Victoire;
		if (!terminal)
		{
			if (_panneauFin != null)
				_panneauFin.Visible = false;
			_finAffichee = false;
			return;
		}

		if (_finAffichee)
			return;
		_finAffichee = true;

		ConstruirePanneauFin();
		RemplirPanneauFin();
		_panneauFin.Visible = true;
	}

	private void ConstruirePanneauFin()
	{
		if (_panneauFin != null)
			return;

		var control = GetNode<Control>("HUD/Control");

		var plein = new Control
		{
			Name = "PanneauFin",
			MouseFilter = Control.MouseFilterEnum.Ignore,
			ZIndex = 20,
		};
		plein.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		control.AddChild(plein);

		// Voile sombre qui assombrit le jeu derriere le classement.
		var voile = new ColorRect
		{
			Color = new Color(0.0f, 0.01f, 0.02f, 0.55f),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		voile.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		plein.AddChild(voile);

		var centre = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		centre.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		plein.AddChild(centre);

		var panneau = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		panneau.AddThemeStyleboxOverride("panel", CreerStylePanneauFin());
		centre.AddChild(panneau);

		var marge = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		marge.AddThemeConstantOverride("margin_left", 36);
		marge.AddThemeConstantOverride("margin_top", 28);
		marge.AddThemeConstantOverride("margin_right", 36);
		marge.AddThemeConstantOverride("margin_bottom", 28);
		panneau.AddChild(marge);

		_panneauFinPile = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		_panneauFinPile.AddThemeConstantOverride("separation", 10);
		marge.AddChild(_panneauFinPile);

		_panneauFin = plein;
	}

	private void RemplirPanneauFin()
	{
		if (_panneauFinPile == null)
			return;

		foreach (Node n in _panneauFinPile.GetChildren())
			n.QueueFree();

		bool victoire = _etat == Etat.Victoire;
		Camp gagnant = _indexGagnant >= 0 && _indexGagnant < _camps.Count ? _camps[_indexGagnant] : null;
		Color couleurTitre = gagnant?.Couleur ?? new Color(0.85f, 0.95f, 1.0f);

		var titre = new Label
		{
			Text = gagnant != null ? $"{gagnant.Nom.ToUpper()} GAGNE !" : "PARTIE TERMINEE",
			HorizontalAlignment = HorizontalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		titre.AddThemeFontSizeOverride("font_size", 42);
		titre.AddThemeColorOverride("font_color", couleurTitre);
		titre.AddThemeColorOverride("font_outline_color", new Color(0.0f, 0.03f, 0.05f, 0.95f));
		titre.AddThemeConstantOverride("outline_size", 6);
		_panneauFinPile.AddChild(titre);

		var sous = new Label
		{
			Text = victoire ? "VICTOIRE" : "GAME OVER",
			HorizontalAlignment = HorizontalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		sous.AddThemeFontSizeOverride("font_size", 16);
		sous.AddThemeColorOverride("font_color", victoire ? new Color(0.55f, 1.0f, 0.7f) : new Color(1.0f, 0.5f, 0.4f));
		_panneauFinPile.AddChild(sous);

		_panneauFinPile.AddChild(CreerSeparateurFin());

		var classement = new List<Camp>(_camps);
		classement.Sort((a, b) => b.Score.CompareTo(a.Score));
		int rang = 1;
		foreach (Camp camp in classement)
		{
			_panneauFinPile.AddChild(CreerLigneClassement(camp, rang, camp == gagnant));
			rang++;
		}

		_panneauFinPile.AddChild(CreerSeparateurFin());

		var footer = new Label
		{
			Text = "Espace pour rejouer",
			HorizontalAlignment = HorizontalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		footer.AddThemeFontSizeOverride("font_size", 18);
		footer.AddThemeColorOverride("font_color", new Color(0.6f, 0.95f, 1.0f));
		_panneauFinPile.AddChild(footer);
	}

	private Control CreerLigneClassement(Camp camp, int rang, bool gagnant)
	{
		var conteneur = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		conteneur.AddThemeStyleboxOverride("panel", CreerStyleLigneClassement(camp.Couleur, gagnant));

		var marge = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		marge.AddThemeConstantOverride("margin_left", 14);
		marge.AddThemeConstantOverride("margin_right", 14);
		marge.AddThemeConstantOverride("margin_top", 7);
		marge.AddThemeConstantOverride("margin_bottom", 7);
		conteneur.AddChild(marge);

		var ligne = new HBoxContainer
		{
			CustomMinimumSize = new Vector2(440.0f, 0.0f),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		ligne.AddThemeConstantOverride("separation", 12);
		marge.AddChild(ligne);

		var rangLabel = new Label
		{
			Text = $"#{rang}",
			CustomMinimumSize = new Vector2(48.0f, 0.0f),
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		rangLabel.AddThemeFontSizeOverride("font_size", 24);
		rangLabel.AddThemeColorOverride("font_color", gagnant ? new Color(1.0f, 0.86f, 0.3f) : new Color(0.7f, 0.8f, 0.88f));
		ligne.AddChild(rangLabel);

		ligne.AddChild(new ColorRect
		{
			Color = camp.Couleur,
			CustomMinimumSize = new Vector2(14.0f, 14.0f),
			SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		});

		var nom = new Label
		{
			Text = camp.Nom,
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		nom.AddThemeFontSizeOverride("font_size", 22);
		nom.AddThemeColorOverride("font_color", camp.Couleur);
		ligne.AddChild(nom);

		ligne.AddChild(new Control
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		});

		var score = new Label
		{
			Text = $"{camp.Score} pts",
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Center,
			CustomMinimumSize = new Vector2(118.0f, 0.0f),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		score.AddThemeFontSizeOverride("font_size", 22);
		score.AddThemeColorOverride("font_color", new Color(0.92f, 0.97f, 1.0f));
		ligne.AddChild(score);

		var etat = new Label
		{
			Text = camp.Elimine ? "elimine" : $"{CompterBatimentsRestants(camp)} bat.",
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Center,
			CustomMinimumSize = new Vector2(92.0f, 0.0f),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		etat.AddThemeFontSizeOverride("font_size", 16);
		etat.AddThemeColorOverride("font_color", camp.Elimine ? new Color(1.0f, 0.45f, 0.4f) : new Color(0.7f, 0.85f, 0.9f));
		ligne.AddChild(etat);

		return conteneur;
	}

	private static StyleBoxFlat CreerStylePanneauFin()
	{
		return new StyleBoxFlat
		{
			BgColor = new Color(0.02f, 0.035f, 0.05f, 0.94f),
			BorderColor = new Color(0.12f, 0.9f, 1.0f, 0.9f),
			BorderWidthLeft = 3,
			BorderWidthTop = 3,
			BorderWidthRight = 3,
			BorderWidthBottom = 3,
			CornerRadiusTopLeft = 16,
			CornerRadiusTopRight = 16,
			CornerRadiusBottomRight = 16,
			CornerRadiusBottomLeft = 16,
			ShadowColor = new Color(0.0f, 0.85f, 1.0f, 0.3f),
			ShadowSize = 26,
		};
	}

	private static StyleBoxFlat CreerStyleLigneClassement(Color couleur, bool gagnant)
	{
		return new StyleBoxFlat
		{
			BgColor = gagnant ? new Color(couleur.R, couleur.G, couleur.B, 0.18f) : new Color(1.0f, 1.0f, 1.0f, 0.03f),
			BorderColor = gagnant ? new Color(couleur.R, couleur.G, couleur.B, 0.85f) : new Color(0.4f, 0.6f, 0.7f, 0.18f),
			BorderWidthLeft = gagnant ? 2 : 1,
			BorderWidthTop = gagnant ? 2 : 1,
			BorderWidthRight = gagnant ? 2 : 1,
			BorderWidthBottom = gagnant ? 2 : 1,
			CornerRadiusTopLeft = 8,
			CornerRadiusTopRight = 8,
			CornerRadiusBottomRight = 8,
			CornerRadiusBottomLeft = 8,
			ContentMarginLeft = 2,
			ContentMarginRight = 2,
		};
	}

	private static ColorRect CreerSeparateurFin()
	{
		return new ColorRect
		{
			Color = new Color(0.18f, 0.9f, 1.0f, 0.4f),
			CustomMinimumSize = new Vector2(0.0f, 2.0f),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
	}

	private void AfficherBonus(Camp camp, string message)
	{
		HudCamp hud = HudDe(camp);
		if (hud?.Message != null)
		{
			hud.Message.Text = message;
			hud.Message.Visible = true;
		}
		camp.MessageBonusRestant = DureeMessageBonus;
		MettreAJourTimersBonusHud();
	}

	private void MasquerMessageBonus(Camp camp)
	{
		HudCamp hud = HudDe(camp);
		if (hud?.Message != null)
			hud.Message.Visible = false;
	}

	private HudCamp HudDe(Camp camp)
	{
		return camp != null && camp.Index >= 0 && camp.Index < _huds.Count ? _huds[camp.Index] : null;
	}

	private static void ViderConteneur(Node conteneur)
	{
		foreach (Node enfant in conteneur.GetChildren())
			enfant.QueueFree();
	}

	private void MettreAJourHud()
	{
		for (int i = 0; i < _camps.Count && i < _huds.Count; i++)
			if (_huds[i].Score != null)
				MettreAJourHudCamp(_camps[i], _huds[i]);

		MettreAJourTimersBonusHud();
		MettreAJourPanneauFin();
	}

	private void MettreAJourHudCamp(Camp camp, HudCamp hud)
	{
		hud.Score.Text = $"Score : {camp.Score}";
		hud.Vies.Text = $"Batiments : {CompterBatimentsRestants(camp)}/{camp.BatimentsVille.Count}";
		hud.Briques.Text = $"Briques : {_briquesRestantes}";
		hud.Balles.Text = $"Balles : {CompterBalles(camp)}";
		hud.Combo.Text = camp.Combo > 1 ? $"Combo : {camp.Combo} x{CalculerMultiplicateurScore(camp)}" : "Combo : -";
		hud.Etat.Text = camp.Elimine ? "Etat : elimine" : _etat switch
		{
			Etat.AttenteLancement => "Etat : lancement",
			Etat.EnJeu => "Etat : en jeu",
			Etat.Pause => "Etat : pause",
			Etat.Victoire => "Etat : victoire",
			Etat.GameOver => "Etat : termine",
			_ => "Etat : -",
		};
	}

	// Coin de l'ecran par joueur (ids : 0=bas-gauche, 1=haut-droite, 2=haut-gauche,
	// 3=bas-droite). Choisi selon le nombre de joueurs pour coller a la position de
	// chaque bras : 2 j. (vertical) -> bas/haut a gauche ; 4 j. (croix) -> un par cote.
	private static int CoinDuCamp(int index, int nbCamps) => nbCamps switch
	{
		2 => index == 0 ? 0 : 2,                          // J1 bas-gauche, J2 haut-gauche
		3 => index switch { 0 => 0, 1 => 1, _ => 2 },     // bas-gauche, haut-droite, haut-gauche
		_ => index switch { 0 => 0, 1 => 3, 2 => 1, _ => 2 }, // bas, droite, haut, gauche
	};

	// Position d'affichage d'un camp relative au joueur local (qui est ramene "en bas").
	// En local / cote hote (slot 0) c'est l'identite.
	private int IndexAffichage(int i)
	{
		int n = _camps.Count;
		if (n <= 0)
			return i;
		int slot = Mathf.Clamp(PartieConfig.SlotLocal, 0, n - 1);
		return (i - slot + n) % n;
	}

	private void MettreAJourPositionTableauScore()
	{
		if (_huds.Count == 0 || _huds[0].Panel == null)
			return;

		Vector2 vp = GetViewport().GetVisibleRect().Size;
		Vector2 marge = new Vector2(10.0f, 10.0f);
		float w = LargeurTableauScore * _echelleHud;
		float h = HauteurTableauScore * _echelleHud;
		float droite = Math.Max(marge.X, vp.X - w - marge.X);
		float bas = Math.Max(marge.Y, vp.Y - h - marge.Y);

		for (int i = 0; i < _huds.Count; i++)
		{
			Vector2 position;
			int affiche = IndexAffichage(i);
			if (_huds.Count == 2)
			{
				// Duel : panneaux centres verticalement ; le joueur local a gauche, l'adversaire a droite.
				float milieuY = Math.Max(marge.Y, (vp.Y - h) * 0.5f);
				position = new Vector2(affiche == 0 ? marge.X : droite, milieuY);
			}
			else
			{
				int coin = CoinDuCamp(affiche, _huds.Count);
				float gauche = coin == 0 || coin == 2 ? marge.X : droite;
				float haut = coin == 2 || coin == 1 ? marge.Y : bas;
				position = new Vector2(gauche, haut);
			}

			_huds[i].Panel.Position = position;
			_huds[i].Panel.Size = new Vector2(LargeurTableauScore, HauteurTableauScore);
			_huds[i].Panel.PivotOffset = Vector2.Zero;
		}
	}

	private void MettreAJourTimersBonusHud()
	{
		for (int i = 0; i < _camps.Count && i < _huds.Count; i++)
			if (_huds[i].NomsBonus[0] != null)
				MettreAJourTimersBonusHud(_camps[i], _huds[i]);
	}

	private void MettreAJourTimersBonusHud(Camp camp, HudCamp hud)
	{
		int ligne = 0;
		AjouterBonusHudCamp(camp, hud, ref ligne);

		for (; ligne < NombreLignesBonusHud; ligne++)
			MasquerLigneBonusHud(hud, ligne);
	}

	private void AjouterBonusHudCamp(Camp camp, HudCamp hud, ref int ligne)
	{
		if (camp.Bar != null && camp.Bar.TempsRedimensionnementRestant > 0.0)
		{
			string nom = camp.Bar.FacteurRedimensionnement > 1.0f ? "Barre +" : "Barre -";
			Color couleur = camp.Bar.FacteurRedimensionnement > 1.0f
				? CouleurBonus(CapsuleScript.TypeBonus.BarreLarge)
				: CouleurBonus(CapsuleScript.TypeBonus.BarrePetite);
			EcrireLigneBonusHud(hud, ligne++, nom, camp.Bar.TempsRedimensionnementRestant, DureeBonus, couleur);
		}
		if (camp.VitesseTemporaireRestante > 0.0)
		{
			string nom = camp.VitesseBalle <= VitesseBalleBase ? "Balle -" : "Balle +";
			Color couleur = camp.VitesseBalle <= VitesseBalleBase
				? CouleurBonus(CapsuleScript.TypeBonus.BalleLente)
				: CouleurBonus(CapsuleScript.TypeBonus.BalleRapide);
			EcrireLigneBonusHud(hud, ligne++, nom, camp.VitesseTemporaireRestante, DureeBonus, couleur);
		}
		if (camp.AimantRestant > 0.0)
			EcrireLigneBonusHud(hud, ligne++, "Aimant", camp.AimantRestant, DureeBonus, CouleurBonus(CapsuleScript.TypeBonus.Aimant));
		if (camp.LaserRestant > 0.0)
			EcrireLigneBonusHud(hud, ligne++, "Laser", camp.LaserRestant, DureeBonus, CouleurBonus(CapsuleScript.TypeBonus.Laser));
		if (camp.BouclierRestant > 0.0)
			EcrireLigneBonusHud(hud, ligne++, "Bouclier", camp.BouclierRestant, DureeBonus, CouleurBonus(CapsuleScript.TypeBonus.BouclierBas));
		if (camp.ScoreDoubleRestant > 0.0)
			EcrireLigneBonusHud(hud, ligne++, "Score x2", camp.ScoreDoubleRestant, DureeBonus, CouleurBonus(CapsuleScript.TypeBonus.ScoreDouble));
		if (camp.BallePercanteRestant > 0.0)
			EcrireLigneBonusHud(hud, ligne++, "Percante", camp.BallePercanteRestant, DureeBonus, CouleurBonus(CapsuleScript.TypeBonus.BallePercante));
	}

	private void EcrireLigneBonusHud(HudCamp hud, int index, string nom, double restant, double duree, Color couleur)
	{
		if (index >= NombreLignesBonusHud)
			return;

		Control ligne = hud.NomsBonus[index].GetParent<Control>();
		ligne.Visible = true;
		hud.NomsBonus[index].Text = nom;
		hud.TempsBonus[index].Text = $"{restant:0.0}s";
		hud.NomsBonus[index].AddThemeColorOverride("font_color", couleur);
		hud.TempsBonus[index].AddThemeColorOverride("font_color", new Color(0.92f, 0.98f, 1.0f));
		hud.JaugesBonus[index].MaxValue = Math.Max(0.1, duree);
		hud.JaugesBonus[index].Value = Mathf.Clamp((float)restant, 0.0f, (float)Math.Max(0.1, duree));
		hud.JaugesBonus[index].AddThemeStyleboxOverride("background", CreerStyleJauge(couleur, true));
		hud.JaugesBonus[index].AddThemeStyleboxOverride("fill", CreerStyleJauge(couleur, false));
	}

	private void MasquerLigneBonusHud(HudCamp hud, int index)
	{
		Control ligne = hud.NomsBonus[index].GetParent<Control>();
		ligne.Visible = false;
	}
}
