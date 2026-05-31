using Godot;
using System;
using System.Collections.Generic;

public partial class GameManager : Node3D
{
	private enum Etat
	{
		Menu,
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

	private BarScript Bar;
	private Area3D ZoneMort;
	private Node3D Briques;
	private Node3D Balles;
	private Node3D Capsules;
	private Label ScoreLabel;
	private Label ViesLabel;
	private Label NiveauLabel;
	private Label MeilleurLabel;
	private Label MessageLabel;
	private Label BonusLabel;
	private Label ComboLabel;
	private Label ModeLabel;
	private PauseMenu PauseMenu;
	private PackedScene BriqueScene;
	private PackedScene BalleScene;
	private PackedScene CapsuleScene;
	private PackedScene ExplosionScene;

	private readonly Dictionary<string, AudioStreamPlayer> _sons = new();
	private readonly RandomNumberGenerator _rng = new();

	[Export] public int ViesDepart = 3;
	[Export] public int ViesMax = 6;
	[Export] public float VitesseBalleBase = 5.0f;
	[Export] public float VitesseBalleMax = 8.0f;
	[Export] public int NombreMaxBalles = 5;
	[Export] public float ProbaCapsule = 0.3f;
	[Export] public double DureeBonus = 8.0;
	[Export] public double DureeMessageBonus = 1.6;
	[Export] public double CadenceLaser = 0.22;

	private static readonly Vector3 PositionBalleRepos = new Vector3(0.0f, 0.8f, 0.0f);

	private static readonly string[][] Niveaux =
	{
		new[] { "111111", "111111", "111111", "111111" },
		new[] { "222222", "111111", "11CC11", "2....2" },
		new[] { "..33..", ".3223.", "32CC23", "111111" },
		new[] { "1X11X1", "222222", "11BB11", "1C11C1" },
		new[] { "S1111S", "1M22M1", "11XX11", "CC11CC" },
		new[] { "B2222B", "1S11S1", "1XCCX1", "111111" },
		new[] { "SS11SS", "M2222M", "1XBBX1", "C1111C" },
		new[] { "333333", "2MSSM2", "1XCCX1", "B1111B" },
		new[] { "S3XX3S", "M2222M", "CCSSCC", "111111" },
		new[] { "B3SS3B", "2XMMX2", "1CCCC1", "111111" },
		new[] { "SXXSXX", "333333", "M2CC2M", "B1111B" },
		new[] { "SS33SS", "X2MM2X", "CCBBCC", "111111" },
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

	private const string CheminMeilleurScore = "user://meilleur_score.dat";
	private const string CheminMeilleurScoreCampagne = "user://meilleur_score_campagne.dat";
	private const string CheminMeilleurScoreArcade = "user://meilleur_score_arcade.dat";

	private Etat _etat;
	private Etat _etatAvantPause;
	private int _score;
	private int _vies;
	private int _niveauActuel;
	private int _briquesRestantes;
	private int _meilleurScore;
	private int _meilleurScoreCampagne;
	private int _meilleurScoreArcade;
	private int _combo;
	private int _meilleurCombo;
	private int _briquesDetruites;
	private int _capsulesRamassees;
	private float _vitesseBalle;
	private bool _modeTest;
	private bool _modeArcade;
	private double _messageBonusRestant;
	private double _aimantRestant;
	private double _laserRestant;
	private double _bouclierRestant;
	private double _scoreDoubleRestant;
	private double _ballePercanteRestant;
	private double _vitesseTemporaireRestante;
	private double _laserCooldownRestant;
	private StaticBody3D _bouclierBas;

	public override void _Ready()
	{
		_modeTest = Array.IndexOf(OS.GetCmdlineArgs(), "--test") >= 0
			|| Array.IndexOf(OS.GetCmdlineUserArgs(), "--test") >= 0;

		_rng.Randomize();
		SettingsManager.Charger();
		SettingsManager.Appliquer(GetTree());
		ProcessMode = Node.ProcessModeEnum.Always;

		Bar = GetNode<BarScript>("Bar");
		ZoneMort = GetNode<Area3D>("ZoneMort");
		Briques = GetNode<Node3D>("Briques");
		Balles = GetNode<Node3D>("Balles");
		Capsules = GetNode<Node3D>("Capsules");
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

		CreerLabelsSupplementaires();
		ChargerSons();

		ZoneMort.BodyEntered += OnBalleSortie;
		ChargerMeilleurScore();
		NouvellePartie();

		if (_modeTest)
			GD.Print("[TEST] Mode test actif. Niveaux : ", Niveaux.Length, ", meilleur score : ", _meilleurScore);
	}

	public override void _Process(double delta)
	{
		if (_modeTest)
			ProcessTest();

		if (_etat != Etat.Pause)
			MettreAJourBonusTemporaires(delta);

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
				LancerBalles();
				_etat = Etat.EnJeu;
				MessageLabel.Visible = false;
				break;
			case Etat.EnJeu:
				if (LancerBallesCollees())
					AfficherBonus("Balle relancee");
				else if (_laserRestant > 0.0)
					TirerLaser();
				break;
			case Etat.GameOver:
			case Etat.Victoire:
				NouvellePartie();
				break;
		}
	}

