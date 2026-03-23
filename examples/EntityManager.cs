using System.Collections.Generic;
using Godot;

namespace GodotTrellisExamples;

public partial class EntityManager : Node
{
    private readonly string[] _entities = ["ent-1", "ent-2"];

    public IEnumerable<string> Entities => _entities;
}