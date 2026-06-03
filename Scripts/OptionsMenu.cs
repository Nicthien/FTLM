using Godot;
using System.Collections.Generic;

public partial class OptionsMenu : PanelContainer
{
	// Lignes (types d'action) et colonnes (joueurs) de la grille de remappage.
	private static readonly string[] _lignesActions = { "Gauche", "Droite", "Lancer", "Capacite", "Cibler" };
	private static readonly (string Titre, string[] Actions)[] _joueurs =
	{
		// La 5e action ("Cibler") du Joueur 1 est la molette souris, non remappable ("").
		("Joueur 1", new[] { "ui_left", "ui_right", "lancer_balle", "tirer_capacite", "" }),
		("Joueur 2", new[] { "j2_gauche", "j2_droite", "j2_action", "j2_capacite", "j2_cibler" }),
		("Joueur 3", new[] { "j3_gauche", "j3_droite", "j3_action", "j3_capacite", "j3_cibler" }),
		("Joueur 4", new[] { "j4_gauche", "j4_droite", "j4_action", "j4_capacite", "j4_cibler" }),
	};

	private readonly Dictionary<string, Button> _boutonsTouche = new();

	private CheckButton _pleinEcran;
	private OptionButton _resolution;
	private CheckButton _vsync;
	private OptionButton _qualite;
	private HSlider _master;
	private HSlider _sfx;
	private HSlider _music;
	private Label _masterLabel;
	private Label _sfxLabel;
	private Label _musicLabel;
	private string _actionEnAttente;
	private bool _rafraichit;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		MenuTheme.Appliquer(this);
		Visible = false;

		_pleinEcran = GetNode<CheckButton>("Margin/VBox/Tabs/Graphique/GraphiqueBox/PleinEcran");
		_resolution = GetNode<OptionButton>("Margin/VBox/Tabs/Graphique/GraphiqueBox/Resolution");
		_vsync = GetNode<CheckButton>("Margin/VBox/Tabs/Graphique/GraphiqueBox/VSync");
		_qualite = GetNode<OptionButton>("Margin/VBox/Tabs/Graphique/GraphiqueBox/Qualite");
		_master = GetNode<HSlider>("Margin/VBox/Tabs/Son/SonBox/MasterSlider");
		_sfx = GetNode<HSlider>("Margin/VBox/Tabs/Son/SonBox/SfxSlider");
		_music = GetNode<HSlider>("Margin/VBox/Tabs/Son/SonBox/MusicSlider");
		_masterLabel = GetNode<Label>("Margin/VBox/Tabs/Son/SonBox/MasterLabel");
		_sfxLabel = GetNode<Label>("Margin/VBox/Tabs/Son/SonBox/SfxLabel");
		_musicLabel = GetNode<Label>("Margin/VBox/Tabs/Son/SonBox/MusicLabel");

		foreach (string resolution in SettingsManager.Resolutions)
			_resolution.AddItem(resolution);
		foreach (string qualite in SettingsManager.Qualites)
			_qualite.AddItem(qualite);

		_pleinEcran.Toggled += value => Modifier(() => SettingsManager.PleinEcran = value);
		_resolution.ItemSelected += index => Modifier(() => SettingsManager.Resolution = SettingsManager.Resolutions[(int)index]);
		_vsync.Toggled += value => Modifier(() => SettingsManager.VSync = value);
		_qualite.ItemSelected += index => Modifier(() => SettingsManager.Qualite = SettingsManager.Qualites[(int)index]);
		_master.ValueChanged += value => Modifier(() => SettingsManager.VolumeMasterDb = (float)value);
		_sfx.ValueChanged += value => Modifier(() => SettingsManager.VolumeSfxDb = (float)value);
		_music.ValueChanged += value => Modifier(() => SettingsManager.VolumeMusicDb = (float)value);

		ConstruireTouches();
		GetNode<Button>("Margin/VBox/RetourButton").Pressed += Fermer;