	private void BasculerPause()
	{
		if (_etat == Etat.Pause)
		{
			ReprendrePartie();
		}
		else
		{
			_etatAvantPause = _etat;
			_etat = Etat.Pause;
			MessageLabel.Visible = false;
			PauseMenu.Ouvrir();
			GetTree().Paused = true;
		}
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
		_score = 0;
		_vies = ViesDepart;
		_niveauActuel = 0;
		_combo = 0;
		_meilleurCombo = 0;
		_briquesDetruites = 0;
		_capsulesRamassees = 0;
		_modeArcade = false;
		_vitesseBalle = CalculerVitesseNiveau();
		ReinitialiserBonusTemporaires();
		GenererNiveau();
		MettreAJourHud();
		PreparerLancement("Appuyez sur Espace pour lancer");
	}

	private void PreparerLancement(string message)
	{
		ViderConteneur(Balles);
		ViderConteneur(Capsules);
		Bar.Redimensionner(1.0f, 0.0);
		_combo = 0;
		_vitesseBalle = CalculerVitesseNiveau();
		ReinitialiserBonusTemporaires();
		CreerBalle(PositionBalleRepos);
		_etat = Etat.AttenteLancement;
		MessageLabel.Text = message;
		MessageLabel.Visible = true;
		MettreAJourHud();
	}

	private void LancerBalles()
	{
		foreach (Node n in Balles.GetChildren())
			if (n is BalleScript b)
				b.Lancer();
	}

	private bool LancerBallesCollees()
	{
		bool relance = false;
		foreach (Node n in Balles.GetChildren())
		{
			if (n is BalleScript b && b.EstCollee)
			{
				b.LancerDepuisBarre(_rng.RandfRange(-0.45f, 0.45f));
				relance = true;
			}
		}
		return relance;
	}

	private void GenererNiveau()
	{
		ViderConteneur(Briques);

		string[] motif = _modeArcade ? GenererMotifArcade() : Niveaux[Mathf.Clamp(_niveauActuel, 0, Niveaux.Length - 1)];
		int rangees = motif.Length;
		int colonnes = 0;
		foreach (string ligne in motif)
			colonnes = Mathf.Max(colonnes, ligne.Length);

		const float pasX = 0.6f;
		const float pasY = 0.32f;
		const float hautY = 4.5f;
		float debutX = -(colonnes - 1) * pasX / 2.0f;

		_briquesRestantes = 0;

		for (int rang = 0; rang < rangees; rang++)
		{
			string ligne = motif[rang];
			float y = hautY - rang * pasY;

			for (int col = 0; col < ligne.Length; col++)
			{
				if (!LireBrique(ligne[col], out int resistance, out BriqueScript.TypeBrique type, out int points, out Color couleur))
					continue;

				var brique = BriqueScene.Instantiate<BriqueScript>();
				Briques.AddChild(brique);
				brique.Position = new Vector3(debutX + col * pasX, y, 0.0f);
				brique.Points = points;
				brique.Initialiser(resistance, couleur, type);

				if (brique.EstDestructible)
					_briquesRestantes++;
			}
		}
	}

