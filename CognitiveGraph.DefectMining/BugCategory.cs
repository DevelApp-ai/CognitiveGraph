namespace DevelApp.CognitiveGraph.DefectMining;

/// <summary>Bug taxonomy used by the ground-truth corpus (issue #55).</summary>
/// <remarks>
/// The categories align with the Minotaur Labyrinth <c>rules/csharp10</c> rule
/// families (epic DevelApp-ai/Minotaur#246, L2 pack): maybe-null deref, unsafe
/// as-cast deref, constant-zero division/modulo and unvalidated array
/// indexing, which subsume the issue's nullability / array-bounds / type-safety
/// taxonomy.
/// </remarks>
public enum BugCategory
{
    /// <summary>Unguarded maybe-null dereference (nullability).</summary>
    Nullability,

    /// <summary>Missing index validation (array bounds).</summary>
    ArrayBounds,

    /// <summary>Unchecked cast (type safety).</summary>
    TypeSafety,

    /// <summary>Division or modulo with an unguarded zero operand.</summary>
    DivisionByZero
}
