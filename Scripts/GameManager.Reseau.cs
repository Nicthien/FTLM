using Godot;
using System.Collections.Generic;

// Partie reseau du GameManager : en mode reseau l'hote simule tout (logique inchangee)
// et diffuse des snapshots (barres, balles, capsules) + des evenements (briques, villes,
// niveau, HUD). Le client n'execute aucune logique : il regenere l'arene de facon
// deterministe puis affiche/interpole ce qu'il recoit. Retire sans toucher au jeu local.
public partial class GameManager : Node3D
{
	private bool _reseauActif;
	private bool _estHote;
	private bool _estClient;

	private int _compteurIdMobile;
	private double _accumSnapshot;
	private int _tickSnapshot;
	private const double IntervalleSnapshot = 1.0 / 60.0;
	private const int SnapshotsParHud = 12; // HUD diffuse ~5x/s

	// Cote client : etat initial recu ? sinon on redemande periodiquement.
	private bool _etatInitialRecu;
	private double _delaiDemandeEtat;
	private bool _sourisClientActive;
	private double _delaiLogDoublon;

	// Briques indexees par identifiant reseau (rempli a la generation, hote et client).
	private readonly Dictionary<int, BriqueScript> _briquesParId = new();
	// Objets mobiles affiches cote client, indexes par identifiant reseau.
	private readonly Dictionary<int, BalleScript> _ballesAffichees = new();
	private readonly Dictionary<int, CapsuleScript> _capsulesAffichees = new();
	private readonly Dictionary<int, ObjetProjectile> _projectilesAffiches = new();
	private readonly HashSet<int> _idsVus = new();

	// ------------------------------------------------------------- Initialisation

	private bool _netTest;
	private int _netFrame;

	private void LoggerEtatBalles()
	{
		ulong f = Engine.GetProcessFrames();
		if (_estHote)
		{
			var sb = new System.Text.StringBuilder($"[NET] f{f} hote :");
			for (int i = 0; i < _camps.Count; i++)
			{
				sb.Append($" camp{i}[");
				foreach (Node n in _camps[i].Balles.GetChildren())
					if (n is BalleScript b && !b.IsQueuedForDeletion())
						sb.Append($"#{b.IdReseau}({b.GlobalPosition.X:0.0},{b.GlobalPosition.Y:0.0}) ");
				sb.Append($"stock={NbStock(_camps[i])} cible={_camps[i].IndexCible}]");
			}
			sb.Append($" projectiles={NbProjectiles()}");
			GD.Print(sb.ToString());
		}
		else
		{
			var sb = new System.Text.StringBuilder($"[NET] f{f} client : nodes=");
			int nodes = 0;
			foreach (Camp camp in _camps)
				foreach (Node n in camp.Balles.GetChildren())
					if (n is BalleScript b && !b.IsQueuedForDeletion())
						nodes++;
			sb.Append(nodes).Append(" dict=").Append(_ballesAffichees.Count);
			int tirs = 0;
			bool bouclier = false;
			foreach (Camp camp in _camps)
			{
				if (IsInstanceValid(camp.Bouclier))
					bouclier = true;
				foreach (Node n in camp.Arm.GetChildren())
					if (n is LaserTir)
						tirs++;
				if (camp.Bar != null && camp.Bar.GetNodeOrNull("CanonsLaser") is Node canons && ((Node3D)canons).Visible)
					sb.Append($" canons{camp.Index}=ON");
			}
			sb.Append($" bouclier={bouclier} tirs={tirs} projectiles={_projectilesAffiches.Count}");
			for (int i = 0; i < _camps.Count; i++)
				sb.Append($" stock{i}={NbStock(_camps[i])}/cible={_camps[i].IndexCible}");
			GD.Print(sb.ToString());
		}
	}

	private static int NbStock(Camp camp)
	{
		int n = 0;
		foreach (CapsuleScript.TypeBonus? o in camp.Stock)
			if (o.HasValue)
				n++;
		return n;
	}

	private int NbProjectiles()
	{
		int n = 0;
		foreach (Node node in _ballesRoot.GetChildren())
			if (node is ObjetProjectile proj && !proj.IsQueuedForDeletion())
				n++;
		return n;
	}

	private bool _repriseSoloFaite;

