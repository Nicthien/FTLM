using Godot;
using System;
using System.Collections.Generic;

public static class SettingsManager
{
	private const string CheminConfig = "user://settings.cfg";
	private const string ActionLancer = "lancer_balle";
	private const string ActionCapacite = "tirer_capacite";

	public static bool PleinEcran { get; set; }
	public static string Resolution { get; set; } = "1280x720";
	public static bool VSync { get; set; } = true;
	public static string Qualite { get; set; } = "Moyen";
	public static float VolumeMasterDb { get; set; } = 0.0f;
	public static float VolumeSfxDb { get; set; } = 0.0f;
	public static float VolumeMusicDb { get; set; } = 0.0f;

	public static readonly string[] Resolutions = { "960x540", "1280x720", "1600x900", "1920x1080" };
	public static readonly string[] Qualites = { "Faible", "Moyen", "Eleve" };

	private static readonly Dictionary<string, Key> TouchesDefaut = ConstruireTouchesDefaut();

	// Defauts des touches remappables : J1 + pause definis ici, J2-J4 repris de
	// PartieConfig (source unique pour les actions clavier des joueurs).
	private static Dictionary<string, Key> ConstruireTouchesDefaut()
	{
		var touches = new Dictionary<string, Key>
		{
			{ "ui_left", Key.Left },
			{ "ui_right", Key.Right },
			{ "lancer_balle", Key.Space },
			{ "tirer_capacite", Key.Alt },
			{ "ui_cancel", Key.Escape },
		};

		foreach ((string action, Key touche) in PartieConfig.ToucheParDefaut())
			touches[action] = touche;

		return touches;
	}

	public static void Charger()
	{
		CreerBusAudioSiBesoin();
		ReinitialiserTouchesSiBesoin();

		var config = new ConfigFile();
		Error err = config.Load(CheminConfig);
		if (err != Error.Ok)
		{
			AjouterEntreesFixes();
			Sauvegarder();
			return;
		}

		PleinEcran = config.GetValue("graphics", "window_mode", PleinEcran).AsBool();
		Resolution = config.GetValue("graphics", "resolution", Resolution).AsString();
		VSync = config.GetValue("graphics", "vsync", VSync).AsBool();
		Qualite = config.GetValue("graphics", "quality", Qualite).AsString();
		VolumeMasterDb = (float)config.GetValue("audio", "master_db", VolumeMasterDb).AsDouble();
		VolumeSfxDb = (float)config.GetValue("audio", "sfx_db", VolumeSfxDb).AsDouble();
		VolumeMusicDb = (float)config.GetValue("audio", "music_db", VolumeMusicDb).AsDouble();

		foreach (string action in TouchesDefaut.Keys)
		{
			int code = config.GetValue("input", action, (int)TouchesDefaut[action]).AsInt32();
			AppliquerTouche(action, (Key)code);
		}

		AjouterEntreesFixes();
	}

	public static void Sauvegarder()
	{
		var config = new ConfigFile();
		config.SetValue("graphics", "window_mode", PleinEcran);
		config.SetValue("graphics", "resolution", Resolution);
		config.SetValue("graphics", "vsync", VSync);
		config.SetValue("graphics", "quality", Qualite);
		config.SetValue("audio", "master_db", VolumeMasterDb);
		config.SetValue("audio", "sfx_db", VolumeSfxDb);
		config.SetValue("audio", "music_db", VolumeMusicDb);

		foreach (string action in TouchesDefaut.Keys)
			config.SetValue("input", action, (int)LireTouche(action));

		config.Save(CheminConfig);
	}

	public static void Appliquer(SceneTree arbre)
	{
		CreerBusAudioSiBesoin();

		DisplayServer.WindowSetVsyncMode(VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);

		if (PleinEcran)
		{
			DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
		}
		else if (DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Maximized)
		{
			// Mode fenetre : on ne force la taille que si l'utilisateur n'a pas
			// maximise la fenetre lui-meme (sinon chaque changement de scene la
			// reduirait a la resolution enregistree).
			DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
			if (LireResolution(Resolution, out Vector2I taille))
				DisplayServer.WindowSetSize(taille);
		}

		ReglerBus("Master", VolumeMasterDb);
		ReglerBus("SFX", VolumeSfxDb);
		ReglerBus("Music", VolumeMusicDb);
		AppliquerQualite(arbre);
	}

