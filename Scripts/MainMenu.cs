using Godot;
using System;

public partial class MainMenu : Control
{
	private OptionsMenu _options;
	private PanelContainer _menuPanel;
	private HBoxContainer _piedStudio;
	private PanelContainer _selectionPanel;
	private readonly Button[] _boutonsNombre = new Button[3];
	private readonly HBoxContainer[] _lignesJoueur = new HBoxContainer[PartieConfig.MaxJoueurs];
	private readonly Button[] _boutonsControle = new Button[PartieConfig.MaxJoueurs];

	// Lobby reseau (construit en code, partage hote/client).
	private PanelContainer _reseauPanel;
	private PanelContainer _joinPanel;
	private Label _titreReseau;
	private Label _statutReseau;
	private HBoxContainer _ligneNombreReseau;
	private readonly Button[] _boutonsNombreReseau = new Button[3];
	private readonly HBoxContainer[] _lignesSlot = new HBoxContainer[PartieConfig.MaxJoueurs];
	private readonly Label[] _labelsSlot = new Label[PartieConfig.MaxJoueurs];
	private readonly Button[] _boutonsSlot = new Button[PartieConfig.MaxJoueurs];
	private Button _demarrerReseau;
	private Button _pretReseau;
	private LineEdit _champIp;
	private LineEdit _champPort;

	// Harnais reseau automatise (--nethost / --netjoin) : pilote hote+client en local
	// pour un test deux instances scripte. Sans effet en jeu normal.
	private bool _netHost;
	private bool _netJoin;
	private bool _netDemarre;
	private double _netRetryRestant;

	private static readonly string[] _indicesTouches =
	{
		"Fleches",
		"A / D",
		"J / L",
		"Pave num. 4 / 6",
	};

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;

		// Serveur dedie : pas de menu, on demarre l'hote sans joueur local et on rend la main
		// a la console / a l'auto-demarrage (tous prets). Doit etre teste avant tout le reste.
		if (DemarrerServeurDedieSiDemande())
			return;

		MenuTheme.Appliquer(this);
		SettingsManager.Charger();
		SettingsManager.Appliquer(GetTree());
		PartieConfig.EnregistrerActions();

		// Retour au menu : on s'assure d'etre hors-ligne (une partie reseau precedente a pu rester active).
		NetworkSession.Instance?.Fermer();

		_options = GetNode<OptionsMenu>("OptionsMenu");
		_menuPanel = GetNode<PanelContainer>("MenuPanel");
		GetNode<Button>("MenuPanel/VBox/NouveauButton").Pressed += OuvrirSelection;
		GetNode<Button>("MenuPanel/VBox/OptionsButton").Pressed += () => _options.Ouvrir();
		GetNode<Button>("MenuPanel/VBox/QuitterButton").Pressed += () => GetTree().Quit();

		ConstruireBoutonsReseau();
		ConstruireSelection();
		ConstruireReseauPanel();
		ConstruireJoinPanel();
		ConstruirePiedStudio();
		ConstruireVersion();
		BrancherSignauxReseau();

		// Lancement direct d'une nouvelle partie sans passer par le menu (pratique pour les tests) :
		// "--jeu" pour jouer, "--test" pour enchainer sur le harnais auto de Jeu.tscn.
		if (LancementDirectDemande())
			CallDeferred(MethodName.Nouveau);