	private void InitReseau()
	{
		NetworkSession session = NetworkSession.Instance;
		_reseauActif = PartieConfig.Mode == PartieConfig.ModePartie.Reseau
			&& session != null && session.EstActif;
		_estHote = _reseauActif && session.EstHote;
		_estClient = _reseauActif && session.EstClient;

		// Reagir aux deconnexions en cours de partie : un client lache -> son camp passe en
		// IA (cote hote) ; l'hote disparait -> reprise solo (cote client).
		if (_reseauActif)
		{
			session.JoueurRemplaceParIA += OnJoueurRemplaceParIA;
			session.HotePerdu += OnHotePerdu;
		}

		string[] args = OS.GetCmdlineArgs();
		string[] userArgs = OS.GetCmdlineUserArgs();
		foreach (string a in new[] { "--nethost", "--netjoin" })
			if (System.Array.IndexOf(args, a) >= 0 || System.Array.IndexOf(userArgs, a) >= 0)
				_netTest = true;
	}

	// Diagnostic du test deux instances : l'hote lance les balles, detruit quelques
	// briques, puis on compare l'etat hote/client (briques, balles, mouvement) et on quitte.
	private void ProcessNetTest()
	{
		_netFrame++;

		// L'hote met le jeu en mouvement pour valider snapshots + evenements de disparition.
		if (_estHote && _netFrame == 30)
		{
			foreach (Camp camp in _camps)
				LancerCamp(camp);
			for (int i = 0; i < 12; i++)
			{
				BriqueScript b = PremiereBrique();
				if (b == null)
					break;
				FrapperBrique(_joueur, b, null, true, false);
			}
			GD.Print("[NET] hote : balles lancees + 12 briques detruites");
		}

		// Applique laser + bouclier au camp1 (client) et tire en rafale -> verifie la
		// replication des canons, du dome et des faisceaux cote client.
		if (_estHote && _netFrame == 40)
		{
			AppliquerBonus(_camps[1], CapsuleScript.TypeBonus.Laser);
			AppliquerBonus(_camps[1], CapsuleScript.TypeBonus.BouclierBas);
			GD.Print("[NET] hote : laser + bouclier sur camp1");
		}
		if (_estHote && _netFrame > 40 && _netFrame < 180)
			TirerLaser(_camps[1]);

		// Objets offensifs : remplit le stock du camp1 puis lance vers sa cible -> verifie la
		// replication des cases (ClientStock) et des projectiles (snapshot) cote client.
		if (_estHote && _netFrame == 50)
		{
			AjouterObjet(_camps[1], CapsuleScript.TypeBonus.Missile);
			AjouterObjet(_camps[1], CapsuleScript.TypeBonus.Gel);
			GD.Print("[NET] hote : 2 objets ajoutes au stock camp1");
		}
		if (_estHote && (_netFrame == 70 || _netFrame == 110))
		{
			LancerObjetStock(_camps[1]);
			GD.Print($"[NET] hote : objet lance par camp1 (f{_netFrame})");
		}

		// Force une fin de niveau (donc ClientChargerNiveau cote client, qui vide/regenere).
		if (_estHote && _netFrame == 250)
		{
			DetruireToutesLesBriques(_joueur);
			GD.Print("[NET] hote : niveau force termine");
		}

		if (_netFrame % 60 == 0)
			LoggerEtatBalles();

		if (_netFrame >= 360)
		{
			GD.Print("[NET] fin du test, fermeture.");
			GetTree().Quit(0);
		}
	}

	// Applique le mode de controle reseau a la barre d'un camp (appele apres Configurer).
	private void ConfigurerModeBarreReseau(Camp camp)
	{
		if (_estClient)
		{
			camp.Bar.DefinirMode(BarScript.ModeControle.Spectateur);
		}
		else if (_estHote && !camp.ControleIA && camp.Index != PartieConfig.SlotLocal)
		{
			// Humain distant : barre pilotee par l'axe recu de son client.
			camp.Bar.DefinirMode(BarScript.ModeControle.Distant);
		}
	}

	// ---------------------------------------------- Deconnexions en cours de partie

