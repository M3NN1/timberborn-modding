using UnityEngine;
using UnityEngine.UIElements;

namespace Mods.WhereWyrmsWait.UI
{
    /// <summary>
    /// Shared visual styling for all Where-Wyrms-Wait entity-panel
    /// fragments. Mirrors the BeaverHRDepartment panel palette
    /// (dark-green card on brass border) so mod-owned panels feel
    /// consistent with the rest of the Timberborn UI.
    /// <para>
    /// WWW deliberately ships no UXML/USS through an AssetBundle
    /// (see <c>Placeholders/README.md</c>), so the colors and metrics
    /// are applied inline via <see cref="IStyle"/>. Keep this file the
    /// single source of truth — fragments only call into the helpers
    /// here, never set raw style values themselves.
    /// </para>
    /// </summary>
    public static class WyrmPanelStyle
    {
        // BHR palette — dark-green card, darker inner row, brass borders.
        public static readonly Color PanelBackground   = new Color32( 34,  54,  42, 255);
        public static readonly Color SectionBackground = new Color32( 21,  39,  34, 255);
        public static readonly Color BarTrack          = new Color32(  0,   0,   0, 102); // ~0.4 alpha
        public static readonly Color BorderBrass       = new Color32(176, 152, 102, 255);
        public static readonly Color ButtonBrown       = new Color32( 98,  83,  66, 255);
        public static readonly Color BodyText          = new Color32(204, 204, 204, 255);

        /// <summary>
        /// Style the outer fragment root: dark-green card with padding,
        /// border-radius, and the BHR-style body-text color.
        /// </summary>
        public static void ApplyPanelStyle(VisualElement root)
        {
            var s = root.style;
            s.backgroundColor = PanelBackground;
            s.color = BodyText;
            s.paddingTop = 9;
            s.paddingBottom = 9;
            s.paddingLeft = 12;
            s.paddingRight = 12;
            s.marginTop = 1;
            s.marginBottom = 1;
            s.borderTopLeftRadius = 2;
            s.borderTopRightRadius = 2;
            s.borderBottomLeftRadius = 2;
            s.borderBottomRightRadius = 2;
            s.fontSize = 12;
        }

        /// <summary>Bold 14px title sitting on top of the panel.</summary>
        public static void ApplyTitleStyle(Label title)
        {
            var s = title.style;
            s.unityFontStyleAndWeight = FontStyle.Bold;
            s.fontSize = 14;
            s.marginBottom = 7;
            s.color = BodyText;
        }

        /// <summary>
        /// Inner status / value label — same body color, small bottom
        /// margin so successive labels stack with breathing room.
        /// </summary>
        public static void ApplyBodyLabelStyle(Label label)
        {
            label.style.color = BodyText;
            label.style.marginBottom = 4;
        }

        /// <summary>
        /// Sunken bar track (the dark slot a fill bar sits in).
        /// Adds a thin brass border so the bar reads as a card, not a stripe.
        /// </summary>
        public static void ApplyBarTrackStyle(VisualElement track, int height = 8)
        {
            var s = track.style;
            s.height = height;
            s.backgroundColor = BarTrack;
            s.borderTopWidth = 1;
            s.borderBottomWidth = 1;
            s.borderLeftWidth = 1;
            s.borderRightWidth = 1;
            s.borderTopColor = BorderBrass;
            s.borderBottomColor = BorderBrass;
            s.borderLeftColor = BorderBrass;
            s.borderRightColor = BorderBrass;
            s.borderTopLeftRadius = 2;
            s.borderTopRightRadius = 2;
            s.borderBottomLeftRadius = 2;
            s.borderBottomRightRadius = 2;
            s.marginBottom = 6;
        }

        /// <summary>Colored fill that grows inside a bar track.</summary>
        public static void ApplyBarFillStyle(VisualElement fill, Color color, int height = 8)
        {
            var s = fill.style;
            s.height = height;
            s.backgroundColor = color;
            s.borderTopLeftRadius = 2;
            s.borderTopRightRadius = 2;
            s.borderBottomLeftRadius = 2;
            s.borderBottomRightRadius = 2;
        }

        /// <summary>
        /// Build a labeled progress bar: returns the (label, fill)
        /// pair so the fragment can update text and width per tick.
        /// </summary>
        public static (Label label, VisualElement fill) AddLabeledBar(
            VisualElement parent, Color fillColor, int height = 6)
        {
            var label = new Label();
            ApplyBodyLabelStyle(label);
            label.style.marginBottom = 2;
            parent.Add(label);

            var track = new VisualElement();
            ApplyBarTrackStyle(track, height);
            var fill = new VisualElement();
            ApplyBarFillStyle(fill, fillColor, height);
            // Fill starts hidden until the first UpdateFragment.
            fill.style.width = Length.Percent(0f);
            track.Add(fill);
            parent.Add(track);

            return (label, fill);
        }
    }
}
