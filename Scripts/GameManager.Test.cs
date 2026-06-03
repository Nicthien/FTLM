using Godot;

// Harnais d'auto-test active avec l'argument de ligne de commande --test.
public partial class GameManager : Node3D
{
	private int _testFrame;
	private bool _testErreur;
	private bool _modeSmoke;
	private int _smokeFrame;
	private float _smokeDistanceMax;
	private bool _smokeFuite;

	// Mode de fumee pour 3/4 joueurs : "--smoke3" / "--smoke4" (defaut 4). Tous les
	// camps sont en IA, on laisse jouer ~600 frames et on verifie qu'aucune balle ne
	// s'echappe de l'arene (detection des trous dans les murs du hub).
	private void ConfigurerSmokeSiDemande()
	{
		string[] args = OS.GetCmdlineArgs();
		string[] userArgs = OS.GetCmdlineUserArgs();
		int nb = 0;
		foreach (string a in new[] { "--smoke2", "--smoke3", "--smoke4" })
			if (System.Array.IndexOf(args, a) >= 0 || System.Array.IndexOf(userArgs, a) >= 0)
				nb = a[^1] - '0';

		if (nb == 0)
			return;

		_modeSmoke = true;
		PartieConfig.NombreJoueurs = nb;
		for (int i = 0; i < PartieConfig.MaxJoueurs; i++)
			PartieConfig.DefinirControle(i, PartieConfig.TypeControle.IA);
	}

	private void ProcessSmoke()
	{
		_smokeFrame++;

		if (_smokeFrame == 5)
		{
			_etat = Etat.EnJeu;
			GD.Print($"[SMOKE] Demarrage {_camps.Count} joueurs.");
		}

		if (_smokeFrame > 10)
		{
			foreach (Camp camp in _camps)
				foreach (Node n in camp.Balles.GetChildren())
					if (n is BalleScript b && !b.IsQueuedForDeletion())
					{
						float d = (b.GlobalPosition - Hub).Length();
						_smokeDistanceMax = Mathf.Max(_smokeDistanceMax, d);
						if (d > 9.0f)
							_smokeFuite = true;
					}
		}

		if (_smokeFrame >= 600)
		{
			GD.Print($"[SMOKE] Distance balle max au hub = {_smokeDistanceMax:0.00}");
			GD.Print(_smokeFuite ? "[SMOKE] ECHEC - une balle s'est echappee de l'arene" : "[SMOKE] OK - aucune balle echappee");
			GetTree().Quit(_smokeFuite ? 1 : 0);
		}
	}
	private float _testBarIaDepartX;
	private BalleScript _testBalleAimantIa;
	private int _testBatimentsIaDepart;
	private int _testBatimentsJoueurDepart;

	private BalleScript PremiereBalle(Camp camp)
	{
		foreach (Node n in camp.Balles.GetChildren())
			if (n is BalleScript b && !b.IsQueuedForDeletion())
				return b;
		return null;
	}

	private BriqueScript PremiereBrique()
	{
		foreach (Node n in _briquesCentrales.GetChildren())
			if (n is BriqueScript b && !b.IsQueuedForDeletion() && b.EstDestructible)
				return b;
		return null;
	}

	private BriqueScript PremiereBrique(BriqueScript.TypeBrique type)
	{
		foreach (Node n in _briquesCentrales.GetChildren())
			if (n is BriqueScript b && !b.IsQueuedForDeletion() && b.EstDestructible && b.TypeSpecial == type)
				return b;
		return null;
	}

	private int CompterCapsules(Camp camp)
	{
		int n = 0;
		foreach (Node enfant in camp.Capsules.GetChildren())
			if (enfant is CapsuleScript capsule && !capsule.IsQueuedForDeletion())
				n++;
		return n;
	}

	private CapsuleScript DerniereCapsule(Camp camp)
	{
		CapsuleScript derniere = null;
		foreach (Node enfant in camp.Capsules.GetChildren())
			if (enfant is CapsuleScript capsule && !capsule.IsQueuedForDeletion())
				derniere = capsule;
		return derniere;
	}