	// Cote hote : un client s'est deconnecte. Son camp est deja passe en IA dans PartieConfig
	// (NetworkSession) ; ici on bascule sa barre en pilotage IA et on annonce le remplacement.
	private void OnJoueurRemplaceParIA(int slot)
	{
		if (slot < 0 || slot >= _camps.Count)
			return;

		Camp camp = _camps[slot];
		camp.ControleIA = true;
		if (camp.Bar != null)
			camp.Bar.DefinirMode(BarScript.ModeControle.IA);

		AfficherBonus(camp, $"{camp.Nom} deconnecte - reprise par l'IA");
		// Prevenir les clients restants pour qu'ils affichent aussi le message.
		if (_estHote)
			Rpc(MethodName.ClientAnnonceCamp, slot, $"{camp.Nom} deconnecte - reprise par l'IA");

		GD.Print($"[NET] Camp {slot} ({camp.Nom}) deconnecte -> IA.");
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ClientAnnonceCamp(int slot, string message)
	{
		if (slot >= 0 && slot < _camps.Count)
			AfficherBonus(_camps[slot], message);
	}

	// Cote client : l'hote a disparu. On reprend la simulation en local : le camp de cette
	// machine devient humain (clavier/souris), tous les autres passent en IA. On garde l'etat
	// courant (briques, scores, villes) et on remet simplement les balles au repos.
	private void OnHotePerdu()
	{
		if (_repriseSoloFaite)
			return;
		_repriseSoloFaite = true;

		PartieConfig.Mode = PartieConfig.ModePartie.Local;
		_reseauActif = false;
		_estClient = false;
		_estHote = false;

		int slotLocal = Mathf.Clamp(PartieConfig.SlotLocal, 0, _camps.Count - 1);

		// Les objets affiches passifs (balles/capsules figees) sont remplaces par de vraies
		// balles simulees : on vide les index d'affichage.
		_ballesAffichees.Clear();
		_capsulesAffichees.Clear();

		for (int i = 0; i < _camps.Count; i++)
		{
			Camp camp = _camps[i];
			bool estLocal = i == slotLocal;
			camp.ControleIA = !estLocal;

			if (estLocal)
			{
				// Seul humain de cette machine : il joue desormais aux fleches + espace.
				camp.ActionGauche = "ui_left";
				camp.ActionDroite = "ui_right";
				camp.ActionLancement = "lancer_balle";
				camp.ActionCapacite = "tirer_capacite";
			}

			if (camp.Bar != null)
			{
				camp.Bar.Configurer(camp.ControleIA, camp.SensAttaque, camp.ActionGauche, camp.ActionDroite, _demiLargeurTerrain - 0.1f);
				camp.Bar.Visible = !camp.Elimine;
			}
			ActiverCollisionBarre(camp, !camp.Elimine);

			// La zone de mort etait inerte cote client (affichage seul) : on la branche pour
			// reprendre la simulation des balles perdues.
			Camp campLocal = camp;
			camp.ZoneDegats.BodyEntered += (body) => OnZoneDegats(campLocal, body);

			if (!camp.Elimine)
				PreparerLancement(camp);
		}

		_etat = Etat.AttenteLancement;
		MessageLabel.Text = "Hote deconnecte - partie en solo contre l'IA.\nEspace pour lancer";
		MessageLabel.Visible = true;
		MettreAJourHud();

		GD.Print("[NET] Hote perdu -> reprise en solo (camp local humain, autres en IA).");
	}

	// Cote client : etat passif, on attend les donnees de l'hote.
	private void DemarrerClient()
	{
		_etat = Etat.AttenteLancement;
		_etatInitialRecu = false;
		_delaiDemandeEtat = 0.0;
		MessageLabel.Text = "Connexion a la partie...";
		MessageLabel.Visible = true;
		MettreAJourHud();
	}

	private void ProcessClient(double delta)
	{
		MettreAJourPositionTableauScore();

		if (_etat is Etat.EnJeu or Etat.AttenteLancement)
			MettreAJourFleches();

		if (!_etatInitialRecu)
		{
			_delaiDemandeEtat -= delta;
			if (_delaiDemandeEtat <= 0.0)
			{
				_delaiDemandeEtat = 0.5;
				RpcId(1, MethodName.HoteDemanderEtatInitial);
			}
		}

		EnvoyerInputClient();

		_delaiLogDoublon -= delta;
		if (_delaiLogDoublon <= 0.0)
			DetecterDoublonClient();

		if (_netTest)
			ProcessNetTest();
	}

	// Detecte automatiquement deux balles affichees tres proches (= doublon visuel) et
	// logge ids/positions/parents pour identifier la cause exacte (orphelin, multi-balle...).
	private void DetecterDoublonClient()
	{
		var balles = new List<BalleScript>();
		foreach (Camp camp in _camps)
			foreach (Node n in camp.Balles.GetChildren())
				if (n is BalleScript b && !b.IsQueuedForDeletion() && IsInstanceValid(b) && b.Visible)
					balles.Add(b);

		for (int a = 0; a < balles.Count; a++)
			for (int c = a + 1; c < balles.Count; c++)
			{
				float d = balles[a].GlobalPosition.DistanceTo(balles[c].GlobalPosition);
				if (d >= 0.4f)
					continue;

				GD.Print($"[NET] DOUBLON client : nodes={balles.Count} dict={_ballesAffichees.Count} | "
					+ $"#{balles[a].IdReseau}({balles[a].GlobalPosition.X:0.0},{balles[a].GlobalPosition.Y:0.0}){balles[a].GetParent().Name} <-> "
					+ $"#{balles[c].IdReseau}({balles[c].GlobalPosition.X:0.0},{balles[c].GlobalPosition.Y:0.0}){balles[c].GetParent().Name} dist={d:0.00}");
				_delaiLogDoublon = 1.0;
				return;
			}
	}

	// Detection de la souris cote client (sa barre est en mode Spectateur, donc son
	// BarScript n'ecoute pas la souris : on le fait ici).
	public override void _Input(InputEvent ev)
	{
		if (_estClient && (ev is InputEventMouseMotion || ev is InputEventMouseButton))
			_sourisClientActive = true;

		// F3 : diagnostic reseau (compte/ids/positions des balles) dans une vraie partie.
		if (_reseauActif && ev is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F3 })
			LoggerEtatBalles();
	}

