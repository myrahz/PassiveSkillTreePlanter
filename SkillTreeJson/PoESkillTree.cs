using System.Collections.Generic;

namespace PassiveSkillTreePlanter.SkillTreeJson;

public class PoESkillTree
{
    public List<Class> Classes { get; set; }
    public Dictionary<string, Group> Groups { get; set; }
    public Dictionary<string, Node> Nodes { get; set; }
    public List<long> JewelSlots { get; set; }
    public long Min_X { get; set; }
    public long Min_Y { get; set; }
    public long Max_X { get; set; }
    public long Max_Y { get; set; }
    public Constants Constants { get; set; }
}

public class Class
{
    public string Name { get; set; }
    public long BaseStr { get; set; }
    public long BaseDex { get; set; }
    public long BaseInt { get; set; }
    public List<Ascendancy> Ascendancies { get; set; }
}

public class Ascendancy
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string FlavourText { get; set; }
    public string FlavourTextColour { get; set; }
    public FlavourTextRect FlavourTextRect { get; set; }
}

public class FlavourTextRect
{
    public long X { get; set; }
    public long Y { get; set; }
    public long Width { get; set; }
    public long Height { get; set; }
}

public class Constants
{
    public Classes Classes { get; set; }
    public CharacterAttributes CharacterAttributes { get; set; }
    public long PssCentreInnerRadius { get; set; }
    public List<int> SkillsPerOrbit { get; set; }
    public List<int> OrbitRadii { get; set; }
}

public class CharacterAttributes
{
    public long Strength { get; set; }
    public long Dexterity { get; set; }
    public long Intelligence { get; set; }
}

public class Classes
{
    public long StrDexIntClass { get; set; }
    public long StrClass { get; set; }
    public long DexClass { get; set; }
    public long IntClass { get; set; }
    public long StrDexClass { get; set; }
    public long StrIntClass { get; set; }
    public long DexIntClass { get; set; }
}

public class Group
{
    public double X { get; set; }
    public double Y { get; set; }
    public List<long> Orbits { get; set; }
    public List<ushort> Nodes { get; set; }
    public bool IsProxy { get; set; }
}

public class Node
{
    public long Skill { get; set; }
    public string Name { get; set; }
    public string Icon { get; set; }
    public bool IsNotable { get; set; }
    public List<Recipe> Recipe { get; set; }
    public List<string> Stats { get; set; }
    public long Group { get; set; }
    public int Orbit { get; set; }
    public long OrbitIndex { get; set; }
    public List<ushort> Out { get; set; }
    public List<ushort> In { get; set; }
    public List<string> ReminderText { get; set; }
    public bool IsMastery { get; set; }
    public long GrantedStrength { get; set; }
    public string AscendancyName { get; set; }
    public long GrantedDexterity { get; set; }
    public bool IsAscendancyStart { get; set; }
    public bool IsMultipleChoice { get; set; }
    public long GrantedIntelligence { get; set; }
    public bool IsJewelSocket { get; set; }
    public ExpansionJewel ExpansionJewel { get; set; }
    public long GrantedPassivePoints { get; set; }
    public bool IsKeystone { get; set; }
    public List<string> FlavourText { get; set; }
    public bool IsProxy { get; set; }
    public bool IsMultipleChoiceOption { get; set; }
    public bool IsBlighted { get; set; }
    public int? ClassStartIndex { get; set; }
}

public class ExpansionJewel
{
    public long Size { get; set; }
    public long Index { get; set; }
    public long Proxy { get; set; }
    public long Parent { get; set; }
}

public enum Recipe { AmberOil, AzureOil, BlackOil, ClearOil, CrimsonOil, GoldenOil, IndigoOil, OpalescentOil, SepiaOil, SilverOil, TealOil, VerdantOil, VioletOil };