	private string[] GenererMotifArcade()
	{
		int profondeur = Mathf.Max(0, _niveauActuel - Niveaux.Length);
		int rangees = Mathf.Clamp(4 + profondeur / 3, 4, 8);
		string[] motif = new string[rangees];

		for (int y = 0; y < rangees; y++)
		{
			char[] ligne = new char[6];
			for (int x = 0; x < ligne.Length; x++)
			{
				float tirage = _rng.Randf();
				ligne[x] = tirage switch
				{
					< 0.08f => '.',
					< 0.15f => 'S',
					< 0.25f => 'X',
					< 0.34f => 'C',
					< 0.42f => 'M',
					< 0.49f => 'B',
					< 0.70f => '1',
					< 0.90f => '2',
					_ => '3',
				};
			}
			motif[y] = new string(ligne);
		}

		return motif;
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

	private BalleScript CreerBalle(Vector3 position)
	{
		var balle = BalleScene.Instantiate<BalleScript>();
		Balles.AddChild(balle);
		balle.VitesseCible = _vitesseBalle;
		balle.Percante = _ballePercanteRestant > 0.0;
		balle.Positionner(position);
		balle.BodyEntered += (body) => OnBalleCollision(balle, body);
		return balle;
	}

	private int CompterBalles()
	{
		int n = 0;
		foreach (Node enfant in Balles.GetChildren())
			if (enfant is BalleScript b && !b.IsQueuedForDeletion())
				n++;
		return n;
	}

	private void OnBalleCollision(BalleScript balle, Node body)
	{
		if (body.IsInGroup("briques") && body is BriqueScript brique && !brique.IsQueuedForDeletion())
		{
			FrapperBrique(brique, balle);
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
			if (_aimantRestant > 0.0 && balle != null)
			{
				balle.CollerA(bar);
				AfficherBonus("Aimant pret");
			}
			else if (balle != null)
			{
				float offset = (balle.GlobalPosition.X - bar.GlobalPosition.X) / bar.DemiLargeur;
				balle.RebondSurBarre(offset);
			}
			Jouer("rebond");
		}
		else if (body is StaticBody3D)
		{
			Jouer("rebond");
		}
	}

	private void FrapperBrique(BriqueScript brique, BalleScript balle = null, bool destructionDirecte = false, bool verifierNiveau = true)
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

		EnregistrerBriqueDetruite(points);
		Jouer("casse");
		Exploser(pos, couleur);

		if (type == BriqueScript.TypeBrique.CapsuleGarantie)
			LacherCapsule(pos, TirerTypeCapsule());
		else
			PeutEtreLacherCapsule(pos);

		if (type == BriqueScript.TypeBrique.Explosive)
			DetruireBriquesVoisines(pos);

		if (_modeTest)
			GD.Print($"[TEST] Brique detruite : score={_score}, restantes={_briquesRestantes}");

		if (verifierNiveau && _briquesRestantes <= 0)
			NiveauTermine();
	}

	private void EnregistrerBriqueDetruite(int points)
	{
		_combo++;
		_meilleurCombo = Mathf.Max(_meilleurCombo, _combo);
		_briquesDetruites++;
		_briquesRestantes--;
		_score += points * CalculerMultiplicateurScore();
		MettreAJourHud();
	}

	private void DetruireBriquesVoisines(Vector3 centre)
	{
		List<BriqueScript> voisines = new();
		foreach (Node n in Briques.GetChildren())
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
			FrapperBrique(b, null, true, false);
	}

	private void OnBalleSortie(Node body)
	{
		if (body is BalleScript balle && balle.IsInGroup("balle"))
		{
			balle.QueueFree();
			if (_etat == Etat.EnJeu && CompterBalles() == 0)
				PerdreVie();
		}
	}

	private void NiveauTermine()
	{
		_niveauActuel++;

		if (_niveauActuel >= Niveaux.Length)
			_modeArcade = true;

		_vitesseBalle = CalculerVitesseNiveau();
		GenererNiveau();
		MettreAJourHud();
		Jouer("niveau");

		string message = _modeArcade
			? $"Arcade {_niveauActuel - Niveaux.Length + 1} - Espace pour lancer"
			: $"Niveau {_niveauActuel + 1} - Espace pour lancer";
		PreparerLancement(message);

		if (_modeTest)
			GD.Print($"[TEST] Passage au niveau {_niveauActuel + 1}, briques={_briquesRestantes}, arcade={_modeArcade}");
	}