	public static void Rebind(string action, InputEventKey touche)
	{
		Key code = touche.PhysicalKeycode != Key.None ? touche.PhysicalKeycode : touche.Keycode;
		if (code == Key.None)
			return;

		AppliquerTouche(action, code);
	}

	public static void ReinitialiserTouches()
	{
		foreach (var paire in TouchesDefaut)
			AppliquerTouche(paire.Key, paire.Value);

		AjouterEntreesFixes();
	}

	public static string TexteTouche(string action)
	{
		Key code = LireTouche(action);
		return code == Key.None ? "-" : OS.GetKeycodeString(code);
	}

	private static void AppliquerQualite(SceneTree arbre)
	{
		if (arbre?.Root == null)
			return;

		Viewport.Msaa msaa = Qualite switch
		{
			"Faible" => Viewport.Msaa.Disabled,
			"Eleve" => Viewport.Msaa.Msaa4X,
			_ => Viewport.Msaa.Msaa2X,
		};

		arbre.Root.Msaa3D = msaa;
	}

	private static void ReglerBus(string nom, float volumeDb)
	{
		int index = AudioServer.GetBusIndex(nom);
		if (index < 0)
			return;

		AudioServer.SetBusVolumeDb(index, volumeDb);
		AudioServer.SetBusMute(index, volumeDb <= -39.5f);
	}

	private static void CreerBusAudioSiBesoin()
	{
		CreerBus("SFX");
		CreerBus("Music");
	}

	private static void CreerBus(string nom)
	{
		if (AudioServer.GetBusIndex(nom) >= 0)
			return;

		int index = AudioServer.BusCount;
		AudioServer.AddBus(index);
		AudioServer.SetBusName(index, nom);
	}

	private static void ReinitialiserTouchesSiBesoin()
	{
		foreach (var paire in TouchesDefaut)
			if (!InputMap.HasAction(paire.Key) || InputMap.ActionGetEvents(paire.Key).Count == 0)
				AppliquerTouche(paire.Key, paire.Value);
	}

	private static void AppliquerTouche(string action, Key code)
	{
		if (!InputMap.HasAction(action))
			InputMap.AddAction(action);

		InputMap.ActionEraseEvents(action);
		InputMap.ActionAddEvent(action, new InputEventKey
		{
			PhysicalKeycode = code,
			Keycode = code,
		});

		AjouterEntreesFixes();
	}

	private static void AjouterEntreesFixes()
	{
		// Bindings souris fixes (non remappables) : clic gauche = lancer la balle,
		// clic droit = capacite/tir. Le clavier de ces actions reste remappable.
		AjouterBoutonSouris(ActionLancer, MouseButton.Left);
		AjouterBoutonSouris(ActionCapacite, MouseButton.Right);
	}

	private static void AjouterBoutonSouris(string action, MouseButton bouton)
	{
		if (!InputMap.HasAction(action))
			InputMap.AddAction(action);

		foreach (InputEvent ev in InputMap.ActionGetEvents(action))
			if (ev is InputEventMouseButton souris && souris.ButtonIndex == bouton)
				return;

		InputMap.ActionAddEvent(action, new InputEventMouseButton
		{
			ButtonIndex = bouton,
		});
	}

	private static Key LireTouche(string action)
	{
		foreach (InputEvent ev in InputMap.ActionGetEvents(action))
			if (ev is InputEventKey key)
				return key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;

		return TouchesDefaut.TryGetValue(action, out Key defaut) ? defaut : Key.None;
	}

	private static bool LireResolution(string texte, out Vector2I taille)
	{
		taille = new Vector2I(1280, 720);
		string[] morceaux = texte.Split('x');
		return morceaux.Length == 2
			&& int.TryParse(morceaux[0], out taille.X)
			&& int.TryParse(morceaux[1], out taille.Y);
	}
}
