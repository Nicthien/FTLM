using Godot;

public partial class MainMenu : Control
{
	private OptionsMenu _options;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		SettingsManager.Charger();
		SettingsManager.Appliquer(GetTree());

		_options = GetNode<OptionsMenu>("OptionsMenu");
		GetNode<Button>("MenuPanel/VBox/NouveauButton").Pressed += Nouveau;
		GetNode<Button>("MenuPanel/VBox/OptionsButton").Pressed += () => _options.Ouvrir();
		GetNode<Button>("MenuPanel/VBox/QuitterButton").Pressed += () => GetTree().Quit();
	}

	private void Nouveau()
	{
		GetTree().Paused = false;
		GetTree().ChangeSceneToFile("res://node_3d.tscn");
	}
}