	private void PerdreVie()
	{
		_vies--;
		_combo = 0;
		MettreAJourHud();

		if (_vies <= 0)
		{
			GameOver();
		}
		else
		{
			Jouer("perte");
			PreparerLancement("Balle perdue ! Espace pour relancer");
		}
	}

	private void PeutEtreLacherCapsule(Vector3 position)
	{
		float proba = Mathf.Max(0.12f, ProbaCapsule - _niveauActuel * 0.01f);
		if (_rng.Randf() <= proba)
			LacherCapsule(position, TirerTypeCapsule());
	}

	private CapsuleScript LacherCapsule(Vector3 position, CapsuleScript.TypeBonus type)
	{
		var capsule = CapsuleScene.Instantiate<CapsuleScript>();
		Capsules.AddChild(capsule);
		capsule.GlobalPosition = position;
		capsule.Initialiser(type, CouleurBonus(type));
		capsule.BodyEntered += (body) => OnCapsule(capsule, body);
		return capsule;
	}

	private CapsuleScript.TypeBonus TirerTypeCapsule()
	{
		float total = 0.0f;
		foreach (TirageCapsule entree in TableCapsules)
			if (BonusAutorise(entree.Type))
				total += PoidsAjuste(entree);

		if (total <= 0.0f)
			return CapsuleScript.TypeBonus.BarreLarge;

		float tirage = _rng.RandfRange(0.0f, total);
		foreach (TirageCapsule entree in TableCapsules)
		{
			if (!BonusAutorise(entree.Type))
				continue;

			tirage -= PoidsAjuste(entree);
			if (tirage <= 0.0f)
				return entree.Type;
		}

		return CapsuleScript.TypeBonus.BarreLarge;
	}

	private float PoidsAjuste(TirageCapsule entree)
	{
		float profondeur = Mathf.Max(0, _niveauActuel);
		if (EstMalus(entree.Type))
			return entree.Poids * Mathf.Min(2.0f, 1.0f + profondeur * 0.05f);

		return entree.Poids * Mathf.Max(0.55f, 1.0f - profondeur * 0.035f);
	}

	private bool BonusAutorise(CapsuleScript.TypeBonus type) => type switch
	{
		CapsuleScript.TypeBonus.BarrePetite => Bar.DemiLargeur > 0.32f,
		CapsuleScript.TypeBonus.BalleRapide => _vitesseBalle < VitesseBalleMax * 0.90f,
		CapsuleScript.TypeBonus.MultiBalle => CompterBalles() < NombreMaxBalles,
		CapsuleScript.TypeBonus.VieBonus => _vies < ViesMax,
		CapsuleScript.TypeBonus.Aimant => _aimantRestant <= 0.0,
		CapsuleScript.TypeBonus.Laser => _laserRestant <= DureeBonus * 0.5,
		CapsuleScript.TypeBonus.BouclierBas => _bouclierRestant <= 0.0,
		CapsuleScript.TypeBonus.ScoreDouble => _scoreDoubleRestant <= DureeBonus * 0.5,
		CapsuleScript.TypeBonus.BallePercante => _ballePercanteRestant <= DureeBonus * 0.5,
		_ => true,
	};

	private static bool EstMalus(CapsuleScript.TypeBonus type)
	{
		return type is CapsuleScript.TypeBonus.BarrePetite or CapsuleScript.TypeBonus.BalleRapide;
	}

	private void OnCapsule(CapsuleScript capsule, Node body)
	{
		if (capsule.IsQueuedForDeletion() || body is not BarScript)
			return;

		_capsulesRamassees++;
		AppliquerBonus(capsule.Type);
		Jouer("bonus");
		if (_modeTest)
			GD.Print($"[TEST] Bonus attrape : {capsule.Type}");
		capsule.QueueFree();
	}

