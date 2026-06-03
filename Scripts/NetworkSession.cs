using Godot;
using System.Threading;

// Autoload : etat reseau de la partie (hote autoritaire / clients). Gere la connexion
// ENet, le lobby (assignation des slots aux peers), la tentative UPnP et le demarrage
// synchronise de Jeu.tscn. La logique de jeu vit dans GameManager ; ici on ne fait que
// le transport lobby + cycle de vie des connexions.
public partial class NetworkSession : Node
{
	public static NetworkSession Instance { get; private set; }

	public const int PortParDefaut = 42424;
	public const int MaxClients = PartieConfig.MaxJoueurs - 1;

	public enum EtatReseau
	{
		Offline,
		Hote,
		Client,
	}

	[Signal] public delegate void LobbyMisAJourEventHandler();
	[Signal] public delegate void ConnexionEchoueeEventHandler(string raison);
	[Signal] public delegate void DeconnecteEventHandler(string raison);
	[Signal] public delegate void PartieDemarreeEventHandler();
	[Signal] public delegate void MessageReseauEventHandler(string message);
	// Partie en cours, cote hote : un client a lache, son slot doit passer en IA.
	[Signal] public delegate void JoueurRemplaceParIAEventHandler(int slot);
	// Partie en cours, cote client : l'hote a disparu, on reprend la main en solo.
	[Signal] public delegate void HotePerduEventHandler();

	public EtatReseau Etat { get; private set; } = EtatReseau.Offline;
	public int Port { get; private set; } = PortParDefaut;
	public bool EstHote => Etat == EtatReseau.Hote;
	public bool EstClient => Etat == EtatReseau.Client;
	public bool EstActif => Etat != EtatReseau.Offline;
	// Vrai une fois Jeu.tscn lancee : distingue une deconnexion en lobby (reorganisation
	// des slots) d'une deconnexion en cours de partie (bascule IA / reprise solo).
	public bool EnPartie { get; private set; }
	public string MessageUpnp { get; private set; } = string.Empty;
	// Serveur dedie : hote sans joueur local (SlotLocal = -1), tous les slots distants,
	// pilote par la console stdin et/ou l'auto-demarrage quand tous les humains sont prets.
	public bool ServeurDedie { get; private set; }

	private ENetMultiplayerPeer _peer;
	private Thread _threadUpnp;
	private Upnp _upnp;
	private bool _ferme;

	// Etat "pret" par emplacement (lobby). L'auto-demarrage se declenche quand tous les
	// humains actifs sont prets. Reinitialise quand la composition du lobby change.
	private readonly bool[] _pretSlot = new bool[PartieConfig.MaxJoueurs];

	// Console serveur dedie (lecture stdin sur un thread d'arriere-plan).
	private Thread _threadConsole;

	public override void _Ready()
	{
		Instance = this;
		ProcessMode = ProcessModeEnum.Always;

		Multiplayer.PeerConnected += OnPeerConnecte;
		Multiplayer.PeerDisconnected += OnPeerDeconnecte;
		Multiplayer.ConnectedToServer += OnConnecteAuServeur;
		Multiplayer.ConnectionFailed += OnConnexionEchouee;
		Multiplayer.ServerDisconnected += OnServeurDeconnecte;
	}

	// ------------------------------------------------------------------ Hote

	public bool DemarrerHote(int port)
	{
		Fermer();
		_ferme = false;
		Port = port;
		ServeurDedie = false;
		ReinitialiserPrets();
		_peer = new ENetMultiplayerPeer();
		Error err = _peer.CreateServer(port, MaxClients);
		if (err != Error.Ok)
		{
			_peer = null;
			EmitSignal(SignalName.ConnexionEchouee, $"Impossible d'ouvrir le port {port} ({err}).");
			return false;
		}

		Multiplayer.MultiplayerPeer = _peer;
		Etat = EtatReseau.Hote;
		PartieConfig.Mode = PartieConfig.ModePartie.Reseau;
		PartieConfig.SlotLocal = 0;
		PartieConfig.ReinitialiserPeers();

		// Slot 0 = hote (humain local), les autres slots actifs attendent un client.
		PartieConfig.DefinirControle(0, PartieConfig.TypeControle.Humain);
		for (int i = 1; i < PartieConfig.MaxJoueurs; i++)
			PartieConfig.DefinirControle(i, PartieConfig.TypeControle.HumainDistant);

		LancerUpnp(port);
		EmitSignal(SignalName.LobbyMisAJour);
		return true;
	}

