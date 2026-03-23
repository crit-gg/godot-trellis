using Godot;
using GodotTrellis;

namespace GodotTrellisExamples;

public partial class Game : Node
{
    [FromAncestor]
    private partial EntityManager EntityManager { get; }

    /// <inheritdoc />
    public override void _Ready()
    {
        foreach (var entity in EntityManager.Entities)
        {
            GD.Print(entity);
        }
    }
}