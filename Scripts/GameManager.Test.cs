using Godot;

// Harnais d'auto-test active avec l'argument de ligne de commande --test.
public partial class GameManager : Node3D
{
	private int _testFrame;
	private float _testBarIaDepartX;

	private BalleScript PremiereBalle(Camp camp)
	{
		foreach (Node n in camp.Balles.GetChildren())
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

	private BriqueScript PremiereBrique(Camp camp)
	{
		foreach (Node n in camp.Briques.GetChildren())
			if (n is BriqueScript b && !b.IsQueuedForDeletion() && b.EstDestructible)
				return b;
		return null;
	}

	private void ProcessTest()
	{
		_testFrame++;

		switch (_testFrame)
		{
			case 5:
				GD.Print($"[TEST] Demarrage duel : etat={_etat}, vies J={_joueur.Vies}, vies IA={_ia.Vies}");
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
				BriqueScript briqueIa = PremiereBrique(_ia);
				if (briqueIa != null)
				{
					GD.Print("[TEST] -> Simulation collision balle joueur/brique IA");
					OnBalleCollision(PremiereBalle(_joueur), briqueIa);
				}
				break;
			}

			case 80:
			{
				BriqueScript briqueJoueur = PremiereBrique(_joueur);
				if (briqueJoueur != null)
				{
					GD.Print("[TEST] -> Simulation collision balle IA/brique joueur");
					OnBalleCollision(PremiereBalle(_ia), briqueJoueur);
				}
				break;
			}

			case 85:
			{
				var capsuleJ = LacherCapsule(_ia, new Vector3(0, 5.7f, 0), CapsuleScript.TypeBonus.BarreLarge);
				OnCapsule(_joueur, capsuleJ, _joueur.Bar);
				var capsuleIa = LacherCapsule(_joueur, new Vector3(0, 4.3f, 0), CapsuleScript.TypeBonus.BarreLarge);
				OnCapsule(_ia, capsuleIa, _ia.Bar);
				GD.Print($"[TEST] Capsules : J={_joueur.CapsulesRamassees}, IA={_ia.CapsulesRamassees}");
				break;
			}

			case 90:
				AppliquerBonus(_joueur, CapsuleScript.TypeBonus.MultiBalle);
				AppliquerBonus(_ia, CapsuleScript.TypeBonus.MultiBalle);
				GD.Print($"[TEST] MultiBalle : J={CompterBalles(_joueur)}, IA={CompterBalles(_ia)}");
				break;

			case 95:
				_etat = Etat.EnJeu;
				OnZoneDegats(_ia, PremiereBalle(_joueur));
				GD.Print($"[TEST] Degat ville IA : vies IA={_ia.Vies}, score J={_joueur.Score}");
				break;

			case 100:
				OnZoneDegats(_joueur, PremiereBalle(_ia));
				GD.Print($"[TEST] Degat ville joueur : vies J={_joueur.Vies}, score IA={_ia.Score}");
				break;

			case 105:
				MettreAJourBonusTemporaires(_joueur, DureeBonus + 0.1);
				MettreAJourBonusTemporaires(_ia, DureeBonus + 0.1);
				GD.Print($"[TEST] Expiration bonus : barre J={_joueur.Bar.DemiLargeur:0.00}, barre IA={_ia.Bar.DemiLargeur:0.00}");
				break;

			case 110:
				EndommagerVille(_ia, _joueur);
				EndommagerVille(_ia, _joueur);
				GD.Print($"[TEST] Fin duel : etat={_etat}, vies IA={_ia.Vies}");
				GD.Print("[TEST] Fin des tests, fermeture.");
				GetTree().Quit();
				break;
		}
	}
}