	private void AppliquerBonus(CapsuleScript.TypeBonus type)
	{
		switch (type)
		{
			case CapsuleScript.TypeBonus.BarreLarge:
				Bar.Redimensionner(1.6f, DureeBonus);
				AfficherBonus("+ Barre large");
				break;
			case CapsuleScript.TypeBonus.BarrePetite:
				if (BonusAutorise(type))
					Bar.Redimensionner(0.6f, DureeBonus);
				AfficherBonus("- Barre petite");
				break;
			case CapsuleScript.TypeBonus.MultiBalle:
				AjouterBalles(Mathf.Min(2, NombreMaxBalles - CompterBalles()));
				AfficherBonus("Multi-balle");
				break;
			case CapsuleScript.TypeBonus.VieBonus:
				_vies = Mathf.Min(ViesMax, _vies + 1);
				AfficherBonus("+ Vie");
				MettreAJourHud();
				break;
			case CapsuleScript.TypeBonus.BalleLente:
				ChangerVitesseBalles(Mathf.Max(3.5f, CalculerVitesseNiveau() * 0.72f), DureeBonus);
				AfficherBonus("Balle lente");
				break;
			case CapsuleScript.TypeBonus.BalleRapide:
				if (BonusAutorise(type))
					ChangerVitesseBalles(Mathf.Min(VitesseBalleMax, CalculerVitesseNiveau() * 1.35f), DureeBonus);
				AfficherBonus("Balle rapide");
				break;
			case CapsuleScript.TypeBonus.Aimant:
				_aimantRestant = DureeBonus;
				AfficherBonus("Aimant");
				break;
			case CapsuleScript.TypeBonus.Laser:
				_laserRestant = DureeBonus;
				_laserCooldownRestant = 0.0;
				AfficherBonus("Laser");
				break;
			case CapsuleScript.TypeBonus.BouclierBas:
				_bouclierRestant = DureeBonus;
				ActiverBouclierBas();
				AfficherBonus("Bouclier");
				break;
			case CapsuleScript.TypeBonus.ScoreDouble:
				_scoreDoubleRestant = DureeBonus;
				AfficherBonus("Score x2");
				MettreAJourHud();
				break;
			case CapsuleScript.TypeBonus.BallePercante:
				_ballePercanteRestant = DureeBonus;
				AppliquerPercanteAuxBalles(true);
				AfficherBonus("Balle percante");
				break;
		}
	}

	private void AjouterBalles(int nombre)
	{
		if (nombre <= 0)
			return;

		Vector3 origine = PositionBalleRepos;
		foreach (Node n in Balles.GetChildren())
			if (n is BalleScript b && !b.IsQueuedForDeletion())
			{
				origine = b.GlobalPosition;
				break;
			}

		for (int i = 0; i < nombre; i++)
		{
			var balle = CreerBalle(origine);
			balle.RebondSurBarre(_rng.RandfRange(-0.85f, 0.85f));
		}
	}

	private void ChangerVitesseBalles(float vitesse, double duree = 0.0)
	{
		_vitesseBalle = Mathf.Clamp(vitesse, 3.0f, VitesseBalleMax);
		_vitesseTemporaireRestante = duree;
		foreach (Node n in Balles.GetChildren())
			if (n is BalleScript b)
				b.VitesseCible = _vitesseBalle;
	}

