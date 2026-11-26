using System.Drawing;

namespace AandmAuto
{
	internal sealed class UiPalette
	{
		public readonly Color BgColor;
		public readonly Color SideOuterColor;
		public readonly Color CardColor;
		public readonly Color InsetColor;
		public readonly Color LogBackColor;
		public readonly Color BorderColor;
		public readonly Color AccentColor;
		public readonly Color LineColor;
		public readonly Color GoldColor;
		public readonly Color TextColor;
		public readonly Color MutedColor;
		public readonly Color GreenColor;
		public readonly Color BlueColor;
		public readonly Color RedColor;
		public readonly Color YellowColor;
		public readonly Color BadgeBackColor;
		public readonly Color BadgeBorderColor;
		public readonly Color ButtonInkColor;
		public readonly Color InputBackColor;

		public UiPalette(
			Color bgColor,
			Color sideOuterColor,
			Color cardColor,
			Color insetColor,
			Color logBackColor,
			Color borderColor,
			Color accentColor,
			Color lineColor,
			Color goldColor,
			Color textColor,
			Color mutedColor,
			Color greenColor,
			Color blueColor,
			Color redColor,
			Color yellowColor,
			Color badgeBackColor,
			Color badgeBorderColor,
			Color buttonInkColor,
			Color inputBackColor)
		{
			BgColor = bgColor;
			SideOuterColor = sideOuterColor;
			CardColor = cardColor;
			InsetColor = insetColor;
			LogBackColor = logBackColor;
			BorderColor = borderColor;
			AccentColor = accentColor;
			LineColor = lineColor;
			GoldColor = goldColor;
			TextColor = textColor;
			MutedColor = mutedColor;
			GreenColor = greenColor;
			BlueColor = blueColor;
			RedColor = redColor;
			YellowColor = yellowColor;
			BadgeBackColor = badgeBackColor;
			BadgeBorderColor = badgeBorderColor;
			ButtonInkColor = buttonInkColor;
			InputBackColor = inputBackColor;
		}
	}
}
