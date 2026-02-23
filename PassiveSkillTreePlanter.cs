using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using ExileCore;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.Components;
using ExileCore.Shared;
using ExileCore.Shared.AtlasHelper;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Helpers;
using ImGuiNET;
using PassiveSkillTreePlanter.SkillTreeJson;
using PassiveSkillTreePlanter.TreeGraph;
using PassiveSkillTreePlanter.UrlDecoders;
using PassiveSkillTreePlanter.UrlImporters;
using SharpDX;
using Vector2 = System.Numerics.Vector2;

namespace PassiveSkillTreePlanter;

public class PassiveSkillTreePlanter : BaseSettingsPlugin<PassiveSkillTreePlanterSettings>
{
    private bool _isProcessing = false;
    private CancellationTokenSource _cancellationTokenSource;
    private const string AtlasTreeDataFile = "AtlasTreeData.json";
    private const string SkillTreeDataFile = "SkillTreeData.json";
    private const string SkillTreeDir = "Builds";
    private readonly PoESkillTreeJsonDecoder _skillTreeData = new PoESkillTreeJsonDecoder();
    private readonly PoESkillTreeJsonDecoder _atlasTreeData = new PoESkillTreeJsonDecoder();
    private string _lastAutoAdvancedCharFromUrl = "";
    private string _lastAutoAdvancedAtlasFromUrl = "";
    private long _lastAutoAdvanceMs;
    private InputHandler _inputHandler;
    private readonly List<BaseUrlImporter> _importers =
    [
        new MaxrollTreeImporter(),
        new PobbinTreeImporter(),
        new PobCodeImporter(),
    ];
    private static readonly HashSet<ushort> DefaultStartNodeIds = new()
{
    // DUELIST
    39725, 47389,

    // WARRIOR
    50904, 31628,

    // TEMPLAR
    20228, 63965,

    // WITCH
    57264, 57226,

    // SHADOW
    45272, 38129,

    // RANGER
    45035, 39821
};
    //List of nodes decoded from URL
    private HashSet<ushort> _characterUrlNodeIds = new HashSet<ushort>();
    private HashSet<ushort> _atlasUrlNodeIds = new HashSet<ushort>();
    private ushort _cachedStarterPick;
    private int _cachedStarterPickKey;
    private readonly Random _starterRng = new Random();
    private int _selectedSettingsTab;
    private string _addNewBuildFile = "";
    private string _buildNameEditorValue;
    private AtlasTexture _ringImage;
    private AtlasTexture _ringFilledImage;
    private SyncTask<bool> _currentTask;
    private SyncTask<bool> _choosePassiveTask;

    private ushort _highlightedNextAtlasNodeId;
    private ushort _highlightedNextCharacterNodeId;
    private List<string> BuildFiles { get; set; } = new List<string>();

    public string SkillTreeUrlFilesDir => Directory.CreateDirectory(Path.Join(ConfigDirectory, SkillTreeDir)).FullName;

    //private TreeConfig.SkillTreeData _selectedBuildData = new TreeConfig.SkillTreeData();
    private TreeConfig.SkillTreeData _selectedCharacterBuildData = new();
    private TreeConfig.SkillTreeData _selectedAtlasBuildData = new();
    private TreeConfig.SkillTreeData _selectedEditBuildData = new();

    private TreeConfig.SkillTreeData GetBuildData(ESkillTreeType type) =>
        type == ESkillTreeType.Atlas ? _selectedAtlasBuildData : _selectedCharacterBuildData;

    private string GetSelectedBuildName(ESkillTreeType type) =>
        type == ESkillTreeType.Atlas ? Settings.SelectedAtlasBuild : Settings.SelectedCharacterBuild;



    private TreeConfig.SkillTreeData GetEditBuildData() => _selectedEditBuildData;
    private string GetSelectedEditBuildName() => Settings.SelectedBuild;
    public override void OnLoad()
    {
        _ringImage = GetAtlasTexture("AtlasMapCircle");
        _ringFilledImage = GetAtlasTexture("AtlasMapCircleFilled");
        Graphics.InitImage("Icons.png");
        ReloadGameTreeData();
        ReloadBuildList();
        if (string.IsNullOrWhiteSpace(Settings.SelectedBuild))
        {
            Settings.SelectedBuild = "default";
        }

        if (string.IsNullOrWhiteSpace(Settings.SelectedCharacterBuild))
            Settings.SelectedCharacterBuild = Settings.SelectedBuild;
        if (string.IsNullOrWhiteSpace(Settings.SelectedAtlasBuild))
            Settings.SelectedAtlasBuild = Settings.SelectedBuild;


        LoadBuild(ESkillTreeType.Character, Settings.SelectedCharacterBuild);
        LoadBuild(ESkillTreeType.Atlas, Settings.SelectedAtlasBuild);



        // Edit menu selection (kept as legacy SelectedBuild)
        if (string.IsNullOrWhiteSpace(Settings.SelectedBuild))
            Settings.SelectedBuild = "default";
        LoadEditBuild(Settings.SelectedBuild);
        LoadUrl(Settings.LastSelectedAtlasUrl);
        LoadUrl(Settings.LastSelectedCharacterUrl);
    }

    private void ReloadBuildList()
    {
        BuildFiles = TreeConfig.GetBuilds(SkillTreeUrlFilesDir);
    }

    private void LoadBuild(ESkillTreeType type, string buildName)
    {
        if (type == ESkillTreeType.Atlas)
        {
            Settings.SelectedAtlasBuild = buildName;
            _selectedAtlasBuildData = TreeConfig.LoadBuild(SkillTreeUrlFilesDir, buildName) ?? new TreeConfig.SkillTreeData();
            _atlasUrlNodeIds = new HashSet<ushort>();   // only reset atlas
        }
        else
        {
            Settings.SelectedCharacterBuild = buildName;
            _selectedCharacterBuildData = TreeConfig.LoadBuild(SkillTreeUrlFilesDir, buildName) ?? new TreeConfig.SkillTreeData();
            _characterUrlNodeIds = new HashSet<ushort>(); // only reset character
        }
    }

    private void LoadEditBuild(string buildName)
    {
        if (string.IsNullOrWhiteSpace(buildName))
            return;

        Settings.SelectedBuild = buildName;
        _selectedEditBuildData = TreeConfig.LoadBuild(SkillTreeUrlFilesDir, buildName) ?? new TreeConfig.SkillTreeData();

        // IMPORTANT: prevent ImGui.InputText null crash
        _buildNameEditorValue = buildName;
    }


