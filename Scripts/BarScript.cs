using Godot;
using System;

public partial class BarScript : AnimatableBody3D
{
	[Export]
	public float Vitesse = 4.0f;

	private const float DemiLargeurBase = 0.5f;
	private const float BordInterieur = 1.9f;
	private const float ToleranceIA = 0.04f;

	public float DemiLargeur { get; private set; } = DemiLargeurBase;
	public double TempsRedimensionnementRestant => Math.Max(0.0, _tempsRedimRestant);
	public float FacteurRedimensionnement { get; private set; } = 1.0f;
	public int SensAttaque { get; private set; } = 1;

	private float LimiteX => BordInterieur - DemiLargeur;

	private MeshInstance3D _corpsMesh;
	private MeshInstance3D _faceAvant;
	private CollisionShape3D _collision;
	private BoxMesh _corpsBox;
	private QuadMesh _faceQuad;
	private BoxShape3D _collisionBox;
	private Vector3 _tailleCorpsBase = new Vector3(1.0f, 0.2f, 0.2f);
	private Vector2 _tailleFaceBase = new Vector2(1.0f, 0.2f);
	private Vector3 _tailleCollisionBase = new Vector3(1.0f, 0.2f, 0.2f);
	private double _tempsRedimRestant;
	private bool _controleSourisActif;
	private bool _controleIA;
	private float? _cibleIA;

	public override void _Ready()
	{
		_corpsMesh = GetNodeOrNull<MeshInstance3D>("MeshInstance3D");
		_faceAvant = GetNodeOrNull<MeshInstance3D>("TextureFaceAvant");
		_collision = GetNodeOrNull<CollisionShape3D>("CollisionShape3D");

		_corpsBox = _corpsMesh?.Mesh as BoxMesh;
		_faceQuad = _faceAvant?.Mesh as QuadMesh;
		_collisionBox = _collision?.Shape as BoxShape3D;

		if (_corpsBox != null)
		{
			_corpsBox = (BoxMesh)_corpsBox.Duplicate();
			_corpsMesh.Mesh = _corpsBox;
		}
		if (_faceQuad != null)
		{
			_faceQuad = (QuadMesh)_faceQuad.Duplicate();
			_faceAvant.Mesh = _faceQuad;
		}
		if (_collisionBox != null)
		{
			_collisionBox = (BoxShape3D)_collisionBox.Duplicate();
			_collision.Shape = _collisionBox;
		}

		if (_corpsBox != null)
			_tailleCorpsBase = _corpsBox.Size;
		if (_faceQuad != null)
			_tailleFaceBase = _faceQuad.Size;
		if (_collisionBox != null)
			_tailleCollisionBase = _collisionBox.Size;

		AppliquerFacteur(1.0f);
	}

	public override void _Input(InputEvent ev)
	{
		if (_controleIA)
			return;

		if (ev is InputEventMouseMotion || ev is InputEventMouseButton)
			_controleSourisActif = true;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_tempsRedimRestant > 0.0)
		{
			_tempsRedimRestant -= delta;
			if (_tempsRedimRestant <= 0.0)
				AppliquerFacteur(1.0f);
		}

		float direction = _controleIA ? DirectionIA() : Input.GetAxis("ui_left", "ui_right");
		Vector3 position = Position;
		if (!Mathf.IsZeroApprox(direction))
		{
			_controleSourisActif = false;
			position.X += direction * Vitesse * (float)delta;
		}
		else if (!_controleIA && _controleSourisActif && EssayerLireXSouris(out float sourisX))
		{
			position.X = sourisX;
		}

		position.X = Mathf.Clamp(position.X, -LimiteX, LimiteX);
		Position = position;
	}

	public void Configurer(bool controleIA, int sensAttaque)
	{
		_controleIA = controleIA;
		SensAttaque = sensAttaque >= 0 ? 1 : -1;
		_controleSourisActif = false;
	}

	public void DefinirCibleIA(float? x)
	{
		_cibleIA = x;
	}

	public void Redimensionner(float facteur, double duree)
	{
		AppliquerFacteur(facteur);
		_tempsRedimRestant = duree;
	}

	private void AppliquerFacteur(float facteur)
	{
		facteur = Mathf.Clamp(facteur, 0.35f, 2.0f);
		Scale = Vector3.One;

		if (_corpsBox != null)
			_corpsBox.Size = new Vector3(_tailleCorpsBase.X * facteur, _tailleCorpsBase.Y, _tailleCorpsBase.Z);

		if (_faceQuad != null)
			_faceQuad.Size = new Vector2(_tailleFaceBase.X * facteur, _tailleFaceBase.Y);

		if (_collisionBox != null)
			_collisionBox.Size = new Vector3(_tailleCollisionBase.X * facteur, _tailleCollisionBase.Y, _tailleCollisionBase.Z);

		DemiLargeur = DemiLargeurBase * facteur;
		FacteurRedimensionnement = facteur;

		Vector3 position = Position;
		position.X = Mathf.Clamp(position.X, -LimiteX, LimiteX);
		Position = position;
	}

	private float DirectionIA()
	{
		if (!_cibleIA.HasValue)
			return 0.0f;

		float cible = Mathf.Clamp(_cibleIA.Value, -LimiteX, LimiteX);
		float ecart = cible - Position.X;
		if (Mathf.Abs(ecart) <= ToleranceIA)
			return 0.0f;

		return Mathf.Sign(ecart);
	}

	private bool EssayerLireXSouris(out float x)
	{
		x = 0.0f;
		Camera3D camera = GetViewport().GetCamera3D();
		if (camera == null)
			return false;

		Vector2 souris = GetViewport().GetMousePosition();
		Vector3 origine = camera.ProjectRayOrigin(souris);
		Vector3 direction = camera.ProjectRayNormal(souris);
		if (Mathf.IsZeroApprox(direction.Z))
			return false;

		float t = (GlobalPosition.Z - origine.Z) / direction.Z;
		if (t < 0.0f)
			return false;

		x = origine.X + direction.X * t;
		return true;
	}
}