		DemarrerHarnaisReseauSiDemande();
	}

	// Serveur dedie headless : "--serveur" demarre un hote sans joueur local. Port et nombre
	// de joueurs reglables (--port <n> / --port=<n>, --joueurs <n> / --joueurs=<n>).
	// Lancement type : Godot ... --headless -- --serveur --port 42424 --joueurs 2
	private bool DemarrerServeurDedieSiDemande()
	{
		bool demande = false;
		foreach (string arg in ArgsCombines())
			if (arg == "--serveur" || arg == "--server" || arg == "--dedie")
			{
				demande = true;
				break;
			}

		if (!demande || NetworkSession.Instance is not NetworkSession session)
			return false;

		int port = LireArgInt("--port", NetworkSession.PortParDefaut);
		int joueurs = LireArgInt("--joueurs", 2);

		if (!session.DemarrerServeurDedie(port, joueurs))
		{
			GD.PushError($"[SERVEUR] Impossible d'ouvrir le serveur sur le port {port}.");
			GetTree().Quit(1);
			return true;
		}

		GD.Print($"[SERVEUR] Serveur dedie demarre sur le port {port} pour {PartieConfig.NombreJoueurs} joueurs.");
		return true;
	}

	// Lit la valeur entiere d'un argument "--cle <valeur>" ou "--cle=<valeur>".
	private static int LireArgInt(string cle, int defaut)
	{
		string prefixe = cle + "=";
		string[] args = System.Linq.Enumerable.ToArray(ArgsCombines());
		for (int i = 0; i < args.Length; i++)
		{
			if (args[i].StartsWith(prefixe, System.StringComparison.Ordinal)
				&& int.TryParse(args[i].Substring(prefixe.Length), out int v))
				return v;
			if (args[i] == cle && i + 1 < args.Length && int.TryParse(args[i + 1], out int v2))
				return v2;
		}
		return defaut;
	}

	// Harnais deux instances : --nethost ouvre un hote 2 joueurs (slot 2 distant) et
	// demarre des qu'un client arrive ; --netjoin rejoint 127.0.0.1.
	private void DemarrerHarnaisReseauSiDemande()
	{
		if (NetworkSession.Instance is not NetworkSession session)
			return;

		foreach (string arg in ArgsCombines())
		{
			if (arg == "--nethost")
				_netHost = true;
			else if (arg == "--netjoin")
				_netJoin = true;
		}

		if (_netHost)
		{
			PartieConfig.NombreJoueurs = 2;
			session.DemarrerHote(NetworkSession.PortParDefaut);
		}
		else if (_netJoin)
		{
			session.RejoindreHote("127.0.0.1", NetworkSession.PortParDefaut);
		}
	}

	public override void _Process(double delta)
	{
		// Le pied de page (studio / Ko-fi) ne s'affiche que sur l'ecran principal,
		// pour ne pas deborder par-dessus les Options ou les sous-panneaux.
		if (_piedStudio != null)
			_piedStudio.Visible = (_menuPanel?.Visible ?? false) && !(_options?.Visible ?? false);

		if (NetworkSession.Instance is not NetworkSession session)
			return;

		if (_netHost && !_netDemarre && session.EstHote && session.PartiePeutDemarrer())
		{
			_netDemarre = true;
			session.HoteDemarrerPartie();
		}

		// Client : retente la connexion tant que l'hote n'est pas joignable.
		if (_netJoin && session.Etat == NetworkSession.EtatReseau.Offline)
		{
			_netRetryRestant -= delta;
			if (_netRetryRestant <= 0.0)
			{
				_netRetryRestant = 1.0;
				session.RejoindreHote("127.0.0.1", NetworkSession.PortParDefaut);
			}
		}
	}

	// Lancement direct dans Jeu.tscn selon l'argument CLI :
	//   --jeu / --test                  -> partie par defaut (2 joueurs, J1 humain + IA)
	//   --jeu2 / --jeu3 / --jeu4         -> partie a 2 / 3 / 4 joueurs (J1 humain, reste IA)
	//   --smoke2 / --smoke3 / --smoke4   -> mode de fumee (tout IA, gere par GameManager)
	private static bool LancementDirectDemande()
	{
		foreach (string arg in ArgsCombines())
		{
			switch (arg)
			{
				case "--jeu":
				case "--test":
				case "--smoke2":
				case "--smoke3":
				case "--smoke4":
					return true;
				case "--jeu2":
					PartieConfig.NombreJoueurs = 2;
					return true;
				case "--jeu3":
					PartieConfig.NombreJoueurs = 3;
					return true;
				case "--jeu4":
					PartieConfig.NombreJoueurs = 4;
					return true;
			}
		}

		return false;
	}

	private static System.Collections.Generic.IEnumerable<string> ArgsCombines()
	{
		foreach (string arg in OS.GetCmdlineArgs())
			yield return arg;
		foreach (string arg in OS.GetCmdlineUserArgs())
			yield return arg;
	}

	// Ajoute "Heberger" et "Rejoindre" au menu principal (avant Options).
	private void ConstruireBoutonsReseau()
	{
		var vbox = GetNode<VBoxContainer>("MenuPanel/VBox");
		var optionsButton = GetNode<Button>("MenuPanel/VBox/OptionsButton");

		var heberger = new Button { Text = "Heberger", CustomMinimumSize = new Vector2(0.0f, 44.0f) };
		heberger.Pressed += Heberger;
		vbox.AddChild(heberger);
		vbox.MoveChild(heberger, optionsButton.GetIndex());

		var rejoindre = new Button { Text = "Rejoindre", CustomMinimumSize = new Vector2(0.0f, 44.0f) };
		rejoindre.Pressed += OuvrirRejoindre;
		vbox.AddChild(rejoindre);
		vbox.MoveChild(rejoindre, optionsButton.GetIndex());
	}

	// Construit le panneau de choix du nombre de joueurs et du type (Humain/IA) par emplacement.
	private void ConstruireSelection()
	{
		_selectionPanel = CreerPanneauCentre("SelectionPanel");

		var pile = NouvellePile(_selectionPanel);

		var titre = new Label { Text = "Nombre de joueurs", HorizontalAlignment = HorizontalAlignment.Center };
		titre.AddThemeFontSizeOverride("font_size", 26);
		pile.AddChild(titre);

		var ligneNombre = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		ligneNombre.AddThemeConstantOverride("separation", 10);
		pile.AddChild(ligneNombre);

		for (int i = 0; i < _boutonsNombre.Length; i++)
		{
			int nombre = i + PartieConfig.MinJoueurs;
			var bouton = new Button
			{
				Text = nombre.ToString(),
				ToggleMode = true,
				CustomMinimumSize = new Vector2(60.0f, 44.0f),
			};
			bouton.Pressed += () => DefinirNombreJoueurs(nombre);
			ligneNombre.AddChild(bouton);
			_boutonsNombre[i] = bouton;
		}

		for (int i = 0; i < PartieConfig.MaxJoueurs; i++)
		{
			int index = i;
			var ligne = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
			ligne.AddThemeConstantOverride("separation", 12);

			var nom = new Label
			{
				Text = $"Joueur {index + 1}  ({_indicesTouches[index]})",
				CustomMinimumSize = new Vector2(230.0f, 0.0f),
				VerticalAlignment = VerticalAlignment.Center,
			};
			ligne.AddChild(nom);

			var controle = new Button { CustomMinimumSize = new Vector2(120.0f, 40.0f) };
			controle.Pressed += () => BasculerControle(index);
			ligne.AddChild(controle);

			pile.AddChild(ligne);
			_lignesJoueur[index] = ligne;
			_boutonsControle[index] = controle;
		}

		var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		actions.AddThemeConstantOverride("separation", 12);
		pile.AddChild(actions);

		var retour = new Button { Text = "Retour", CustomMinimumSize = new Vector2(140.0f, 44.0f) };
		retour.Pressed += FermerSelection;
		actions.AddChild(retour);

		var demarrer = new Button { Text = "Demarrer", CustomMinimumSize = new Vector2(160.0f, 44.0f) };
		demarrer.Pressed += Nouveau;
		actions.AddChild(demarrer);

		RafraichirSelection();
	}

	// Lobby reseau partage (hote : interactif ; client : lecture seule).
	private void ConstruireReseauPanel()
	{
		_reseauPanel = CreerPanneauCentre("ReseauPanel");
		var pile = NouvellePile(_reseauPanel);

		_titreReseau = new Label { Text = "Lobby reseau", HorizontalAlignment = HorizontalAlignment.Center };
		_titreReseau.AddThemeFontSizeOverride("font_size", 26);
		pile.AddChild(_titreReseau);

		_ligneNombreReseau = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		_ligneNombreReseau.AddThemeConstantOverride("separation", 10);
		pile.AddChild(_ligneNombreReseau);

		for (int i = 0; i < _boutonsNombreReseau.Length; i++)
		{
			int nombre = i + PartieConfig.MinJoueurs;
			var bouton = new Button
			{
				Text = nombre.ToString(),
				ToggleMode = true,
				CustomMinimumSize = new Vector2(60.0f, 44.0f),
			};
			bouton.Pressed += () => NetworkSession.Instance?.HoteDefinirNombreJoueurs(nombre);
			_ligneNombreReseau.AddChild(bouton);
			_boutonsNombreReseau[i] = bouton;
		}

		for (int i = 0; i < PartieConfig.MaxJoueurs; i++)
		{
			int index = i;
			var ligne = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
			ligne.AddThemeConstantOverride("separation", 12);

			var nom = new Label
			{
				CustomMinimumSize = new Vector2(260.0f, 0.0f),
				VerticalAlignment = VerticalAlignment.Center,
			};
			ligne.AddChild(nom);

			var bouton = new Button { CustomMinimumSize = new Vector2(120.0f, 40.0f) };
			bouton.Pressed += () => NetworkSession.Instance?.HoteBasculerSlotIA(index);
			ligne.AddChild(bouton);

			pile.AddChild(ligne);
			_lignesSlot[index] = ligne;
			_labelsSlot[index] = nom;
			_boutonsSlot[index] = bouton;
		}

		_statutReseau = new Label
		{
			HorizontalAlignment = HorizontalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			CustomMinimumSize = new Vector2(360.0f, 0.0f),
		};
		pile.AddChild(_statutReseau);

		var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		actions.AddThemeConstantOverride("separation", 12);
		pile.AddChild(actions);

		var retour = new Button { Text = "Quitter", CustomMinimumSize = new Vector2(140.0f, 44.0f) };
		retour.Pressed += QuitterReseau;
		actions.AddChild(retour);

		_pretReseau = new Button { Text = "Pret", ToggleMode = true, CustomMinimumSize = new Vector2(140.0f, 44.0f) };
		_pretReseau.Toggled += (pressed) => NetworkSession.Instance?.DefinirPretLocal(pressed);
		actions.AddChild(_pretReseau);

		// "Demarrer" reste un forcage reserve a l'hote (lance meme si tout le monde n'est pas pret).
		_demarrerReseau = new Button { Text = "Demarrer", CustomMinimumSize = new Vector2(160.0f, 44.0f) };
		_demarrerReseau.Pressed += () => NetworkSession.Instance?.HoteDemarrerPartie();
		actions.AddChild(_demarrerReseau);
	}

	private void ConstruireJoinPanel()
	{
		_joinPanel = CreerPanneauCentre("JoinPanel");
		var pile = NouvellePile(_joinPanel);

		var titre = new Label { Text = "Rejoindre une partie", HorizontalAlignment = HorizontalAlignment.Center };
		titre.AddThemeFontSizeOverride("font_size", 26);
		pile.AddChild(titre);

		var ligneIp = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		ligneIp.AddThemeConstantOverride("separation", 10);
		pile.AddChild(ligneIp);
		ligneIp.AddChild(new Label { Text = "Adresse", CustomMinimumSize = new Vector2(80.0f, 0.0f), VerticalAlignment = VerticalAlignment.Center });
		_champIp = new LineEdit { Text = "127.0.0.1", CustomMinimumSize = new Vector2(220.0f, 0.0f) };
		ligneIp.AddChild(_champIp);

		var lignePort = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		lignePort.AddThemeConstantOverride("separation", 10);
		pile.AddChild(lignePort);
		lignePort.AddChild(new Label { Text = "Port", CustomMinimumSize = new Vector2(80.0f, 0.0f), VerticalAlignment = VerticalAlignment.Center });
		_champPort = new LineEdit { Text = NetworkSession.PortParDefaut.ToString(), CustomMinimumSize = new Vector2(220.0f, 0.0f) };
		lignePort.AddChild(_champPort);

		var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		actions.AddThemeConstantOverride("separation", 12);
		pile.AddChild(actions);

		var retour = new Button { Text = "Retour", CustomMinimumSize = new Vector2(140.0f, 44.0f) };
		retour.Pressed += () => { _joinPanel.Visible = false; _menuPanel.Visible = true; };
		actions.AddChild(retour);

		var connecter = new Button { Text = "Connecter", CustomMinimumSize = new Vector2(160.0f, 44.0f) };
		connecter.Pressed += Rejoindre;
		actions.AddChild(connecter);
	}

	// Numero de version (lu depuis project.godot -> application/config/version),
	// affiche discretement dans le coin bas-droit du menu. La CI injecte le tag a la release.
	private void ConstruireVersion()
	{
		string version = ProjectSettings.GetSetting("application/config/version", "").AsString();
		if (string.IsNullOrWhiteSpace(version))
			return;

		var label = new Label
		{
			Name = "Version",
			Text = "v" + version,
			MouseFilter = MouseFilterEnum.Ignore,
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Bottom,
		};
		label.AddThemeFontSizeOverride("font_size", 14);
		label.Modulate = new Color(1.0f, 1.0f, 1.0f, 0.5f);
		label.SetAnchorsPreset(LayoutPreset.BottomRight);
		label.GrowHorizontal = GrowDirection.Begin;
		label.GrowVertical = GrowDirection.Begin;
		label.OffsetLeft = -160.0f;
		label.OffsetTop = -34.0f;
		label.OffsetRight = -12.0f;
		label.OffsetBottom = -10.0f;
		AddChild(label);
	}

	// Pied de page : logo + nom du studio (lien nthstudio.eu) et logo + lien Ko-fi.
	// Toujours visible en bas du menu ; les clics ouvrent le navigateur via OS.ShellOpen.
	private void ConstruirePiedStudio()
	{
		var pied = new HBoxContainer
		{
			Name = "PiedStudio",
			Alignment = BoxContainer.AlignmentMode.Center,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		pied.AddThemeConstantOverride("separation", 10);
		pied.AnchorLeft = 0.0f;
		pied.AnchorRight = 1.0f;
		pied.AnchorTop = 1.0f;
		pied.AnchorBottom = 1.0f;
		pied.OffsetLeft = 0.0f;
		pied.OffsetRight = 0.0f;
		pied.OffsetTop = -112.0f;
		pied.OffsetBottom = -12.0f;
		pied.GrowHorizontal = GrowDirection.Both;
		pied.GrowVertical = GrowDirection.Begin;
		AddChild(pied);
		_piedStudio = pied;

		AjouterLienAvecLogo(pied, "res://logo_nthstudio.png", "NTH Studio", "https://nthstudio.eu", 88.0f);
		pied.AddChild(new VSeparator());
		AjouterLienAvecLogo(pied, "res://Textures/logo_kofi.png", "Soutenir sur Ko-fi", "https://ko-fi.com/nthstudio", 36.0f);
	}

	// Ajoute un logo cliquable + un libelle cliquable ouvrant l'URL donnee.
	private static void AjouterLienAvecLogo(Control parent, string cheminTexture, string libelle, string url, float taille)
	{
		if (ResourceLoader.Exists(cheminTexture) && GD.Load<Texture2D>(cheminTexture) is Texture2D texture)
		{
			var logo = new TextureButton
			{
				TextureNormal = texture,
				IgnoreTextureSize = true,
				StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
				CustomMinimumSize = new Vector2(taille, taille),
				SizeFlagsVertical = SizeFlags.ShrinkCenter,
				TooltipText = url,
			};
			logo.Pressed += () => OS.ShellOpen(url);
			parent.AddChild(logo);
		}

		var lien = new LinkButton
		{
			Text = libelle,
			TooltipText = url,
			SizeFlagsVertical = SizeFlags.ShrinkCenter,
		};
		lien.Pressed += () => OS.ShellOpen(url);
		parent.AddChild(lien);
	}

	private PanelContainer CreerPanneauCentre(string nom)
	{
		var panneau = new PanelContainer { Name = nom, Visible = false };
		panneau.SetAnchorsPreset(LayoutPreset.Center);
		panneau.GrowHorizontal = GrowDirection.Both;
		panneau.GrowVertical = GrowDirection.Both;
		panneau.CustomMinimumSize = new Vector2(440.0f, 0.0f);
		AddChild(panneau);
		return panneau;
	}

	private static VBoxContainer NouvellePile(Control parent)
	{
		var marge = new MarginContainer();
		marge.AddThemeConstantOverride("margin_left", 22);
		marge.AddThemeConstantOverride("margin_top", 18);
		marge.AddThemeConstantOverride("margin_right", 22);
		marge.AddThemeConstantOverride("margin_bottom", 18);
		parent.AddChild(marge);

		var pile = new VBoxContainer();
		pile.AddThemeConstantOverride("separation", 14);
		marge.AddChild(pile);
		return pile;
	}

	private void BrancherSignauxReseau()
	{
		if (NetworkSession.Instance is not NetworkSession session)
			return;

		session.LobbyMisAJour += RafraichirReseau;
		session.MessageReseau += OnMessageReseau;
		session.ConnexionEchouee += OnConnexionEchouee;
		session.Deconnecte += OnDeconnecte;
	}

	// Le menu est libere au changement de scene ; on coupe les signaux de l'autoload
	// NetworkSession (qui persiste) pour ne pas notifier un menu detruit (ex. message UPnP tardif).
	public override void _ExitTree()
	{
		if (NetworkSession.Instance is NetworkSession session)
		{
			session.LobbyMisAJour -= RafraichirReseau;
			session.MessageReseau -= OnMessageReseau;
			session.ConnexionEchouee -= OnConnexionEchouee;
			session.Deconnecte -= OnDeconnecte;
		}
	}

	// ----------------------------------------------------------- Actions menu

	private void OuvrirSelection()
	{
		PartieConfig.Mode = PartieConfig.ModePartie.Local;
		_menuPanel.Visible = false;
		_selectionPanel.Visible = true;
		RafraichirSelection();
	}

	private void FermerSelection()
	{
		_selectionPanel.Visible = false;
		_menuPanel.Visible = true;
	}

	private void Heberger()
	{
		if (NetworkSession.Instance is not NetworkSession session)
			return;

		if (!session.DemarrerHote(NetworkSession.PortParDefaut))
			return;

		_menuPanel.Visible = false;
		_joinPanel.Visible = false;
		_reseauPanel.Visible = true;
		RafraichirReseau();
	}

	private void OuvrirRejoindre()
	{
		_menuPanel.Visible = false;
		_joinPanel.Visible = true;
	}

	private void Rejoindre()
	{
		if (NetworkSession.Instance is not NetworkSession session)
			return;

		int port = int.TryParse(_champPort.Text, out int p) ? p : NetworkSession.PortParDefaut;
		string ip = string.IsNullOrWhiteSpace(_champIp.Text) ? "127.0.0.1" : _champIp.Text.Trim();
		if (!session.RejoindreHote(ip, port))
			return;

		_joinPanel.Visible = false;
		_reseauPanel.Visible = true;
		RafraichirReseau();
		_statutReseau.Text = "Connexion a l'hote...";
	}

	private void QuitterReseau()
	{
		NetworkSession.Instance?.Fermer();
		_reseauPanel.Visible = false;
		_menuPanel.Visible = true;
	}

	private void OnMessageReseau(string message)
	{
		if (_statutReseau != null)
			_statutReseau.Text = message;
	}

	private void OnConnexionEchouee(string raison)
	{
		_reseauPanel.Visible = false;
		_joinPanel.Visible = false;
		_menuPanel.Visible = true;
		OnMessageReseau(raison);
		GD.PushWarning(raison);
	}

	private void OnDeconnecte(string raison)
	{
		_reseauPanel.Visible = false;
		_menuPanel.Visible = true;
		GD.Print(raison);
	}

	// --------------------------------------------------------- Rafraichissement

	private void DefinirNombreJoueurs(int nombre)
	{
		PartieConfig.NombreJoueurs = Mathf.Clamp(nombre, PartieConfig.MinJoueurs, PartieConfig.MaxJoueurs);
		RafraichirSelection();
	}

	private void BasculerControle(int index)
	{
		PartieConfig.TypeControle actuel = PartieConfig.ControleDe(index);
		PartieConfig.DefinirControle(
			index,
			actuel == PartieConfig.TypeControle.Humain
				? PartieConfig.TypeControle.IA
				: PartieConfig.TypeControle.Humain);
		RafraichirSelection();
	}

	private void RafraichirSelection()
	{
		for (int i = 0; i < _boutonsNombre.Length; i++)
			_boutonsNombre[i].ButtonPressed = i + PartieConfig.MinJoueurs == PartieConfig.NombreJoueurs;

		for (int i = 0; i < PartieConfig.MaxJoueurs; i++)
		{
			_lignesJoueur[i].Visible = i < PartieConfig.NombreJoueurs;
			_boutonsControle[i].Text = PartieConfig.ControleDe(i) == PartieConfig.TypeControle.Humain ? "Humain" : "IA";
		}
	}

	private void RafraichirReseau()
	{
		if (_reseauPanel == null || !_reseauPanel.Visible)
			return;

		NetworkSession session = NetworkSession.Instance;
		bool hote = session?.EstHote ?? false;
		_titreReseau.Text = hote ? "Lobby (hote)" : "Lobby (client)";
		_ligneNombreReseau.Visible = hote;
		_demarrerReseau.Visible = hote;
		if (hote)
			_demarrerReseau.Disabled = !(session?.PartiePeutDemarrer() ?? false);

		// Bouton "Pret" : visible si cette machine controle un emplacement humain.
		int slotLocal = PartieConfig.SlotLocal;
		bool slotHumainLocal = slotLocal >= 0 && slotLocal < PartieConfig.NombreJoueurs
			&& PartieConfig.ControleDe(slotLocal) != PartieConfig.TypeControle.IA;
		_pretReseau.Visible = slotHumainLocal;
		if (slotHumainLocal && session != null)
			_pretReseau.SetPressedNoSignal(session.EstPret(slotLocal));

		for (int i = 0; i < _boutonsNombreReseau.Length; i++)
			_boutonsNombreReseau[i].ButtonPressed = i + PartieConfig.MinJoueurs == PartieConfig.NombreJoueurs;

		for (int i = 0; i < PartieConfig.MaxJoueurs; i++)
		{
			bool actif = i < PartieConfig.NombreJoueurs;
			_lignesSlot[i].Visible = actif;
			if (!actif)
				continue;

			_labelsSlot[i].Text = DescriptionSlot(i);
			bool basculable = hote && i > 0;
			_boutonsSlot[i].Visible = basculable;
			if (basculable)
				_boutonsSlot[i].Text = PartieConfig.ControleDe(i) == PartieConfig.TypeControle.IA ? "IA" : "Humain";
		}
	}

	private static string DescriptionSlot(int i)
	{
		string moi = i == PartieConfig.SlotLocal ? "  (vous)" : string.Empty;
		switch (PartieConfig.ControleDe(i))
		{
			case PartieConfig.TypeControle.Humain:
				return $"Joueur {i + 1} : Hote{moi}{MentionPret(i)}";
			case PartieConfig.TypeControle.IA:
				return $"Joueur {i + 1} : IA";
			default:
				int peer = PartieConfig.PeerControleurDe(i);
				if (peer == 0)
					return $"Joueur {i + 1} : en attente...";
				return $"Joueur {i + 1} : connecte{moi}{MentionPret(i)}";
		}
	}

	// Indique si l'emplacement humain est pret (lobby).
	private static string MentionPret(int i)
	{
		return (NetworkSession.Instance?.EstPret(i) ?? false) ? "  - PRET" : "  - pas pret";
	}

	private void Nouveau()
	{
		GetTree().Paused = false;
		GetTree().ChangeSceneToFile("res://Jeu.tscn");
	}
}