    private void LoadUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        var cleanedUrl = RemoveAccName(url).Trim();
        var (nodes, type) = TreeEncoder.DecodeUrl(cleanedUrl);
        if (nodes == null)
        {
            LogMessage($"PassiveSkillTree: Can't decode url {url}", 10);
            return;
        }

        if (type == ESkillTreeType.Character)
        {
            _characterUrlNodeIds = nodes;
            Settings.LastSelectedCharacterUrl = url;
            ValidateNodes(_characterUrlNodeIds, _skillTreeData.SkillNodes);
        }

        if (type == ESkillTreeType.Atlas)
        {
            _atlasUrlNodeIds = nodes;
            Settings.LastSelectedAtlasUrl = url;
            ValidateNodes(_atlasUrlNodeIds, _atlasTreeData.SkillNodes);
        }
    }

    public override bool Initialise()
    {
        var windowPosition = GameController.Window.GetWindowRectangleTimeCache.TopLeft;
        var windowFix = new Vector2(windowPosition.X, windowPosition.Y);
        _inputHandler = new InputHandler(windowFix, Settings);
        return true;
    }

    private void ReloadGameTreeData()
    {
        var atlasTreePath = Path.Join(DirectoryFullName, AtlasTreeDataFile);
        if (!File.Exists(atlasTreePath))
        {
            LogMessage($"Atlas passive skill tree: Can't find file {atlasTreePath} with atlas skill tree data.", 10);
        }
        else
        {
            _atlasTreeData.Decode(File.ReadAllText(atlasTreePath));
        }

        var skillTreeDataPath = Path.Join(DirectoryFullName, SkillTreeDataFile);
        if (!File.Exists(skillTreeDataPath))
        {
            LogMessage($"Passive skill tree: Can't find file {skillTreeDataPath} with skill tree data.", 10);
        }
        else
        {
            var skillTreeJson = File.ReadAllText(skillTreeDataPath);
            _skillTreeData.Decode(skillTreeJson);
        }
    }

    public override void Render()
    {
        
        DrawTree(GameController.Game.IngameState.IngameUi.TreePanel.AsObject<TreePanel>(),
            _skillTreeData, _characterUrlNodeIds,
            () => GameController.Game.IngameState.ServerData.PassiveSkillIds.ToHashSet(),
            ESkillTreeType.Character);
        DrawTree(GameController.Game.IngameState.IngameUi.AtlasTreePanel.AsObject<TreePanel>(),
            _atlasTreeData, _atlasUrlNodeIds,
            () => GameController.Game.IngameState.ServerData.AtlasPassiveSkillIds.ToHashSet(),
            ESkillTreeType.Atlas);



        if (Settings.CtrlClickHighlightedHotkey.PressedOnce())
        {
            _isProcessing = !_isProcessing;

            if (_isProcessing)
            {
                LogMessage("Auto choose passive started", 5, Color.LimeGreen);
                _cancellationTokenSource = new CancellationTokenSource();
            }
            else
            {
                LogMessage("Auto choose passive stopped", 5, Color.Yellow);
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                _choosePassiveTask = null;
            }
        }
        if (_isProcessing)
        {
            //_repricerTask = null;
            TaskUtils.RunOrRestart(ref _choosePassiveTask, () => RunAutoPassiveTask(_cancellationTokenSource.Token));
        }

        
        
    }
    private ushort PickBestNextNodeWithStarterFallback(
    IReadOnlySet<ushort> allocatedNodeIds,
    HashSet<ushort> missingNodes,
    PoESkillTreeJsonDecoder treeData)
    {

        var filteredMissingNodes = FilterMissingNodes(missingNodes, treeData);

        if (filteredMissingNodes.Count == 0)
            return 0;

        // 1) Normal behavior: choose best frontier node (adjacent missing)
        var bestFrontier = PickBestAdjacentMissingNodeByDepth(allocatedNodeIds, filteredMissingNodes, treeData);
        if (bestFrontier != 0)
            return bestFrontier;

        // 2) Fallback: pick among default starter nodes present in missingNodes
        ushort bestStarter = 0;
        int bestDepth = int.MaxValue;

        foreach (var id in missingNodes)
        {
            if (!DefaultStartNodeIds.Contains(id))
                continue;

            // Depth is computed within the missing graph
            var depth = MissingDepthBfs(id, missingNodes, treeData);

            // Pick smallest depth; deterministic tie-break by id
            if (depth < bestDepth || (depth == bestDepth && (bestStarter == 0 || id < bestStarter)))
            {
                bestStarter = id;
                bestDepth = depth;
            }
        }

        return bestStarter; // 0 if none
    }
    private HashSet<ushort> FilterMissingNodes(
    IEnumerable<ushort> missingNodes,
    PoESkillTreeJsonDecoder treeData)
    {
        var filtered = new HashSet<ushort>();

        foreach (var id in missingNodes)
        {
            if (id == 0)
                continue;

            if (!treeData.SkillNodes.TryGetValue(id, out var node) || node == null)
                continue;

            if (IsIgnoredAscendancyNode(node))
                continue;

            filtered.Add(id);
        }

        return filtered;
    }
    private int MissingDepthBfs(
    ushort start,
    HashSet<ushort> missingNodes,
    PoESkillTreeJsonDecoder treeData)
    {
        var q = new Queue<(ushort node, int dist)>();
        var seen = new HashSet<ushort>();

        q.Enqueue((start, 0));
        seen.Add(start);

        int maxDepth = 0;

        while (q.Count > 0)
        {
            var (cur, dist) = q.Dequeue();
            if (dist > maxDepth) maxDepth = dist;

            var curNode = treeData.SkillNodes[cur];
            if (curNode?.linkedNodes == null) continue;

            foreach (var nxt in curNode.linkedNodes)
            {
                if (!missingNodes.Contains(nxt)) continue;
                if (seen.Add(nxt))
                    q.Enqueue((nxt, dist + 1));
            }
        }

        return maxDepth;
    }

    private ushort PickBestAdjacentMissingNodeByDepth(
    IReadOnlySet<ushort> allocatedNodeIds,
    HashSet<ushort> missingNodes,
    PoESkillTreeJsonDecoder treeData)
    {
        // frontier = missing nodes adjacent to allocated nodes
        var frontier = new List<ushort>();

        foreach (var id in missingNodes)
        {
            if (!treeData.SkillNodes.TryGetValue(id, out var node) || node?.linkedNodes == null)
                continue;

            // Adjacent to an allocated node?
            if (node.linkedNodes.Any(allocatedNodeIds.Contains))
                frontier.Add(id);
        }

        if (frontier.Count == 0)
            return 0;

        // Tie-break by "depth" into remaining missing graph:
        // depth(candidate) = max BFS distance from candidate through only missingNodes.
        

        ushort best = 0;
        int bestDepth = int.MaxValue;

        foreach (var c in frontier)
        {
            var depth = MissingDepthBfs(c, missingNodes, treeData);

            // pick smallest depth; final deterministic tie-break by id
            if (depth < bestDepth || (depth == bestDepth && (best == 0 || c < best)))
            {
                best = c;
                bestDepth = depth;
            }
        }

        return best;
    }
    private void DrawControlPanel(ESkillTreeType skillTreeType, TreePanel treePanel, IReadOnlySet<ushort> allocatedNodeIds, IReadOnlySet<ushort> targetNodeIds)
    {
        if (!Settings.ShowControlPanel)
            return;

        var isOpen = true;
        ImGui.SetNextWindowPos(new Vector2(0, 0), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("#treeSwitcher", ref isOpen, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar))
        {
            var buildData = GetBuildData(skillTreeType);
            var trees = buildData.Trees.Where(x => x.Type == skillTreeType).ToList();
            var currentBuild = GetSelectedBuildName(skillTreeType);

            var newBuildName = ImGuiExtension.ComboBox(
                skillTreeType == ESkillTreeType.Atlas ? "Atlas Build" : "Character Build",
                currentBuild,
                BuildFiles,
                out var buildSelected,
                ImGuiComboFlags.HeightLarge);

            if (buildSelected)
            {
                LoadBuild(skillTreeType, newBuildName);
            }

            foreach (var tree in trees)
            {
                var lastSelectedUrl = skillTreeType switch
                {
                    ESkillTreeType.Character => Settings.LastSelectedCharacterUrl,
                    ESkillTreeType.Atlas => Settings.LastSelectedAtlasUrl,
                };
                ImGui.BeginDisabled(lastSelectedUrl == tree.SkillTreeUrl);
                if (ImGui.Button($"Load {tree.Tag}"))
                {
                    LoadUrl(tree.SkillTreeUrl);
                }

                ImGui.EndDisabled();
            }

            if (ImGui.Button("Operate tree"))
            {
                _currentTask = ChangeTree(allocatedNodeIds, targetNodeIds, treePanel);
            }

            if (ImGui.Button("Show/hide editor"))
            {
                _editorShown = !_editorShown;
                _nodeMap.Clear();
                _pathingNodes = null;
            }

            //ImGui.EndMenu();
            ImGui.End();
        }
    }

    private static string CleanFileName(string fileName)
    {
        return Path.GetInvalidFileNameChars()
            .Aggregate(fileName, (current, c) => current.Replace(c.ToString(), string.Empty));
    }

    private void RenameFile(string fileName, string oldFileName)
    {
        fileName = CleanFileName(fileName);

        var oldFilePath = Path.Combine(SkillTreeUrlFilesDir, $"{oldFileName}.json");
        var newFilePath = Path.Combine(SkillTreeUrlFilesDir, $"{fileName}.json");
        File.Move(oldFilePath, newFilePath);

        // Update selections if they pointed to the renamed build
        if (string.Equals(Settings.SelectedBuild, oldFileName, StringComparison.Ordinal))
            Settings.SelectedBuild = fileName;

        if (string.Equals(Settings.SelectedCharacterBuild, oldFileName, StringComparison.Ordinal))
            Settings.SelectedCharacterBuild = fileName;

        if (string.Equals(Settings.SelectedAtlasBuild, oldFileName, StringComparison.Ordinal))
            Settings.SelectedAtlasBuild = fileName;

        ReloadBuildList();

        // Reload the edit build data to the new name
        LoadEditBuild(Settings.SelectedBuild);

        // Also reload the per-tree selections so the UI reflects the updated names
        LoadBuild(ESkillTreeType.Character, Settings.SelectedCharacterBuild);
        LoadBuild(ESkillTreeType.Atlas, Settings.SelectedAtlasBuild);
    }


    private bool CanRename(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Intersect(Path.GetInvalidFileNameChars()).Any())
        {
            return false;
        }

        var newFilePath = Path.Combine(SkillTreeUrlFilesDir, $"{fileName}.json");
        return !File.Exists(newFilePath);
    }

    private static string RemoveAccName(string url)
    {
        // Aim is to remove the string content but keep the info inside the text file in case user wants to revisit that account/char in the future
        url = url.Split("?accountName")[0];
        url = url.Split("?characterName")[0];
        return url;
    }

    public override void DrawSettings()
    {
        string[] settingName =
        {
            "Build Selection",
            "Build Edit",
            "Settings",
        };

        if (ImGui.BeginChild("LeftSettings", new Vector2(150, ImGui.GetContentRegionAvail().Y),
                ImGuiChildFlags.Border, ImGuiWindowFlags.None))
        {
            for (var i = 0; i < settingName.Length; i++)
            {
                if (ImGui.Selectable(settingName[i], _selectedSettingsTab == i))
                    _selectedSettingsTab = i;
            }
        }

        ImGui.EndChild();
        ImGui.SameLine();
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 5.0f);

        var contentRegionArea = ImGui.GetContentRegionAvail();
        if (ImGui.BeginChild("RightSettings", contentRegionArea, ImGuiChildFlags.Border, ImGuiWindowFlags.None))
        {
            switch (settingName[_selectedSettingsTab])
            {
                case "Build Selection":
                    {
                        if (ImGui.Button("Open Build Folder"))
                            Process.Start("explorer.exe", SkillTreeUrlFilesDir);

                        ImGui.SameLine();
                        if (ImGui.Button("(Re)Load List"))
                            ReloadBuildList();

                        ImGui.Separator();

                        ImGui.TextUnformatted("Character Build:");
                        ImGui.SameLine();
                        var newCharacterBuildName = ImGuiExtension.ComboBox(
                            "##CharacterBuilds",
                            Settings.SelectedCharacterBuild,
                            BuildFiles,
                            out var characterBuildSelected,
                            ImGuiComboFlags.HeightLarge);

                        if (characterBuildSelected)
                            LoadBuild(ESkillTreeType.Character, newCharacterBuildName);

                        ImGui.TextUnformatted("Atlas Build:");
                        ImGui.SameLine();
                        var newAtlasBuildName = ImGuiExtension.ComboBox(
                            "##AtlasBuilds",
                            Settings.SelectedAtlasBuild,
                            BuildFiles,
                            out var atlasBuildSelected,
                            ImGuiComboFlags.HeightLarge);

                        if (atlasBuildSelected)
                            LoadBuild(ESkillTreeType.Atlas, newAtlasBuildName);
                        ImGui.Separator();
                        ImGui.TextUnformatted("Create new build:");

                        ImGui.InputText("##CreationLabel", ref _addNewBuildFile, 1024, ImGuiInputTextFlags.EnterReturnsTrue);

                        ImGui.BeginDisabled(!CanRename(_addNewBuildFile));
                        if (ImGui.Button($"Add new build {_addNewBuildFile}"))
                        {
                            var fileName = CleanFileName(_addNewBuildFile);

                            // Create empty build JSON
                            TreeConfig.SaveSettingFile(Path.Join(SkillTreeUrlFilesDir, fileName), new TreeConfig.SkillTreeData());

                            _addNewBuildFile = string.Empty;
                            ReloadBuildList();
                        }
                        ImGui.EndDisabled();
                        break;
                    }

                case "Build Edit":
                    {
                        // This edit menu is intentionally NOT split into atlas/character.
                        // You can edit any build file here.
                        var editBuildName = Settings.SelectedBuild;

                        var newEditBuildName = ImGuiExtension.ComboBox(
                            "Builds",
                            editBuildName,
                            BuildFiles,
                            out var editBuildSelected,
                            ImGuiComboFlags.HeightLarge);

                        if (editBuildSelected)
                            LoadEditBuild(newEditBuildName);

                        var buildData = GetEditBuildData();
                        var trees = buildData.Trees;

                        if (!string.IsNullOrEmpty(buildData.BuildLink))
                        {
                            ImGui.SameLine();
                            if (ImGui.Button("Open Forum Thread"))
                            {
                                Process.Start(new ProcessStartInfo(buildData.BuildLink) { UseShellExecute = true });
                            }
                        }

                        DrawBuildEdit(buildData, Settings.SelectedBuild, trees, contentRegionArea);
                        break;
                    }

                case "Settings":
                default:
                    base.DrawSettings();
                    break;
            }
        }

        ImGui.PopStyleVar();
        ImGui.EndChild();
    }

    private void DrawBuildEdit(
        TreeConfig.SkillTreeData buildData,
        string selectedBuildName,
        List<TreeConfig.Tree> trees,
        Vector2 contentRegionArea)
    {
        if (trees.Count > 0)
        {
            ImGui.Separator();
            var buildLink = buildData.BuildLink ?? string.Empty;
            if (ImGui.InputText("Forum Thread", ref buildLink, 1024, ImGuiInputTextFlags.None))
            {
                buildData.BuildLink = buildLink.Replace("\u0000", null);
                buildData.Modified = true;
            }

            ImGui.Text("Notes");
            // Keep at max 4k byte size not sure why it crashes when upped, not going to bother dealing with this either.
            var notes = buildData.Notes ?? string.Empty;
            if (ImGui.InputTextMultiline("##Notes", ref notes, 150000, new Vector2(contentRegionArea.X - 20, 200)))
            {
                buildData.Notes = notes.Replace("\u0000", null);
                buildData.Modified = true;
            }

            ImGui.Separator();
            ImGui.Columns(5, "EditColums", true);
            ImGui.SetColumnWidth(0, 30f);
            ImGui.SetColumnWidth(1, 50f);
            ImGui.SetColumnWidth(3, 38f);
            ImGui.Text("");
            ImGui.NextColumn();
            ImGui.Text("Move");
            ImGui.NextColumn();
            ImGui.Text("Tree Name");
            ImGui.NextColumn();
            ImGui.Text("Type");
            ImGui.NextColumn();
            ImGui.Text("Skill Tree");
            ImGui.NextColumn();
            if (trees.Count != 0)
                ImGui.Separator();
            for (var j = 0; j < trees.Count; j++)
            {
                ImGui.PushID($"{j}");
                DrawTreeEdit(buildData, trees, j);
                ImGui.PopID();
            }

            ImGui.Separator();
            ImGui.Columns(1, "", false);
        }
        else
        {
            ImGui.Text("No Data Selected");
        }

        if (ImGui.Button("+##AN"))
        {
            trees.Add(new TreeConfig.Tree());
            buildData.Modified = true;
        }

        ImGui.Text("Export current build");
        ImGui.SameLine();
        var rectMyPlayer = SpriteHelper.GetUV(MapIconsIndex.MyPlayer);
        if (ImGui.ImageButton("charBtn", Graphics.GetTextureId("Icons.png"), new Vector2(ImGui.CalcTextSize("A").Y),
                rectMyPlayer.TopLeft.ToVector2Num(), rectMyPlayer.BottomRight.ToVector2Num()))
        {
            trees.Add(new TreeConfig.Tree
            {
                Tag = "Current character tree",
                SkillTreeUrl = PathOfExileUrlDecoder.Encode(
                    GameController.Game.IngameState.ServerData.PassiveSkillIds.ToHashSet(),
                    ESkillTreeType.Character)
            });
            buildData.Modified = true;
        }

        ImGui.SameLine();
        var rectTangle = SpriteHelper.GetUV(MapIconsIndex.TangleAltar);
        if (ImGui.ImageButton("atlasBtn", Graphics.GetTextureId("Icons.png"), new Vector2(ImGui.CalcTextSize("A").Y),
                rectTangle.TopLeft.ToVector2Num(), rectTangle.BottomRight.ToVector2Num()))
        {
            trees.Add(new TreeConfig.Tree
            {
                Tag = "Current atlas tree",
                SkillTreeUrl = PathOfExileUrlDecoder.Encode(
                    GameController.Game.IngameState.ServerData.AtlasPassiveSkillIds.ToHashSet(),
                    ESkillTreeType.Atlas)
            });
            buildData.Modified = true;
        }

        foreach (var importer in _importers)
        {
            if (importer.DrawAddInterface() is { } newTree)
            {
                trees.Add(newTree);
                buildData.Modified = true;
            }
        }

        ImGui.Separator();
        _buildNameEditorValue ??= selectedBuildName ?? string.Empty;
        ImGui.InputText("##RenameLabel", ref _buildNameEditorValue, 200, ImGuiInputTextFlags.None);
        ImGui.SameLine();
        ImGui.BeginDisabled(!CanRename(_buildNameEditorValue));
        if (ImGui.Button("Rename Build"))
        {
            RenameFile(_buildNameEditorValue, selectedBuildName);
        }

        ImGui.EndDisabled();

        if (ImGui.Button($"Save Build to File: {selectedBuildName}") ||
            (buildData.Modified && Settings.SaveChangesAutomatically))
        {
            buildData.Modified = false;
            TreeConfig.SaveSettingFile(Path.Join(SkillTreeUrlFilesDir, selectedBuildName), buildData);
            ReloadBuildList();
        }

        if (buildData.Modified)
        {
            ImGui.TextColored(Color.Red.ToImguiVec4(), "Unsaved changes detected");
        }
    }

    private void DrawTreeEdit(TreeConfig.SkillTreeData buildData, List<TreeConfig.Tree> trees, int treeIndex)
    {
        var tree = trees[treeIndex];

        // --- Column 0: Remove ---
        if (ImGui.SmallButton("X"))
        {
            trees.RemoveAt(treeIndex);
            buildData.Modified = true;

            // Fill the remaining columns for this row so ImGui columns don't desync
            ImGui.NextColumn(); ImGui.NextColumn(); ImGui.NextColumn(); ImGui.NextColumn();
            return;
        }
        ImGui.NextColumn();

        // --- Column 1: Move Up/Down ---
        ImGui.BeginDisabled(treeIndex == 0);
        if (ImGui.SmallButton("Up"))
        {
            (trees[treeIndex - 1], trees[treeIndex]) = (trees[treeIndex], trees[treeIndex - 1]);
            buildData.Modified = true;
        }
        ImGui.EndDisabled();

        ImGui.SameLine();

        ImGui.BeginDisabled(treeIndex >= trees.Count - 1);
        if (ImGui.SmallButton("Dn"))
        {
            (trees[treeIndex + 1], trees[treeIndex]) = (trees[treeIndex], trees[treeIndex + 1]);
            buildData.Modified = true;
        }
        ImGui.EndDisabled();

        ImGui.NextColumn();

        // --- Column 2: Tree Name (Tag) ---
        var tag = tree.Tag ?? string.Empty;
        ImGui.PushItemWidth(-1);
        if (ImGui.InputText("##Tag", ref tag, 128))
        {
            tree.Tag = tag.Replace("\u0000", null);
            buildData.Modified = true;
        }
        ImGui.PopItemWidth();
        ImGui.NextColumn();

        // --- Column 3: Type (derived from URL) ---
        // Your TreeConfig.Tree.Type is [JsonIgnore] and derived from the URL decoder, which is fine here.
        var typeText = tree.Type.ToString();
        ImGui.TextUnformatted(typeText);
        ImGui.NextColumn();

        // --- Column 4: Skill Tree URL ---
        var url = tree.SkillTreeUrl ?? string.Empty;
        ImGui.PushItemWidth(-1);
        if (ImGui.InputText("##Url", ref url, 2048))
        {
            tree.SkillTreeUrl = url.Replace("\u0000", null);
            buildData.Modified = true;
        }
        ImGui.PopItemWidth();
        ImGui.NextColumn();
    }
    private static void MoveElement<T>(List<T> list, int changeIndex, bool moveUp)
    {
        if (moveUp)
        {
            // Move Up
            if (changeIndex > 0)
            {
                (list[changeIndex], list[changeIndex - 1]) = (list[changeIndex - 1], list[changeIndex]);
            }
        }
        else
        {
            // Move Down                               
            if (changeIndex < list.Count - 1)
            {
                (list[changeIndex], list[changeIndex + 1]) = (list[changeIndex + 1], list[changeIndex]);
            }
        }
    }

    private void ValidateNodes(HashSet<ushort> currentNodes, Dictionary<ushort, SkillNode> nodeDict)
    {
        foreach (var urlNodeId in currentNodes)
        {
            if (!nodeDict.TryGetValue(urlNodeId, out var node))
            {
                LogError($"PassiveSkillTree: Can't find passive skill tree node with id: {urlNodeId}", 5);
                continue;
            }

            foreach (var lNodeId in node.linkedNodes?.Where(currentNodes.Contains) ?? [])
            {
                if (!nodeDict.ContainsKey(lNodeId))
                {
                    LogError($"PassiveSkillTree: Can't find passive skill tree node with id: {lNodeId} to draw the link", 5);
                }
            }
        }
    }

    private void DrawTree(TreePanel treePanel, PoESkillTreeJsonDecoder treeData,
    IReadOnlySet<ushort> targetNodeIds, Func<IReadOnlySet<ushort>> allocatedNodeIdsFunc, ESkillTreeType type)
    {
        if (!treePanel.IsVisible)
            return;


        // Always draw the control panel (build/tree picker)
        var allocatedNodeIds = allocatedNodeIdsFunc();
        DrawControlPanel(type, treePanel, allocatedNodeIds, targetNodeIds);

        // If no target nodes, don't draw overlays, but keep menu visible
        if (targetNodeIds is not { Count: > 0 })
            return;

        var canvas = treePanel.CanvasElement;
        var baseOffset = new Vector2(canvas.Center.X, canvas.Center.Y);

        DrawTreeOverlay(allocatedNodeIds, targetNodeIds, treeData, canvas.Scale, baseOffset, type);
        DrawTreeEditOverlay(treeData, canvas.Scale, baseOffset);
    }
    private enum ConnectionType
    {
        Deallocate,
        Allocate,
        Allocated,
    }

    private async SyncTask<bool> ChangeTree(IReadOnlySet<ushort> allocatedNodeIds, IReadOnlySet<ushort> targetNodeIds, TreePanel panel)
    {
        var passivesById = panel.Passives.DistinctBy(x => x.PassiveSkill.PassiveId).ToDictionary(x => x.PassiveSkill.PassiveId);
        var wrongNodes = allocatedNodeIds.Except(targetNodeIds).ToHashSet();
        var nodesToTake = targetNodeIds.Except(allocatedNodeIds).ToHashSet();
        while (panel.IsVisible)
        {
            var nodeToRemove = wrongNodes.Select(arg => passivesById.GetValueOrDefault(arg)).FirstOrDefault(x => x is { IsAllocatedForPlan: true, CanDeallocate: true });
            if (nodeToRemove != null)
            {
                var windowRect = GameController.Window.GetWindowRectangleTimeCache.TopLeft.ToVector2Num();
                if (panel.RefundButton.IsVisible)
                {
                    if (panel.RefundButton.HasShinyHighlight)
                    {
                        DebugWindow.LogMsg("Clicking refund button");
                        Input.Click(MouseButtons.Left);
                    }
                    else
                    {
                        DebugWindow.LogMsg("Hovering refund button");
                        Input.SetCursorPos(windowRect + panel.RefundButton.GetClientRectCache.Center.ToVector2Num());
                    }

                    await Task.Delay(250);
                    continue;
                }

                if (GameController.IngameState.UIHover?.Address == nodeToRemove.Address)
                {
                    DebugWindow.LogMsg($"Clicking passive {nodeToRemove.PassiveSkill?.PassiveId} ({nodeToRemove.PassiveSkill?.Name})");
                    Input.Click(MouseButtons.Left);
                }
                else
                {
                    DebugWindow.LogMsg($"Hovering passive {nodeToRemove.PassiveSkill?.PassiveId} ({nodeToRemove.PassiveSkill?.Name})");
                    Input.SetCursorPos(windowRect + nodeToRemove.GetClientRectCache.Center.ToVector2Num());
                }

                await Task.Delay(250);
            }
            else if (panel.RefundButton.IsVisible)
            {
                var nodeToTake = nodesToTake.Select(arg => passivesById.GetValueOrDefault(arg)).FirstOrDefault(x => x is { IsAllocatedForPlan: false, CanAllocate: true });
                if (nodeToTake != null)
                {
                    var windowRect = GameController.Window.GetWindowRectangleTimeCache.TopLeft.ToVector2Num();

                    if (GameController.IngameState.UIHover?.Address == nodeToTake.Address)
                    {
                        DebugWindow.LogMsg($"Clicking passive {nodeToTake.PassiveSkill?.PassiveId} ({nodeToTake.PassiveSkill?.Name})");
                        Input.Click(MouseButtons.Left);
                    }
                    else
                    {
                        DebugWindow.LogMsg($"Hovering passive {nodeToTake.PassiveSkill?.PassiveId} ({nodeToTake.PassiveSkill?.Name})");
                        Input.SetCursorPos(windowRect + nodeToTake.GetClientRectCache.Center.ToVector2Num());
                    }

                    await Task.Delay(250);
                }
                else
                {
                    break;
                }
            }
            else
            {
                break;
            }
        }

        return true;
    }

    private Dictionary<uint, bool> _nodeMap = new();
    private Task<HashSet<uint>> _pathingNodes;
    private bool _editorShown;

    private void DrawTreeEditOverlay(PoESkillTreeJsonDecoder treeData, float scale, Vector2 baseOffset)
    {
        if (!_editorShown)
        {
            return;
        }

        var nodes = treeData.SkillNodes.Where(x => x.Value.linkedNodes != null).Select(x => x.Value).ToList();
        var pathingNodes = _pathingNodes is { IsCompletedSuccessfully: true } ? _pathingNodes.Result : [];
        DebugWindow.LogMsg($"Solved optimization in {pathingNodes.Count} nodes");
        foreach (var node in nodes)
        {
            var drawSize = node.DrawSize * scale;
            var posX = baseOffset.X + node.DrawPosition.X * scale;
            var posY = baseOffset.Y + node.DrawPosition.Y * scale;
            ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, Vector2.Zero);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.SetNextWindowPos(new Vector2(posX, posY) - new Vector2(drawSize / 2));
            ImGui.SetNextWindowSize(new Vector2(drawSize));
            ImGui.PushStyleColor(ImGuiCol.WindowBg, Color.Transparent.ToImgui());
            ImGui.PushStyleColor(ImGuiCol.Border,
                _nodeMap.GetValueOrDefault(node.Id, false) ? Color.Green.ToImgui() : pathingNodes.Contains(node.Id) ? Color.Blue.ToImgui() : Color.Red.ToImgui());
            foreach (var linkedNode in node.linkedNodes)
            {
                if (linkedNode < node.Id)
                {
                    continue;
                }

                if (pathingNodes.Contains(linkedNode) && pathingNodes.Contains(node.Id))
                {
                    Graphics.DrawLine(treeData.SkillNodes[linkedNode].DrawPosition * scale + baseOffset, new Vector2(posX, posY), 5, Color.Blue);
                }
            }
            if (ImGui.Begin($"planter_node_{node.Id}", ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoTitleBar))
            {
                ImGui.SetCursorPos(Vector2.Zero);
                if (ImGui.InvisibleButton("button", ImGui.GetContentRegionAvail()))
                {
                    _nodeMap[node.Id] = !_nodeMap.GetValueOrDefault(node.Id, false);
                    var nodesToPick = _nodeMap.Where(x => x.Value).Select(x => new Vertex((int)x.Key)).ToList();
                    _pathingNodes = Task.Run(() =>
                    {
                        var matrix = nodes.ToDictionary(x => new Vertex(x.Id), x => x.linkedNodes.Select(l => new Vertex(l)).ToList());
                        var graph = new Graph(matrix);
                        return GraphOptimizer.ReduceGraph(graph, nodesToPick).Select(x => (uint)x.Id).ToHashSet();
                    });
                }

                ImGui.End();
            }

            ImGui.PopStyleColor();
            ImGui.PopStyleColor();
            ImGui.PopStyleVar();
            ImGui.PopStyleVar();
        }
    }

    private void DrawTreeOverlay(IReadOnlySet<ushort> allocatedNodeIds, IReadOnlySet<ushort> targetNodeIds, PoESkillTreeJsonDecoder treeData, float scale, Vector2 baseOffset,  ESkillTreeType type)
    {
        if (_editorShown)
        {
            return;
        }
        var wrongNodes = allocatedNodeIds.Except(targetNodeIds).ToHashSet();
        var missingNodes = targetNodeIds.Except(allocatedNodeIds).ToHashSet();

        var filteredMissingNodes = FilterMissingNodes(missingNodes, treeData);
  
        if (filteredMissingNodes.Count == 0)
        {
            // Use the correct build data for that tree type
            
            var buildData = (type == ESkillTreeType.Atlas) ? _selectedAtlasBuildData : _selectedCharacterBuildData;
            TryAutoAdvanceTree(type, buildData);
        }
        var bestNextNodeId = PickBestNextNodeWithStarterFallback(allocatedNodeIds, missingNodes, treeData);
        var allNodes = targetNodeIds.Union(allocatedNodeIds).Select(x => treeData.SkillNodes.GetValueOrDefault(x)).Where(x => x?.linkedNodes != null).ToList();
        var allConnections = allNodes
            .SelectMany(node => node.linkedNodes
                .Where(treeData.SkillNodes.ContainsKey)
                .Where(id => targetNodeIds.Contains(id) || allocatedNodeIds.Contains(id))
                .Select(linkedNode => (Math.Min(node.Id, linkedNode), Math.Max(node.Id, linkedNode))))
            .Distinct()
            .Select(pair => (ids: pair, type: pair switch
            {
                var (a, b) when wrongNodes.Contains(a) || wrongNodes.Contains(b) => ConnectionType.Deallocate,
                var (a, b) when missingNodes.Contains(a) || missingNodes.Contains(b) => ConnectionType.Allocate,
                _ => ConnectionType.Allocated,
            }))
            .ToList();
        foreach (var node in allNodes)
        {
            var drawSize = node.DrawSize * scale;
            var posX = baseOffset.X + node.DrawPosition.X * scale;
            var posY = baseOffset.Y + node.DrawPosition.Y * scale;

            var color = (allocatedNodeIds.Contains(node.Id), targetNodeIds.Contains(node.Id)) switch
            {
                (true, true) => Settings.PickedBorderColor.Value,
                (true, false) => Settings.WrongPickedBorderColor.Value,
                (false, true) => Settings.UnpickedBorderColor.Value,
                (false, false) => Color.Orange,
            };
            
            Graphics.DrawImage(_ringImage, new RectangleF(posX - drawSize / 2, posY - drawSize / 2, drawSize, drawSize), color);
            if (bestNextNodeId != 0 && treeData.SkillNodes.TryGetValue(bestNextNodeId, out var bestNode) && bestNode?.linkedNodes != null)
            {
                var drawSizeBest = bestNode.DrawSize * scale * 1.45f; // slightly bigger so it stands out
                var posXBest = baseOffset.X + bestNode.DrawPosition.X * scale;
                var posYBest = baseOffset.Y + bestNode.DrawPosition.Y * scale;
                var bestNodeId = bestNode.Name;
                var ascendancyName = bestNode.AscendancyName;

                Graphics.DrawImage(
                    _ringImage ?? _ringImage,
                    new RectangleF(posXBest - drawSizeBest / 2, posYBest - drawSizeBest / 2, drawSizeBest, drawSizeBest),
                    Color.Yellow);

                if (type == ESkillTreeType.Atlas)
                    _highlightedNextAtlasNodeId = bestNextNodeId;
                else
                    _highlightedNextCharacterNodeId = bestNextNodeId;
            }

        }

        if (Settings.LineWidth > 0)
        {
            foreach (var link in allConnections)
            {
                var node1 = treeData.SkillNodes[link.ids.Item1];
                var node2 = treeData.SkillNodes[link.ids.Item2];
                var node1Pos = baseOffset + node1.DrawPosition * scale;
                var node2Pos = baseOffset + node2.DrawPosition * scale;
                var diffVector = Vector2.Normalize(node2Pos - node1Pos);
                node1Pos += diffVector * node1.DrawSize * scale / 2;
                node2Pos -= diffVector * node2.DrawSize * scale / 2;

                Graphics.DrawLine(node1Pos, node2Pos, Settings.LineWidth, link.type switch
                {
                    ConnectionType.Deallocate => Settings.WrongPickedBorderColor,
                    ConnectionType.Allocate => Settings.UnpickedBorderColor,
                    ConnectionType.Allocated => Settings.PickedBorderColor,
                    _ => Color.Orange,
                });
            }
        }

        var textPos = new Vector2(50, 300);
        Graphics.DrawText($"Total Tree Nodes: {targetNodeIds.Count}", textPos, Color.White, 15);
        textPos.Y += 20;
        Graphics.DrawText($"Picked Nodes: {allocatedNodeIds.Count}", textPos, Color.Green, 15);
        textPos.Y += 20;
        Graphics.DrawText($"Wrong Picked Nodes: {wrongNodes.Count}", textPos, Color.Red, 15);
    }


    private readonly string[] IgnoredAscendancyNames =
    {
        "Berserker",
        "Guardian",
        "Juggernaut",
        "Hierophant",
        "Chieftain",
        "Inquisitor",
        "Ascendant",
        "Gladiator",
        "Occultist",
        "Elementalist",
        "Champion",
        "Necromancer",
        "Slayer",
        "Assassin",
        "Pathfinder",
        "Trickster",
        "Saboteur",
        "Raider",
        "Deadeye"
    };

    private bool IsIgnoredAscendancyNode(SkillNode node)
    {
        var asc = node?.AscendancyName;
        if (string.IsNullOrWhiteSpace(asc))
            return false;

        for (int i = 0; i < IgnoredAscendancyNames.Length; i++)
        {
            if (asc.IndexOf(IgnoredAscendancyNames[i], StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    private async SyncTask<bool> CtrlClickHighlightedNode()
    {
        var originalMousePos = Input.MousePositionNum;
        LogMessage("Entered the synctask");
        try
        {
            var ingameUi = GameController?.Game?.IngameState?.IngameUi;
            if (ingameUi == null)
                return false;

            var passivePanel = ingameUi.TreePanel?.AsObject<TreePanel>();
            var atlasPanel = ingameUi.AtlasTreePanel?.AsObject<TreePanel>();

            TreePanel panel = null;
            ushort nodeId = 0;

            // Prefer whichever panel is visible
            if (atlasPanel != null && atlasPanel.IsVisible)
            {
                panel = atlasPanel;
                nodeId = _highlightedNextAtlasNodeId;
            }
            else if (passivePanel != null && passivePanel.IsVisible)
            {
                panel = passivePanel;
                nodeId = _highlightedNextCharacterNodeId;
            }

            if (panel == null || nodeId == 0)
                return false;

            var passivesById = panel.Passives
                .Where(p => p?.PassiveSkill != null)
                .GroupBy(p => (ushort)p.PassiveSkill.PassiveId)
                .ToDictionary(g => g.Key, g => g.First());

            if (!passivesById.TryGetValue(nodeId, out var nodeElem) || nodeElem == null)
                return false;

            // IMPORTANT: this is client-relative; InputHandler adds window offset internally
            LogMessage("Went for the click the synctask");
            var clickPos = nodeElem.GetClientRectCache.Center.ToVector2Num();
            Input.KeyDown(Keys.LControlKey);
            await Task.Delay(Settings.InputDelay);
            //await SetCursorPosAndLeftClick(clickPos, Settings.InputDelay);
            await SetCursorPosAndLeftClick(new Vector2(677,312), Settings.InputDelay);
            await Task.Delay(Settings.InputDelay);
            Input.KeyUp(Keys.LControlKey);

            /*
            try
            {

                await _inputHandler.MoveCursorAndControlClick(clickPos, CancellationToken.None);
            }
            finally
            {
                // always restore for this run
                Input.SetCursorPos(originalMousePos);
            }

            */
            return true;
        }
        catch
        {
            
            return false;
        }
    }
    private async SyncTask<bool> RunAutoPassiveTask(CancellationToken token)
    {
        LogMessage("Entered the Autopassive task");
        try
        {
            var originalMousePos = Input.MousePositionNum;

            var ingameUi = GameController?.Game?.IngameState?.IngameUi;
            if (ingameUi == null)
                return false;

            var passivePanel = ingameUi.TreePanel?.AsObject<TreePanel>();
            var passivePanelVisible = ingameUi.TreePanel?.IsVisible ?? false;
            
            var atlasPanel = ingameUi.AtlasTreePanel?.AsObject<TreePanel>();
            var atlasPanelVisible = ingameUi.AtlasTreePanel?.IsVisible ?? false;
            TreePanel panel = null;
            ushort nodeId = 0;


            // Prefer whichever panel is visible
            if (atlasPanel != null && atlasPanelVisible)
            {
                panel = atlasPanel;
                nodeId = _highlightedNextAtlasNodeId;
            }
            else if (passivePanel != null && passivePanelVisible)
            {
                panel = passivePanel;
                nodeId = _highlightedNextCharacterNodeId;
            }

            if (panel == null || nodeId == 0)
            {
                LogMessage("All panels hidden");
                _isProcessing = false;
                return false;
            }
                


            var passivesById = panel.Passives
                .Where(p => p?.PassiveSkill != null)
                .GroupBy(p => (ushort)p.PassiveSkill.PassiveId)
                .ToDictionary(g => g.Key, g => g.First());

            if (!passivesById.TryGetValue(nodeId, out var nodeElem) || nodeElem == null)
            {
                _isProcessing = false;
                return false;
            }
                


            token.ThrowIfCancellationRequested();
            var clickPos = nodeElem.GetClientRectCache.Center.ToVector2Num();

            try
            {
                LogMessage("Entered try");
                Input.KeyDown(Keys.LControlKey);
                await Task.Delay(Settings.InputDelay, token);
                await _inputHandler.MoveCursorAndClick(clickPos, token);
                await Task.Delay(Settings.InputDelay, token);

            }
            finally
            {
                Input.KeyUp(Keys.LControlKey);
                await Task.Delay(Settings.InputDelay, token);
                Input.SetCursorPos(originalMousePos);
                _isProcessing = false;

            }
            return true;
            // IMPORTANT: this is client-relative; InputHandler adds window offset internally


        }
        catch (OperationCanceledException)
        {
            LogMessage("Autopassive cancelled.", 5, Color.Yellow);
            _isProcessing = false;
            return false;
        }
        catch (Exception ex)
        {
            LogError($"Autopassive failed: {ex.Message}");
            _isProcessing = false;
            return false;
        }
    }
      
    private void TryAutoAdvanceTree(ESkillTreeType type, TreeConfig.SkillTreeData buildData)
    {
        // Respect per-tree-type settings
        if (type == ESkillTreeType.Character && !Settings.AutoAdvanceCharacterTree)
            return;

        if (type == ESkillTreeType.Atlas && !Settings.AutoAdvanceAtlasTree)
            return;
        if (buildData?.Trees == null || buildData.Trees.Count == 0)
            return;
       
        // Determine current url for that tree type
        var currentUrl = type == ESkillTreeType.Atlas
            ? Settings.LastSelectedAtlasUrl
            : Settings.LastSelectedCharacterUrl;

        if (string.IsNullOrWhiteSpace(currentUrl))
            return;

        // Throttle to avoid repeated triggering on same frame/rapid refresh
        var now = Environment.TickCount64;
        if (now - _lastAutoAdvanceMs < 250) // 250ms is plenty
            return;

        // Prevent re-advancing from the same completed tree over and over
        if (type == ESkillTreeType.Atlas)
        {
            if (string.Equals(_lastAutoAdvancedAtlasFromUrl, currentUrl, StringComparison.Ordinal))
                return;
        }
        else
        {
            if (string.Equals(_lastAutoAdvancedCharFromUrl, currentUrl, StringComparison.Ordinal))
                return;
        }

        // Only consider trees of the same type, in existing order
        var list = buildData.Trees.Where(t => t.Type == type).ToList();
        if (list.Count == 0)
            return;

        // Find current index in that ordered list
        var idx = list.FindIndex(t => string.Equals(t.SkillTreeUrl, currentUrl, StringComparison.Ordinal));
        if (idx < 0)
            idx = 0; // if current url isn't found, start from beginning

        // Move to next (wrap around)
        var nextIdx = (idx + 1) % list.Count;
        var nextUrl = list[nextIdx].SkillTreeUrl;

        if (string.IsNullOrWhiteSpace(nextUrl))
            return;

        // Mark we advanced from this url
        _lastAutoAdvanceMs = now;
        if (type == ESkillTreeType.Atlas)
            _lastAutoAdvancedAtlasFromUrl = currentUrl;
        else
            _lastAutoAdvancedCharFromUrl = currentUrl;

  

        LoadUrl(nextUrl);
    }
    public static async SyncTask<bool> SetCursorPosAndLeftClick(Vector2 coords, int extraDelay)
    {
        Input.SetCursorPos(coords);
        await Task.Delay(10 + extraDelay);
        Input.LeftDown();
        await Task.Delay(1);
        Input.LeftUp();
        return true;
    }
}