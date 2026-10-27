// Test-only stand-ins for the game-owned runtime types used by
// VariableIndexExpressionLine. These let CI compile and test its pure expression
// rewriting helpers without requiring a local DDLC+ installation.
namespace RenPyParser
{
    public abstract class Line
    {
    }
}

namespace RenPyParser.Images
{
    public interface IApply
    {
        void Apply(RenpyParser.IContext context);
    }

    internal sealed class TestImagesNamespaceMarker
    {
    }
}

namespace RenPyParser.Sprites
{
    internal sealed class TestSpritesNamespaceMarker
    {
    }
}

namespace RenPyParser.Transforms
{
    internal sealed class TestTransformsNamespaceMarker
    {
    }
}

namespace RenpyParser
{
    public interface IContext
    {
        bool ResolveVariable(string name, out SimpleExpressionEngine.DataValue value);
    }
}

namespace SimpleExpressionEngine
{
    public struct DataValue
    {
        private object objectRef;
    }
}

namespace StoryAPI.Game
{
    internal static class GameBridge
    {
        public static bool TrySetVariableLiteral(
            string name, object value, bool defaultOnly, RenpyParser.IContext context)
        {
            return true;
        }
    }
}

namespace StoryAPI.Runtime
{
    internal sealed class VariableInitializationState
    {
        public bool Initialized;
        public bool Failed;
    }

    internal static class Log
    {
        public static void Info(string message) { }
        public static void Warn(string message) { }
    }
}