		Rafraichir();
	}

	public void Ouvrir()
	{
		Visible = true;
		Rafraichir();
		GrabFocus();
	}

	public void Fermer()
	{
		_actionEnAttente = null;
		Visible = false;
	}

	public override void _UnhandledInput(InputEvent ev)
	{
		if (!Visible)
			return;

		if (_actionEnAttente == null)
		{
			if (ev.IsActionPressed("ui_cancel"))
			{
				Fermer();
				GetViewport().SetInputAsHandled();
			}
			return;
		}

		if (ev is not InputEventKey touche || !touche.Pressed || touche.Echo)
			return;

		SettingsManager.Rebind(_actionEnAttente, touche);
		_actionEnAttente = null;
		SauverAppliquer();
		Rafraichir();
		GetViewport().SetInputAsHandled();
	}

	private void Modifier(System.Action modification)
	{
		if (_rafraichit)
			return;

		modification();
		SauverAppliquer();
		RafraichirVolumes();
	}

	private void ResetTouches()
	{
		SettingsManager.ReinitialiserTouches();
		SauverAppliquer();
		Rafraichir();
	}

	// Genere la grille de remappage : une colonne par joueur, une ligne par action,
	// puis la touche de pause commune et le bouton de reinitialisation.
	private void ConstruireTouches()
	{
		var box = GetNode<VBoxContainer>("Margin/VBox/Tabs/Touches/TouchesBox");
		foreach (Node enfant in box.GetChildren())
			enfant.QueueFree();
		_boutonsTouche.Clear();

		var grille = new GridContainer
		{
			Columns = _joueurs.Length + 1,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		grille.AddThemeConstantOverride("h_separation", 8);
		grille.AddThemeConstantOverride("v_separation", 6);
		box.AddChild(grille);

		// En-tete : cellule vide puis un titre par joueur.
		grille.AddChild(new Label());
		foreach ((string titre, string[] _) in _joueurs)
			grille.AddChild(new Label { Text = titre, HorizontalAlignment = HorizontalAlignment.Center });

		for (int ligne = 0; ligne < _lignesActions.Length; ligne++)
		{
			grille.AddChild(new Label { Text = _lignesActions[ligne] });
			foreach ((string _, string[] actions) in _joueurs)
				grille.AddChild(CreerBoutonTouche(actions[ligne]));
		}

		box.AddChild(CreerBoutonTouche("ui_cancel"));

		var reset = new Button { Text = "Reset touches" };
		reset.Pressed += ResetTouches;
		box.AddChild(reset);
	}

	private Button CreerBoutonTouche(string action)
	{
		// Action vide = liaison fixe (ex. molette souris du Joueur 1) : bouton inerte.
		if (string.IsNullOrEmpty(action))
			return new Button { Text = "Molette", Disabled = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };

		var bouton = new Button { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		bouton.Pressed += () => AttendreTouche(action);
		_boutonsTouche[action] = bouton;
		return bouton;
	}

	private void AttendreTouche(string action)
	{
		_actionEnAttente = action;
		Button bouton = BoutonAction(action);
		if (bouton != null)
			bouton.Text = "...";
	}

	private void SauverAppliquer()
	{
		SettingsManager.Sauvegarder();
		SettingsManager.Appliquer(GetTree());
	}

	private void Rafraichir()
	{
		_rafraichit = true;
		_pleinEcran.ButtonPressed = SettingsManager.PleinEcran;
		_resolution.Select(System.Array.IndexOf(SettingsManager.Resolutions, SettingsManager.Resolution));
		_vsync.ButtonPressed = SettingsManager.VSync;
		_qualite.Select(System.Array.IndexOf(SettingsManager.Qualites, SettingsManager.Qualite));
		_master.Value = SettingsManager.VolumeMasterDb;
		_sfx.Value = SettingsManager.VolumeSfxDb;
		_music.Value = SettingsManager.VolumeMusicDb;
		_rafraichit = false;

		foreach (var paire in _boutonsTouche)
		{
			string touche = SettingsManager.TexteTouche(paire.Key);
			paire.Value.Text = paire.Key == "ui_cancel" ? $"Pause : {touche}" : touche;
		}

		RafraichirVolumes();
	}

	private void RafraichirVolumes()
	{
		_masterLabel.Text = $"Master : {(int)_master.Value} dB";
		_sfxLabel.Text = $"SFX : {(int)_sfx.Value} dB";
		_musicLabel.Text = $"Music : {(int)_music.Value} dB";
	}

	private Button BoutonAction(string action) => _boutonsTouche.TryGetValue(action, out Button bouton) ? bouton : null;
}