	// ---------------------------------------------------------- Serveur dedie

	// Demarre un hote SANS joueur local : tous les slots sont distants (humains ou IA en
	// complement), SlotLocal = -1. Pilote par la console stdin et l'auto-demarrage.
	public bool DemarrerServeurDedie(int port, int nbJoueurs)
	{
		Fermer();
		_ferme = false;
		Port = port;
		ServeurDedie = true;
		ReinitialiserPrets();
		PartieConfig.NombreJoueurs = Mathf.Clamp(nbJoueurs, PartieConfig.MinJoueurs, PartieConfig.MaxJoueurs);

		_peer = new ENetMultiplayerPeer();
		// Aucune place reservee a un joueur local : capacite = nombre de slots distants.
		Error err = _peer.CreateServer(port, PartieConfig.NombreJoueurs);
		if (err != Error.Ok)
		{
			_peer = null;
			ServeurDedie = false;
			EmitSignal(SignalName.ConnexionEchouee, $"Impossible d'ouvrir le port {port} ({err}).");
			return false;
		}

		Multiplayer.MultiplayerPeer = _peer;
		Etat = EtatReseau.Hote;
		PartieConfig.Mode = PartieConfig.ModePartie.Reseau;
		PartieConfig.SlotLocal = -1;
		PartieConfig.ReinitialiserPeers();

		// Tous les slots actifs attendent un client (l'IA comble les vides au demarrage).
		for (int i = 0; i < PartieConfig.MaxJoueurs; i++)
			PartieConfig.DefinirControle(i, PartieConfig.TypeControle.HumainDistant);

		LancerUpnp(port);
		DemarrerConsoleServeur();
		EmitSignal(SignalName.LobbyMisAJour);
		return true;
	}

	// Comble chaque slot distant encore vide (aucun peer) par une IA. Appele avant le
	// demarrage d'un serveur dedie (auto-demarrage ou commande "start").
	public void HoteRemplirVidesAvecIA()
	{
		if (!EstHote)
			return;

		for (int i = 0; i < PartieConfig.NombreJoueurs; i++)
			if (PartieConfig.ControleDe(i) == PartieConfig.TypeControle.HumainDistant
				&& PartieConfig.PeerControleurDe(i) == 0)
			{
				PartieConfig.DefinirPeer(i, 0);
				PartieConfig.DefinirControle(i, PartieConfig.TypeControle.IA);
			}
	}

	// ----------------------------------------------------------------- Client

	public bool RejoindreHote(string adresse, int port)
	{
		Fermer();
		_ferme = false;
		Port = port;
		_peer = new ENetMultiplayerPeer();
		Error err = _peer.CreateClient(adresse, port);
		if (err != Error.Ok)
		{
			_peer = null;
			EmitSignal(SignalName.ConnexionEchouee, $"Connexion impossible vers {adresse}:{port} ({err}).");
			return false;
		}

		Multiplayer.MultiplayerPeer = _peer;
		Etat = EtatReseau.Client;
		PartieConfig.Mode = PartieConfig.ModePartie.Reseau;
		return true;
	}

	// Ferme uniquement le transport ENet (peer, UPnP) sans toucher a PartieConfig : utilise
	// lors d'une reprise solo (l'hote a disparu) ou le GameManager garde le slot local courant.
	private void FermerTransport()
	{
		_ferme = true;
		ArreterUpnp();

		if (_peer != null)
		{
			_peer.Close();
			_peer = null;
		}

		if (Multiplayer.MultiplayerPeer != null)
			Multiplayer.MultiplayerPeer = null;

		Etat = EtatReseau.Offline;
		EnPartie = false;
	}

	public void Fermer()
	{
		FermerTransport();
		ServeurDedie = false;
		ReinitialiserPrets();
		PartieConfig.Mode = PartieConfig.ModePartie.Local;
		PartieConfig.SlotLocal = 0;
		PartieConfig.ReinitialiserPeers();
	}

	// ------------------------------------------------------- Lobby (cote hote)

