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

	private enum CampId
	{
		Joueur,
		IA,
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
			Node3D racine,
			MeshInstance3D corps,
			MeshInstance3D lumiere,
			MeshInstance3D antenne,
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
			MateriauCorps = materiauCorps;
			MateriauLumiere = materiauLumiere;
			PositionBase = positionBase;
			Largeur = largeur;
			Hauteur = hauteur;
			CouleurBase = couleurBase;
			CouleurNeon = couleurNeon;
			OrdreDegats = ordreDegats;
		}

		public Node3D Racine { get; }
		public MeshInstance3D Corps { get; }
		public MeshInstance3D Lumiere { get; }
		public MeshInstance3D Antenne { get; }
		public StandardMaterial3D MateriauCorps { get; }
		public StandardMaterial3D MateriauLumiere { get; }
		public Vector3 PositionBase { get; }
		public float Largeur { get; }
		public float Hauteur { get; }
		public Color CouleurBase { get; }
		public Color CouleurNeon { get; }
		public int OrdreDegats { get; }
	}

	private sealed class Camp
	{
		public Camp(CampId id, string nom, bool controleIA, int sensAttaque)
		{
			Id = id;
			Nom = nom;
			ControleIA = controleIA;
			SensAttaque = sensAttaque;
		}

		public CampId Id { get; }
		public string Nom { get; }
		public bool ControleIA { get; }
		public int SensAttaque { get; }
		public int SensCapsuleVersBarre => -SensAttaque;
		public string Cle => Id == CampId.Joueur ? "joueur" : "ia";
		public BarScript Bar;
		public Area3D ZoneDegats;
		public Node3D Briques;
		public Node3D Balles;
		public Node3D Capsules;
		public Node3D Ville;
		public Vector3 PositionBalleRepos;
		public Vector3 PositionVilleLocale;
		public readonly List<BatimentVille> BatimentsVille = new();
		public int Score;
		public int Vies;
		public int Combo;
		public int MeilleurCombo;
		public int BriquesDetruites;
		public int CapsulesRamassees;
		public int BriquesRestantes;
		public float VitesseBalle;
		public double MessageBonusRestant;
		public double AimantRestant;
		public double LaserRestant;
		public double BouclierRestant;
		public double ScoreDoubleRestant;
		public double BallePercanteRestant;
		public double VitesseTemporaireRestante;
		public double LaserCooldownRestant;
		public StaticBody3D Bouclier;
	}

	private sealed class HudCamp
	{
		public PanelContainer Panel;
		public Label Score;
		public Label Vies;
		public Label Briques;
		public Label Balles;
		public Label Combo;
		public Label Etat;
		public readonly Label[] NomsBonus = new Label[NombreLignesBonusHud];
		public readonly Label[] TempsBonus = new Label[NombreLignesBonusHud];
		public readonly ProgressBar[] JaugesBonus = new ProgressBar[NombreLignesBonusHud];
	}

	private readonly Dictionary<string, AudioStreamPlayer> _sons = new();
	private readonly RandomNumberGenerator _rng = new();
	private readonly Camp _joueur = new(CampId.Joueur, "Joueur", false, 1);
	private readonly Camp _ia = new(CampId.IA, "IA", true, -1);
	private readonly List<Camp> _camps = new();

	private Etat _etat;
	private Etat _etatAvantPause;
	private bool _modeTest;
	private Camera3D CameraJeu;
	private Label ScoreLabel;
	private Label ViesLabel;
	private Label NiveauLabel;
	private Label MeilleurLabel;
	private Label MessageLabel;
	private Label BonusLabel;
	private PauseMenu PauseMenu;
	private PackedScene BriqueScene;
	private PackedScene BalleScene;
	private PackedScene CapsuleScene;
	private PackedScene ExplosionScene;
	private Node3D MurGauche;
	private Node3D MurDroit;
	private readonly HudCamp _hudJoueur = new();
	private readonly HudCamp _hudIA = new();

	[Export] public int ViesVilleDepart = 3;
	[Export] public int ViesVilleMax = 6;
	[Export] public float VitesseBalleBase = 5.0f;
	[Export] public float VitesseBalleMax = 8.0f;
	[Export] public int NombreMaxBallesParCamp = 5;
	[Export] public float ProbaCapsule = 0.3f;
	[Export] public double DureeBonus = 8.0;
	[Export] public double DureeMessageBonus = 1.6;
	[Export] public double CadenceLaser = 0.22;
	[Export] public double DelaiLancementIA = 0.75;

	private const float LargeurTableauScore = 258.0f;
	private const float HauteurTableauScore = 392.0f;
	private const int NombreLignesBonusHud = 5;
	private const float VilleExplosionZ = 0.12f;
	private const float VilleBaseY = -0.36f;

	private static readonly string[][] MotifDuel =
	{
		new[] { "1X11X1", "222222", "11BB11", "1C11C1" },
		new[] { "S1111S", "1M22M1", "11XX11", "CC11CC" },
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
		new(CapsuleScript.TypeBonus.Laser, 10.0f),
		new(CapsuleScript.TypeBonus.MultiBalle, 9.0f),
		new(CapsuleScript.TypeBonus.BouclierBas, 8.0f),
		new(CapsuleScript.TypeBonus.ScoreDouble, 8.0f),
		new(CapsuleScript.TypeBonus.VieBonus, 7.0f),
		new(CapsuleScript.TypeBonus.BallePercante, 7.0f),
		new(CapsuleScript.TypeBonus.BarrePetite, 4.0f),
		new(CapsuleScript.TypeBonus.BalleRapide, 4.0f),
	};

	public override void _Ready()
	{
		_modeTest = Array.IndexOf(OS.GetCmdlineArgs(), "--test") >= 0
			|| Array.IndexOf(OS.GetCmdlineUserArgs(), "--test") >= 0;

		_rng.Randomize();
		SettingsManager.Charger();
		SettingsManager.Appliquer(GetTree());
		ProcessMode = Node.ProcessModeEnum.Always;

		_camps.Clear();
		_camps.Add(_joueur);
		_camps.Add(_ia);

		InitialiserNoeudsCamp(_joueur, "", new Vector3(0.0f, 0.8f, 0.0f));
		InitialiserNoeudsCamp(_ia, "_IA", new Vector3(0.0f, 8.2f, 0.0f));

		CameraJeu = GetNode<Camera3D>("Camera3D");
		MurGauche = GetNode<Node3D>("Mur_Gauche");
		MurDroit = GetNode<Node3D>("Mur_Droit");
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

		foreach (Camp camp in _camps)
		{
			ChargerVille(camp);
			camp.ZoneDegats.BodyEntered += (body) => OnZoneDegats(camp, body);
		}

		CreerLabelsSupplementaires();
		ChargerSons();
		NouvellePartie();

		if (_modeTest)
			GD.Print("[TEST] Mode duel actif.");
	}

	public override void _Process(double delta)
	{
		MettreAJourPositionTableauScore();
		MettreAJourIA(delta);

		if (_modeTest)
			ProcessTest();

		if (_etat != Etat.Pause)
			foreach (Camp camp in _camps)
				MettreAJourBonusTemporaires(camp, delta);

		if (_etat == Etat.Pause && PauseMenu.OptionsOuvertes)
			return;

		if (Input.IsActionJustPressed("ui_cancel")
			&& _etat is Etat.EnJeu or Etat.AttenteLancement or Etat.Pause)
		{
			BasculerPause();
			return;
		}

		if (!Input.IsActionJustPressed("lancer_balle"))
			return;

		switch (_etat)
		{
			case Etat.AttenteLancement:
				LancerCamp(_joueur);
				if (LancerCamp(_ia))
					_etat = Etat.EnJeu;
				MessageLabel.Visible = false;
				break;
			case Etat.EnJeu:
				if (LancerBallesCollees(_joueur))
					AfficherBonus(_joueur, "Balle relancee");
				else if (_joueur.LaserRestant > 0.0)
					TirerLaser(_joueur);
				break;
			case Etat.GameOver:
			case Etat.Victoire:
				NouvellePartie();
				break;
		}
	}

	private void InitialiserNoeudsCamp(Camp camp, string suffixe, Vector3 positionBalleRepos)
	{
		camp.Bar = GetNode<BarScript>($"Bar{suffixe}");
		camp.ZoneDegats = GetNode<Area3D>($"ZoneMort{suffixe}");
		camp.Briques = GetNode<Node3D>($"Briques{suffixe}");
		camp.Balles = GetNode<Node3D>($"Balles{suffixe}");
		camp.Capsules = GetNode<Node3D>($"Capsules{suffixe}");
		camp.Ville = GetNode<Node3D>($"Ville{suffixe}");
		camp.PositionBalleRepos = positionBalleRepos;
		camp.PositionVilleLocale = camp.Ville.Position;
		camp.Bar.Configurer(camp.ControleIA, camp.SensAttaque);
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
		GetTree().ChangeSceneToFile("res://MainMenu.tscn");
	}

	private void NouvellePartie()
	{
		foreach (Camp camp in _camps)
		{
			camp.Score = 0;
			camp.Vies = ViesVilleDepart;
			camp.Combo = 0;
			camp.MeilleurCombo = 0;
			camp.BriquesDetruites = 0;
			camp.CapsulesRamassees = 0;
			camp.VitesseBalle = VitesseBalleBase;
			camp.Bar.Position = new Vector3(0.0f, camp.Bar.Position.Y, camp.Bar.Position.Z);
			ReinitialiserBonusTemporaires(camp);
			GenererBriques(camp);
			MettreAJourVille(camp);
			PreparerLancement(camp);
		}

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
		CreerBalle(camp, camp.PositionBalleRepos);
	}

	private bool LancerCamp(Camp camp)
	{
		bool lance = false;
		foreach (Node n in camp.Balles.GetChildren())
		{
			if (n is BalleScript b)
			{
				b.SensAttaque = camp.SensAttaque;
				b.Proprietaire = camp.Cle;
				b.Lancer();
				lance = true;
			}
		}

		return lance;
	}

	private bool LancerBallesCollees(Camp camp)
	{
		bool relance = false;
		foreach (Node n in camp.Balles.GetChildren())
		{
			if (n is BalleScript b && b.EstCollee)
			{
				b.Proprietaire = camp.Cle;
				b.LancerDepuisBarre(_rng.RandfRange(-0.45f, 0.45f), camp.SensAttaque);
				relance = true;
			}
		}
		return relance;
	}

	private void GenererBriques(Camp camp)
	{
		ViderConteneur(camp.Briques);

		string[] motif = MotifDuel[camp.Id == CampId.Joueur ? 0 : 1];
		int rangees = motif.Length;
		int colonnes = 0;
		foreach (string ligne in motif)
			colonnes = Mathf.Max(colonnes, ligne.Length);

		const float pasX = 0.6f;
		const float pasY = 0.32f;
		float debutX = -(colonnes - 1) * pasX / 2.0f;
		float premierY = camp.Id == CampId.Joueur ? 4.5f : 5.5f;
		camp.BriquesRestantes = 0;

		for (int rang = 0; rang < rangees; rang++)
		{
			string ligne = motif[rang];
			float y = premierY - camp.SensAttaque * rang * pasY;
			for (int col = 0; col < ligne.Length; col++)
			{
				if (!LireBrique(ligne[col], out int resistance, out BriqueScript.TypeBrique type, out int points, out Color couleur))
					continue;

				var brique = BriqueScene.Instantiate<BriqueScript>();
				camp.Briques.AddChild(brique);
				brique.Position = new Vector3(debutX + col * pasX, y, 0.0f);
				brique.Points = points;
				brique.Initialiser(resistance, couleur, type);
				if (brique.EstDestructible)
					camp.BriquesRestantes++;
			}
		}
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

	private BalleScript CreerBalle(Camp camp, Vector3 position)
	{
		var balle = BalleScene.Instantiate<BalleScript>();
		camp.Balles.AddChild(balle);
		balle.VitesseCible = camp.VitesseBalle;
		balle.SensAttaque = camp.SensAttaque;
		balle.Proprietaire = camp.Cle;
		balle.Percante = camp.BallePercanteRestant > 0.0;
		balle.Positionner(position);
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
		return cle == _ia.Cle ? _ia : _joueur;
	}

	private Camp CampParBarre(BarScript bar)
	{
		return bar == _ia.Bar ? _ia : _joueur;
	}

	private Camp CampParBrique(BriqueScript brique)
	{
		return brique.GetParent() == _ia.Briques ? _ia : _joueur;
	}

	private Camp CampOppose(Camp camp)
	{
		return camp == _joueur ? _ia : _joueur;
	}

	private void OnBalleCollision(BalleScript balle, Node body)
	{
		if (body.IsInGroup("briques") && body is BriqueScript brique && !brique.IsQueuedForDeletion())
		{
			Camp campBrique = CampParBrique(brique);
			Camp proprietaire = balle != null ? CampParCle(balle.Proprietaire) : CampOppose(campBrique);
			FrapperBrique(campBrique, proprietaire, brique, balle);
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
			{
				balle.Proprietaire = campBarre.Cle;
				balle.SensAttaque = campBarre.SensAttaque;
			}

			if (campBarre.AimantRestant > 0.0 && balle != null)
			{
				balle.CollerA(bar);
				AfficherBonus(campBarre, "Aimant pret");
			}
			else if (balle != null)
			{
				float offset = (balle.GlobalPosition.X - bar.GlobalPosition.X) / bar.DemiLargeur;
				balle.RebondSurBarre(offset, campBarre.SensAttaque);
			}
			Jouer("rebond");
		}
		else if (body is StaticBody3D)
		{
			Jouer("rebond");
		}
	}

	private void FrapperBrique(Camp campBrique, Camp proprietaire, BriqueScript brique, BalleScript balle = null, bool destructionDirecte = false, bool verifierOuverture = true)
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
		bool detruite = destructionDirecte;

		if (destructionDirecte)
			brique.QueueFree();
		else
			detruite = brique.Frapper();

		if (!detruite)
		{
			Jouer("rebond");
			return;
		}

		EnregistrerBriqueDetruite(proprietaire, campBrique, points);
		Jouer("casse");
		float tailleExplosion = type == BriqueScript.TypeBrique.Explosive ? 1.35f : 1.0f;
		Color couleurExplosion = type == BriqueScript.TypeBrique.Explosive ? new Color(1.0f, 0.24f, 0.08f) : couleur;
		Exploser(pos, couleurExplosion, tailleExplosion);

		if (type == BriqueScript.TypeBrique.CapsuleGarantie)
			LacherCapsule(campBrique, pos, TirerTypeCapsule(proprietaire));
		else
			PeutEtreLacherCapsule(campBrique, proprietaire, pos);

		if (type == BriqueScript.TypeBrique.Explosive)
			DetruireBriquesVoisines(campBrique, proprietaire, pos);

		if (verifierOuverture && campBrique.BriquesRestantes <= 0)
		{
			AfficherBonus(proprietaire, $"{proprietaire.Nom} a ouvert le passage");
			MettreAJourHud();
		}
	}

	private void EnregistrerBriqueDetruite(Camp proprietaire, Camp campBrique, int points)
	{
		proprietaire.Combo++;
		proprietaire.MeilleurCombo = Mathf.Max(proprietaire.MeilleurCombo, proprietaire.Combo);
		proprietaire.BriquesDetruites++;
		campBrique.BriquesRestantes--;
		proprietaire.Score += points * CalculerMultiplicateurScore(proprietaire);
		MettreAJourHud();
	}

	private void DetruireBriquesVoisines(Camp campBrique, Camp proprietaire, Vector3 centre)
	{
		List<BriqueScript> voisines = new();
		foreach (Node n in campBrique.Briques.GetChildren())
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
			FrapperBrique(campBrique, proprietaire, b, null, true, false);
	}

	private void OnZoneDegats(Camp campTouche, Node body)
	{
		if (body is not BalleScript balle || !balle.IsInGroup("balle"))
			return;

		Camp attaquant = CampParCle(balle.Proprietaire);
		balle.QueueFree();
		if (_etat == Etat.EnJeu || _etat == Etat.AttenteLancement)
			EndommagerVille(campTouche, attaquant);
	}

	private void EndommagerVille(Camp campTouche, Camp attaquant)
	{
		campTouche.Vies--;
		campTouche.Combo = 0;
		attaquant.Score += 250 * CalculerMultiplicateurScore(attaquant);
		MettreAJourVille(campTouche);
		DeclencherExplosionsVille(campTouche, campTouche.Vies <= 0);
		MettreAJourHud();

		if (campTouche.Vies <= 0)
		{
			FinPartie(attaquant, campTouche);
			return;
		}

		Jouer("perte");
		if (CompterBalles(campTouche) == 0)
			CreerBalle(campTouche, campTouche.PositionBalleRepos);
	}

	private void FinPartie(Camp gagnant, Camp perdant)
	{
		if (_etat is Etat.GameOver or Etat.Victoire)
			return;

		ArreterBalles();
		_etat = gagnant == _joueur ? Etat.Victoire : Etat.GameOver;
		Jouer(gagnant == _joueur ? "niveau" : "gameover");
		MessageLabel.Text = gagnant == _joueur
			? $"Victoire !\nVille IA detruite\n{ResumePartie()}\nEspace pour rejouer"
			: $"Defaite\nVotre ville est detruite\n{ResumePartie()}\nEspace pour rejouer";
		MessageLabel.Visible = true;
	}

	private string ResumePartie()
	{
		return $"Joueur : {_joueur.Score} pts, {_joueur.BriquesDetruites} briques\nIA : {_ia.Score} pts, {_ia.BriquesDetruites} briques";
	}

	private void PeutEtreLacherCapsule(Camp campBrique, Camp proprietaire, Vector3 position)
	{
		if (_rng.Randf() <= ProbaCapsule)
			LacherCapsule(campBrique, position, TirerTypeCapsule(proprietaire));
	}

	private CapsuleScript LacherCapsule(Camp campBrique, Vector3 position, CapsuleScript.TypeBonus type)
	{
		Camp destinataire = CampOppose(campBrique);
		var capsule = CapsuleScene.Instantiate<CapsuleScript>();
		destinataire.Capsules.AddChild(capsule);
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
		float retard = camp.Vies <= 1 ? 0.12f : 0.0f;
		if (EstMalus(entree.Type))
			return entree.Poids * (1.0f + retard);

		return entree.Poids;
	}

	private bool BonusAutorise(Camp camp, CapsuleScript.TypeBonus type) => type switch
	{
		CapsuleScript.TypeBonus.BarrePetite => camp.Bar.DemiLargeur > 0.32f,
		CapsuleScript.TypeBonus.BalleRapide => camp.VitesseBalle < VitesseBalleMax * 0.90f,
		CapsuleScript.TypeBonus.MultiBalle => CompterBalles(camp) < NombreMaxBallesParCamp,
		CapsuleScript.TypeBonus.VieBonus => camp.Vies < ViesVilleMax,
		CapsuleScript.TypeBonus.Aimant => camp.AimantRestant <= 0.0,
		CapsuleScript.TypeBonus.Laser => camp.LaserRestant <= DureeBonus * 0.5,
		CapsuleScript.TypeBonus.BouclierBas => camp.BouclierRestant <= 0.0,
		CapsuleScript.TypeBonus.ScoreDouble => camp.ScoreDoubleRestant <= DureeBonus * 0.5,
		CapsuleScript.TypeBonus.BallePercante => camp.BallePercanteRestant <= DureeBonus * 0.5,
		_ => true,
	};

	private static bool EstMalus(CapsuleScript.TypeBonus type)
	{
		return type is CapsuleScript.TypeBonus.BarrePetite or CapsuleScript.TypeBonus.BalleRapide;
	}

	private void OnCapsule(Camp camp, CapsuleScript capsule, Node body)
	{
		if (capsule.IsQueuedForDeletion() || body != camp.Bar)
			return;

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
				camp.Vies = Mathf.Min(ViesVilleMax, camp.Vies + 1);
				AfficherBonus(camp, "+ Vie ville");
				MettreAJourVille(camp);
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
			balle.RebondSurBarre(_rng.RandfRange(-0.85f, 0.85f), camp.SensAttaque);
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

	private void MettreAJourBonusTemporaires(Camp camp, double delta)
	{
		Decompter(ref camp.MessageBonusRestant, delta, () => BonusLabel.Visible = false);
		Decompter(ref camp.AimantRestant, delta);
		Decompter(ref camp.LaserRestant, delta);
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
		camp.BouclierRestant = 0.0;
		camp.ScoreDoubleRestant = 0.0;
		camp.BallePercanteRestant = 0.0;
		camp.VitesseTemporaireRestante = 0.0;
		camp.LaserCooldownRestant = 0.0;
		if (BonusLabel != null)
			BonusLabel.Visible = false;
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

	private void TirerLaser(Camp camp)
	{
		if (camp.LaserCooldownRestant > 0.0)
			return;

		camp.LaserCooldownRestant = CadenceLaser;
		CreerTirLaser(camp, camp.Bar.GlobalPosition + new Vector3(-0.24f, camp.SensAttaque * 0.18f, 0.0f));
		CreerTirLaser(camp, camp.Bar.GlobalPosition + new Vector3(0.24f, camp.SensAttaque * 0.18f, 0.0f));
	}

	private void CreerTirLaser(Camp camp, Vector3 position)
	{
		var tir = new LaserTir
		{
			Name = $"LaserTir_{camp.Nom}",
			SensVertical = camp.SensAttaque,
			LimiteY = camp.Id == CampId.Joueur ? 9.8f : -0.8f,
		};
		AddChild(tir);
		tir.GlobalPosition = position;

		var mesh = new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = new Vector3(0.06f, 0.32f, 0.06f) },
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = camp.Id == CampId.Joueur ? new Color(0.15f, 0.95f, 1.0f) : new Color(1.0f, 0.45f, 0.18f),
				EmissionEnabled = true,
				Emission = camp.Id == CampId.Joueur ? new Color(0.15f, 0.95f, 1.0f) : new Color(1.0f, 0.45f, 0.18f),
				EmissionEnergyMultiplier = 1.5f,
			},
		};
		tir.AddChild(mesh);
		tir.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.08f, 0.34f, 0.08f) } });
		tir.BodyEntered += (body) => OnLaserCollision(camp, tir, body);
	}

	private void OnLaserCollision(Camp camp, LaserTir tir, Node body)
	{
		if (!IsInstanceValid(tir) || tir.IsQueuedForDeletion())
			return;

		if (body is BriqueScript brique)
			FrapperBrique(CampParBrique(brique), camp, brique);

		tir.QueueFree();
	}

	private void ActiverBouclier(Camp camp)
	{
		if (IsInstanceValid(camp.Bouclier))
			return;

		camp.Bouclier = new StaticBody3D { Name = $"Bouclier_{camp.Nom}" };
		AddChild(camp.Bouclier);
		camp.Bouclier.GlobalPosition = camp.Bar.GlobalPosition - new Vector3(0.0f, camp.SensAttaque * 0.52f, 0.0f);

		var couleur = camp.Id == CampId.Joueur ? new Color(0.15f, 0.75f, 1.0f, 0.75f) : new Color(1.0f, 0.42f, 0.18f, 0.75f);
		var mesh = new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = new Vector3(4.0f, 0.08f, 0.12f) },
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = couleur,
				EmissionEnabled = true,
				Emission = couleur,
				EmissionEnergyMultiplier = 0.8f,
			},
		};
		camp.Bouclier.AddChild(mesh);
		camp.Bouclier.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(4.0f, 0.08f, 0.12f) } });
	}

	private void DesactiverBouclier(Camp camp)
	{
		if (IsInstanceValid(camp.Bouclier))
			camp.Bouclier.QueueFree();
		camp.Bouclier = null;
	}

	private void MettreAJourIA(double delta)
	{
		if (_ia?.Bar == null)
			return;

		BalleScript cible = ChoisirBalleMenacanteIA();
		_ia.Bar.DefinirCibleIA(cible?.GlobalPosition.X);

		if (_etat == Etat.AttenteLancement)
		{
			_ia.LaserCooldownRestant += delta;
			if (_ia.LaserCooldownRestant >= DelaiLancementIA)
			{
				LancerCamp(_ia);
				_ia.LaserCooldownRestant = 0.0;
			}
		}
		else if (_etat == Etat.EnJeu)
		{
			LancerBallesCollees(_ia);
			if (_ia.LaserRestant > 0.0)
				TirerLaser(_ia);
		}
	}

	private BalleScript ChoisirBalleMenacanteIA()
	{
		BalleScript meilleure = null;
		float meilleureDistance = float.MaxValue;
		foreach (Camp camp in _camps)
		{
			foreach (Node n in camp.Balles.GetChildren())
			{
				if (n is not BalleScript balle || balle.IsQueuedForDeletion())
					continue;

				bool menace = balle.GlobalPosition.Y > 5.0f || balle.LinearVelocity.Y > 0.0f;
				float distance = Math.Abs(balle.GlobalPosition.Y - _ia.Bar.GlobalPosition.Y);
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
			Node3D racine = new Node3D
			{
				Name = "BatimentVille",
				Position = new Vector3(plan.X, VilleBaseY, 0.0f),
				Scale = new Vector3(1.0f, camp.SensAttaque, 1.0f),
			};
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

		int etatDegats = camp.Vies switch
		{
			>= 3 => 0,
			2 => 1,
			1 => 2,
			_ => 3,
		};

		foreach (BatimentVille batiment in camp.BatimentsVille)
			AppliquerEtatBatiment(batiment, CalculerDegatsBatiment(batiment.OrdreDegats, etatDegats));
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

		BonusLabel = CreerLabel("BonusLabel", 0.0f, 132.0f, 26);
		BonusLabel.HorizontalAlignment = HorizontalAlignment.Center;
		BonusLabel.AnchorRight = 1.0f;
		BonusLabel.OffsetRight = 0.0f;
		BonusLabel.Visible = false;
		BonusLabel.AddThemeColorOverride("font_color", new Color(0.84f, 1.0f, 0.96f));
		BonusLabel.AddThemeColorOverride("font_outline_color", new Color(0.0f, 0.05f, 0.08f, 0.95f));
		BonusLabel.AddThemeConstantOverride("outline_size", 5);
		control.AddChild(BonusLabel);
	}

	private static void CacherLabelScene(Label label)
	{
		label.Visible = false;
		label.MouseFilter = Control.MouseFilterEnum.Ignore;
	}

	private void CreerTableauxScore(Control control)
	{
		CreerTableauScoreCamp(control, _hudJoueur, "JOUEUR", false);
		CreerTableauScoreCamp(control, _hudIA, "ADVERSAIRE", true);
		MettreAJourPositionTableauScore();
	}

	private void CreerTableauScoreCamp(Control control, HudCamp hud, string titre, bool retourne)
	{
		hud.Panel = new PanelContainer
		{
			Name = retourne ? "TableauScoreAdversaire" : "TableauScoreJoueur",
			CustomMinimumSize = new Vector2(LargeurTableauScore, HauteurTableauScore),
			Size = new Vector2(LargeurTableauScore, HauteurTableauScore),
			PivotOffset = new Vector2(LargeurTableauScore * 0.5f, HauteurTableauScore * 0.5f),
			RotationDegrees = retourne ? 180.0f : 0.0f,
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

		pile.AddChild(CreerTitreHud(titre));
		hud.Score = CreerLigneInfoHud("Score");
		hud.Vies = CreerLigneInfoHud("Ville");
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
		pile.AddChild(CreerTitreHud("BONUS"));

		for (int i = 0; i < NombreLignesBonusHud; i++)
			pile.AddChild(CreerLigneBonusHud(hud, i));
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

	private static Label CreerLabel(string nom, float gauche, float haut, int taille)
	{
		var label = new Label
		{
			Name = nom,
			OffsetLeft = gauche,
			OffsetTop = haut,
			OffsetRight = gauche + 280.0f,
			OffsetBottom = haut + 28.0f,
			Text = "",
		};
		label.AddThemeFontSizeOverride("font_size", taille);
		return label;
	}

	private void AfficherBonus(Camp camp, string message)
	{
		BonusLabel.Text = $"{camp.Nom} : {message}";
		BonusLabel.Visible = true;
		camp.MessageBonusRestant = DureeMessageBonus;
		MettreAJourTimersBonusHud();
	}

	private static void ViderConteneur(Node conteneur)
	{
		foreach (Node enfant in conteneur.GetChildren())
			enfant.QueueFree();
	}

	private void MettreAJourHud()
	{
		if (_hudJoueur.Score == null || _hudIA.Score == null)
			return;

		MettreAJourHudCamp(_joueur, _hudJoueur);
		MettreAJourHudCamp(_ia, _hudIA);
		MettreAJourTimersBonusHud();
	}

	private void MettreAJourHudCamp(Camp camp, HudCamp hud)
	{
		hud.Score.Text = $"Score : {camp.Score}";
		hud.Vies.Text = $"Ville : {camp.Vies}/{ViesVilleMax}";
		hud.Briques.Text = $"Briques : {camp.BriquesRestantes}";
		hud.Balles.Text = $"Balles : {CompterBalles(camp)}";
		hud.Combo.Text = camp.Combo > 1 ? $"Combo : {camp.Combo} x{CalculerMultiplicateurScore(camp)}" : "Combo : -";
		hud.Etat.Text = _etat switch
		{
			Etat.AttenteLancement => "Etat : lancement",
			Etat.EnJeu => "Etat : duel",
			Etat.Pause => "Etat : pause",
			Etat.Victoire when camp == _joueur => "Etat : victoire",
			Etat.Victoire => "Etat : defaite",
			Etat.GameOver when camp == _joueur => "Etat : defaite",
			Etat.GameOver => "Etat : victoire",
			_ => "Etat : -",
		};
	}

	private void MettreAJourPositionTableauScore()
	{
		if (_hudJoueur.Panel == null || _hudIA.Panel == null)
			return;

		Vector2 tailleViewport = GetViewport().GetVisibleRect().Size;
		Vector2 marge = new Vector2(8.0f, 12.0f);
		float joueurX = marge.X;
		float iaX = Math.Max(marge.X, tailleViewport.X - LargeurTableauScore - marge.X);

		if (CameraJeu != null && MurGauche != null && MurDroit != null)
		{
			const float espaceMur = 6.0f;
			float yAncre = (_joueur.Bar.GlobalPosition.Y + _ia.Bar.GlobalPosition.Y) * 0.5f;
			float murGaucheX = CameraJeu.UnprojectPosition(new Vector3(MurGauche.GlobalPosition.X, yAncre, 0.0f)).X;
			float murDroitX = CameraJeu.UnprojectPosition(new Vector3(MurDroit.GlobalPosition.X, yAncre, 0.0f)).X;
			joueurX = murGaucheX - LargeurTableauScore - espaceMur;
			iaX = murDroitX + espaceMur;
		}

		float limiteX = Math.Max(marge.X, tailleViewport.X - LargeurTableauScore - marge.X);
		_hudJoueur.Panel.Position = new Vector2(Mathf.Clamp(joueurX, marge.X, limiteX), marge.Y);
		_hudIA.Panel.Position = new Vector2(Mathf.Clamp(iaX, marge.X, limiteX), marge.Y);
		_hudJoueur.Panel.Size = new Vector2(LargeurTableauScore, HauteurTableauScore);
		_hudIA.Panel.Size = new Vector2(LargeurTableauScore, HauteurTableauScore);
		_hudJoueur.Panel.PivotOffset = _hudJoueur.Panel.Size * 0.5f;
		_hudIA.Panel.PivotOffset = _hudIA.Panel.Size * 0.5f;
	}

	private void MettreAJourTimersBonusHud()
	{
		if (_hudJoueur.NomsBonus[0] == null || _hudIA.NomsBonus[0] == null)
			return;

		MettreAJourTimersBonusHud(_joueur, _hudJoueur);
		MettreAJourTimersBonusHud(_ia, _hudIA);
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