	// Le client envoie toujours ses fleches/souris/espace ; l'hote les route vers son slot.
	private void EnvoyerInputClient()
	{
		if (PartieConfig.SlotLocal < 0)
			return;

		float axe = Input.GetAxis("ui_left", "ui_right");
		if (!Mathf.IsZeroApprox(axe))
			_sourisClientActive = false;

		float cible = 0.0f;
		bool aCible = _sourisClientActive && Mathf.IsZeroApprox(axe) && CalculerCibleSourisLocale(out cible);
		RpcId(1, MethodName.HoteRecevoirInput, axe, aCible, cible);

		if (Input.IsActionJustPressed("lancer_balle"))
			RpcId(1, MethodName.HoteRecevoirLancer);

		if (Input.IsActionJustPressed("tirer_capacite"))
			RpcId(1, MethodName.HoteRecevoirCapacite);
	}

	// Projette la souris sur le plan de jeu (z=0) et renvoie le X dans le repere du
	// couloir local (la barre se deplace sur son X local). Gere la camera tournee.
	private bool CalculerCibleSourisLocale(out float cibleX)
	{
		cibleX = 0.0f;
		int slot = PartieConfig.SlotLocal;
		if (slot < 0 || slot >= _camps.Count || _camps[slot].Arm == null)
			return false;

		Camera3D cam = GetViewport().GetCamera3D();
		if (cam == null)
			return false;

		Vector2 souris = GetViewport().GetMousePosition();
		Vector3 origine = cam.ProjectRayOrigin(souris);
		Vector3 direction = cam.ProjectRayNormal(souris);
		if (Mathf.IsZeroApprox(direction.Z))
			return false;

		float t = -origine.Z / direction.Z;
		if (t < 0.0f)
			return false;

		Vector3 monde = origine + direction * t;
		cibleX = _camps[slot].Arm.ToLocal(monde).X;
		return true;
	}

	// --------------------------------------------------- Diffusion (cote hote)

