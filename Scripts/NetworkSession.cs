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

	private ENetMultiplayerPeer _peer;
	private Thread _threadUpnp;
	private Upnp _upnp;
	private bool _ferme;

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
		ReassignerPeers();
		DiffuserLobby();
	}

	// Bascule un slot entre HumainDistant (attend/contient un client) et IA. Le slot 0
	// (hote) reste toujours humain local.
	public void HoteBasculerSlotIA(int slot)
	{
		if (!EstHote || slot <= 0 || slot >= PartieConfig.NombreJoueurs)
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

		ReassignerPeers();
		DiffuserLobby();
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
		Rpc(MethodName.ClientRecevoirLobby, PartieConfig.NombreJoueurs, controles, peers);
		EmitSignal(SignalName.LobbyMisAJour);
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

	private static void AppliquerLobby(int nombre, int[] controles, int[] peers)
	{
		PartieConfig.NombreJoueurs = Mathf.Clamp(nombre, PartieConfig.MinJoueurs, PartieConfig.MaxJoueurs);
		for (int i = 0; i < PartieConfig.MaxJoueurs && i < controles.Length; i++)
			PartieConfig.DefinirControle(i, (PartieConfig.TypeControle)controles[i]);
		for (int i = 0; i < PartieConfig.MaxJoueurs && i < peers.Length; i++)
			PartieConfig.DefinirPeer(i, peers[i]);
	}

	// ------------------------------------------------------- RPC (cote client)

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ClientRecevoirLobby(int nombre, int[] controles, int[] peers)
	{
		AppliquerLobby(nombre, controles, peers);
		PartieConfig.SlotLocal = PartieConfig.SlotDuPeer(Multiplayer.GetUniqueId());
		EmitSignal(SignalName.LobbyMisAJour);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ClientDemarrerPartie(int nombre, int[] controles, int[] peers, int slotLocal)
	{
		AppliquerLobby(nombre, controles, peers);
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
