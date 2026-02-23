using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
using SharpDX;
using System.Windows.Forms;

namespace PassiveSkillTreePlanter;

public class PassiveSkillTreePlanterSettings : ISettings
{
    public string SelectedCharacterBuild { get; set; } = "";
    public string SelectedAtlasBuild { get; set; } = "";

    // Keep legacy if you already had it (for migration)
    public string SelectedBuild { get; set; } = "default";
    public string LastSelectedCharacterUrl { get; set; }
    public string LastSelectedAtlasUrl { get; set; }

    public HotkeyNode CtrlClickHighlightedHotkey { get; set; } = Keys.F6;
    public RangeNode<int> InputDelay { get; set; } = new(50, 25, 150);

    public ToggleNode AutoAdvanceCharacterTree { get; set; } = new ToggleNode(true);
    public ToggleNode AutoAdvanceAtlasTree { get; set; } = new ToggleNode(true);
    public RangeNode<int> LineWidth { get; set; } = new(3, 0, 5);

    public ColorNode PickedBorderColor { get; set; } = new ColorNode();
    public ColorNode UnpickedBorderColor { get; set; } = new ColorNode(Color.Green);
    public ColorNode WrongPickedBorderColor { get; set; } = new ColorNode(Color.Red);

    public ToggleNode ShowControlPanel { get; set; } = new ToggleNode(true);
    public ToggleNode SaveChangesAutomatically { get; set; } = new ToggleNode(true);
    public ToggleNode Enable { get; set; } = new ToggleNode(false);
}