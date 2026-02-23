using System;
using System.Collections.Generic;
using System.Numerics;
using PassiveSkillTreePlanter.SkillTreeJson;

namespace PassiveSkillTreePlanter;

public class SkillNode
{
    public Constants Constants { private get; init; }
    public List<int> OrbitRadii => Constants.OrbitRadii;
    public List<int> SkillsPerOrbit => Constants.SkillsPerOrbit;
    public bool bJevel;
    public bool bKeyStone;
    public bool bMastery;
    public bool bMult;
    public bool bNotable;
    public Vector2 DrawPosition;

    //Cached for drawing
    public float DrawSize = 100;
    public ushort Id; // "id": -28194677,
    public string AscendancyName; 
    public List<ushort> linkedNodes = new List<ushort>();
    public string Name; //"dn": "Block Recovery",
    public int Orbit; //  "o": 1,
    public long OrbitIndex; // "oidx": 3,
    public SkillNodeGroup SkillNodeGroup;

    public Vector2 Position => GetPositionAtAngle(Arc);

    public Vector2 GetPositionAtAngle(float angle)
    {
        if (SkillNodeGroup == null) return new Vector2();
        return SkillNodeGroup.Position + OrbitRadius * GetAngleVector(angle);
    }

    public int OrbitRadius => OrbitRadii[Orbit];

    public float Arc => GetOrbitAngle(OrbitIndex, SkillsPerOrbit[Orbit]);

    public void Init()
    {
        DrawPosition = Position;

        if (bJevel)
            DrawSize = 160;

        if (bNotable)
            DrawSize = 170;

        if (bKeyStone)
            DrawSize = 250;
    }

    private static readonly int[] Angles16 = { 0, 30, 45, 60, 90, 120, 135, 150, 180, 210, 225, 240, 270, 300, 315, 330 };
    private static readonly int[] Angles40 = { 0, 10, 20, 30, 40, 45, 50, 60, 70, 80, 90, 100, 110, 120, 130, 135, 140, 150, 160, 170, 180, 190, 200, 210, 220, 225, 230, 240, 250, 260, 270, 280, 290, 300, 310, 315, 320, 330, 340, 350 };

    private static float GetOrbitAngle(long orbitIndex, long maxNodePositions)
    {
        return maxNodePositions switch
        {
            16 => Angles16[orbitIndex] * MathF.PI / 180,
            40 => Angles40[orbitIndex] * MathF.PI / 180,
            _ => 2 * MathF.PI * orbitIndex / maxNodePositions
        };
    }

    public static Vector2 GetAngleVector(float angle)
    {
        return new Vector2(MathF.Sin(angle), -MathF.Cos(angle));
    }
}

public class SkillNodeGroup
{
    public List<SkillNode> Nodes = new List<SkillNode>();
    public Vector2 Position;
}