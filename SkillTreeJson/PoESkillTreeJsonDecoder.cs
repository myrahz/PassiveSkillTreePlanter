using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ExileCore;
using Newtonsoft.Json;

namespace PassiveSkillTreePlanter.SkillTreeJson;
//Many thanks to https://github.com/EmmittJ/PoESkillTree

public class PoESkillTreeJsonDecoder
{
    private List<SkillNodeGroup> NodeGroups = new List<SkillNodeGroup>();
    private List<SkillNode> Nodes = new List<SkillNode>();
    public Dictionary<ushort, SkillNode> SkillNodes = new Dictionary<ushort, SkillNode>();
    private PoESkillTree SkillTree;

    public void Decode(string jsonTree)
    {
        Nodes = new List<SkillNode>();

        var jss = new JsonSerializerSettings
        {
            Error = (sender, args) =>
            {
                // This one is known: "509":{"x":_,"y":_,"oo":[],"n":[]}} has an Array in "oo".
                // if (args.ErrorContext.Path != "groups.509.oo")
                // PoeHUD.Plugins.BasePlugin.LogError("Exception while deserializing Json tree" + args.ErrorContext.Error, 5);
                if (args.ErrorContext.Path == null || !args.ErrorContext.Path.EndsWith(".oo"))
                    Logger.Log.Error("Exception while deserializing Json tree" + args.ErrorContext.Error);

                args.ErrorContext.Handled = true;
            }
        };

        SkillTree = JsonConvert.DeserializeObject<PoESkillTree>(jsonTree, jss);
        SkillNodes = new Dictionary<ushort, SkillNode>();
        NodeGroups = new List<SkillNodeGroup>();

        foreach (var nd in SkillTree.Nodes)
        {
            var skillNode = new SkillNode
            {
                Id = (ushort)nd.Value.Skill,
                AscendancyName = nd.Value.AscendancyName,
                Name = nd.Value.Name,
                Orbit = nd.Value.Orbit,
                OrbitIndex = nd.Value.OrbitIndex,
                bJevel = nd.Value.IsJewelSocket,
                bMastery = nd.Value.IsMastery,
                bMult = nd.Value.IsMultipleChoice,
                linkedNodes = [..nd.Value.Out ?? [], .. nd.Value.In ?? []],
                bKeyStone = nd.Value.IsKeystone,
                Constants = SkillTree.Constants,
            };

            Nodes.Add(skillNode);
            SkillNodes.Add((ushort)nd.Value.Skill, skillNode);
        }

        NodeGroups = new List<SkillNodeGroup>();

        foreach (var gp in SkillTree.Groups)
        {
            var ng = new SkillNodeGroup
            {
                Position = new Vector2((float)gp.Value.X, (float)gp.Value.Y)
            };

            foreach (var node in gp.Value.Nodes)
            {
                var nodeToAdd = SkillNodes[node];
                ng.Nodes.Add(nodeToAdd);
                nodeToAdd.SkillNodeGroup = ng;
            }

            NodeGroups.Add(ng);
        }

        foreach (var node in Nodes)
        {
            node.Init();
        }
    }
}