using Godot;

// Theme partage par tous les menus (principal, pause, options, lobby reseau).
// Style futuriste assorti au jeu : fond bleu nuit, accents cyan neon,
// coins arrondis et survols lumineux.
public static class MenuTheme
{
	private static readonly Color FondPanneau = new(0.04f, 0.06f, 0.11f, 0.94f);
	private static readonly Color FondBouton = new(0.09f, 0.13f, 0.20f, 0.95f);
	private static readonly Color FondBoutonSurvol = new(0.14f, 0.26f, 0.36f, 1.0f);
	private static readonly Color FondChamp = new(0.06f, 0.09f, 0.15f, 0.95f);
	private static readonly Color Accent = new(0.30f, 0.85f, 0.98f);
	private static readonly Color AccentSombre = new(0.16f, 0.42f, 0.52f);
	private static readonly Color Texte = new(0.90f, 0.95f, 1.0f);
	private static readonly Color TexteAttenue = new(0.58f, 0.68f, 0.80f);

	private static Theme _partage;
	public static Theme Partage => _partage ??= Construire();

	public static void Appliquer(Control cible)
	{
		if (cible != null)
			cible.Theme = Partage;
	}

	private static Theme Construire()
	{
		var theme = new Theme();

		// --- Panneaux ---
		StyleBoxFlat panneau = Cadre(FondPanneau, Accent, 2, 16, 22);
		theme.SetStylebox("panel", "PanelContainer", panneau);
		theme.SetStylebox("panel", "Panel", panneau);

		// --- Boutons (Button + OptionButton) ---
		StyleBoxFlat btnNormal = Cadre(FondBouton, AccentSombre, 1, 10, 16);
		StyleBoxFlat btnSurvol = Cadre(FondBoutonSurvol, Accent, 2, 10, 16);
		StyleBoxFlat btnPresse = Cadre(Accent, Accent, 2, 10, 16);
		StyleBoxFlat btnDesactive = Cadre(new Color(0.08f, 0.10f, 0.14f, 0.55f), new Color(0.2f, 0.2f, 0.2f, 0.35f), 1, 10, 16);
		StyleBoxFlat btnFocus = Contour(Accent, 2, 10);

		foreach (string type in new[] { "Button", "OptionButton" })
		{
			theme.SetStylebox("normal", type, btnNormal);
			theme.SetStylebox("hover", type, btnSurvol);
			theme.SetStylebox("pressed", type, btnPresse);
			theme.SetStylebox("disabled", type, btnDesactive);
			theme.SetStylebox("focus", type, btnFocus);
			theme.SetColor("font_color", type, Texte);
			theme.SetColor("font_hover_color", type, new Color(1.0f, 1.0f, 1.0f));
			theme.SetColor("font_pressed_color", type, new Color(0.03f, 0.06f, 0.10f));
			theme.SetColor("font_focus_color", type, Texte);
			theme.SetColor("font_disabled_color", type, TexteAttenue);
			theme.SetFontSize("font_size", type, 18);
		}

		// --- Cases a cocher (interrupteur, pas de cadre plein) ---
		theme.SetColor("font_color", "CheckButton", Texte);
		theme.SetColor("font_hover_color", "CheckButton", new Color(1.0f, 1.0f, 1.0f));
		theme.SetFontSize("font_size", "CheckButton", 18);

		// --- Libelles ---
		theme.SetColor("font_color", "Label", Texte);
		theme.SetFontSize("font_size", "Label", 17);

		// --- Champs de saisie ---
		theme.SetStylebox("normal", "LineEdit", Cadre(FondChamp, AccentSombre, 1, 8, 12));
		theme.SetStylebox("focus", "LineEdit", Contour(Accent, 2, 8));
		theme.SetColor("font_color", "LineEdit", Texte);
		theme.SetColor("caret_color", "LineEdit", Accent);

		// --- Onglets (Options) ---
		theme.SetColor("font_selected_color", "TabContainer", Accent);
		theme.SetColor("font_unselected_color", "TabContainer", TexteAttenue);
		theme.SetColor("font_hovered_color", "TabContainer", Texte);

		return theme;
	}

	private static StyleBoxFlat Cadre(Color fond, Color bordure, int largeurBordure, int rayon, int margeH)
	{
		var sb = new StyleBoxFlat
		{
			BgColor = fond,
			BorderColor = bordure,
			CornerRadiusTopLeft = rayon,
			CornerRadiusTopRight = rayon,
			CornerRadiusBottomLeft = rayon,
			CornerRadiusBottomRight = rayon,
			ContentMarginLeft = margeH,
			ContentMarginRight = margeH,
			ContentMarginTop = 8,
			ContentMarginBottom = 8,
		};
		sb.SetBorderWidthAll(largeurBordure);
		return sb;
	}

	private static StyleBoxFlat Contour(Color bordure, int largeur, int rayon)
	{
		var sb = new StyleBoxFlat
		{
			BgColor = new Color(0.0f, 0.0f, 0.0f, 0.0f),
			DrawCenter = false,
			BorderColor = bordure,
			CornerRadiusTopLeft = rayon,
			CornerRadiusTopRight = rayon,
			CornerRadiusBottomLeft = rayon,
			CornerRadiusBottomRight = rayon,
		};
		sb.SetBorderWidthAll(largeur);
		return sb;
	}
}