	// Definit le nombre de joueurs et reorganise les slots ; appele par l'UI hote.
	public void HoteDefinirNombreJoueurs(int nombre)
	{
		if (!EstHote)
			return;

		PartieConfig.NombreJoueurs = Mathf.Clamp(nombre, PartieConfig.MinJoueurs, PartieConfig.MaxJoueurs);
		ReinitialiserPrets();
		ReassignerPeers();
		DiffuserLobby();
	}

	// Bascule un slot entre HumainDistant (attend/contient un client) et IA. En hote
	// normal le slot 0 (humain local) n'est pas basculable ; en serveur dedie tous le sont.
	public void HoteBasculerSlotIA(int slot)
	{
		if (!EstHote || slot < 0 || slot >= PartieConfig.NombreJoueurs)
			return;
		if (!ServeurDedie && slot == 0)
			return;

		PartieConfig.TypeControle actuel = PartieConfig.ControleDe(slot);
		if (actuel == PartieConfig.TypeControle.IA)
		{
			PartieConfig.DefinirControle(slot, PartieConfig.TypeControle.HumainDistant);
		}
		else
		{
			PartieConfig.DefinirPeer(slot, 0);
			PartieConfig.DefinirControle(slot, PartieConfig.TypeControle.IA);
		}

		_pretSlot[slot] = false;
		ReassignerPeers();
		DiffuserLobby();
	}

	// ---------------------------------------------------- Etat "pret" (lobby)

	// Declare l'emplacement local pret/pas pret. Cote client : envoie a l'hote ; cote
	// hote-joueur : applique localement et reevalue l'auto-demarrage.
	public void DefinirPretLocal(bool pret)
	{
		if (EstClient)
		{
			RpcId(1, MethodName.HoteRecevoirPret, pret);
			return;
		}

		if (EstHote && PartieConfig.SlotLocal >= 0 && PartieConfig.SlotLocal < _pretSlot.Length)
		{
			_pretSlot[PartieConfig.SlotLocal] = pret;
			DiffuserLobby();
			EvaluerDemarrageAuto();
		}
	}

	public bool EstPret(int slot) => slot >= 0 && slot < _pretSlot.Length && _pretSlot[slot];

	private void ReinitialiserPrets()
	{
		for (int i = 0; i < _pretSlot.Length; i++)
			_pretSlot[i] = false;
	}

	// Vrai si chaque humain actif (local ou client connecte) est pret. Les slots distants
	// vides sont tolerees en serveur dedie (combles par IA au demarrage). Exige >= 1 humain.
	private bool TousHumainsPrets()
	{
		if (!EstHote || EnPartie)
			return false;

		int humains = 0;
		for (int i = 0; i < PartieConfig.NombreJoueurs; i++)
		{
			switch (PartieConfig.ControleDe(i))
			{
				case PartieConfig.TypeControle.Humain:
					humains++;
					if (!_pretSlot[i])
						return false;
					break;
				case PartieConfig.TypeControle.HumainDistant:
					if (PartieConfig.PeerControleurDe(i) != 0)
					{
						humains++;
						if (!_pretSlot[i])
							return false;
					}
					else if (!ServeurDedie)
					{
						// Hote normal : un slot distant vide doit etre comble avant de partir.
						return false;
					}
					break;
			}
		}

		return humains > 0;
	}

	// Lance la partie si toutes les conditions "pret" sont reunies. En serveur dedie on
	// comble d'abord les places vides par de l'IA.
	private void EvaluerDemarrageAuto()
	{
		if (!TousHumainsPrets())
			return;

		if (ServeurDedie)
		{
			HoteRemplirVidesAvecIA();
			GD.Print("[SERVEUR] Tous les humains prets -> demarrage automatique.");
		}

		HoteDemarrerPartie();
	}

	// Vrai si chaque slot actif a un controleur (hote, IA, ou client connecte).
	public bool PartiePeutDemarrer()
	{
		if (!EstHote)
			return false;

		for (int i = 0; i < PartieConfig.NombreJoueurs; i++)
		{
			if (PartieConfig.ControleDe(i) == PartieConfig.TypeControle.HumainDistant
				&& PartieConfig.PeerControleurDe(i) == 0)
				return false;
		}

		return true;
	}

	public void HoteDemarrerPartie()
	{
		if (!PartiePeutDemarrer())
			return;

		// Envoyer la config a chaque client (slot local = son slot) puis charger Jeu.tscn.
		int[] controles = SerialiserControles();
		int[] peers = SerialiserPeers();
		foreach (int id in Multiplayer.GetPeers())
		{
			int slot = PartieConfig.SlotDuPeer(id);
			RpcId(id, MethodName.ClientDemarrerPartie, PartieConfig.NombreJoueurs, controles, peers, slot);
		}

		DemarrerJeuLocal();
	}

