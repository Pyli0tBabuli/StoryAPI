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

    internal sealed class DynamicIndexTestContext : IContext
    {
        private readonly System.Collections.Generic.Dictionary<string, SimpleExpressionEngine.DataValue>
            _variables = new System.Collections.Generic.Dictionary<string, SimpleExpressionEngine.DataValue>();

        public void SetVariable(string name, SimpleExpressionEngine.DataValue value)
        {
            _variables[name] = value;
        }

        public bool ResolveVariable(string name, out SimpleExpressionEngine.DataValue value)
        {
            return _variables.TryGetValue(name, out value);
        }
    }
}

namespace SimpleExpressionEngine
{
    public enum TestDataValueType
    {
        Float,
        String,
        Object
    }

    public struct DataValue
    {
        private TestDataValueType type;
        private object objectRef;
        private float floatVal;
        private string stringVal;

        public DataValue(float value)
        {
            type = TestDataValueType.Float;
            objectRef = null;
            floatVal = value;
            stringVal = string.Empty; // Inactive field is deliberately non-null like the game wrapper.
        }

        public DataValue(string value)
        {
            type = TestDataValueType.String;
            objectRef = null;
            floatVal = 0;
            stringVal = value;
        }

        public DataValue(object value)
        {
            type = TestDataValueType.Object;
            objectRef = value;
            floatVal = 0;
            stringVal = string.Empty;
        }
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