	private void MettreAJourBonusTemporaires(double delta)
	{
		Decompter(ref _messageBonusRestant, delta, () => BonusLabel.Visible = false);
		Decompter(ref _aimantRestant, delta);
		Decompter(ref _laserRestant, delta);
		Decompter(ref _laserCooldownRestant, delta);
		Decompter(ref _scoreDoubleRestant, delta, MettreAJourHud);
		Decompter(ref _bouclierRestant, delta, DesactiverBouclierBas);
		Decompter(ref _ballePercanteRestant, delta, () => AppliquerPercanteAuxBalles(false));
		Decompter(ref _vitesseTemporaireRestante, delta, () => ChangerVitesseBalles(CalculerVitesseNiveau()));
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

	private void ReinitialiserBonusTemporaires()
	{
		_messageBonusRestant = 0.0;
		_aimantRestant = 0.0;
		_laserRestant = 0.0;
		_bouclierRestant = 0.0;
		_scoreDoubleRestant = 0.0;
		_ballePercanteRestant = 0.0;
		_vitesseTemporaireRestante = 0.0;
		_laserCooldownRestant = 0.0;
		BonusLabel.Visible = false;
		DesactiverBouclierBas();
		AppliquerPercanteAuxBalles(false);
	}

	private void AppliquerPercanteAuxBalles(bool actif)
	{
		foreach (Node n in Balles.GetChildren())
			if (n is BalleScript b)
				b.Percante = actif;
	}

	private void TirerLaser()
	{
		if (_laserCooldownRestant > 0.0)
			return;

		_laserCooldownRestant = CadenceLaser;
		CreerTirLaser(Bar.GlobalPosition + new Vector3(-0.24f, 0.18f, 0.0f));
		CreerTirLaser(Bar.GlobalPosition + new Vector3(0.24f, 0.18f, 0.0f));
	}

	private void CreerTirLaser(Vector3 position)
	{
		var tir = new LaserTir { Name = "LaserTir" };
		AddChild(tir);
		tir.GlobalPosition = position;

		var mesh = new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = new Vector3(0.06f, 0.32f, 0.06f) },
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.15f, 0.95f, 1.0f),
				EmissionEnabled = true,
				Emission = new Color(0.15f, 0.95f, 1.0f),
				EmissionEnergyMultiplier = 1.5f,
			},
		};
		tir.AddChild(mesh);

		var collision = new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.08f, 0.34f, 0.08f) } };
		tir.AddChild(collision);
		tir.BodyEntered += (body) => OnLaserCollision(tir, body);
	}

	private void OnLaserCollision(LaserTir tir, Node body)
	{
		if (!IsInstanceValid(tir) || tir.IsQueuedForDeletion())
			return;

		if (body is BriqueScript brique)
			FrapperBrique(brique);

		tir.QueueFree();
	}

	private void ActiverBouclierBas()
	{
		if (IsInstanceValid(_bouclierBas))
			return;

		_bouclierBas = new StaticBody3D { Name = "BouclierBas" };
		AddChild(_bouclierBas);
		_bouclierBas.GlobalPosition = new Vector3(0.0f, -0.12f, 0.0f);

		var mesh = new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = new Vector3(4.0f, 0.08f, 0.12f) },
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.15f, 0.75f, 1.0f, 0.75f),
				EmissionEnabled = true,
				Emission = new Color(0.0f, 0.45f, 1.0f),
				EmissionEnergyMultiplier = 0.8f,
			},
		};
		_bouclierBas.AddChild(mesh);
		_bouclierBas.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(4.0f, 0.08f, 0.12f) } });
	}

	private void DesactiverBouclierBas()
	{
		if (IsInstanceValid(_bouclierBas))
			_bouclierBas.QueueFree();
		_bouclierBas = null;
	}

	private void Victoire()
	{
		if (_etat == Etat.Victoire)
			return;

		ArreterBalles();
		_etat = Etat.Victoire;
		EnregistrerMeilleurScore();
		Jouer("niveau");
		MessageLabel.Text = $"Bravo, jeu termine !\n{ResumePartie()}\nEspace pour rejouer";
		MessageLabel.Visible = true;
	}

	private void GameOver()
	{
		if (_etat == Etat.GameOver)
			return;

		ArreterBalles();
		_etat = Etat.GameOver;
		EnregistrerMeilleurScore();
		Jouer("gameover");
		MessageLabel.Text = $"Game Over\n{ResumePartie()}\nEspace pour rejouer";
		MessageLabel.Visible = true;
	}

	private string ResumePartie()
	{
		string mode = _modeArcade ? "Arcade" : "Campagne";
		int niveauAffiche = _modeArcade ? _niveauActuel - Niveaux.Length + 1 : _niveauActuel + 1;
		return $"Mode : {mode}\nScore : {_score}\nNiveau : {niveauAffiche}\nBriques : {_briquesDetruites}\nCapsules : {_capsulesRamassees}\nMeilleur combo : {_meilleurCombo}";
	}

	private void ArreterBalles()
	{
		foreach (Node n in Balles.GetChildren())
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

	private void Exploser(Vector3 position, Color couleur)
	{
		var explosion = ExplosionScene.Instantiate<CpuParticles3D>();
		AddChild(explosion);
		explosion.GlobalPosition = position;
		explosion.Color = couleur;
		explosion.Emitting = true;
		GetTree().CreateTimer(1.0).Timeout += () =>
		{
			if (IsInstanceValid(explosion))
				explosion.QueueFree();
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

	private float CalculerVitesseNiveau()
	{
		return Mathf.Min(VitesseBalleMax, VitesseBalleBase + Mathf.Max(0, _niveauActuel) * 0.22f);
	}

	private int CalculerMultiplicateurScore()
	{
		int comboMul = Mathf.Clamp(1 + _combo / 5, 1, 4);
		int bonusMul = _scoreDoubleRestant > 0.0 ? 2 : 1;
		return comboMul * bonusMul;
	}

	private void ChargerMeilleurScore()
	{
		_meilleurScoreCampagne = LireScore(CheminMeilleurScoreCampagne);
		_meilleurScoreArcade = LireScore(CheminMeilleurScoreArcade);

		int ancienScore = LireScore(CheminMeilleurScore);
		if (ancienScore > _meilleurScoreCampagne)
			_meilleurScoreCampagne = ancienScore;

		_meilleurScore = _meilleurScoreCampagne;
	}

	private static int LireScore(string chemin)
	{
		if (!FileAccess.FileExists(chemin))
			return 0;

		using FileAccess fichier = FileAccess.Open(chemin, FileAccess.ModeFlags.Read);
		return fichier != null && int.TryParse(fichier.GetAsText().Trim(), out int score) ? score : 0;
	}

	private void EnregistrerMeilleurScore()
	{
		if (_modeArcade)
		{
			if (_score <= _meilleurScoreArcade)
				return;

			_meilleurScoreArcade = _score;
			EcrireScore(CheminMeilleurScoreArcade, _score);
		}
		else
		{
			if (_score <= _meilleurScoreCampagne)
				return;

			_meilleurScoreCampagne = _score;
			EcrireScore(CheminMeilleurScoreCampagne, _score);
			EcrireScore(CheminMeilleurScore, _score);
		}

		_meilleurScore = _modeArcade ? _meilleurScoreArcade : _meilleurScoreCampagne;
		MettreAJourHud();
	}

	private static void EcrireScore(string chemin, int score)
	{
		using FileAccess fichier = FileAccess.Open(chemin, FileAccess.ModeFlags.Write);
		fichier?.StoreString(score.ToString());
	}

	private void CreerLabelsSupplementaires()
	{
		var control = GetNode<Control>("HUD/Control");
		ComboLabel = CreerLabel("ComboLabel", 12.0f, 128.0f, 20);
		ModeLabel = CreerLabel("ModeLabel", 12.0f, 158.0f, 20);
		BonusLabel = CreerLabel("BonusLabel", 0.0f, 132.0f, 26);
		BonusLabel.HorizontalAlignment = HorizontalAlignment.Center;
		BonusLabel.AnchorRight = 1.0f;
		BonusLabel.OffsetRight = 0.0f;
		BonusLabel.Visible = false;
		control.AddChild(ComboLabel);
		control.AddChild(ModeLabel);
		control.AddChild(BonusLabel);
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

	private void AfficherBonus(string message)
	{
		BonusLabel.Text = message;
		BonusLabel.Visible = true;
		_messageBonusRestant = DureeMessageBonus;
	}

	private static void ViderConteneur(Node conteneur)
	{
		foreach (Node enfant in conteneur.GetChildren())
			enfant.QueueFree();
	}

	private void MettreAJourHud()
	{
		_meilleurScore = _modeArcade ? _meilleurScoreArcade : _meilleurScoreCampagne;
		ScoreLabel.Text = $"Score : {_score}";
		ViesLabel.Text = $"Vies : {_vies}";
		NiveauLabel.Text = _modeArcade ? $"Arcade : {_niveauActuel - Niveaux.Length + 1}" : $"Niveau : {_niveauActuel + 1}";
		MeilleurLabel.Text = $"Record : {_meilleurScore}";
		ComboLabel.Text = _combo > 1 ? $"Combo : {_combo}  x{CalculerMultiplicateurScore()}" : "Combo : -";
		ModeLabel.Text = _modeArcade ? "Mode : Arcade" : "Mode : Campagne";
	}
}