	private void DemarrerJeuLocal()
	{
		EnPartie = true;
		EmitSignal(SignalName.PartieDemarree);
		GetTree().ChangeSceneToFile("res://Jeu.tscn");
	}

	// Place les peers connectes dans les slots HumainDistant disponibles, vide les autres.
	private void ReassignerPeers()
	{
		var ids = new System.Collections.Generic.List<int>(Multiplayer.GetPeers());

		for (int i = 0; i < PartieConfig.MaxJoueurs; i++)
			if (PartieConfig.ControleDe(i) != PartieConfig.TypeControle.HumainDistant
				|| i >= PartieConfig.NombreJoueurs)
				PartieConfig.DefinirPeer(i, 0);

		// Conserver les assignations encore valides.
		for (int i = 0; i < PartieConfig.NombreJoueurs; i++)
		{
			int peer = PartieConfig.PeerControleurDe(i);
			if (peer != 0 && ids.Contains(peer))
				ids.Remove(peer);
			else
				PartieConfig.DefinirPeer(i, 0);
		}

		// Attribuer les peers restants aux slots distants libres.
		foreach (int id in ids)
		{
			int slot = PremierSlotDistantLibre();
			if (slot < 0)
				break;
			PartieConfig.DefinirPeer(slot, id);
		}
	}

	private int PremierSlotDistantLibre()
	{
		for (int i = 0; i < PartieConfig.NombreJoueurs; i++)
			if (PartieConfig.ControleDe(i) == PartieConfig.TypeControle.HumainDistant
				&& PartieConfig.PeerControleurDe(i) == 0)
				return i;
		return -1;
	}

	private void DiffuserLobby()
	{
		int[] controles = SerialiserControles();
		int[] peers = SerialiserPeers();
		int[] prets = SerialiserPrets();
		Rpc(MethodName.ClientRecevoirLobby, PartieConfig.NombreJoueurs, controles, peers, prets);
		EmitSignal(SignalName.LobbyMisAJour);
	}

	private int[] SerialiserPrets()
	{
		var arr = new int[PartieConfig.MaxJoueurs];
		for (int i = 0; i < arr.Length; i++)
			arr[i] = _pretSlot[i] ? 1 : 0;
		return arr;
	}

	private static int[] SerialiserControles()
	{
		var arr = new int[PartieConfig.MaxJoueurs];
		for (int i = 0; i < arr.Length; i++)
			arr[i] = (int)PartieConfig.ControleDe(i);
		return arr;
	}

	private static int[] SerialiserPeers()
	{
		var arr = new int[PartieConfig.MaxJoueurs];
		for (int i = 0; i < arr.Length; i++)
			arr[i] = PartieConfig.PeerControleurDe(i);
		return arr;
	}

	private void AppliquerLobby(int nombre, int[] controles, int[] peers, int[] prets)
	{
		PartieConfig.NombreJoueurs = Mathf.Clamp(nombre, PartieConfig.MinJoueurs, PartieConfig.MaxJoueurs);
		for (int i = 0; i < PartieConfig.MaxJoueurs && i < controles.Length; i++)
			PartieConfig.DefinirControle(i, (PartieConfig.TypeControle)controles[i]);
		for (int i = 0; i < PartieConfig.MaxJoueurs && i < peers.Length; i++)
			PartieConfig.DefinirPeer(i, peers[i]);
		for (int i = 0; i < _pretSlot.Length && i < prets.Length; i++)
			_pretSlot[i] = prets[i] != 0;
	}

	// ------------------------------------------------------- RPC (cote client)

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ClientRecevoirLobby(int nombre, int[] controles, int[] peers, int[] prets)
	{
		AppliquerLobby(nombre, controles, peers, prets);
		PartieConfig.SlotLocal = PartieConfig.SlotDuPeer(Multiplayer.GetUniqueId());
		EmitSignal(SignalName.LobbyMisAJour);
	}

