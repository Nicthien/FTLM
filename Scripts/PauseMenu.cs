using Godot;

public partial class PauseMenu : CanvasLayer
{
	private GameManager _gameManager;
	private OptionsMenu _options;

	public bool OptionsOuvertes => _options != null && _options.Visible;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		Visible = false;
		_gameManager = GetParent<GameManager>();
		_options = GetNode<OptionsMenu>("OptionsMenu");

		GetNode<Button>("Root/MenuPanel/VBox/ReprendreButton").Pressed += () => _gameManager.ReprendrePartie();
		GetNode<Button>("Root/MenuPanel/VBox/NouveauButton").Pressed += () => _gameManager.NouvellePartieDepuisMenu();
		GetNode<Button>("Root/MenuPanel/VBox/OptionsButton").Pressed += () => _options.Ouvrir();
		GetNode<Button>("Root/MenuPanel/VBox/QuitterButton").Pressed += () => _gameManager.RetourAccueil();
	}

	public void Ouvrir()
	{
		Visible = true;
	}

	public void Fermer()
	{
		_options.Fermer();
		Visible = false;
	}
}
