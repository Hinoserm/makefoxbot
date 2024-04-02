namespace Autofocus.CtrlNet;

public sealed class ControlNetModule
{
    public string Name { get; }

    public Parameter? Resolution { get; }
    public IReadOnlyList<Parameter> Parameters { get; }

    internal ControlNetModule(string name, Parameter? resolution, IReadOnlyList<Parameter> parameters)
    {
        Name = name;
        Resolution = resolution;
        Parameters = parameters;
    }

    public class Parameter
    {
        public string Name { get; init; }
        public float Value { get; init; }
        public float Min { get; init; }
        public float Max { get; init; }

        internal Parameter(string name, float value, float min, float max)
        {
            Name = name;
            Value = value;
            Min = min;
            Max = max;
        }
    }
}