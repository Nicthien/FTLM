using Godot;

// Harnais d'auto-test active avec l'argument de ligne de commande --test.
public partial class GameManager : Node3D
{
	private int _testFrame;
	private float _testBarDepartX;

	private BalleScript PremiereBalle()
	{
		foreach (Node n in Balles.GetChildren())
			if (n is BalleScript b && !b.IsQueuedForDeletion())
				return b;
		return null;
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

	private void ProcessTest()
	{
		_testFrame++;

		switch (_testFrame)
		{
			case 5:
				GD.Print($"[TEST] Demarrage : etat={_etat} (attendu AttenteLancement)");
				GD.Print("[TEST] -> Espace pour lancer la balle");
				InjecterAction("lancer_balle", true);
				break;

			case 6:
				InjecterAction("lancer_balle", false);
				break;

			case 9:
			{
				var b = PremiereBalle();
				GD.Print($"[TEST] Lancement : etat={_etat}, |vitesseBalle|={(b != null ? b.LinearVelocity.Length() : 0):0.00}");
				break;
			}

			case 14:
				BasculerPause();
				GD.Print($"[TEST] Pause : Paused={GetTree().Paused}, etat={_etat}");
				break;

			case 15:
				BasculerPause();
				GD.Print($"[TEST] Reprise : Paused={GetTree().Paused}, etat={_etat}");
				_testBarDepartX = Bar.Position.X;
				GD.Print("[TEST] -> Injection 'ui_right' maintenu");
				InjecterAction("ui_right", true);
				break;

			case 46:
				InjecterAction("ui_right", false);
				GD.Print($"[TEST] Barre : X {_testBarDepartX:0.000} -> {Bar.Position.X:0.000}");
				break;

			case 50:
				if (Briques.GetChildCount() > 0 && Briques.GetChild(0) is BriqueScript br)
				{
					GD.Print("[TEST] -> Simulation collision balle/brique");
					OnBalleCollision(null, br);
				}
				break;

			case 54:
			{
				GD.Print("[TEST] -> Simulation balle perdue");
				_etat = Etat.EnJeu;
				var b = PremiereBalle();
				if (b != null)
					OnBalleSortie(b);
				GD.Print($"[TEST] Apres perte : vies={_vies}, balles={CompterBalles()}, combo={_combo}");
				break;
			}

			case 56:
			{
				int avant = CompterBalles();
				AppliquerBonus(CapsuleScript.TypeBonus.MultiBalle);
				GD.Print($"[TEST] Bonus MultiBalle : balles {avant} -> {CompterBalles()}");
				break;
			}

			case 57:
			{
				int avant = _vies;
				AppliquerBonus(CapsuleScript.TypeBonus.VieBonus);
				GD.Print($"[TEST] Bonus VieBonus : vies {avant} -> {_vies}");
				break;
			}

			case 58:
			{
				float avant = Bar.DemiLargeur;
				AppliquerBonus(CapsuleScript.TypeBonus.BarreLarge);
				GD.Print($"[TEST] Bonus BarreLarge : demiLargeur {avant:0.00} -> {Bar.DemiLargeur:0.00}");
				break;
			}

			case 59:
			{
				var caps = LacherCapsule(new Vector3(0, 0.5f, 0), CapsuleScript.TypeBonus.BalleRapide);
				OnCapsule(caps, Bar);
				GD.Print($"[TEST] Capsule attrapee (BalleRapide) -> vitesseBalle={_vitesseBalle:0.0}");
				break;
			}

			case 60:
				AppliquerBonus(CapsuleScript.TypeBonus.Aimant);
				AppliquerBonus(CapsuleScript.TypeBonus.Laser);
				AppliquerBonus(CapsuleScript.TypeBonus.BouclierBas);
				GD.Print($"[TEST] Bonus actifs : aimant={_aimantRestant:0.0}, laser={_laserRestant:0.0}, bouclier={_bouclierRestant:0.0}");
				break;

			case 61:
				AppliquerBonus(CapsuleScript.TypeBonus.ScoreDouble);
				AppliquerBonus(CapsuleScript.TypeBonus.BallePercante);
				GD.Print($"[TEST] ScoreDouble={_scoreDoubleRestant:0.0}, Percante={_ballePercanteRestant:0.0}");
				break;

			case 62:
				MettreAJourBonusTemporaires(DureeBonus + 0.1);
				GD.Print($"[TEST] Expiration bonus : laser={_laserRestant:0.0}, bouclierValide={IsInstanceValid(_bouclierBas)}, vitesse={_vitesseBalle:0.0}");
				break;

			case >= 65:
				if (_modeArcade && _briquesRestantes > 0)
				{
					GD.Print($"[TEST] Bascule Arcade OK : niveau={_niveauActuel}, score={_score}, record={_meilleurScore}, vies={_vies}, comboMax={_meilleurCombo}");
					GD.Print("[TEST] Fin des tests, fermeture.");
					GetTree().Quit();
				}
				else if (_testFrame > 700)
				{
					GD.Print("[TEST] ECHEC : le mode Arcade n'a pas ete atteint (timeout).");
					GetTree().Quit();
				}
				else
				{
					foreach (Node n in Briques.GetChildren())
						if (n is BriqueScript b && !b.IsQueuedForDeletion() && b.EstDestructible)
							OnBalleCollision(null, b);
				}
				break;
		}
	}
}
