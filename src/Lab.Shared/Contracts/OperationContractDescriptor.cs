namespace Lab.Shared.Contracts;

public sealed class OperationContractDescriptor
{
    public string OperationName { get; }

    public IReadOnlyList<string> Inputs { get; }

    public IReadOnlyList<string> Preconditions { get; }

    public IReadOnlyList<string> Postconditions { get; }

    public IReadOnlyList<string> Invariants { get; }

    public string ObservationStart { get; }

    public string ObservationEnd { get; }

    private OperationContractDescriptor(
        string operationName,
        IReadOnlyList<string> inputs,
        IReadOnlyList<string> preconditions,
        IReadOnlyList<string> postconditions,
        IReadOnlyList<string> invariants,
        string observationStart,
        string observationEnd)
    {
        OperationName = operationName;
        Inputs = inputs;
        Preconditions = preconditions;
        Postconditions = postconditions;
        Invariants = invariants;
        ObservationStart = observationStart;
        ObservationEnd = observationEnd;
    }

    public static OperationContractDescriptor Create(
        string operationName,
        IEnumerable<string>? inputs = null,
        IEnumerable<string>? preconditions = null,
        IEnumerable<string>? postconditions = null,
        IEnumerable<string>? invariants = null,
        string observationStart = "",
        string observationEnd = "")
    {
        if (string.IsNullOrWhiteSpace(operationName))
        {
            throw new ArgumentException("Operation name is required.", nameof(operationName));
        }

        if (string.IsNullOrWhiteSpace(observationStart))
        {
            throw new ArgumentException("Observation start is required.", nameof(observationStart));
        }

        if (string.IsNullOrWhiteSpace(observationEnd))
        {
            throw new ArgumentException("Observation end is required.", nameof(observationEnd));
        }

        return new OperationContractDescriptor(
            operationName.Trim(),
            Freeze(inputs),
            Freeze(preconditions),
            Freeze(postconditions),
            Freeze(invariants),
            observationStart.Trim(),
            observationEnd.Trim());
    }

    private static IReadOnlyList<string> Freeze(IEnumerable<string>? values)
    {
        string[] copy = (values ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToArray();

        return Array.AsReadOnly(copy);
    }
}
