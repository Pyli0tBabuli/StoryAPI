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
    public sealed class RenpyOneLinePython : RenPyParser.Line
    {
        public string Code { get; }
        public RenpyOneLinePython(string code) { Code = code; }
    }

    public sealed class RenpyGoToLineUnless : RenPyParser.Line
    {
        public string VariableName { get; }
        public int Target { get; }
        public RenpyGoToLineUnless(string variableName, int target)
        {
            VariableName = variableName;
            Target = target;
        }
    }

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
        None,
        Float,
        String,
        Boolean,
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

        public DataValue(bool value)
        {
            type = TestDataValueType.Boolean;
            objectRef = value;
            floatVal = value ? 1f : 0f;
            stringVal = string.Empty;
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
        private static readonly System.Collections.Generic.Dictionary<string, object>
            LastLiteralValues = new System.Collections.Generic.Dictionary<string, object>();

        public static bool TrySetVariableLiteral(
            string name, object value, bool defaultOnly, RenpyParser.IContext context)
        {
            LastLiteralValues[name] = value;

            // Mirror the production bridge: assigned values must be visible through the
            // live execution context as well as the test-only result capture.
            if (context is RenpyParser.DynamicIndexTestContext testContext)
            {
                SimpleExpressionEngine.DataValue dataValue;
                if (value == null)
                    dataValue = default(SimpleExpressionEngine.DataValue);
                else if (value is string stringValue)
                    dataValue = new SimpleExpressionEngine.DataValue(stringValue);
                else if (value is bool booleanValue)
                    dataValue = new SimpleExpressionEngine.DataValue(booleanValue);
                else if (value is byte || value is sbyte || value is short || value is ushort ||
                         value is int || value is uint || value is long || value is ulong ||
                         value is float || value is double || value is decimal)
                    dataValue = new SimpleExpressionEngine.DataValue(
                        System.Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture));
                else
                    dataValue = new SimpleExpressionEngine.DataValue(value);

                testContext.SetVariable(name, dataValue);
            }

            return true;
        }

        public static bool TryGetLastTestLiteral(string name, out object value)
        {
            return LastLiteralValues.TryGetValue(name, out value);
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
