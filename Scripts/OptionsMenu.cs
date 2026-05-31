using Godot;
using System.Collections.Generic;

public partial class OptionsMenu : PanelContainer
{
	private readonly Dictionary<string, string> _libellesActions = new()
	{
		{ "ui_left", "Gauche" },
		{ "ui_right", "Droite" },
		{ "lancer_balle", "Lancer" },
		{ "ui_cancel", "Pause" },
	};

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

		GetNode<Button>("Margin/VBox/Tabs/Touches/TouchesBox/GaucheButton").Pressed += () => AttendreTouche("ui_left");
		GetNode<Button>("Margin/VBox/Tabs/Touches/TouchesBox/DroiteButton").Pressed += () => AttendreTouche("ui_right");
		GetNode<Button>("Margin/VBox/Tabs/Touches/TouchesBox/LancerButton").Pressed += () => AttendreTouche("lancer_balle");
		GetNode<Button>("Margin/VBox/Tabs/Touches/TouchesBox/PauseButton").Pressed += () => AttendreTouche("ui_cancel");
		GetNode<Button>("Margin/VBox/Tabs/Touches/TouchesBox/ResetButton").Pressed += ResetTouches;
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

	private void AttendreTouche(string action)
	{
		_actionEnAttente = action;
		Button bouton = BoutonAction(action);
		bouton.Text = $"{_libellesActions[action]} : ...";
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

		foreach (string action in _libellesActions.Keys)
			BoutonAction(action).Text = $"{_libellesActions[action]} : {SettingsManager.TexteTouche(action)}";

		RafraichirVolumes();
	}

	private void RafraichirVolumes()
	{
		_masterLabel.Text = $"Master : {(int)_master.Value} dB";
		_sfxLabel.Text = $"SFX : {(int)_sfx.Value} dB";
		_musicLabel.Text = $"Music : {(int)_music.Value} dB";
	}

	private Button BoutonAction(string action) => action switch
	{
		"ui_left" => GetNode<Button>("Margin/VBox/Tabs/Touches/TouchesBox/GaucheButton"),
		"ui_right" => GetNode<Button>("Margin/VBox/Tabs/Touches/TouchesBox/DroiteButton"),
		"lancer_balle" => GetNode<Button>("Margin/VBox/Tabs/Touches/TouchesBox/LancerButton"),
		"ui_cancel" => GetNode<Button>("Margin/VBox/Tabs/Touches/TouchesBox/PauseButton"),
		_ => null,
	};
}