	private void DiffuserSnapshot(double delta)
	{
		_accumSnapshot += delta;
		if (_accumSnapshot < IntervalleSnapshot)
			return;
		_accumSnapshot = 0.0;

		int n = _camps.Count;
		var barresX = new float[n];
		var balleIds = new List<int>();
		var balleCamps = new List<int>();
		var balleX = new List<float>();
		var balleY = new List<float>();
		var capsuleIds = new List<int>();
		var capsuleCamps = new List<int>();
		var capsuleTypes = new List<int>();
		var capsuleX = new List<float>();
		var capsuleY = new List<float>();
		var projIds = new List<int>();
		var projCamps = new List<int>();
		var projX = new List<float>();
		var projY = new List<float>();

		// Projectiles d'objets : enfants directs de BallesRoot (non parentes a un camp).
		foreach (Node node in _ballesRoot.GetChildren())
		{
			if (node is not ObjetProjectile proj || proj.IsQueuedForDeletion())
				continue;
			projIds.Add(proj.IdReseau);
			projCamps.Add(proj.CampSource);
			projX.Add(proj.GlobalPosition.X);
			projY.Add(proj.GlobalPosition.Y);
		}

		for (int i = 0; i < n; i++)
		{
			Camp camp = _camps[i];
			barresX[i] = camp.Bar != null ? camp.Bar.Position.X : 0.0f;

			foreach (Node node in camp.Balles.GetChildren())
			{
				if (node is not BalleScript balle || balle.IsQueuedForDeletion())
					continue;
				balleIds.Add(balle.IdReseau);
				balleCamps.Add(i);
				balleX.Add(balle.GlobalPosition.X);
				balleY.Add(balle.GlobalPosition.Y);
			}

			foreach (Node node in camp.Capsules.GetChildren())
			{
				if (node is not CapsuleScript capsule || capsule.IsQueuedForDeletion())
					continue;
				capsuleIds.Add(capsule.IdReseau);
				capsuleCamps.Add(i);
				capsuleTypes.Add((int)capsule.Type);
				capsuleX.Add(capsule.GlobalPosition.X);
				capsuleY.Add(capsule.GlobalPosition.Y);
			}
		}

		Rpc(MethodName.ClientSnapshot, barresX,
			balleIds.ToArray(), balleCamps.ToArray(), balleX.ToArray(), balleY.ToArray(),
			capsuleIds.ToArray(), capsuleCamps.ToArray(), capsuleTypes.ToArray(), capsuleX.ToArray(), capsuleY.ToArray(),
			projIds.ToArray(), projCamps.ToArray(), projX.ToArray(), projY.ToArray());

		_tickSnapshot++;
		if (_tickSnapshot % SnapshotsParHud == 0)
		{
			DiffuserHud();
			DiffuserBonus();
			DiffuserStock();
		}
	}

	// Diffuse l'etat des bonus par camp (temps restants + taille de barre) pour que le
	// client affiche canons laser, dome bouclier, taille de barre et timers de bonus.
	private const int ChampsBonus = 9;

	private void DiffuserBonus()
	{
		if (!_estHote)
			return;

		int n = _camps.Count;
		var v = new float[n * ChampsBonus];
		for (int i = 0; i < n; i++)
		{
			Camp c = _camps[i];
			int b = i * ChampsBonus;
			v[b + 0] = (float)c.LaserRestant;
			v[b + 1] = (float)c.AimantRestant;
			v[b + 2] = (float)c.BouclierRestant;
			v[b + 3] = (float)c.ScoreDoubleRestant;
			v[b + 4] = (float)c.BallePercanteRestant;
			v[b + 5] = (float)c.VitesseTemporaireRestante;
			v[b + 6] = c.VitesseBalle;
			v[b + 7] = c.Bar != null ? c.Bar.FacteurRedimensionnement : 1.0f;
			v[b + 8] = c.Bar != null ? (float)c.Bar.TempsRedimensionnementRestant : 0.0f;
		}

		Rpc(MethodName.ClientBonus, v);
	}

	// Diffuse le stock d'objets (2 cases par camp, -1 = vide) et la cible de chacun.
	private void DiffuserStock()
	{
		if (!_estHote)
			return;

		int n = _camps.Count;
		var stock = new int[n * 2];
		var cibles = new int[n];
		for (int i = 0; i < n; i++)
		{
			Camp c = _camps[i];
			stock[i * 2 + 0] = c.Stock[0].HasValue ? (int)c.Stock[0].Value : -1;
			stock[i * 2 + 1] = c.Stock[1].HasValue ? (int)c.Stock[1].Value : -1;
			cibles[i] = c.IndexCible;
		}

		Rpc(MethodName.ClientStock, stock, cibles);
	}

