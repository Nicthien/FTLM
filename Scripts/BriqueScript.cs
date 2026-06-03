using Godot;
using System;

public partial class BriqueScript : StaticBody3D
{
	public enum TypeBrique
	{
		Normale,
		Solide,
		Explosive,
		CapsuleGarantie,
		Mobile,
		BonusScore,
	}

	// Points gagnes quand cette brique est definitivement cassee.
	[Export]
	public int Points = 10;

	[Export]
	public Texture2D TextureBrique;

	[Export]
	public TypeBrique TypeSpecial { get; set; } = TypeBrique.Normale;

	// Nombre de coups restants avant destruction.
	public int Resistance { get; private set; } = 1;

	// Identifiant reseau, assigne de facon deterministe a la generation (meme ordre
	// sur l'hote et les clients). Sert a cibler les disparitions/coups en reseau.
	public int IdReseau { get; set; }

	// Resistance initiale, pour calculer l'usure visuelle.
	private int _resistanceMax = 1;

	// Couleur de base (pleine resistance), assombrie quand la brique est abimee.
	private Color _couleurBase = Colors.White;

	public Color CouleurBase => _couleurBase;
	public bool EstDestructible => TypeSpecial != TypeBrique.Solide;

	private MeshInstance3D _mesh;
	private StandardMaterial3D _materiau;
	private float _origineX;
	private float _mobilePhase;

	public override void _Ready()
	{
		_mesh = GetNodeOrNull<MeshInstance3D>("MeshInstance3D");
		_origineX = Position.X;
		_mobilePhase = Position.X * 3.1f;
		AppliquerCouleur();
	}

	public override void _PhysicsProcess(double delta)
	{
		if (TypeSpecial != TypeBrique.Mobile)
			return;

		Vector3 position = Position;
		_mobilePhase += (float)delta * 1.2f;
		position.X = _origineX + Mathf.Sin(_mobilePhase) * 0.18f;
		Position = position;
	}

	public void Initialiser(int resistance, Color couleur, TypeBrique type = TypeBrique.Normale)
	{
		TypeSpecial = type;
		Resistance = type == TypeBrique.Solide ? int.MaxValue : Mathf.Max(1, resistance);
		_resistanceMax = Resistance;
		_couleurBase = couleur;
		AppliquerCouleur();
	}

	// Encaisse un coup. Retourne true si la brique est detruite.
	public bool Frapper()
	{
		if (!EstDestructible)
			return false;

		Resistance--;
		if (Resistance <= 0)
		{
			QueueFree();
			return true;
		}

		AppliquerCouleur();
		return false;
	}

	// Cote client : applique une resistance recue du reseau et rafraichit la couleur.
	public void DefinirResistanceReseau(int resistance)
	{
		if (!EstDestructible)
			return;

		Resistance = Mathf.Max(1, resistance);
		AppliquerCouleur();
	}

	private void AppliquerCouleur()
	{
		if (_mesh == null)
			return;

		float ratio = TypeSpecial == TypeBrique.Solide ? 1.0f : (float)Resistance / _resistanceMax;
		float facteur = 0.45f + 0.55f * ratio;
		Color couleur = _couleurBase * facteur;
		couleur.A = _couleurBase.A;

		_materiau = new StandardMaterial3D
		{
			AlbedoColor = couleur,
			AlbedoTexture = TextureBrique,
			Metallic = 0.25f,
			Roughness = 0.42f,
		};
		_mesh.MaterialOverride = _materiau;
	}
}
