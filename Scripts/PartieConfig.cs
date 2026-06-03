using Godot;

// Configuration de la partie choisie dans le menu (nombre de joueurs et type de
// controle par emplacement). Lue par GameManager au lancement de Jeu.tscn.
// Sur le modele de SettingsManager : etat statique partage entre scenes.
public static class PartieConfig
{
	public const int MinJoueurs = 2;
	public const int MaxJoueurs = 4;

	// Mode de la partie : locale (clavier(s) sur la meme machine) ou reseau (un
	// hote autoritaire, des clients distants). En reseau seul l'hote simule.
	public enum ModePartie
	{
		Local,
		Reseau,
	}

	public enum TypeControle
	{
		Humain,         // humain local (clavier/souris de cette machine)
		IA,             // controle par l'ordinateur (sur l'hote en reseau)
		HumainDistant,  // humain pilote par un peer distant (mode reseau seulement)
	}

	public static ModePartie Mode { get; set; } = ModePartie.Local;
	public static int NombreJoueurs { get; set; } = 2;

	// Slot (index de joueur) controle par CETTE machine. Pertinent surtout cote
	// client reseau ; l'hote utilise SlotLocal pour son propre joueur humain.
	public static int SlotLocal { get; set; } = 0;

	// Type de controle par emplacement. Defaut : J1 humain, le reste en IA
	// (= ancien duel Joueur vs IA, comportement attendu par --jeu / --test).
	private static readonly TypeControle[] _controles =
	{
		TypeControle.Humain,
		TypeControle.IA,
		TypeControle.IA,
		TypeControle.IA,
	};

	// Peer reseau (ENet ID) qui controle chaque emplacement. 0 = hote / local.
	// Renseigne par NetworkSession quand un client prend un slot.
	private static readonly int[] _peers = new int[MaxJoueurs];

	// Quadruplets d'actions (gauche, droite, lancement, capacite) par emplacement de
	// joueur. J1 reutilise ui_left/ui_right/lancer_balle/tirer_capacite (remappables
	// dans les Options) ; J2-J4 ont leurs propres actions enregistrees au runtime via
	// EnregistrerActions(). Le lancement n'envoie que la balle ; la capacite tire le laser.
	private static readonly (string Gauche, string Droite, string Action, string Capacite)[] _actions =
	{
		("ui_left", "ui_right", "lancer_balle", "tirer_capacite"),
		("j2_gauche", "j2_droite", "j2_action", "j2_capacite"),
		("j3_gauche", "j3_droite", "j3_action", "j3_capacite"),
		("j4_gauche", "j4_droite", "j4_action", "j4_capacite"),
	};

	// Action de cyclage de cible par emplacement. J1 utilise la molette souris (pas
	// d'action), J2-J4 ont une touche dediee enregistree au runtime.
	private static readonly string[] _actionsCible =
	{
		"",
		"j2_cibler",
		"j3_cibler",
		"j4_cibler",
	};

	private static readonly (string Action, Key Touche)[] _toucheParDefaut =
	{
		("j2_gauche", Key.A),
		("j2_droite", Key.D),
		("j2_action", Key.W),
		("j2_capacite", Key.S),
		("j2_cibler", Key.Q),
		("j3_gauche", Key.J),
		("j3_droite", Key.L),
		("j3_action", Key.I),
		("j3_capacite", Key.K),
		("j3_cibler", Key.U),
		("j4_gauche", Key.Kp4),
		("j4_droite", Key.Kp6),
		("j4_action", Key.Kp5),
		("j4_capacite", Key.Kp8),
		("j4_cibler", Key.Kp7),
	};

	public static TypeControle ControleDe(int index)
	{
		return index >= 0 && index < _controles.Length ? _controles[index] : TypeControle.IA;
	}

	public static void DefinirControle(int index, TypeControle type)
	{
		if (index >= 0 && index < _controles.Length)
			_controles[index] = type;
	}

	// Vrai si l'emplacement doit etre simule par l'IA (sur l'hote/en local).
	public static bool EstIA(int index) => ControleDe(index) == TypeControle.IA;

	// Vrai si l'emplacement est pilote par un humain (local ou distant).
	public static bool EstHumain(int index) => ControleDe(index) != TypeControle.IA;

	public static (string Gauche, string Droite, string Action) ActionsDe(int index)
	{
		var a = index >= 0 && index < _actions.Length ? _actions[index] : _actions[0];
		return (a.Gauche, a.Droite, a.Action);
	}

	public static string ActionLancementDe(int index)
	{
		return index >= 0 && index < _actions.Length ? _actions[index].Action : _actions[0].Action;
	}

	public static string ActionCapaciteDe(int index)
	{
		return index >= 0 && index < _actions.Length ? _actions[index].Capacite : _actions[0].Capacite;
	}

	// Action de cyclage de cible (objets offensifs). "" pour J1 (molette souris).
	public static string ActionCibleDe(int index)
	{
		return index >= 0 && index < _actionsCible.Length ? _actionsCible[index] : "";
	}

	// Touches par defaut des joueurs 2 a 4 (source unique, reutilisee par
	// SettingsManager pour le chargement/sauvegarde/remappage de ces actions).
	public static (string Action, Key Touche)[] ToucheParDefaut() => _toucheParDefaut;

	public static int PeerControleurDe(int index)
	{
		return index >= 0 && index < _peers.Length ? _peers[index] : 0;
	}

	public static void DefinirPeer(int index, int peerId)
	{
		if (index >= 0 && index < _peers.Length)
			_peers[index] = peerId;
	}

	// Index du slot controle par un peer donne, ou -1 si aucun.
	public static int SlotDuPeer(int peerId)
	{
		for (int i = 0; i < _peers.Length; i++)
			if (_peers[i] == peerId)
				return i;
		return -1;
	}

	// Remet la table des peers a zero (retour lobby / menu).
	public static void ReinitialiserPeers()
	{
		for (int i = 0; i < _peers.Length; i++)
			_peers[i] = 0;
	}

	// Enregistre les actions clavier des joueurs 2 a 4 dans l'InputMap si elles
	// n'existent pas deja (les actions de J1 sont gerees par SettingsManager).
	public static void EnregistrerActions()
	{
		foreach ((string action, Key touche) in _toucheParDefaut)
		{
			if (!InputMap.HasAction(action))
				InputMap.AddAction(action);

			if (InputMap.ActionGetEvents(action).Count > 0)
				continue;

			InputMap.ActionAddEvent(action, new InputEventKey
			{
				PhysicalKeycode = touche,
				Keycode = touche,
			});
		}
	}
}