	// Faisceau laser visuel chez les clients (le tir reel + degats restent sur l'hote).
	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
	private void ClientTirLaser(int camp, float gx, float gy, float dx, float dy)
	{
		if (camp < 0 || camp >= _camps.Count)
			return;

		CreerTirLaser(_camps[camp], new Vector3(gx, gy, 0.0f), false);
		CreerTirLaser(_camps[camp], new Vector3(dx, dy, 0.0f), false);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ClientBonus(float[] v)
	{
		for (int i = 0; i < _camps.Count; i++)
		{
			int b = i * ChampsBonus;
			if (b + ChampsBonus > v.Length)
				break;

			Camp c = _camps[i];
			c.LaserRestant = v[b + 0];
			c.AimantRestant = v[b + 1];
			c.BouclierRestant = v[b + 2];
			c.ScoreDoubleRestant = v[b + 3];
			c.BallePercanteRestant = v[b + 4];
			c.VitesseTemporaireRestante = v[b + 5];
			c.VitesseBalle = v[b + 6];

			if (c.Bar != null)
			{
				// Canons laser : visibles tant que le bonus laser est actif.
				c.Bar.AfficherCanonsLaser(c.LaserRestant > 0.0, c.Couleur);
				// Taille de barre (le timer ne tourne pas sur une barre spectatrice : on
				// reapplique le facteur + temps recus a chaque diffusion).
				c.Bar.Redimensionner(v[b + 7], v[b + 8]);
			}

			// Dome bouclier : cree/detruit selon l'etat recu.
			if (c.BouclierRestant > 0.0)
				ActiverBouclier(c);
			else
				DesactiverBouclier(c);
		}

		MettreAJourTimersBonusHud();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ClientStock(int[] stock, int[] cibles)
	{
		for (int i = 0; i < _camps.Count; i++)
		{
			Camp c = _camps[i];
			if (i * 2 + 1 < stock.Length)
			{
				c.Stock[0] = stock[i * 2 + 0] >= 0 ? (CapsuleScript.TypeBonus)stock[i * 2 + 0] : (CapsuleScript.TypeBonus?)null;
				c.Stock[1] = stock[i * 2 + 1] >= 0 ? (CapsuleScript.TypeBonus)stock[i * 2 + 1] : (CapsuleScript.TypeBonus?)null;
			}
			if (i < cibles.Length)
				c.IndexCible = cibles[i];
			MettreAJourCasesObjets(c);
		}
	}

	private void DiffuserHud()
	{
		if (!_estHote)
			return;

		int n = _camps.Count;
		var scores = new int[n];
		var niveaux = new int[n];
		var elimine = new int[n];
		for (int i = 0; i < n; i++)
		{
			scores[i] = _camps[i].Score;
			niveaux[i] = _camps[i].Niveau;
			elimine[i] = _camps[i].Elimine ? 1 : 0;
		}

		string message = MessageLabel.Visible ? MessageLabel.Text : string.Empty;
		Rpc(MethodName.ClientHud, scores, niveaux, elimine, _briquesRestantes, (int)_etat, message, MessageLabel.Visible, _indexGagnant);
	}

	private void DiffuserDespawnBrique(int id)
	{
		if (_estHote)
			Rpc(MethodName.ClientDespawnBrique, id);
	}

	private void DiffuserBriqueFrappee(int id, int resistance)
	{
		if (_estHote)
			Rpc(MethodName.ClientBriqueFrappee, id, resistance);
	}

	private void DiffuserBatimentDetruit(int camp, int index)
	{
		if (_estHote)
			Rpc(MethodName.ClientBatimentDetruit, camp, index);
	}

	private void DiffuserChargerNiveau(int niveau, bool reparerVilles)
	{
		if (_estHote)
			Rpc(MethodName.ClientChargerNiveau, niveau, reparerVilles);
	}

	private void EnvoyerEtatInitialA(int idPeer)
	{
		var vivants = new int[_briquesParId.Count];
		_briquesParId.Keys.CopyTo(vivants, 0);

		var detruits = new List<int>();
		for (int c = 0; c < _camps.Count; c++)
			for (int b = 0; b < _camps[c].BatimentsVille.Count; b++)
				if (_camps[c].BatimentsVille[b].Detruit)
					detruits.Add(c * 100 + b);

		int niveau = _joueur?.Niveau ?? 1;
		RpcId(idPeer, MethodName.ClientEtatInitial, niveau, vivants, detruits.ToArray());
	}

	// --------------------------------------------------- RPC recues (cote hote)

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
	private void HoteRecevoirInput(float axe, bool aCible, float cible)
	{
		Camp camp = CampDuPeerEmetteur();
		if (camp?.Bar == null)
			return;

		camp.Bar.DefinirAxeDistant(axe);
		camp.Bar.DefinirCibleDistante(aCible ? cible : (float?)null);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void HoteRecevoirLancer()
	{
		Camp camp = CampDuPeerEmetteur();
		if (camp != null && !camp.ControleIA && !camp.Elimine
			&& _etat is Etat.EnJeu or Etat.AttenteLancement)
			GererLancementCamp(camp);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void HoteRecevoirCapacite()
	{
		Camp camp = CampDuPeerEmetteur();
		if (camp != null && !camp.ControleIA && !camp.Elimine && _etat == Etat.EnJeu)
			GererCapaciteCamp(camp);
	}

	// Cyclage de cible demande par un client (molette) : applique au slot qu'il controle.
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void HoteRecevoirCible(int sens)
	{
		Camp camp = CampDuPeerEmetteur();
		if (camp != null && !camp.ControleIA && !camp.Elimine
			&& _etat is Etat.EnJeu or Etat.AttenteLancement)
			CyclerCible(camp, sens > 0 ? 1 : -1);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void HoteDemanderEtatInitial()
	{
		if (_estHote)
			EnvoyerEtatInitialA(Multiplayer.GetRemoteSenderId());
	}

	// Camp pilote par le peer emetteur, avec verification que ce peer controle bien le slot.
	private Camp CampDuPeerEmetteur()
	{
		int sender = Multiplayer.GetRemoteSenderId();
		int slot = PartieConfig.SlotDuPeer(sender);
		if (slot < 0 || slot >= _camps.Count)
			return null;
		if (PartieConfig.PeerControleurDe(slot) != sender)
			return null;
		return _camps[slot];
	}

	// ------------------------------------------------- RPC recues (cote client)

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
	private void ClientSnapshot(float[] barresX,
		int[] balleIds, int[] balleCamps, float[] balleX, float[] balleY,
		int[] capsuleIds, int[] capsuleCamps, int[] capsuleTypes, float[] capsuleX, float[] capsuleY,
		int[] projIds, int[] projCamps, float[] projX, float[] projY)
	{
		for (int i = 0; i < barresX.Length && i < _camps.Count; i++)
			_camps[i].Bar?.DefinirCibleSpectateur(barresX[i]);

		ReconcilierBalles(balleIds, balleCamps, balleX, balleY);
		ReconcilierCapsules(capsuleIds, capsuleCamps, capsuleTypes, capsuleX, capsuleY);
		ReconcilierProjectiles(projIds, projCamps, projX, projY);
	}

	private void ReconcilierProjectiles(int[] ids, int[] camps, float[] xs, float[] ys)
	{
		_idsVus.Clear();
		for (int k = 0; k < ids.Length; k++)
		{
			int id = ids[k];
			_idsVus.Add(id);
			if (!_projectilesAffiches.TryGetValue(id, out ObjetProjectile proj) || !IsInstanceValid(proj))
			{
				int campSource = Mathf.Clamp(camps[k], 0, _camps.Count - 1);
				proj = CreerProjectileAffiche(id, _camps[campSource].Couleur);
				_projectilesAffiches[id] = proj;
			}
			proj.DefinirCibleAffichage(new Vector3(xs[k], ys[k], 0.0f));
		}

		SupprimerAbsents(_projectilesAffiches);
	}

	private ObjetProjectile CreerProjectileAffiche(int id, Color couleur)
	{
		var proj = new ObjetProjectile
		{
			Name = $"ObjetAffiche_{id}",
			IdReseau = id,
			ModeAffichage = true,
		};
		_ballesRoot.AddChild(proj);
		ConstruireVisuelProjectile(proj, couleur);
		return proj;
	}

	private void ReconcilierBalles(int[] ids, int[] camps, float[] xs, float[] ys)
	{
		_idsVus.Clear();
		for (int k = 0; k < ids.Length; k++)
		{
			int id = ids[k];
			_idsVus.Add(id);
			if (!_ballesAffichees.TryGetValue(id, out BalleScript balle) || !IsInstanceValid(balle))
			{
				int campIndex = Mathf.Clamp(camps[k], 0, _camps.Count - 1);
				balle = CreerBalleAffichee(_camps[campIndex], id);
				_ballesAffichees[id] = balle;
			}
			balle.DefinirCibleAffichage(new Vector3(xs[k], ys[k], 0.0f));
		}

		SupprimerAbsents(_ballesAffichees);
	}

	private void ReconcilierCapsules(int[] ids, int[] camps, int[] types, float[] xs, float[] ys)
	{
		_idsVus.Clear();
		for (int k = 0; k < ids.Length; k++)
		{
			int id = ids[k];
			_idsVus.Add(id);
			if (!_capsulesAffichees.TryGetValue(id, out CapsuleScript capsule) || !IsInstanceValid(capsule))
			{
				int campIndex = Mathf.Clamp(camps[k], 0, _camps.Count - 1);
				capsule = CreerCapsuleAffichee(_camps[campIndex], id, (CapsuleScript.TypeBonus)types[k]);
				_capsulesAffichees[id] = capsule;
			}
			capsule.DefinirCibleAffichage(new Vector3(xs[k], ys[k], 0.0f));
		}

		SupprimerAbsents(_capsulesAffichees);
	}

	private void SupprimerAbsents<T>(Dictionary<int, T> objets) where T : Node
	{
		var aRetirer = new List<int>();
		foreach (KeyValuePair<int, T> kv in objets)
			if (!_idsVus.Contains(kv.Key))
			{
				if (IsInstanceValid(kv.Value))
				{
					// Masquer tout de suite : QueueFree est differe a la fin de la frame,
					// sinon l'objet retire reste affiche une frame -> impression de doublon.
					if (kv.Value is Node3D n3d)
						n3d.Visible = false;
					kv.Value.QueueFree();
				}
				aRetirer.Add(kv.Key);
			}

		foreach (int id in aRetirer)
			objets.Remove(id);
	}

	private BalleScript CreerBalleAffichee(Camp camp, int id)
	{
		var balle = BalleScene.Instantiate<BalleScript>();
		camp.Balles.AddChild(balle);
		balle.IdReseau = id;
		balle.ConfigurerAffichage();
		return balle;
	}

	private CapsuleScript CreerCapsuleAffichee(Camp camp, int id, CapsuleScript.TypeBonus type)
	{
		var capsule = CapsuleScene.Instantiate<CapsuleScript>();
		camp.Capsules.AddChild(capsule);
		capsule.ModeAffichage = true;
		capsule.SensVertical = camp.SensCapsuleVersBarre;
		capsule.Initialiser(type, CouleurBonus(type));
		capsule.IdReseau = id;
		return capsule;
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ClientDespawnBrique(int id)
	{
		if (_briquesParId.TryGetValue(id, out BriqueScript brique))
		{
			if (IsInstanceValid(brique))
				brique.QueueFree();
			_briquesParId.Remove(id);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ClientBriqueFrappee(int id, int resistance)
	{
		if (_briquesParId.TryGetValue(id, out BriqueScript brique) && IsInstanceValid(brique))
			brique.DefinirResistanceReseau(resistance);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ClientBatimentDetruit(int camp, int index)
	{
		if (camp < 0 || camp >= _camps.Count)
			return;
		List<BatimentVille> batiments = _camps[camp].BatimentsVille;
		if (index < 0 || index >= batiments.Count)
			return;

		BatimentVille batiment = batiments[index];
		batiment.Detruit = true;
		if (batiment.Collision != null)
			batiment.Collision.Disabled = true;
		AppliquerEtatBatiment(batiment, 3);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ClientChargerNiveau(int niveau, bool reparerVilles)
	{
		foreach (Camp camp in _camps)
		{
			camp.Niveau = niveau;
			if (reparerVilles)
				ReparerTousLesBatiments(camp);
			ViderConteneur(camp.Balles);
			ViderConteneur(camp.Capsules);
		}

		_ballesAffichees.Clear();
		_capsulesAffichees.Clear();
		SupprimerProjectiles();
		_projectilesAffiches.Clear();
		GenererBriquesCentrales();
		MettreAJourHud();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ClientEtatInitial(int niveau, int[] briquesVivantes, int[] batimentsDetruits)
	{
		foreach (Camp camp in _camps)
			camp.Niveau = niveau;

		GenererBriquesCentrales();

		var vivants = new HashSet<int>(briquesVivantes);
		var aRetirer = new List<int>();
		foreach (KeyValuePair<int, BriqueScript> kv in _briquesParId)
			if (!vivants.Contains(kv.Key))
			{
				if (IsInstanceValid(kv.Value))
					kv.Value.QueueFree();
				aRetirer.Add(kv.Key);
			}
		foreach (int id in aRetirer)
			_briquesParId.Remove(id);

		foreach (int code in batimentsDetruits)
			ClientBatimentDetruit(code / 100, code % 100);

		_etatInitialRecu = true;
		MettreAJourHud();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ClientHud(int[] scores, int[] niveaux, int[] elimine, int briquesRestantes, int etat, string message, bool messageVisible, int indexGagnant)
	{
		_indexGagnant = indexGagnant;
		for (int i = 0; i < _camps.Count; i++)
		{
			if (i < scores.Length)
				_camps[i].Score = scores[i];
			if (i < niveaux.Length)
				_camps[i].Niveau = niveaux[i];
			if (i < elimine.Length)
			{
				bool el = elimine[i] != 0;
				_camps[i].Elimine = el;
				if (_camps[i].Bar != null)
					_camps[i].Bar.Visible = !el;
			}
		}

		_briquesRestantes = briquesRestantes;
		_etat = (Etat)etat;
		MessageLabel.Text = message;
		MessageLabel.Visible = messageVisible;
		MettreAJourHud();
	}
}