	private BatimentVille PremierBatiment(Camp camp, bool detruit)
	{
		foreach (BatimentVille batiment in camp.BatimentsVille)
			if (batiment.Detruit == detruit)
				return batiment;
		return null;
	}

	// Vide l'amas central partage en creditant `proprietaire`. On detruit sans verifier
	// l'ouverture (sinon NiveauTermine regenererait l'amas en pleine boucle), puis on
	// declenche une seule fin de niveau a la fin.
	private void DetruireToutesLesBriques(Camp proprietaire)
	{
		while (true)
		{
			BriqueScript brique = PremiereBrique();
			if (brique == null)
				break;

			FrapperBrique(proprietaire, brique, null, true, false);
		}

		if (_briquesRestantes <= 0)
			NiveauTermine();
	}

	private static void InjecterAction(string action, bool presse)
	{
		var ev = new InputEventAction
		{
			Action = action,
			Pressed = presse,
			Strength = presse ? 1.0f : 0.0f,
		};
		Input.ParseInputEvent(ev);
	}

	private void VerifierTest(bool condition, string message)
	{
		if (condition)
		{
			GD.Print($"[TEST] OK - {message}");
			return;
		}

		_testErreur = true;
		GD.PushError($"[TEST] ECHEC - {message}");
	}

	private void ProcessTest()
	{
		_testFrame++;

		switch (_testFrame)
		{
			case 5:
				_testBatimentsIaDepart = CompterBatimentsRestants(_ia);
				_testBatimentsJoueurDepart = CompterBatimentsRestants(_joueur);
				GD.Print($"[TEST] Demarrage duel : etat={_etat}, batiments J={_testBatimentsJoueurDepart}, batiments IA={_testBatimentsIaDepart}");
				GD.Print("[TEST] -> Espace pour lancer le joueur");
				InjecterAction("lancer_balle", true);
				break;

			case 6:
				InjecterAction("lancer_balle", false);
				break;

			case 40:
			{
				var bJoueur = PremiereBalle(_joueur);
				var bIa = PremiereBalle(_ia);
				GD.Print($"[TEST] Lancement : etat={_etat}, vitesse J={(bJoueur != null ? bJoueur.LinearVelocity.Length() : 0):0.00}, vitesse IA={(bIa != null ? bIa.LinearVelocity.Length() : 0):0.00}");
				_testBarIaDepartX = _ia.Bar.Position.X;
				if (bJoueur != null)
				{
					bJoueur.GlobalPosition = new Vector3(1.2f, 8.6f, 0.0f);
					bJoueur.LinearVelocity = new Vector3(0.2f, 1.0f, 0.0f).Normalized() * bJoueur.VitesseCible;
				}
				break;
			}

			case 70:
				GD.Print($"[TEST] IA : barre X {_testBarIaDepartX:0.000} -> {_ia.Bar.Position.X:0.000}");
				break;

			case 75:
			{
				BriqueScript briqueIa = PremiereBrique();
				if (briqueIa != null)
				{
					GD.Print("[TEST] -> Simulation collision balle joueur/brique IA");
					OnBalleCollision(PremiereBalle(_joueur), briqueIa);
				}
				break;
			}

			case 80:
			{
				BriqueScript briqueJoueur = PremiereBrique();
				if (briqueJoueur != null)
				{
					GD.Print("[TEST] -> Simulation collision balle IA/brique joueur");
					OnBalleCollision(PremiereBalle(_ia), briqueJoueur);
				}
				break;
			}

			case 82:
			{
				var balleJoueur = PremiereBalle(_joueur) ?? CreerBalle(_joueur, _joueur.PositionBalleRepos);
				var briqueIaCapsule = PremiereBrique(BriqueScript.TypeBrique.CapsuleGarantie);
				int capsulesIaAvant = CompterCapsules(_ia);
				int scoreIaAvant = _ia.Score;
				if (briqueIaCapsule != null)
				{
					OnBalleCollision(balleJoueur, _ia.Bar);
					OnBalleCollision(balleJoueur, briqueIaCapsule);
					CapsuleScript capsule = DerniereCapsule(_ia);
					VerifierTest(
						balleJoueur.DestinataireBonus == _ia.Cle
							&& _ia.Score > scoreIaAvant
							&& CompterCapsules(_ia) == capsulesIaAvant + 1
							&& capsule != null
							&& capsule.GetParent() == _ia.Capsules
							&& capsule.SensVertical == _ia.SensCapsuleVersBarre,
						"balle joueur touchee par barre IA : capsule vers IA");
				}
				break;
			}

			case 83:
			{
				var balleIa = PremiereBalle(_ia) ?? CreerBalle(_ia, _ia.PositionBalleRepos);
				var briqueJoueurCapsule = PremiereBrique(BriqueScript.TypeBrique.CapsuleGarantie);
				int capsulesJoueurAvant = CompterCapsules(_joueur);
				int scoreJoueurAvant = _joueur.Score;
				if (briqueJoueurCapsule != null)
				{
					OnBalleCollision(balleIa, _joueur.Bar);
					OnBalleCollision(balleIa, briqueJoueurCapsule);
					CapsuleScript capsule = DerniereCapsule(_joueur);
					VerifierTest(
						balleIa.DestinataireBonus == _joueur.Cle
							&& _joueur.Score > scoreJoueurAvant
							&& CompterCapsules(_joueur) == capsulesJoueurAvant + 1
							&& capsule != null
							&& capsule.GetParent() == _joueur.Capsules
							&& capsule.SensVertical == _joueur.SensCapsuleVersBarre,
						"balle IA touchee par barre joueur : capsule vers joueur");
				}
				break;
			}

			case 84:
			{
				_etat = Etat.EnJeu;
				_testBalleAimantIa = CreerBalle(_joueur, _ia.Bar.GlobalPosition + _ia.Bar.GlobalTransform.Basis.Y.Normalized() * 0.18f);
				DefinirContactBalle(_testBalleAimantIa, _joueur);
				AppliquerBonus(_ia, CapsuleScript.TypeBonus.Aimant);
				OnBalleCollision(_testBalleAimantIa, _ia.Bar);
				VerifierTest(
					_testBalleAimantIa.EstCollee,
					"aimant IA colle une balle adverse");
				break;
			}

			case 85:
			{
				VerifierTest(
					_testBalleAimantIa != null && _testBalleAimantIa.GetParent() == _ia.Balles,
					"aimant IA capture une balle adverse dans son conteneur");
				MettreAJourCampIA(_ia, 0.1);
				VerifierTest(
					_testBalleAimantIa != null && !_testBalleAimantIa.EstCollee && _testBalleAimantIa.LinearVelocity.Length() > 0.01f,
					"IA relance une balle collee par l'aimant");

				var capsuleIa = LacherCapsule(_ia, new Vector3(0, 5.7f, 0), CapsuleScript.TypeBonus.BarreLarge);
				OnCapsule(_ia, capsuleIa, _ia.Bar);
				var capsuleJoueur = LacherCapsule(_joueur, new Vector3(0, 4.3f, 0), CapsuleScript.TypeBonus.BarreLarge);
				OnCapsule(_joueur, capsuleJoueur, _joueur.Bar);
				GD.Print($"[TEST] Capsules : J={_joueur.CapsulesRamassees}, IA={_ia.CapsulesRamassees}");
				break;
			}

			case 90:
				for (int i = 0; i < 8; i++)
				{
					AppliquerBonus(_joueur, CapsuleScript.TypeBonus.MultiBalle);
					AppliquerBonus(_ia, CapsuleScript.TypeBonus.MultiBalle);
				}
				VerifierTest(
					CompterBalles(_joueur) <= NombreMaxBallesParCamp
						&& CompterBalles(_ia) <= NombreMaxBallesParCamp,
					"MultiBalle respecte NombreMaxBallesParCamp");
				GD.Print($"[TEST] MultiBalle : J={CompterBalles(_joueur)}, IA={CompterBalles(_ia)}");
				break;

			case 92:
			{
				AppliquerBonus(_ia, CapsuleScript.TypeBonus.BouclierBas);
				int avant = CompterBatimentsRestants(_ia);
				BatimentVille cibleIa = PremierBatiment(_ia, false);
				var balleJoueur = CreerBalle(_joueur, _joueur.PositionBalleRepos);
				DefinirContactBalle(balleJoueur, _joueur);
				OnBalleCollision(balleJoueur, cibleIa.Racine);
				GD.Print($"[TEST] Bouclier protege IA : {avant}->{CompterBatimentsRestants(_ia)}");
				MettreAJourBonusTemporaires(_ia, DureeBonus + 0.1);
				break;
			}

			case 94:
			{
				_etat = Etat.EnJeu;
				ViderConteneur(_ia.Balles);
				var balleIa = CreerBalle(_ia, _ia.PositionBalleRepos);
				DefinirContactBalle(balleIa, _ia);
				OnZoneDegats(_joueur, balleIa);
				VerifierTest(CompterBalles(_joueur) >= 1 && CompterBalles(_ia) >= 1, "minimum une balle par joueur apres perte");
				GD.Print($"[TEST] Relance proprietaire IA apres balle consommee : IA={CompterBalles(_ia)}, J={CompterBalles(_joueur)}");
				break;
			}

			case 95:
			{
				_etat = Etat.EnJeu;
				int scoreAvant = _joueur.Score;
				var balleJoueur = PremiereBalle(_joueur) ?? CreerBalle(_joueur, _joueur.PositionBalleRepos);
				DefinirContactBalle(balleJoueur, _joueur);
				OnZoneDegats(_joueur, balleJoueur);
				GD.Print($"[TEST] Zone ratee joueur : score {scoreAvant}->{_joueur.Score}, batiments J={CompterBatimentsRestants(_joueur)}");
				break;
			}

			case 100:
				GD.Print("[TEST] -> Relance apres balle ratee");
				InjecterAction("lancer_balle", true);
				break;

			case 101:
				InjecterAction("lancer_balle", false);
				GD.Print($"[TEST] Relance joueur : vitesse={(PremiereBalle(_joueur) != null ? PremiereBalle(_joueur).LinearVelocity.Length() : 0):0.00}");
				break;

			case 105:
			{
				BatimentVille cibleIa = PremierBatiment(_ia, false);
				var balleJoueur = CreerBalle(_joueur, _joueur.PositionBalleRepos);
				DefinirContactBalle(balleJoueur, _joueur);
				OnBalleCollision(balleJoueur, cibleIa.Racine);
				GD.Print($"[TEST] Batiment IA touche : {_testBatimentsIaDepart}->{CompterBatimentsRestants(_ia)}, etat={_etat}");
				break;
			}

			case 110:
				AppliquerBonus(_ia, CapsuleScript.TypeBonus.VieBonus);
				GD.Print($"[TEST] Reparation IA : batiments IA={CompterBatimentsRestants(_ia)}");
				break;

			case 115:
			{
				int niveauAvant = _joueur.Niveau;
				int batimentsAvant = CompterBatimentsRestants(_ia);
				DetruireToutesLesBriques(_joueur);
				GD.Print($"[TEST] Niveau termine : {niveauAvant}->{_joueur.Niveau}, batiments IA={batimentsAvant}->{CompterBatimentsRestants(_ia)}");
				break;
			}

			case 120:
			{
				_etat = Etat.EnJeu;
				foreach (BatimentVille batiment in _joueur.BatimentsVille)
				{
					if (batiment.Detruit)
						continue;

					var balleIa = CreerBalle(_ia, _ia.PositionBalleRepos);
					DefinirContactBalle(balleIa, _ia);
					OnBalleCollision(balleIa, batiment.Racine);
				}
				GD.Print($"[TEST] Fin duel par batiments : etat={_etat}, batiments J={CompterBatimentsRestants(_joueur)}");
				GD.Print("[TEST] Fin des tests, fermeture.");
				GetTree().Quit(_testErreur ? 1 : 0);
				break;
			}
		}
	}
}