	// Recue sur l'hote : un client (ou l'hote-joueur via boucle locale) declare son etat pret.
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void HoteRecevoirPret(bool pret)
	{
		if (!EstHote)
			return;

		int slot = PartieConfig.SlotDuPeer(Multiplayer.GetRemoteSenderId());
		if (slot < 0 || slot >= _pretSlot.Length)
			return;

		_pretSlot[slot] = pret;
		DiffuserLobby();
		EvaluerDemarrageAuto();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ClientDemarrerPartie(int nombre, int[] controles, int[] peers, int slotLocal)
	{
		AppliquerLobby(nombre, controles, peers, System.Array.Empty<int>());
		PartieConfig.Mode = PartieConfig.ModePartie.Reseau;
		PartieConfig.SlotLocal = slotLocal;
		EnPartie = true;
		EmitSignal(SignalName.PartieDemarree);
		GetTree().ChangeSceneToFile("res://Jeu.tscn");
	}

	// ----------------------------------------------- Cycle de vie connexions

	private void OnPeerConnecte(long id)
	{
		if (_ferme || !EstHote || Multiplayer.MultiplayerPeer == null)
			return;

		// Nouvelle composition : les "prets" precedents ne sont plus valables.
		ReinitialiserPrets();
		ReassignerPeers();
		DiffuserLobby();
		EmitSignal(SignalName.MessageReseau, $"Joueur connecte (peer {id}).");
	}

	private void OnPeerDeconnecte(long id)
	{
		if (_ferme || !EstHote || Multiplayer.MultiplayerPeer == null)
			return;

		int slot = PartieConfig.SlotDuPeer((int)id);

		// Partie en cours : le client lache, son emplacement passe en IA et le jeu continue.
		if (EnPartie)
		{
			if (slot >= 0)
			{
				PartieConfig.DefinirControle(slot, PartieConfig.TypeControle.IA);
				PartieConfig.DefinirPeer(slot, 0);
				EmitSignal(SignalName.JoueurRemplaceParIA, slot);
			}
			return;
		}

		// Lobby : on libere le slot et on reorganise.
		if (slot >= 0)
			PartieConfig.DefinirPeer(slot, 0);
		ReinitialiserPrets();
		ReassignerPeers();
		DiffuserLobby();
		EmitSignal(SignalName.MessageReseau, $"Joueur deconnecte (peer {id}).");
	}

	private void OnConnecteAuServeur()
	{
		PartieConfig.SlotLocal = -1;
		EmitSignal(SignalName.MessageReseau, "Connecte a l'hote.");
		EmitSignal(SignalName.LobbyMisAJour);
	}

	private void OnConnexionEchouee()
	{
		Fermer();
		EmitSignal(SignalName.ConnexionEchouee, "Connexion a l'hote echouee.");
	}

	private void OnServeurDeconnecte()
	{
		// Partie en cours : l'hote a disparu. On coupe juste le transport (sans toucher au
		// slot local) et on laisse le GameManager reprendre la partie en solo contre l'IA.
		if (EnPartie)
		{
			FermerTransport();
			EmitSignal(SignalName.HotePerdu);
			return;
		}

		Fermer();
		EmitSignal(SignalName.Deconnecte, "Hote deconnecte.");
	}

	// --------------------------------------------------- Console serveur dedie

	// Force le demarrage (commande "start") : comble les vides par IA puis lance, meme si
	// tous les humains ne sont pas prets.
	public void DemarrerServeurMaintenant()
	{
		if (!EstHote)
			return;
		HoteRemplirVidesAvecIA();
		HoteDemarrerPartie();
	}

	// Lit les commandes operateur sur stdin (thread d'arriere-plan). NetworkSession etant un
	// autoload, le thread survit au changement de scene. Marshale chaque ligne vers le thread
	// principal via CallDeferred (thread-safe dans Godot).
	private void DemarrerConsoleServeur()
	{
		AfficherAideServeur();
		_threadConsole = new Thread(BoucleConsole) { IsBackground = true };
		_threadConsole.Start();
	}

	private void BoucleConsole()
	{
		while (!_ferme)
		{
			string ligne;
			try
			{
				ligne = System.Console.ReadLine();
			}
			catch (System.Exception)
			{
				return;
			}

			if (ligne == null)
				return; // stdin ferme

			CallDeferred(MethodName.ExecuterCommandeServeur, ligne);
		}
	}

	private static void AfficherAideServeur()
	{
		GD.Print("[SERVEUR] Serveur dedie pret. Commandes : "
			+ "start | status | ia <slot> | humain <slot> | joueurs <n> | quit");
	}

	private void ExecuterCommandeServeur(string ligne)
	{
		if (string.IsNullOrWhiteSpace(ligne))
			return;

		string[] mots = ligne.Trim().Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
		string cmd = mots[0].ToLowerInvariant();

		switch (cmd)
		{
			case "start":
				GD.Print("[SERVEUR] Demarrage force.");
				DemarrerServeurMaintenant();
				break;
			case "status":
				AfficherStatutServeur();
				break;
			case "ia":
			case "humain":
				if (mots.Length >= 2 && int.TryParse(mots[1], out int slot))
				{
					PartieConfig.TypeControle vise = cmd == "ia"
						? PartieConfig.TypeControle.IA
						: PartieConfig.TypeControle.HumainDistant;
					if (PartieConfig.ControleDe(slot) != vise)
						HoteBasculerSlotIA(slot);
					AfficherStatutServeur();
				}
				else
				{
					GD.Print($"[SERVEUR] Usage : {cmd} <slot>");
				}
				break;
			case "joueurs":
				if (mots.Length >= 2 && int.TryParse(mots[1], out int n))
				{
					HoteDefinirNombreJoueurs(n);
					AfficherStatutServeur();
				}
				else
				{
					GD.Print("[SERVEUR] Usage : joueurs <2-4>");
				}
				break;
			case "quit":
			case "exit":
				GD.Print("[SERVEUR] Arret.");
				GetTree().Quit();
				break;
			default:
				GD.Print($"[SERVEUR] Commande inconnue : {cmd}");
				AfficherAideServeur();
				break;
		}
	}

	private void AfficherStatutServeur()
	{
		var sb = new System.Text.StringBuilder($"[SERVEUR] {PartieConfig.NombreJoueurs} joueurs | etat={Etat}");
		for (int i = 0; i < PartieConfig.NombreJoueurs; i++)
		{
			string type = PartieConfig.ControleDe(i).ToString();
			int peer = PartieConfig.PeerControleurDe(i);
			string co = PartieConfig.ControleDe(i) == PartieConfig.TypeControle.HumainDistant
				? (peer != 0 ? $"peer {peer}" : "en attente") : "-";
			string pret = _pretSlot[i] ? "PRET" : "non pret";
			sb.Append($"\n  slot {i}: {type} ({co}) {pret}");
		}
		GD.Print(sb.ToString());
	}

	// -------------------------------------------------------------- UPnP

	private void LancerUpnp(int port)
	{
		MessageUpnp = "Tentative d'ouverture du port (UPnP)...";
		EmitSignal(SignalName.MessageReseau, MessageUpnp);
		_threadUpnp = new Thread(() => TacheUpnp(port)) { IsBackground = true };
		_threadUpnp.Start();
	}

	private void TacheUpnp(int port)
	{
		_upnp = new Upnp();
		int resultat = _upnp.Discover();
		string message;

		if (resultat == (int)Upnp.UpnpResult.Success
			&& _upnp.GetGateway() != null
			&& _upnp.GetGateway().IsValidGateway())
		{
			Error map = (Error)_upnp.AddPortMapping(port, port, "FTLM", "UDP");
			if (map == Error.Ok)
			{
				string ip = _upnp.QueryExternalAddress();
				message = string.IsNullOrEmpty(ip)
					? "UPnP : port ouvert."
					: $"UPnP OK - les joueurs peuvent rejoindre via {ip}:{port}.";
			}
			else
			{
				message = $"UPnP a echoue (mapping). Ouvrez le port UDP {port} sur votre box.";
			}
		}
		else
		{
			message = $"UPnP indisponible. Ouvrez le port UDP {port} sur votre box.";
		}

		MessageUpnp = message;
		CallDeferred(MethodName.SignalerMessageReseau, message);
	}

	private void SignalerMessageReseau(string message)
	{
		EmitSignal(SignalName.MessageReseau, message);
	}

	private void ArreterUpnp()
	{
		if (_threadUpnp != null && _threadUpnp.IsAlive)
			_threadUpnp.Join(500);
		_threadUpnp = null;

		if (_upnp != null)
		{
			_upnp.DeletePortMapping(Port, "UDP");
			_upnp = null;
		}
	}

	public override void _ExitTree()
	{
		_ferme = true;
		ArreterUpnp();
	}
}
